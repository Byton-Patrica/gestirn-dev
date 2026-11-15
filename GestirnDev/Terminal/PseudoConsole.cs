using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace GestirnDev
{
// A Windows pseudo console (ConPTY, Windows 10 1809+). The child sees a real console, so interactive
// tools - shells, REPLs, AI coding agents - behave exactly as they do in Windows Terminal.
internal sealed class PseudoConsole : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Coord { public short X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr lpAttributeList; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr pc);
    [DllImport("kernel32.dll")] private static extern int ResizePseudoConsole(IntPtr pc, Coord size);
    [DllImport("kernel32.dll")] private static extern void ClosePseudoConsole(IntPtr pc);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string app, StringBuilder commandLine, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref StartupInfoEx si, out ProcessInformation pi);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr handle, uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    private const uint ExtendedStartupInfoPresent = 0x00080000, CreateUnicodeEnvironment = 0x00000400;
    private static readonly IntPtr AttributePseudoConsole = new IntPtr(0x00020016);

    private IntPtr pc, process, thread, attributes;
    private FileStream input, output;
    private readonly object writeLock = new object();
    private int disposed;

    public int ProcessId { get; private set; }
    public event Action<string> Output;
    public event Action<int> Exited;

    public static bool Supported
    {
        get
        {
            try { IntPtr p; CreatePseudoConsole(new Coord { X = 1, Y = 1 }, null, null, 0, out p); return true; }
            catch (EntryPointNotFoundException) { return false; }
            catch (Exception) { return true; }
        }
    }

    public void Start(string commandLine, string workingDir, int cols, int rows, IDictionary<string, string> extraEnv)
    {
        SafeFileHandle inRead, inWrite, outRead, outWrite;
        if (!CreatePipe(out inRead, out inWrite, IntPtr.Zero, 0) || !CreatePipe(out outRead, out outWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        int hr = CreatePseudoConsole(new Coord { X = (short)Math.Max(2, cols), Y = (short)Math.Max(2, rows) }, inRead, outWrite, 0, out pc);
        if (hr != 0) throw new Win32Exception(hr);

        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        attributes = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!UpdateProcThreadAttribute(attributes, 0, AttributePseudoConsole, pc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var si = new StartupInfoEx();
        si.StartupInfo.cb = Marshal.SizeOf(typeof(StartupInfoEx));
        si.lpAttributeList = attributes;
        // Don't let the child inherit this process's own (possibly redirected) std handles.
        si.StartupInfo.dwFlags = 0x00000100;
        IntPtr env = BuildEnvironment(extraEnv);
        ProcessInformation pi;
        try
        {
            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment, env, Directory.Exists(workingDir) ? workingDir : null, ref si, out pi))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(env); }
        process = pi.hProcess;
        thread = pi.hThread;
        ProcessId = pi.dwProcessId;
        inRead.Dispose();
        outWrite.Dispose();
        input = new FileStream(inWrite, FileAccess.Write);
        output = new FileStream(outRead, FileAccess.Read);

        new Thread(ReadLoop) { IsBackground = true, Name = "pty-read" }.Start();
        new Thread(WaitLoop) { IsBackground = true, Name = "pty-wait" }.Start();
    }

    private static IntPtr BuildEnvironment(IDictionary<string, string> extra)
    {
        var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry e in Environment.GetEnvironmentVariables()) vars[(string)e.Key] = (string)e.Value;
        if (extra != null) foreach (var kv in extra) vars[kv.Key] = kv.Value;
        var sb = new StringBuilder();
        foreach (var kv in vars) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
        sb.Append('\0');
        return Marshal.StringToHGlobalUni(sb.ToString());
    }

    private void ReadLoop()
    {
        var decoder = new UTF8Encoding(false).GetDecoder();
        var buffer = new byte[16384];
        var chars = new char[16384 * 2];
        try
        {
            int n;
            while ((n = output.Read(buffer, 0, buffer.Length)) > 0)
            {
                int c = decoder.GetChars(buffer, 0, n, chars, 0);
                if (c > 0 && Output != null) Output(new string(chars, 0, c));
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    private void WaitLoop()
    {
        WaitForSingleObject(process, 0xFFFFFFFF);
        uint code;
        GetExitCodeProcess(process, out code);
        // Closing the pseudo console flushes and ends the output pipe.
        Dispose();
        if (Exited != null) Exited((int)code);
    }

    public void Write(string text)
    {
        if (disposed != 0 || input == null) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        lock (writeLock)
        {
            try { input.Write(bytes, 0, bytes.Length); input.Flush(); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public void Resize(int cols, int rows)
    {
        if (disposed == 0 && pc != IntPtr.Zero) ResizePseudoConsole(pc, new Coord { X = (short)Math.Max(2, cols), Y = (short)Math.Max(2, rows) });
    }

    public void Kill()
    {
        if (process != IntPtr.Zero && disposed == 0)
        {
            try
            {
                using (var k = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskkill", "/PID " + ProcessId + " /T /F") { UseShellExecute = false, CreateNoWindow = true }))
                    k.WaitForExit(3000);
            }
            catch (Win32Exception) { }
            TerminateProcess(process, 1);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { if (input != null) input.Dispose(); } catch (IOException) { }
        if (pc != IntPtr.Zero) { var p = pc; pc = IntPtr.Zero; new Thread(() => ClosePseudoConsole(p)) { IsBackground = true }.Start(); }
        if (attributes != IntPtr.Zero) { DeleteProcThreadAttributeList(attributes); Marshal.FreeHGlobal(attributes); attributes = IntPtr.Zero; }
        if (thread != IntPtr.Zero) { CloseHandle(thread); thread = IntPtr.Zero; }
    }
}

internal struct TermCell
{
    public char Ch, Low;
    public int Fg, Bg;     // -1 default, 0..255 palette, 0x1000000 | rgb for true colour
    public byte Attr;
}

// An xterm-compatible screen: the escape sequences ConPTY and modern CLIs emit (colours, cursor
// movement, scroll regions, the alternate screen, bracketed paste, device status reports).
internal sealed class VtScreen
{
    public const byte Bold = 1, Dim = 2, Italic = 4, Underline = 8, Inverse = 16, Wide = 32, WideTail = 64;

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    private TermCell[][] main, alt, screen;
    public readonly List<TermCell[]> Scrollback = new List<TermCell[]>();
    public int MaxScrollback = 5000;
    public int CursorX, CursorY;
    private bool wrapPending;
    private int top, bottom;
    private int savedX, savedY, savedFg = -1, savedBg = -1;
    private byte savedAttr;
    private int fg = -1, bg = -1;
    private byte attr;
    public bool AppCursor, BracketedPaste, CursorVisible = true, AltScreen, FocusReporting, AutoWrap = true;
    public string Title = "";
    public Action<string> Respond;
    public int Version;

    private enum State { Ground, Escape, EscapeSkip, Csi, Osc, OscEscape }
    private State state;
    private readonly StringBuilder param = new StringBuilder();
    private readonly StringBuilder osc = new StringBuilder();
    private bool privateMode;
    private char pendingHigh;

    public VtScreen(int cols, int rows)
    {
        Cols = Math.Max(2, cols);
        Rows = Math.Max(2, rows);
        main = NewBuffer(Rows, Cols);
        alt = NewBuffer(Rows, Cols);
        screen = main;
        top = 0;
        bottom = Rows - 1;
    }

    public TermCell[] Line(int y) { return screen[y]; }

    private static TermCell[][] NewBuffer(int rows, int cols)
    {
        var b = new TermCell[rows][];
        for (int i = 0; i < rows; i++) b[i] = NewLine(cols, -1);
        return b;
    }

    private static TermCell[] NewLine(int cols, int bg)
    {
        var l = new TermCell[cols];
        for (int i = 0; i < cols; i++) { l[i].Ch = ' '; l[i].Fg = -1; l[i].Bg = bg; }
        return l;
    }

    public void Resize(int cols, int rows)
    {
        cols = Math.Max(2, cols);
        rows = Math.Max(2, rows);
        if (cols == Cols && rows == Rows) return;
        main = ResizeBuffer(main, cols, rows, screen == main);
        alt = ResizeBuffer(alt, cols, rows, false);
        screen = AltScreen ? alt : main;
        Cols = cols;
        Rows = rows;
        top = 0;
        bottom = rows - 1;
        CursorX = Math.Min(CursorX, cols - 1);
        CursorY = Math.Min(CursorY, rows - 1);
        wrapPending = false;
        Version++;
    }

    private TermCell[][] ResizeBuffer(TermCell[][] buf, int cols, int rows, bool keepBottom)
    {
        var lines = buf.ToList();
        // When shrinking, move lines above the cursor into scrollback so the prompt stays visible.
        while (lines.Count > rows)
        {
            if (keepBottom && CursorY > 0) { Scrollback.Add(lines[0]); lines.RemoveAt(0); CursorY--; }
            else lines.RemoveAt(lines.Count - 1);
        }
        while (lines.Count < rows) lines.Add(NewLine(cols, -1));
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Length == cols) continue;
            var l = NewLine(cols, -1);
            Array.Copy(lines[i], l, Math.Min(cols, lines[i].Length));
            lines[i] = l;
        }
        TrimScrollback();
        return lines.ToArray();
    }

    private void TrimScrollback()
    {
        if (Scrollback.Count > MaxScrollback) Scrollback.RemoveRange(0, Scrollback.Count - MaxScrollback);
    }

    public void Clear()
    {
        Scrollback.Clear();
        Version++;
    }

    public static bool IsWide(int cp)
    {
        return cp >= 0x1100 && (cp <= 0x115F || (cp >= 0x2E80 && cp <= 0xA4CF && cp != 0x303F) || (cp >= 0xAC00 && cp <= 0xD7A3) ||
               (cp >= 0xF900 && cp <= 0xFAFF) || (cp >= 0xFE30 && cp <= 0xFE4F) || (cp >= 0xFF00 && cp <= 0xFF60) || (cp >= 0xFFE0 && cp <= 0xFFE6) ||
               (cp >= 0x1F300 && cp <= 0x1F64F) || (cp >= 0x1F900 && cp <= 0x1F9FF) || (cp >= 0x20000 && cp <= 0x3FFFD));
    }

    public void Feed(string text)
    {
        foreach (char c in text) Step(c);
        Version++;
    }

    private void Step(char c)
    {
        switch (state)
        {
            case State.Ground:
                if (c == '\x1b') { state = State.Escape; return; }
                if (c < 0x20 || c == 0x7f) { Control(c); return; }
                if (char.IsHighSurrogate(c)) { pendingHigh = c; return; }
                if (char.IsLowSurrogate(c) && pendingHigh != 0) { Print(pendingHigh, c); pendingHigh = '\0'; return; }
                Print(c, '\0');
                return;
            case State.Escape:
                state = State.Ground;
                switch (c)
                {
                    case '[': state = State.Csi; param.Clear(); privateMode = false; return;
                    case ']': state = State.Osc; osc.Clear(); return;
                    case '(': case ')': case '*': case '+': case '#': case '%': state = State.EscapeSkip; return;
                    case '7': SaveCursor(); return;
                    case '8': RestoreCursor(); return;
                    case 'D': Index(); return;
                    case 'E': CursorX = 0; Index(); return;
                    case 'M': ReverseIndex(); return;
                    case 'c': Reset(); return;
                }
                return;
            case State.EscapeSkip:
                state = State.Ground;
                return;
            case State.Csi:
                if (c >= 0x40 && c <= 0x7e) { state = State.Ground; Csi(c); return; }
                if (c == '?' || c == '>' || c == '<' || c == '=') { privateMode = c == '?'; if (c != '?') param.Append(c); return; }
                if (c == '\x1b') { state = State.Escape; return; }
                param.Append(c);
                return;
            case State.Osc:
                if (c == '\x07') { state = State.Ground; Osc(); return; }
                if (c == '\x1b') { state = State.OscEscape; return; }
                if (osc.Length < 4096) osc.Append(c);
                return;
            case State.OscEscape:
                state = State.Ground;
                Osc();
                return;
        }
    }

    private void Osc()
    {
        string s = osc.ToString();
        int semi = s.IndexOf(';');
        if (semi > 0 && (s.StartsWith("0;") || s.StartsWith("2;"))) Title = s.Substring(semi + 1);
    }

    private void Control(char c)
    {
        switch (c)
        {
            case '\r': CursorX = 0; wrapPending = false; break;
            case '\n': case '\v': case '\f': Index(); break;
            case '\b': if (CursorX > 0) CursorX--; wrapPending = false; break;
            case '\t':
                CursorX = Math.Min(Cols - 1, (CursorX / 8 + 1) * 8);
                break;
        }
    }

    private void Print(char c, char low)
    {
        int cp = low != '\0' ? char.ConvertToUtf32(c, low) : c;
        int width = IsWide(cp) ? 2 : 1;
        if (wrapPending)
        {
            if (AutoWrap) { CursorX = 0; Index(); }
            wrapPending = false;
        }
        if (width == 2 && CursorX == Cols - 1)
        {
            if (AutoWrap) { screen[CursorY][CursorX].Ch = ' '; CursorX = 0; Index(); }
            else return;
        }
        var line = screen[CursorY];
        line[CursorX] = new TermCell { Ch = c, Low = low, Fg = fg, Bg = bg, Attr = (byte)(attr | (width == 2 ? Wide : 0)) };
        if (width == 2) line[CursorX + 1] = new TermCell { Ch = ' ', Fg = fg, Bg = bg, Attr = (byte)(attr | WideTail) };
        if (CursorX + width >= Cols) { CursorX = Cols - 1; wrapPending = true; }
        else CursorX += width;
    }

    private void Index()
    {
        if (CursorY == bottom) ScrollUp(1);
        else if (CursorY < Rows - 1) CursorY++;
        wrapPending = false;
    }

    private void ReverseIndex()
    {
        if (CursorY == top) ScrollDown(1);
        else if (CursorY > 0) CursorY--;
    }

    private void ScrollUp(int n)
    {
        for (int k = 0; k < n; k++)
        {
            if (screen == main && top == 0) { Scrollback.Add(screen[top]); TrimScrollback(); }
            for (int y = top; y < bottom; y++) screen[y] = screen[y + 1];
            screen[bottom] = NewLine(Cols, bg);
        }
    }

    private void ScrollDown(int n)
    {
        for (int k = 0; k < n; k++)
        {
            for (int y = bottom; y > top; y--) screen[y] = screen[y - 1];
            screen[top] = NewLine(Cols, bg);
        }
    }

    private void SaveCursor() { savedX = CursorX; savedY = CursorY; savedFg = fg; savedBg = bg; savedAttr = attr; }
    private void RestoreCursor() { CursorX = Math.Min(savedX, Cols - 1); CursorY = Math.Min(savedY, Rows - 1); fg = savedFg; bg = savedBg; attr = savedAttr; wrapPending = false; }

    private void Reset()
    {
        fg = bg = -1; attr = 0;
        top = 0; bottom = Rows - 1;
        CursorX = CursorY = 0;
        AppCursor = BracketedPaste = false;
        CursorVisible = true;
        if (AltScreen) { screen = main; AltScreen = false; }
        for (int y = 0; y < Rows; y++) screen[y] = NewLine(Cols, -1);
    }

    private int[] Params(int defaultValue, int min)
    {
        var parts = param.ToString().Split(';');
        var result = new int[Math.Max(1, parts.Length)];
        for (int i = 0; i < parts.Length; i++)
        {
            int v;
            string p = parts[i].Split(':')[0];
            result[i] = int.TryParse(p, out v) ? v : defaultValue;
            if (p.Length == 0) result[i] = defaultValue;
        }
        if (parts.Length == 0) result[0] = defaultValue;
        for (int i = 0; i < result.Length; i++) if (result[i] < min) result[i] = min == 1 && result[i] == 0 ? 1 : result[i];
        return result;
    }

    private void EraseLine(int y, int from, int to)
    {
        var line = screen[y];
        for (int x = Math.Max(0, from); x <= Math.Min(Cols - 1, to); x++) line[x] = new TermCell { Ch = ' ', Fg = -1, Bg = bg };
    }

    private void Csi(char final)
    {
        string raw = param.ToString();
        if (raw.StartsWith(">") || raw.StartsWith("<") || raw.StartsWith("="))
        {
            if (final == 'c' && raw.StartsWith(">") && Respond != null) Respond("\x1b[>0;10;1c");
            return;
        }
        if (privateMode && (final == 'h' || final == 'l')) { PrivateMode(final == 'h'); return; }
        int[] p;
        wrapPending = final == 'm' && wrapPending;
        switch (final)
        {
            case 'A': p = Params(1, 1); CursorY = Math.Max(CursorY >= top ? top : 0, CursorY - p[0]); break;
            case 'B': case 'e': p = Params(1, 1); CursorY = Math.Min(CursorY <= bottom ? bottom : Rows - 1, CursorY + p[0]); break;
            case 'C': case 'a': p = Params(1, 1); CursorX = Math.Min(Cols - 1, CursorX + p[0]); break;
            case 'D': p = Params(1, 1); CursorX = Math.Max(0, CursorX - p[0]); break;
            case 'E': p = Params(1, 1); CursorX = 0; CursorY = Math.Min(Rows - 1, CursorY + p[0]); break;
            case 'F': p = Params(1, 1); CursorX = 0; CursorY = Math.Max(0, CursorY - p[0]); break;
            case 'G': case '`': p = Params(1, 1); CursorX = Math.Min(Cols - 1, p[0] - 1); break;
            case 'd': p = Params(1, 1); CursorY = Math.Min(Rows - 1, p[0] - 1); break;
            case 'H': case 'f':
                p = Params(1, 1);
                CursorY = Math.Min(Rows - 1, Math.Max(0, p[0] - 1));
                CursorX = Math.Min(Cols - 1, Math.Max(0, (p.Length > 1 ? p[1] : 1) - 1));
                break;
            case 'J':
                p = Params(0, 0);
                if (p[0] == 0) { EraseLine(CursorY, CursorX, Cols - 1); for (int y = CursorY + 1; y < Rows; y++) EraseLine(y, 0, Cols - 1); }
                else if (p[0] == 1) { EraseLine(CursorY, 0, CursorX); for (int y = 0; y < CursorY; y++) EraseLine(y, 0, Cols - 1); }
                else if (p[0] == 2) { for (int y = 0; y < Rows; y++) EraseLine(y, 0, Cols - 1); }
                else if (p[0] == 3) Scrollback.Clear();
                break;
            case 'K':
                p = Params(0, 0);
                if (p[0] == 0) EraseLine(CursorY, CursorX, Cols - 1);
                else if (p[0] == 1) EraseLine(CursorY, 0, CursorX);
                else EraseLine(CursorY, 0, Cols - 1);
                break;
            case 'L':
                p = Params(1, 1);
                if (CursorY >= top && CursorY <= bottom)
                {
                    int save = top; top = CursorY;
                    ScrollDown(Math.Min(p[0], bottom - CursorY + 1));
                    top = save;
                }
                break;
            case 'M':
                p = Params(1, 1);
                if (CursorY >= top && CursorY <= bottom)
                {
                    int save = top; top = CursorY;
                    for (int k = 0; k < Math.Min(p[0], bottom - CursorY + 1); k++)
                    {
                        for (int y = top; y < bottom; y++) screen[y] = screen[y + 1];
                        screen[bottom] = NewLine(Cols, bg);
                    }
                    top = save;
                }
                break;
            case 'P':
            {
                p = Params(1, 1);
                var line = screen[CursorY];
                int n = Math.Min(p[0], Cols - CursorX);
                Array.Copy(line, CursorX + n, line, CursorX, Cols - CursorX - n);
                for (int x = Cols - n; x < Cols; x++) line[x] = new TermCell { Ch = ' ', Fg = -1, Bg = bg };
                break;
            }
            case '@':
            {
                p = Params(1, 1);
                var line = screen[CursorY];
                int n = Math.Min(p[0], Cols - CursorX);
                Array.Copy(line, CursorX, line, CursorX + n, Cols - CursorX - n);
                for (int x = CursorX; x < CursorX + n; x++) line[x] = new TermCell { Ch = ' ', Fg = -1, Bg = bg };
                break;
            }
            case 'X': p = Params(1, 1); EraseLine(CursorY, CursorX, CursorX + p[0] - 1); break;
            case 'S': p = Params(1, 1); ScrollUp(p[0]); break;
            case 'T': p = Params(1, 1); ScrollDown(p[0]); break;
            case 'r':
                p = Params(0, 0);
                top = p[0] > 0 ? p[0] - 1 : 0;
                bottom = p.Length > 1 && p[1] > 0 ? Math.Min(Rows - 1, p[1] - 1) : Rows - 1;
                if (top >= bottom) { top = 0; bottom = Rows - 1; }
                CursorX = 0; CursorY = 0;
                break;
            case 's': SaveCursor(); break;
            case 'u': RestoreCursor(); break;
            case 'm': Sgr(); break;
            case 'n':
                p = Params(0, 0);
                if (Respond == null) break;
                if (p[0] == 6) Respond("\x1b[" + (CursorY + 1) + ";" + (CursorX + 1) + "R");
                else if (p[0] == 5) Respond("\x1b[0n");
                break;
            case 'c': if (Respond != null) Respond("\x1b[?1;2c"); break;
        }
    }

    private void PrivateMode(bool on)
    {
        foreach (var part in param.ToString().Split(';'))
        {
            int mode;
            if (!int.TryParse(part, out mode)) continue;
            switch (mode)
            {
                case 1: AppCursor = on; break;
                case 7: AutoWrap = on; break;
                case 25: CursorVisible = on; break;
                case 1004: FocusReporting = on; break;
                case 2004: BracketedPaste = on; break;
                case 47: case 1047: case 1049:
                    if (on && !AltScreen)
                    {
                        if (mode == 1049) SaveCursor();
                        alt = NewBuffer(Rows, Cols);
                        screen = alt;
                        AltScreen = true;
                    }
                    else if (!on && AltScreen)
                    {
                        screen = main;
                        AltScreen = false;
                        if (mode == 1049) RestoreCursor();
                    }
                    top = 0; bottom = Rows - 1;
                    break;
            }
        }
    }

    private void Sgr()
    {
        var parts = param.ToString().Split(';');
        if (param.Length == 0) parts = new[] { "0" };
        for (int i = 0; i < parts.Length; i++)
        {
            int v;
            if (!int.TryParse(parts[i].Split(':')[0], out v)) v = 0;
            if (v == 0) { fg = bg = -1; attr = 0; }
            else if (v == 1) attr |= Bold;
            else if (v == 2) attr |= Dim;
            else if (v == 3) attr |= Italic;
            else if (v == 4) attr |= Underline;
            else if (v == 7) attr |= Inverse;
            else if (v == 22) attr &= unchecked((byte)~(Bold | Dim));
            else if (v == 23) attr &= unchecked((byte)~Italic);
            else if (v == 24) attr &= unchecked((byte)~Underline);
            else if (v == 27) attr &= unchecked((byte)~Inverse);
            else if (v >= 30 && v <= 37) fg = v - 30;
            else if (v >= 90 && v <= 97) fg = v - 90 + 8;
            else if (v == 39) fg = -1;
            else if (v >= 40 && v <= 47) bg = v - 40;
            else if (v >= 100 && v <= 107) bg = v - 100 + 8;
            else if (v == 49) bg = -1;
            else if (v == 38 || v == 48)
            {
                int color = -1;
                int mode;
                if (i + 1 < parts.Length && int.TryParse(parts[i + 1], out mode))
                {
                    if (mode == 5 && i + 2 < parts.Length) { int.TryParse(parts[i + 2], out color); i += 2; }
                    else if (mode == 2 && i + 4 < parts.Length)
                    {
                        int r, g, b;
                        int.TryParse(parts[i + 2], out r); int.TryParse(parts[i + 3], out g); int.TryParse(parts[i + 4], out b);
                        color = 0x1000000 | (r << 16) | (g << 8) | b;
                        i += 4;
                    }
                }
                if (v == 38) fg = color; else bg = color;
            }
        }
    }
}
}
