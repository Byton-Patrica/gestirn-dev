using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace GestirnDev
{
internal struct TextPos : IComparable<TextPos>, IEquatable<TextPos>
{
    public int Line, Col;
    public TextPos(int line, int col) { Line = line; Col = col; }
    public int CompareTo(TextPos o) { return Line != o.Line ? Line.CompareTo(o.Line) : Col.CompareTo(o.Col); }
    public bool Equals(TextPos o) { return Line == o.Line && Col == o.Col; }
    public override bool Equals(object obj) { return obj is TextPos && Equals((TextPos)obj); }
    public override int GetHashCode() { return Line * 397 ^ Col; }
    public static bool operator ==(TextPos a, TextPos b) { return a.Equals(b); }
    public static bool operator !=(TextPos a, TextPos b) { return !a.Equals(b); }
    public static bool operator <(TextPos a, TextPos b) { return a.CompareTo(b) < 0; }
    public static bool operator >(TextPos a, TextPos b) { return a.CompareTo(b) > 0; }
    public static bool operator <=(TextPos a, TextPos b) { return a.CompareTo(b) <= 0; }
    public static bool operator >=(TextPos a, TextPos b) { return a.CompareTo(b) >= 0; }
    public static readonly TextPos None = new TextPos(-1, -1);
}

internal enum Severity { Error, Warning, Note }

internal sealed class Diagnostic
{
    public string File;
    public int Line;      // 0-based
    public int Col;       // 1-based column from the compiler, 0 when unknown
    public Severity Severity;
    public string Message, Hint;
}

// A self-drawn code editor: text model, undo, syntax colouring, IME, completion and markers.
internal sealed class CodeEditor : Control
{
    private readonly List<string> lines = new List<string> { "" };
    private TextPos caret, anchor;
    private int desiredCell = -1;

    private Font font, cjkFont;
    private float cellW;
    private int lineH, textH, textOffsetY, gutterW, textLeft;
    private readonly AnimatedFloat scrollY = new AnimatedFloat(0f);
    private readonly AnimatedFloat scrollX = new AnimatedFloat(0f);
    private readonly AnimatedFloat barAlpha = new AnimatedFloat(0f);
    private readonly Timer barFade = new Timer { Interval = 1100 };

    private readonly List<int> lineStates = new List<int> { 0 };
    private readonly List<Token> tokenBuffer = new List<Token>();
    private readonly List<Token> scratchTokens = new List<Token>();

    private sealed class UndoStep { public TextPos Start; public string Removed, Inserted; }
    private sealed class UndoEntry
    {
        public readonly List<UndoStep> Steps = new List<UndoStep>();
        public TextPos CaretBefore, AnchorBefore, CaretAfter, AnchorAfter;
        public int Kind;
        public double Time;
    }
    private const int MergeNone = 0, MergeTyping = 1, MergeDeleting = 2;
    private readonly List<UndoEntry> undo = new List<UndoEntry>();
    private readonly List<UndoEntry> redo = new List<UndoEntry>();
    private UndoEntry current, savePoint, noMerge;
    private bool currentIsNew;
    private int depth;
    private int version;

    private readonly HashSet<int> breakpoints = new HashSet<int>();
    private readonly Dictionary<int, int> bookmarks = new Dictionary<int, int>();
    private readonly AnimatedFloat glideX = new AnimatedFloat(0f), glideY = new AnimatedFloat(0f);
    private float glideTargetX = -1, glideTargetY = -1;
    private readonly List<Diagnostic> diagnostics = new List<Diagnostic>();
    private int execLine = -1;
    private readonly AnimatedFloat execArrive = new AnimatedFloat(1f);
    private Action<double> execTicker;
    private double execFrame;

    private string searchTerm = "";
    private bool searchCase, searchWord;

    private TextPos bracketA = TextPos.None, bracketB = TextPos.None;

    private readonly CompletionPopup popup = new CompletionPopup();
    private TextPos popupStart;
    private CompletionContext popupContext;
    private HashSet<string> wordCache;
    private int wordCacheVersion = -1;
    private bool usesStd;

    private double blinkStart, lastActivity;
    private readonly Action<double> blinkTicker;
    private bool blinking;
    private float caretAlpha = 1f;

    private bool dragging, thumbDrag;
    private float thumbGrab;
    private int clickCount;
    private double lastClickTime;
    private Point lastClickPoint;
    private readonly Action<double> dragTicker;

    private Diagnostic hoverDiag, hoverCandidate;
    private Point hoverPoint;
    private readonly Timer hoverTimer = new Timer { Interval = 380 };
    private string lastLineCopy;
    private int longestCache = -1, longestVersion = -1;

    public int TabSize = 4;
    public bool InsertSpaces = true, AutoPairs = true, AutoComplete = true;
    public CompletionProvider Provider;

    public event EventHandler TextChangedEx;
    public event EventHandler CaretMoved;
    public event EventHandler ZoomChanged;
    public event Action<int> BreakpointToggled;
    public event EventHandler EscapePressed;
    public event Action<string> DefinitionRequested;

    public CodeEditor()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.EditorBg;
        Cursor = Cursors.IBeam;
        ImeMode = ImeMode.On;
        Controls.Add(popup);
        popup.Accepted += AcceptCompletion;
        scrollY.Changed += () => { Invalidate(); };
        scrollX.Changed += () => { Invalidate(); };
        barAlpha.Changed += () => Invalidate();
        barFade.Tick += (_, _) => { barFade.Stop(); if (!thumbDrag) barAlpha.To(0f, 400); };
        hoverTimer.Tick += (_, _) => { hoverTimer.Stop(); hoverDiag = hoverCandidate; Invalidate(); };
        blinkTicker = now =>
        {
            if (!Focused) return;
            double idle = now - lastActivity;
            float a = idle > 12000 ? 1f : (float)(0.5 + 0.5 * Math.Cos((now - blinkStart) / 1100.0 * Math.PI * 2));
            a = Math.Max(0f, Math.Min(1f, (a - 0.15f) / 0.7f));
            if (Math.Abs(a - caretAlpha) > 0.04f) { caretAlpha = a; InvalidateCaret(); }
            if (idle > 12000 && caretAlpha >= 1f) StopBlink();
        };
        dragTicker = now => AutoScrollWhileDragging();
        glideX.Changed += Invalidate;
        glideY.Changed += Invalidate;
        SetCodeFont(Theme.CodeFamily, Theme.Code.SizeInPoints);
    }

    protected override bool CanEnableIme { get { return true; } }

    // ------------------------------------------------------------------ text model

    public int LineCount { get { return lines.Count; } }
    public string LineText(int index) { return lines[index]; }
    public int Version { get { return version; } }
    public TextPos Caret { get { return caret; } }
    public TextPos SelStart { get { return caret < anchor ? caret : anchor; } }
    public TextPos SelEnd { get { return caret < anchor ? anchor : caret; } }
    public bool HasSelection { get { return caret != anchor; } }
    public string SelectedText { get { return HasSelection ? GetRange(SelStart, SelEnd) : ""; } }
    public bool IsModified { get { return TopEntry() != savePoint; } }
    public bool CanUndo { get { return undo.Count > 0; } }
    public bool CanRedo { get { return redo.Count > 0; } }
    public float FontSizePt { get { return font.SizeInPoints; } }
    public string IndentUnit { get { return InsertSpaces ? new string(' ', TabSize) : "\t"; } }

    public override string Text
    {
        get { return string.Join("\n", lines); }
        set { LoadText(value ?? ""); }
    }

    public string GetText(string newline) { return string.Join(newline, lines); }

    public void LoadText(string text)
    {
        lines.Clear();
        lines.AddRange(Normalize(text).Split('\n'));
        undo.Clear(); redo.Clear(); current = null; depth = 0;
        savePoint = noMerge = null;
        caret = anchor = new TextPos(0, 0);
        lineStates.Clear(); lineStates.Add(0);
        breakpoints.Clear(); diagnostics.Clear(); execLine = -1;
        version++;
        scrollY.Snap(0); scrollX.Snap(0);
        UpdateGutter();
        Invalidate();
        if (TextChangedEx != null) TextChangedEx(this, EventArgs.Empty);
        if (CaretMoved != null) CaretMoved(this, EventArgs.Empty);
    }

    // Replaces the whole buffer as a single undoable step (used by formatting and reloads).
    public void ReplaceAllText(string text)
    {
        int line = caret.Line, col = caret.Col;
        Begin(MergeNone);
        try
        {
            Replace(new TextPos(0, 0), new TextPos(lines.Count - 1, lines[lines.Count - 1].Length), Normalize(text));
            caret = anchor = Clamp(new TextPos(line, col));
        }
        finally { End(); }
    }

    public void MarkSaved() { savePoint = TopEntry(); noMerge = savePoint; }

    private UndoEntry TopEntry() { return undo.Count > 0 ? undo[undo.Count - 1] : null; }

    private static string Normalize(string text) { return text.Replace("\r\n", "\n").Replace('\r', '\n'); }

    public string GetRange(TextPos a, TextPos b)
    {
        if (b < a) { var t = a; a = b; b = t; }
        if (a.Line == b.Line) return lines[a.Line].Substring(a.Col, b.Col - a.Col);
        var sb = new StringBuilder();
        sb.Append(lines[a.Line], a.Col, lines[a.Line].Length - a.Col);
        for (int l = a.Line + 1; l < b.Line; l++) sb.Append('\n').Append(lines[l]);
        sb.Append('\n').Append(lines[b.Line], 0, b.Col);
        return sb.ToString();
    }

    private TextPos Clamp(TextPos p)
    {
        int line = Math.Max(0, Math.Min(lines.Count - 1, p.Line));
        int col = Math.Max(0, Math.Min(lines[line].Length, p.Col));
        if (col > 0 && col < lines[line].Length && char.IsLowSurrogate(lines[line][col]) && char.IsHighSurrogate(lines[line][col - 1])) col--;
        return new TextPos(line, col);
    }

    private static TextPos EndOf(TextPos start, string text)
    {
        int breaks = 0, last = -1;
        for (int i = 0; i < text.Length; i++) if (text[i] == '\n') { breaks++; last = i; }
        return breaks == 0 ? new TextPos(start.Line, start.Col + text.Length) : new TextPos(start.Line + breaks, text.Length - last - 1);
    }

    private TextPos RawReplace(TextPos a, TextPos b, string text)
    {
        string head = lines[a.Line].Substring(0, a.Col);
        string tail = lines[b.Line].Substring(b.Col);
        int removedBreaks = b.Line - a.Line;
        if (removedBreaks > 0) lines.RemoveRange(a.Line + 1, removedBreaks);
        TextPos end;
        if (text.IndexOf('\n') < 0)
        {
            lines[a.Line] = head + text + tail;
            end = new TextPos(a.Line, a.Col + text.Length);
        }
        else
        {
            var parts = text.Split('\n');
            lines[a.Line] = head + parts[0];
            var insert = new List<string>(parts.Length - 1);
            for (int i = 1; i < parts.Length - 1; i++) insert.Add(parts[i]);
            insert.Add(parts[parts.Length - 1] + tail);
            lines.InsertRange(a.Line + 1, insert);
            end = new TextPos(a.Line + parts.Length - 1, parts[parts.Length - 1].Length);
        }
        ShiftMarkers(a, removedBreaks, (end.Line - a.Line) - removedBreaks);
        version++;
        return end;
    }

    private void ShiftMarkers(TextPos at, int removedBreaks, int delta)
    {
        if (lineStates.Count > at.Line + 1) lineStates.RemoveRange(at.Line + 1, lineStates.Count - at.Line - 1);
        if (delta == 0 && removedBreaks == 0) return;
        Func<int, int> map = line =>
        {
            if (line < at.Line || (line == at.Line && !(at.Col == 0 && delta > 0 && removedBreaks == 0))) return line;
            if (line == at.Line) return line + delta;
            if (line <= at.Line + removedBreaks) return at.Line;
            return line + delta;
        };
        var moved = breakpoints.Select(map).Where(l => l >= 0 && l < lines.Count).ToList();
        breakpoints.Clear();
        foreach (var l in moved) breakpoints.Add(l);
        foreach (var d in diagnostics) d.Line = map(d.Line);
        if (execLine >= 0) execLine = map(execLine);
        foreach (var k in bookmarks.Keys.ToList()) bookmarks[k] = Math.Max(0, Math.Min(lines.Count - 1, map(bookmarks[k])));
        UpdateGutter();
    }

    // ------------------------------------------------------------------ undo groups

    private void Begin(int kind)
    {
        if (depth++ > 0) return;
        double now = Animator.Now;
        var top = TopEntry();
        if (kind != MergeNone && top != null && top.Kind == kind && top != noMerge && now - top.Time < 1500 &&
            top.CaretAfter == caret && top.AnchorAfter == anchor && redo.Count == 0)
        {
            current = top;
            currentIsNew = false;
        }
        else
        {
            current = new UndoEntry { CaretBefore = caret, AnchorBefore = anchor, Kind = kind };
            currentIsNew = true;
        }
        current.Time = now;
    }

    private void End()
    {
        if (--depth > 0) return;
        depth = 0;
        var entry = current;
        current = null;
        if (entry == null || entry.Steps.Count == 0) { AfterCaretChange(); return; }
        if (currentIsNew)
        {
            undo.Add(entry);
            if (undo.Count > 3000) undo.RemoveAt(0);
            redo.Clear();
        }
        entry.CaretAfter = caret;
        entry.AnchorAfter = anchor;
        AfterTextChange();
    }

    private TextPos Replace(TextPos a, TextPos b, string text)
    {
        if (b < a) { var t = a; a = b; b = t; }
        string removed = GetRange(a, b);
        if (removed.Length == 0 && text.Length == 0) return a;
        var end = RawReplace(a, b, text);
        if (current != null) current.Steps.Add(new UndoStep { Start = a, Removed = removed, Inserted = text });
        return end;
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        HidePopup();
        var entry = undo[undo.Count - 1];
        undo.RemoveAt(undo.Count - 1);
        for (int i = entry.Steps.Count - 1; i >= 0; i--)
        {
            var s = entry.Steps[i];
            RawReplace(s.Start, EndOf(s.Start, s.Inserted), s.Removed);
        }
        redo.Add(entry);
        caret = Clamp(entry.CaretBefore);
        anchor = Clamp(entry.AnchorBefore);
        noMerge = TopEntry();
        AfterTextChange();
    }

    public void Redo()
    {
        if (redo.Count == 0) return;
        HidePopup();
        var entry = redo[redo.Count - 1];
        redo.RemoveAt(redo.Count - 1);
        foreach (var s in entry.Steps) RawReplace(s.Start, EndOf(s.Start, s.Removed), s.Inserted);
        undo.Add(entry);
        caret = Clamp(entry.CaretAfter);
        anchor = Clamp(entry.AnchorAfter);
        noMerge = entry;
        AfterTextChange();
    }

    private void AfterTextChange()
    {
        UpdateGutter();
        AfterCaretChange();
        if (TextChangedEx != null) TextChangedEx(this, EventArgs.Empty);
    }

    private void AfterCaretChange()
    {
        desiredCell = desiredCellKeep ? desiredCell : -1;
        EnsureCaretVisible(false);
        UpdateBracketMatch();
        Touch();
        Invalidate();
        if (CaretMoved != null) CaretMoved(this, EventArgs.Empty);
    }

    private bool desiredCellKeep;

    // ------------------------------------------------------------------ caret

    private void SetCaret(TextPos p, bool extend, bool keepDesired = false)
    {
        caret = Clamp(p);
        if (!extend) anchor = caret;
        desiredCellKeep = keepDesired;
        AfterCaretChange();
        desiredCellKeep = false;
    }

    public void Select(TextPos a, TextPos b)
    {
        anchor = Clamp(a);
        caret = Clamp(b);
        AfterCaretChange();
    }

    public void SelectAll() { Select(new TextPos(0, 0), new TextPos(lines.Count - 1, lines[lines.Count - 1].Length)); }

    public void GoTo(int line, int col, bool selectLine = false)
    {
        HidePopup();
        line = Math.Max(0, Math.Min(lines.Count - 1, line));
        if (selectLine) { anchor = new TextPos(line, 0); caret = new TextPos(line, lines[line].Length); }
        else caret = anchor = Clamp(new TextPos(line, col));
        UpdateBracketMatch();
        CenterOn(line, true);
        EnsureCaretVisibleX();
        Touch();
        Invalidate();
        if (CaretMoved != null) CaretMoved(this, EventArgs.Empty);
    }

    private TextPos StepLeft(TextPos p)
    {
        if (p.Col > 0)
        {
            int c = p.Col - 1;
            string s = lines[p.Line];
            if (c > 0 && char.IsLowSurrogate(s[c]) && char.IsHighSurrogate(s[c - 1])) c--;
            return new TextPos(p.Line, c);
        }
        return p.Line > 0 ? new TextPos(p.Line - 1, lines[p.Line - 1].Length) : p;
    }

    private TextPos StepRight(TextPos p)
    {
        string s = lines[p.Line];
        if (p.Col < s.Length)
        {
            int c = p.Col + 1;
            if (c < s.Length && char.IsLowSurrogate(s[c]) && char.IsHighSurrogate(s[c - 1])) c++;
            return new TextPos(p.Line, c);
        }
        return p.Line < lines.Count - 1 ? new TextPos(p.Line + 1, 0) : p;
    }

    private static int CharClass(char c) { return char.IsWhiteSpace(c) ? 0 : CppSyntax.IsIdentChar(c) ? 1 : 2; }

    private TextPos WordLeft(TextPos p)
    {
        if (p.Col == 0) return StepLeft(p);
        string s = lines[p.Line];
        int i = p.Col;
        while (i > 0 && CharClass(s[i - 1]) == 0) i--;
        if (i == 0) return new TextPos(p.Line, 0);
        int cls = CharClass(s[i - 1]);
        while (i > 0 && CharClass(s[i - 1]) == cls) i--;
        return new TextPos(p.Line, i);
    }

    private TextPos WordRight(TextPos p)
    {
        string s = lines[p.Line];
        if (p.Col >= s.Length) return StepRight(p);
        int i = p.Col;
        while (i < s.Length && CharClass(s[i]) == 0) i++;
        if (i >= s.Length) return new TextPos(p.Line, s.Length);
        int cls = CharClass(s[i]);
        while (i < s.Length && CharClass(s[i]) == cls) i++;
        return new TextPos(p.Line, i);
    }

    private void MoveVertical(int delta, bool extend)
    {
        if (desiredCell < 0) desiredCell = CellOf(caret.Line, caret.Col);
        int line = Math.Max(0, Math.Min(lines.Count - 1, caret.Line + delta));
        TextPos target;
        if (line == caret.Line && delta < 0) target = new TextPos(0, 0);
        else if (line == caret.Line && delta > 0) target = new TextPos(line, lines[line].Length);
        else target = new TextPos(line, ColAtCell(line, desiredCell, false));
        int keep = desiredCell;
        SetCaret(target, extend, true);
        desiredCell = keep;
    }

    private void SmartHome(bool extend)
    {
        string s = lines[caret.Line];
        int first = 0;
        while (first < s.Length && (s[first] == ' ' || s[first] == '\t')) first++;
        SetCaret(new TextPos(caret.Line, caret.Col == first ? 0 : first), extend);
    }

    // ------------------------------------------------------------------ cells (tabs, CJK, surrogates)

    private static bool IsWide(char c)
    {
        return c >= 0x1100 && (c <= 0x115F || (c >= 0x2E80 && c <= 0xA4CF) || (c >= 0xAC00 && c <= 0xD7A3) ||
               (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE4F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6));
    }

    private int CharCells(char c, int cell)
    {
        if (c == '\t') return TabSize - cell % TabSize;
        if (char.IsLowSurrogate(c)) return 0;
        if (char.IsHighSurrogate(c) || IsWide(c)) return 2;
        return 1;
    }

    private int CellOf(int line, int col)
    {
        string s = lines[line];
        int cell = 0;
        for (int i = 0; i < col && i < s.Length; i++) cell += CharCells(s[i], cell);
        return cell;
    }

    private int ColAtCell(int line, float target, bool nearest)
    {
        string s = lines[line];
        int cell = 0;
        for (int i = 0; i < s.Length; i++)
        {
            int w = CharCells(s[i], cell);
            if (w == 0) continue;
            if (nearest ? target < cell + w / 2f : target < cell + w / 2f) return i;
            cell += w;
        }
        return s.Length;
    }

    private int LongestCells()
    {
        if (longestVersion == version) return longestCache;
        int best = 0;
        int limit = Math.Min(lines.Count, 20000);
        for (int i = 0; i < limit; i++)
        {
            if (lines[i].Length + lines[i].Length < best) continue;
            best = Math.Max(best, CellOf(i, lines[i].Length));
        }
        longestCache = best;
        longestVersion = version;
        return best;
    }

    // ------------------------------------------------------------------ editing commands

    public void InsertText(string text)
    {
        Begin(MergeNone);
        try
        {
            DeleteSelectionInternal();
            var end = Replace(caret, caret, Normalize(text));
            caret = anchor = end;
        }
        finally { End(); }
    }

    private void DeleteSelectionInternal()
    {
        if (!HasSelection) return;
        var a = SelStart;
        Replace(a, SelEnd, "");
        caret = anchor = a;
    }

    private static string CloserFor(char c)
    {
        switch (c) { case '(': return ")"; case '[': return "]"; case '{': return "}"; case '"': return "\""; case '\'': return "'"; }
        return null;
    }

    public void TypeChar(char c)
    {
        Begin(MergeTyping);
        try
        {
            string closer = CloserFor(c);
            string line = lines[caret.Line];
            char next = caret.Col < line.Length ? line[caret.Col] : '\0';
            if (HasSelection && AutoPairs && closer != null && SelStart.Line == SelEnd.Line)
            {
                var a = SelStart; var b = SelEnd;
                string inner = GetRange(a, b);
                Replace(a, b, c + inner + closer);
                anchor = new TextPos(a.Line, a.Col + 1);
                caret = new TextPos(a.Line, a.Col + 1 + inner.Length);
            }
            else if (!HasSelection && AutoPairs && (c == ')' || c == ']' || c == '}' || c == '"' || c == '\'') && next == c)
            {
                caret = anchor = new TextPos(caret.Line, caret.Col + 1);
            }
            else
            {
                DeleteSelectionInternal();
                line = lines[caret.Line];
                next = caret.Col < line.Length ? line[caret.Col] : '\0';
                char prev = caret.Col > 0 ? line[caret.Col - 1] : '\0';
                if (c == '}' && line.Substring(0, caret.Col).Trim().Length == 0)
                {
                    string indent = IndentOfMatchingOpen(caret);
                    if (indent != null)
                    {
                        Replace(new TextPos(caret.Line, 0), caret, indent);
                        caret = anchor = new TextPos(caret.Line, indent.Length);
                    }
                }
                bool pair = AutoPairs && closer != null && ShouldAutoClose(c, prev, next);
                var end = Replace(caret, caret, pair ? c + closer : c.ToString());
                caret = anchor = pair ? new TextPos(end.Line, end.Col - 1) : end;
            }
        }
        finally { End(); }
        UpdateCompletionAfterTyping(c);
    }

    private bool ShouldAutoClose(char c, char prev, char next)
    {
        bool nextOk = next == '\0' || char.IsWhiteSpace(next) || ")]};,".IndexOf(next) >= 0;
        if (c == '(' || c == '[' || c == '{') return nextOk;
        if (CppSyntax.IsIdentChar(prev) || prev == '\\' || prev == c) return false;
        if (InCommentOrString(caret)) return false;
        return nextOk;
    }

    private string IndentOfMatchingOpen(TextPos from)
    {
        int balance = 0;
        for (int l = from.Line; l >= 0 && l > from.Line - 3000; l--)
        {
            string s = lines[l];
            int start = l == from.Line ? from.Col - 1 : s.Length - 1;
            for (int i = start; i >= 0; i--)
            {
                if (s[i] == '}') balance++;
                else if (s[i] == '{')
                {
                    if (balance == 0) return LeadingWhitespace(s);
                    balance--;
                }
            }
        }
        return null;
    }

    private static string LeadingWhitespace(string s)
    {
        int i = 0;
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
        return s.Substring(0, i);
    }

    public void NewLine()
    {
        HidePopup();
        Begin(MergeNone);
        try
        {
            DeleteSelectionInternal();
            string line = lines[caret.Line];
            string before = line.Substring(0, caret.Col), after = line.Substring(caret.Col);
            string ws = LeadingWhitespace(line);
            string indent = before.Length < ws.Length ? before : ws;
            string tb = before.TrimEnd();
            char last = tb.Length > 0 ? tb[tb.Length - 1] : '\0';
            string ta = after.TrimStart();
            char first = ta.Length > 0 ? ta[0] : '\0';
            bool open = last == '{' || last == '(' || last == '[' ||
                        (last == ':' && Regex.IsMatch(tb, @"^\s*(case\b.*|default|public|private|protected)\s*:$"));
            string closer = last == '{' ? "}" : last == '(' ? ")" : last == '[' ? "]" : null;
            var from = tb.Length == 0 ? new TextPos(caret.Line, 0) : caret;
            var to = new TextPos(caret.Line, caret.Col + (after.Length - ta.Length));
            string unit = IndentUnit;
            if (open && closer != null && first == closer[0])
            {
                Replace(from, to, "\n" + indent + unit + "\n" + indent);
                caret = anchor = new TextPos(from.Line + 1, indent.Length + unit.Length);
            }
            else
            {
                var end = Replace(from, to, "\n" + indent + (open ? unit : ""));
                caret = anchor = end;
            }
        }
        finally { End(); }
    }

    public void Backspace(bool word)
    {
        Begin(MergeDeleting);
        try
        {
            if (HasSelection) { DeleteSelectionInternal(); return; }
            if (caret.Col == 0)
            {
                if (caret.Line == 0) return;
                var p = new TextPos(caret.Line - 1, lines[caret.Line - 1].Length);
                Replace(p, caret, "");
                caret = anchor = p;
                return;
            }
            if (word)
            {
                var p = WordLeft(caret);
                Replace(p, caret, "");
                caret = anchor = p;
                return;
            }
            string s = lines[caret.Line];
            int col = caret.Col;
            char prev = s[col - 1];
            char next = col < s.Length ? s[col] : '\0';
            string closer = CloserFor(prev);
            if (AutoPairs && closer != null && next == closer[0])
            {
                Replace(new TextPos(caret.Line, col - 1), new TextPos(caret.Line, col + 1), "");
                caret = anchor = new TextPos(caret.Line, col - 1);
            }
            else if (InsertSpaces && prev == ' ' && s.Substring(0, col).Trim().Length == 0)
            {
                int cells = CellOf(caret.Line, col);
                int target = ((cells - 1) / TabSize) * TabSize;
                int remove = 0;
                while (remove < col && cells - remove > target && s[col - 1 - remove] == ' ') remove++;
                Replace(new TextPos(caret.Line, col - remove), caret, "");
                caret = anchor = new TextPos(caret.Line, col - remove);
            }
            else
            {
                var p = StepLeft(caret);
                Replace(p, caret, "");
                caret = anchor = p;
            }
        }
        finally { End(); }
        if (popup.Visible) RefreshCompletion(false);
    }

    public void DeleteForward(bool word)
    {
        Begin(MergeDeleting);
        try
        {
            if (HasSelection) { DeleteSelectionInternal(); return; }
            var p = word ? WordRight(caret) : StepRight(caret);
            Replace(caret, p, "");
        }
        finally { End(); }
    }

    public void Tab(bool shift)
    {
        if (popup.Visible && !shift) { AcceptCompletion(popup.Selected); return; }
        if (shift || (HasSelection && SelStart.Line != SelEnd.Line)) { IndentLines(shift ? -1 : 1); return; }
        Begin(MergeTyping);
        try
        {
            DeleteSelectionInternal();
            string text = InsertSpaces ? new string(' ', TabSize - CellOf(caret.Line, caret.Col) % TabSize) : "\t";
            caret = anchor = Replace(caret, caret, text);
        }
        finally { End(); }
    }

    private void LineRange(out int first, out int last)
    {
        first = SelStart.Line;
        last = SelEnd.Line;
        if (last > first && SelEnd.Col == 0) last--;
    }

    public void IndentLines(int direction)
    {
        int first, last;
        LineRange(out first, out last);
        Begin(MergeNone);
        try
        {
            var shift = new Dictionary<int, int>();
            for (int l = first; l <= last; l++)
            {
                string s = lines[l];
                if (direction > 0)
                {
                    if (s.Trim().Length == 0) continue;
                    Replace(new TextPos(l, 0), new TextPos(l, 0), IndentUnit);
                    shift[l] = IndentUnit.Length;
                }
                else
                {
                    int remove = 0;
                    if (s.StartsWith("\t")) remove = 1;
                    else while (remove < TabSize && remove < s.Length && s[remove] == ' ') remove++;
                    if (remove == 0) continue;
                    Replace(new TextPos(l, 0), new TextPos(l, remove), "");
                    shift[l] = -remove;
                }
            }
            int d;
            if (shift.TryGetValue(caret.Line, out d)) caret = Clamp(new TextPos(caret.Line, Math.Max(0, caret.Col + d)));
            if (shift.TryGetValue(anchor.Line, out d)) anchor = Clamp(new TextPos(anchor.Line, Math.Max(0, anchor.Col + d)));
        }
        finally { End(); }
    }

    public void ToggleComment()
    {
        int first, last;
        LineRange(out first, out last);
        var nonBlank = Enumerable.Range(first, last - first + 1).Where(l => lines[l].Trim().Length > 0).ToList();
        if (nonBlank.Count == 0) return;
        bool allCommented = nonBlank.All(l => lines[l].TrimStart().StartsWith("//"));
        Begin(MergeNone);
        try
        {
            var shift = new Dictionary<int, Tuple<int, int>>();
            if (allCommented)
            {
                foreach (int l in nonBlank)
                {
                    string s = lines[l];
                    int idx = s.IndexOf("//", StringComparison.Ordinal);
                    int len = 2 + (s.Length > idx + 2 && s[idx + 2] == ' ' ? 1 : 0);
                    Replace(new TextPos(l, idx), new TextPos(l, idx + len), "");
                    shift[l] = Tuple.Create(idx, -len);
                }
            }
            else
            {
                int indent = nonBlank.Min(l => LeadingWhitespace(lines[l]).Length);
                foreach (int l in nonBlank)
                {
                    Replace(new TextPos(l, indent), new TextPos(l, indent), "// ");
                    shift[l] = Tuple.Create(indent, 3);
                }
            }
            Tuple<int, int> sh;
            if (shift.TryGetValue(caret.Line, out sh) && caret.Col >= sh.Item1) caret = Clamp(new TextPos(caret.Line, Math.Max(sh.Item1, caret.Col + sh.Item2)));
            if (shift.TryGetValue(anchor.Line, out sh) && anchor.Col >= sh.Item1) anchor = Clamp(new TextPos(anchor.Line, Math.Max(sh.Item1, anchor.Col + sh.Item2)));
        }
        finally { End(); }
    }

    public void DuplicateLine()
    {
        Begin(MergeNone);
        try
        {
            if (!HasSelection)
            {
                string s = lines[caret.Line];
                Replace(new TextPos(caret.Line, s.Length), new TextPos(caret.Line, s.Length), "\n" + s);
                caret = anchor = new TextPos(caret.Line + 1, caret.Col);
            }
            else
            {
                string text = SelectedText;
                var end = SelEnd;
                Replace(end, end, text);
                anchor = end;
                caret = EndOf(end, text);
            }
        }
        finally { End(); }
    }

    public void MoveLines(int direction)
    {
        int first, last;
        LineRange(out first, out last);
        if (direction < 0 && first == 0) return;
        if (direction > 0 && last >= lines.Count - 1) return;
        Begin(MergeNone);
        try
        {
            string block = GetRange(new TextPos(first, 0), new TextPos(last, lines[last].Length));
            if (direction < 0)
            {
                string above = lines[first - 1];
                Replace(new TextPos(first - 1, 0), new TextPos(last, lines[last].Length), block + "\n" + above);
            }
            else
            {
                string below = lines[last + 1];
                Replace(new TextPos(first, 0), new TextPos(last + 1, lines[last + 1].Length), below + "\n" + block);
            }
            caret = Clamp(new TextPos(caret.Line + direction, caret.Col));
            anchor = Clamp(new TextPos(anchor.Line + direction, anchor.Col));
        }
        finally { End(); }
    }

    public void DeleteLines()
    {
        int first, last;
        LineRange(out first, out last);
        Begin(MergeNone);
        try
        {
            if (last < lines.Count - 1) Replace(new TextPos(first, 0), new TextPos(last + 1, 0), "");
            else if (first > 0) Replace(new TextPos(first - 1, lines[first - 1].Length), new TextPos(last, lines[last].Length), "");
            else Replace(new TextPos(0, 0), new TextPos(last, lines[last].Length), "");
            caret = anchor = Clamp(new TextPos(Math.Min(first, lines.Count - 1), caret.Col));
        }
        finally { End(); }
    }

    public void InsertLineBelow()
    {
        Begin(MergeNone);
        try
        {
            string s = lines[caret.Line];
            string indent = LeadingWhitespace(s);
            var end = Replace(new TextPos(caret.Line, s.Length), new TextPos(caret.Line, s.Length), "\n" + indent);
            caret = anchor = end;
        }
        finally { End(); }
    }

    public void InsertLineAbove()
    {
        Begin(MergeNone);
        try
        {
            string indent = LeadingWhitespace(lines[caret.Line]);
            Replace(new TextPos(caret.Line, 0), new TextPos(caret.Line, 0), indent + "\n");
            caret = anchor = new TextPos(caret.Line, indent.Length);
        }
        finally { End(); }
    }

    public void SelectLine(int line)
    {
        line = Math.Max(0, Math.Min(lines.Count - 1, line));
        anchor = new TextPos(line, 0);
        caret = line < lines.Count - 1 ? new TextPos(line + 1, 0) : new TextPos(line, lines[line].Length);
        AfterCaretChange();
    }

    private void SelectWordAt(TextPos p)
    {
        string s = lines[p.Line];
        if (s.Length == 0) { SetCaret(p, false); return; }
        int i = Math.Min(p.Col, s.Length - 1);
        if (p.Col == s.Length || (p.Col > 0 && CharClass(s[i]) == 0 && CharClass(s[p.Col - 1]) != 0)) i = Math.Max(0, p.Col - 1);
        int cls = CharClass(s[i]);
        int a = i, b = i + 1;
        while (a > 0 && CharClass(s[a - 1]) == cls) a--;
        while (b < s.Length && CharClass(s[b]) == cls) b++;
        anchor = new TextPos(p.Line, a);
        caret = new TextPos(p.Line, b);
        AfterCaretChange();
    }

    // ------------------------------------------------------------------ clipboard

    public void Copy()
    {
        string text = HasSelection ? SelectedText : lines[caret.Line] + "\n";
        lastLineCopy = HasSelection ? null : text;
        try { Clipboard.SetDataObject(text.Replace("\n", "\r\n"), true, 5, 60); }
        catch (ExternalException) { }
    }

    public void Cut()
    {
        Copy();
        if (HasSelection)
        {
            Begin(MergeNone);
            try { DeleteSelectionInternal(); }
            finally { End(); }
        }
        else DeleteLines();
    }

    public void Paste()
    {
        string text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); }
        catch (ExternalException) { }
        if (string.IsNullOrEmpty(text)) return;
        text = Normalize(text);
        if (!HasSelection && lastLineCopy != null && text == lastLineCopy)
        {
            Begin(MergeNone);
            try
            {
                var p = new TextPos(caret.Line, 0);
                Replace(p, p, text);
                caret = anchor = new TextPos(caret.Line + 1, caret.Col);
            }
            finally { End(); }
            return;
        }
        InsertText(text);
    }

    // ------------------------------------------------------------------ search

    public void SetSearch(string term, bool matchCase, bool wholeWord)
    {
        searchTerm = term ?? "";
        searchCase = matchCase;
        searchWord = wholeWord;
        Invalidate();
    }

    private IEnumerable<int> MatchesIn(string s)
    {
        if (searchTerm.Length == 0) yield break;
        var cmp = searchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int i = 0;
        while ((i = s.IndexOf(searchTerm, i, cmp)) >= 0)
        {
            int end = i + searchTerm.Length;
            bool ok = !searchWord || ((i == 0 || !CppSyntax.IsIdentChar(s[i - 1])) && (end >= s.Length || !CppSyntax.IsIdentChar(s[end])));
            if (ok) yield return i;
            i = end > i ? (ok ? end : i + 1) : i + 1;
        }
    }

    public int CountMatches(out int currentIndex)
    {
        currentIndex = 0;
        int count = 0;
        var selStart = SelStart;
        for (int l = 0; l < lines.Count; l++)
            foreach (int i in MatchesIn(lines[l]))
            {
                count++;
                if (HasSelection && l == selStart.Line && i == selStart.Col) currentIndex = count;
            }
        return count;
    }

    public bool FindNext(bool forward)
    {
        if (searchTerm.Length == 0) return false;
        int n = lines.Count;
        var from = forward ? SelEnd : SelStart;
        for (int k = 0; k <= n; k++)
        {
            int l = forward ? (from.Line + k) % n : (from.Line - k + n * 2) % n;
            var hits = MatchesIn(lines[l]).ToList();
            if (!forward) hits.Reverse();
            foreach (int i in hits)
            {
                if (k == 0 && forward && i < from.Col) continue;
                if (k == 0 && !forward && i >= from.Col) continue;
                anchor = new TextPos(l, i);
                caret = new TextPos(l, i + searchTerm.Length);
                UpdateBracketMatch();
                if (caret.Line * lineH < scrollY.Target || (caret.Line + 1) * lineH > scrollY.Target + ClientSize.Height) CenterOn(l, true);
                EnsureCaretVisibleX();
                Invalidate();
                if (CaretMoved != null) CaretMoved(this, EventArgs.Empty);
                return true;
            }
            if (k == n) break;
        }
        return false;
    }

    public bool ReplaceCurrent(string replacement)
    {
        if (HasSelection && string.Equals(SelectedText, searchTerm, searchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
        {
            Begin(MergeNone);
            try { caret = anchor = Replace(SelStart, SelEnd, replacement); }
            finally { End(); }
        }
        return FindNext(true);
    }

    public int ReplaceAll(string replacement)
    {
        if (searchTerm.Length == 0) return 0;
        int count = 0;
        Begin(MergeNone);
        try
        {
            for (int l = lines.Count - 1; l >= 0; l--)
            {
                var hits = MatchesIn(lines[l]).ToList();
                for (int h = hits.Count - 1; h >= 0; h--)
                {
                    Replace(new TextPos(l, hits[h]), new TextPos(l, hits[h] + searchTerm.Length), replacement);
                    count++;
                }
            }
            caret = anchor = Clamp(caret);
        }
        finally { End(); }
        return count;
    }

    // ------------------------------------------------------------------ markers

    public ICollection<int> Breakpoints { get { return breakpoints; } }

    public void ToggleBreakpoint(int line)
    {
        if (line < 0 || line >= lines.Count) return;
        if (!breakpoints.Remove(line)) breakpoints.Add(line);
        Invalidate();
        if (BreakpointToggled != null) BreakpointToggled(line);
    }

    public void SetBreakpoints(IEnumerable<int> set)
    {
        breakpoints.Clear();
        foreach (var l in set) if (l >= 0 && l < lines.Count) breakpoints.Add(l);
        Invalidate();
    }

    public int ExecLine
    {
        get { return execLine; }
        set
        {
            bool moved = value != execLine;
            execLine = value;
            if (value >= 0) GoTo(value, caret.Line == value ? caret.Col : 0);
            if (execTicker == null)
            {
                // the paused line breathes, and a light sweeps across it each time execution stops on a new line
                execTicker = now =>
                {
                    if (execLine < 0 || !Visible || Animator.Reduced || now - execFrame < 33) return;
                    execFrame = now;
                    Invalidate();
                };
                execArrive.Changed += Invalidate;
            }
            if (value >= 0) { Animator.AddTicker(execTicker); if (moved && !Animator.Reduced) { execArrive.Snap(0f); execArrive.To(1f, 520, Ease.OutCubic); } }
            else Animator.RemoveTicker(execTicker);
            Invalidate();
        }
    }

    public void SetDiagnostics(IEnumerable<Diagnostic> list)
    {
        diagnostics.Clear();
        diagnostics.AddRange(list);
        hoverDiag = null;
        Invalidate();
    }

    public IList<Diagnostic> Diagnostics { get { return diagnostics; } }

    // ------------------------------------------------------------------ bookmarks & words

    public void ToggleBookmark(int number)
    {
        int line;
        if (bookmarks.TryGetValue(number, out line) && line == caret.Line) bookmarks.Remove(number);
        else
        {
            foreach (var k in bookmarks.Where(b => b.Value == caret.Line).Select(b => b.Key).ToList()) bookmarks.Remove(k);
            bookmarks[number] = caret.Line;
        }
        Invalidate();
    }

    public void GoToBookmark(int number)
    {
        int line;
        if (bookmarks.TryGetValue(number, out line)) GoTo(line, 0);
    }

    public int FirstFreeBookmark()
    {
        foreach (var b in bookmarks) if (b.Value == caret.Line) return b.Key;
        for (int i = 1; i <= 9; i++) if (!bookmarks.ContainsKey(i)) return i;
        return 9;
    }

    public void NextBookmark()
    {
        if (bookmarks.Count == 0) return;
        var ordered = bookmarks.Values.OrderBy(l => l).ToList();
        int next = ordered.FirstOrDefault(l => l > caret.Line);
        GoTo(next > caret.Line ? next : ordered[0], 0);
    }

    public string WordAtCaret()
    {
        string s = lines[caret.Line];
        int a = caret.Col, b = caret.Col;
        while (a > 0 && CppSyntax.IsIdentChar(s[a - 1])) a--;
        while (b < s.Length && CppSyntax.IsIdentChar(s[b])) b++;
        return b > a ? s.Substring(a, b - a) : null;
    }

    // ------------------------------------------------------------------ syntax helpers

    private int StateAt(int line)
    {
        while (lineStates.Count <= line && lineStates.Count <= lines.Count)
        {
            int i = lineStates.Count - 1;
            lineStates.Add(CppSyntax.Tokenize(lines[i], lineStates[i], scratchTokens));
        }
        return lineStates[Math.Min(line, lineStates.Count - 1)];
    }

    public bool InCommentOrString(TextPos p)
    {
        var tokens = new List<Token>();
        CppSyntax.Tokenize(lines[p.Line], StateAt(p.Line), tokens);
        foreach (var t in tokens)
        {
            if (t.Kind != Tok.Comment && t.Kind != Tok.String) continue;
            int end = t.Start + t.Length;
            if (t.Kind == Tok.Comment && p.Col > t.Start && p.Col <= end) return true;
            if (t.Kind == Tok.String && p.Col > t.Start && (p.Col < end || (end == lines[p.Line].Length && !ClosedString(lines[p.Line], t)))) return true;
        }
        return false;
    }

    private static bool ClosedString(string s, Token t)
    {
        if (t.Length < 2) return false;
        char q = s[t.Start];
        return s[t.Start + t.Length - 1] == q && (t.Length < 3 || s[t.Start + t.Length - 2] != '\\');
    }

    private void UpdateBracketMatch()
    {
        bracketA = bracketB = TextPos.None;
        string s = lines[caret.Line];
        int col = -1;
        if (caret.Col > 0 && "()[]{}".IndexOf(s[caret.Col - 1]) >= 0) col = caret.Col - 1;
        else if (caret.Col < s.Length && "()[]{}".IndexOf(s[caret.Col]) >= 0) col = caret.Col;
        if (col < 0) return;
        var start = new TextPos(caret.Line, col);
        if (InCommentOrString(new TextPos(caret.Line, col + 1)) && s[col] != '"') return;
        char c = s[col];
        const string opens = "([{", closes = ")]}";
        int kind = opens.IndexOf(c);
        bool forward = kind >= 0;
        if (!forward) kind = closes.IndexOf(c);
        char open = opens[kind], close = closes[kind];
        int balance = 0;
        int dir = forward ? 1 : -1;
        for (int l = start.Line; l >= 0 && l < lines.Count && Math.Abs(l - start.Line) < 3000; l += dir)
        {
            string text = lines[l];
            var mask = CodeMask(l);
            int i = l == start.Line ? col : (forward ? 0 : text.Length - 1);
            for (; i >= 0 && i < text.Length; i += dir)
            {
                if (!mask[i]) continue;
                if (text[i] == open) balance += forward ? 1 : -1;
                else if (text[i] == close) balance += forward ? -1 : 1;
                if (balance == 0) { bracketA = start; bracketB = new TextPos(l, i); return; }
            }
        }
    }

    private bool[] CodeMask(int line)
    {
        string s = lines[line];
        var mask = new bool[s.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = true;
        var tokens = new List<Token>();
        CppSyntax.Tokenize(s, StateAt(line), tokens);
        foreach (var t in tokens)
            if (t.Kind == Tok.Comment || t.Kind == Tok.String || t.Kind == Tok.Include)
                for (int i = t.Start; i < t.Start + t.Length && i < mask.Length; i++) mask[i] = false;
        return mask;
    }

    // ------------------------------------------------------------------ completion

    public void ShowCompletion() { RefreshCompletion(true); }

    private void UpdateCompletionAfterTyping(char c)
    {
        if (!AutoComplete || Provider == null) { HidePopup(); return; }
        bool trigger = CppSyntax.IsIdentChar(c) || c == '.' || c == '>' || c == ':' || c == '<' || c == '#' || c == '"' || c == '/';
        if (!trigger) { HidePopup(); return; }
        RefreshCompletion(false);
    }

    private void RefreshCompletion(bool explicitRequest)
    {
        if (Provider == null) return;
        CompletionContext ctx;
        TextPos start;
        string prefix;
        if (!GetCompletionContext(explicitRequest, out ctx, out start, out prefix)) { HidePopup(); return; }
        if (ctx != CompletionContext.Include && InCommentOrString(caret)) { HidePopup(); return; }
        var items = Provider.Collect(ctx, prefix, DocumentWords(prefix), usesStd);
        if (items.Count == 0) { HidePopup(); return; }
        popupStart = start;
        popupContext = ctx;
        var p = CaretPixel();
        popup.ShowItems(items, new Point(p.X, p.Y + lineH), lineH, ClientSize);
    }

    private bool GetCompletionContext(bool explicitRequest, out CompletionContext ctx, out TextPos start, out string prefix)
    {
        string s = lines[caret.Line];
        string upto = s.Substring(0, caret.Col);
        var inc = Regex.Match(upto, "^\\s*#\\s*include\\s*[<\"]([^>\"]*)$");
        if (inc.Success)
        {
            ctx = CompletionContext.Include;
            prefix = inc.Groups[1].Value;
            start = new TextPos(caret.Line, caret.Col - prefix.Length);
            return true;
        }
        int b = caret.Col;
        while (b > 0 && CppSyntax.IsIdentChar(s[b - 1])) b--;
        prefix = s.Substring(b, caret.Col - b);
        start = new TextPos(caret.Line, b);
        string before = s.Substring(0, b);
        if (Regex.IsMatch(before, "^\\s*#\\s*$")) { ctx = CompletionContext.Directive; return true; }
        if (before.EndsWith("->") || (before.EndsWith(".") && before.Length > 1 && (CppSyntax.IsIdentChar(before[before.Length - 2]) || ")]".IndexOf(before[before.Length - 2]) >= 0) && !char.IsDigit(before[before.Length - 2])))
        {
            ctx = CompletionContext.Member;
            return true;
        }
        ctx = CompletionContext.Word;
        if (prefix.Length > 0 && char.IsDigit(prefix[0])) return false;
        return prefix.Length > 0 || explicitRequest || before.EndsWith("::");
    }

    private ICollection<string> DocumentWords(string exclude)
    {
        if (wordCacheVersion != version || wordCache == null)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var re = new Regex("[A-Za-z_][A-Za-z0-9_]{2,}");
            int limit = Math.Min(lines.Count, 20000);
            usesStd = false;
            for (int l = 0; l < limit; l++)
            {
                string s = lines[l];
                if (!usesStd && s.Contains("using namespace std")) usesStd = true;
                if (l == caret.Line) continue;
                foreach (Match m in re.Matches(s))
                    if (!CppSyntax.Keywords.Contains(m.Value) && !CppSyntax.Types.Contains(m.Value)) set.Add(m.Value);
            }
            wordCache = set;
            wordCacheVersion = version;
        }
        return wordCache;
    }

    public IEnumerable<string> Words() { return DocumentWords(null); }

    private void AcceptCompletion(CompletionItem item)
    {
        if (item == null) { HidePopup(); return; }
        HidePopup();
        Begin(MergeNone);
        try
        {
            var start = popupStart;
            var end = caret;
            if (start.Line != end.Line || start.Col > end.Col) start = end;
            string text = item.Insert;
            if (item.Kind == CompletionKind.Snippet)
            {
                string indent = LeadingWhitespace(lines[caret.Line]);
                text = text.Replace("\t", IndentUnit).Replace("\n", "\n" + indent);
                int mark = text.IndexOf("$0", StringComparison.Ordinal);
                text = text.Replace("$0", "");
                var e = Replace(start, end, text);
                caret = anchor = mark >= 0 ? EndOf(start, text.Substring(0, mark)) : e;
            }
            else if (item.Kind == CompletionKind.Header)
            {
                string line = lines[caret.Line];
                char opener = line.Contains("<") ? '<' : '"';
                char closer = opener == '<' ? '>' : '"';
                bool closed = caret.Col < line.Length && line[caret.Col] == closer;
                var e = Replace(start, end, text + (closed ? "" : closer.ToString()));
                caret = anchor = closed ? new TextPos(e.Line, e.Col + 1) : e;
            }
            else
            {
                var e = Replace(start, end, text);
                caret = anchor = e;
                string line = lines[caret.Line];
                bool nextIsParen = caret.Col < line.Length && line[caret.Col] == '(';
                if (item.Kind == CompletionKind.Function && item.Detail == "std" && !nextIsParen && AutoPairs)
                {
                    Replace(caret, caret, "()");
                    caret = anchor = new TextPos(caret.Line, caret.Col + 1);
                }
            }
        }
        finally { End(); }
    }

    public void HidePopup() { if (popup.Visible) popup.Hide2(); }
    public bool CompletionVisible { get { return popup.Visible; } }

    // ------------------------------------------------------------------ fonts & metrics

    public void SetCodeFont(string family, float sizePt)
    {
        sizePt = Math.Max(7f, Math.Min(36f, sizePt));
        if (font != null) font.Dispose();
        if (cjkFont != null) cjkFont.Dispose();
        font = new Font(family, sizePt);
        string cjk = Theme.FontExists("Microsoft YaHei UI") ? "Microsoft YaHei UI" : Theme.FontExists("Microsoft YaHei") ? "Microsoft YaHei" : family;
        cjkFont = new Font(cjk, sizePt * 0.94f);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        using (var g = Graphics.FromHwnd(IntPtr.Zero))
        {
            cellW = TextRenderer.MeasureText(g, new string('M', 100), font, Size.Empty, flags).Width / 100f;
            textH = TextRenderer.MeasureText(g, "Ag", font, Size.Empty, flags).Height;
        }
        lineH = (int)Math.Round(textH * 1.45f);
        textOffsetY = (lineH - textH) / 2;
        UpdateGutter();
        EnsureCaretVisible(false);
        Invalidate();
    }

    public void Zoom(int steps)
    {
        SetCodeFont(font.FontFamily.Name, steps == 0 ? Theme.Code.SizeInPoints : font.SizeInPoints + steps);
        if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
    }

    private void UpdateGutter()
    {
        int digits = Math.Max(3, lines.Count.ToString().Length);
        int old = gutterW;
        gutterW = Ui.D(26) + (int)Math.Ceiling(digits * cellW) + Ui.D(14);
        textLeft = gutterW + Ui.D(10);
        if (old != gutterW) Invalidate();
    }

    public Point CaretPixel()
    {
        return new Point((int)Math.Round(textLeft + CellOf(caret.Line, caret.Col) * cellW - scrollX.Value),
                         (int)Math.Round(caret.Line * lineH - scrollY.Value));
    }

    // ------------------------------------------------------------------ scrolling

    private float MaxScrollY
    {
        get
        {
            float content = lines.Count * lineH;
            float view = ClientSize.Height;
            return content <= view ? 0 : content - view / 3f;
        }
    }

    private float MaxScrollX { get { return Math.Max(0, LongestCells() * cellW - (ClientSize.Width - textLeft) + cellW * 6); } }

    private void ScrollTo(float y, bool animate)
    {
        y = Math.Max(0, Math.Min(MaxScrollY, y));
        if (animate) scrollY.To(y, 160); else scrollY.Snap(y);
        ShowBarBriefly();
        HidePopup();
    }

    private void CenterOn(int line, bool animate)
    {
        float target = line * lineH - ClientSize.Height / 2f + lineH / 2f;
        float y = Math.Max(0, Math.Min(MaxScrollY, target));
        if (animate) scrollY.To(y, 220); else scrollY.Snap(y);
    }

    private void EnsureCaretVisible(bool animate)
    {
        if (ClientSize.Height <= 0 || lineH == 0) return;
        float top = scrollY.Target, view = ClientSize.Height;
        float y = caret.Line * lineH;
        float margin = lineH;
        float target = top;
        if (y < top + margin * 0.5f) target = y - margin;
        else if (y + lineH > top + view - margin * 0.5f) target = y + lineH - view + margin;
        target = Math.Max(0, Math.Min(Math.Max(MaxScrollY, y), target));
        if (Math.Abs(target - top) > 0.5f) { if (animate) scrollY.To(target, 120); else scrollY.Snap(target); }
        EnsureCaretVisibleX();
    }

    private void EnsureCaretVisibleX()
    {
        float x = CellOf(caret.Line, caret.Col) * cellW;
        float width = ClientSize.Width - textLeft - Ui.D(16);
        float left = scrollX.Target;
        float target = left;
        if (x < left + cellW * 2) target = Math.Max(0, x - cellW * 6);
        else if (x > left + width - cellW * 2) target = x - width + cellW * 6;
        if (Math.Abs(target - left) > 0.5f) scrollX.Snap(target);
    }

    private void ShowBarBriefly()
    {
        barAlpha.To(1f, 120);
        barFade.Stop();
        barFade.Start();
    }

    private Rectangle VBarArea { get { return new Rectangle(ClientSize.Width - Ui.D(12), 0, Ui.D(12), ClientSize.Height); } }

    private RectangleF VThumb()
    {
        float max = MaxScrollY, view = ClientSize.Height;
        if (max <= 0) return RectangleF.Empty;
        float thumbH = Math.Max(Ui.D(36), view * view / (view + max));
        float y = (view - thumbH) * (scrollY.Value / max);
        return new RectangleF(ClientSize.Width - Ui.D(10), y + Ui.D(2), Ui.D(7), thumbH - Ui.D(4));
    }

    // ------------------------------------------------------------------ input

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.Tab: case Keys.Enter:
            case Keys.Escape: case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                return true;
        }
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (HandleKey(e.KeyData)) { e.Handled = true; e.SuppressKeyPress = true; }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.Handled) return;
        char c = e.KeyChar;
        if (c < 32 || c == 127) return;
        var mods = ModifierKeys;
        if ((mods & Keys.Control) != 0 && (mods & Keys.Alt) == 0) return;
        TypeChar(c);
        e.Handled = true;
    }

    // Returns true when the key was a command for the editor.
    public bool HandleKey(Keys keys)
    {
        Keys code = keys & Keys.KeyCode;
        bool ctrl = (keys & Keys.Control) != 0, shift = (keys & Keys.Shift) != 0, alt = (keys & Keys.Alt) != 0;

        if (popup.Visible)
        {
            switch (code)
            {
                case Keys.Up: popup.MoveSelection(-1); return true;
                case Keys.Down: popup.MoveSelection(1); return true;
                case Keys.PageUp: popup.MoveSelection(-8); return true;
                case Keys.PageDown: popup.MoveSelection(8); return true;
                case Keys.Enter:
                case Keys.Tab:
                    if (!ctrl && !shift && !alt) { AcceptCompletion(popup.Selected); return true; }
                    break;
                case Keys.Escape: HidePopup(); return true;
                case Keys.Left: case Keys.Right: case Keys.Home: case Keys.End: HidePopup(); break;
            }
        }

        switch (code)
        {
            case Keys.Left:
                if (alt) return false;
                SetCaret(ctrl ? WordLeft(caret) : (HasSelection && !shift ? SelStart : StepLeft(caret)), shift);
                return true;
            case Keys.Right:
                if (alt) return false;
                SetCaret(ctrl ? WordRight(caret) : (HasSelection && !shift ? SelEnd : StepRight(caret)), shift);
                return true;
            case Keys.Up:
                if (alt) { MoveLines(-1); return true; }
                if (ctrl) { ScrollTo(scrollY.Target - lineH, true); return true; }
                MoveVertical(-1, shift);
                return true;
            case Keys.Down:
                if (alt) { MoveLines(1); return true; }
                if (ctrl) { ScrollTo(scrollY.Target + lineH, true); return true; }
                MoveVertical(1, shift);
                return true;
            case Keys.Home:
                if (ctrl) SetCaret(new TextPos(0, 0), shift); else SmartHome(shift);
                return true;
            case Keys.End:
                if (ctrl) SetCaret(new TextPos(lines.Count - 1, lines[lines.Count - 1].Length), shift);
                else SetCaret(new TextPos(caret.Line, lines[caret.Line].Length), shift);
                return true;
            case Keys.PageUp:
            case Keys.PageDown:
            {
                int page = Math.Max(1, ClientSize.Height / Math.Max(1, lineH) - 2);
                int dir = code == Keys.PageUp ? -1 : 1;
                ScrollTo(scrollY.Target + dir * page * lineH, true);
                MoveVertical(dir * page, shift);
                return true;
            }
            case Keys.Back: Backspace(ctrl); return true;
            case Keys.Delete:
                if (shift && !ctrl) { Cut(); return true; }
                DeleteForward(ctrl);
                return true;
            case Keys.Enter:
                if (ctrl && shift) InsertLineAbove();
                else if (ctrl) InsertLineBelow();
                else NewLine();
                return true;
            case Keys.Tab:
                if (ctrl) return false;
                Tab(shift);
                return true;
            case Keys.Escape:
                if (EscapePressed != null) EscapePressed(this, EventArgs.Empty);
                return true;
            case Keys.Insert:
                if (ctrl) { Copy(); return true; }
                if (shift) { Paste(); return true; }
                return false;
        }

        if (ctrl && !alt)
        {
            if (code >= Keys.D1 && code <= Keys.D9)
            {
                int n = code - Keys.D0;
                if (shift) ToggleBookmark(n); else GoToBookmark(n);
                return true;
            }
            switch (code)
            {
                case Keys.A: SelectAll(); return true;
                case Keys.C: Copy(); return true;
                case Keys.X: Cut(); return true;
                case Keys.V: Paste(); return true;
                case Keys.Z: if (shift) Redo(); else Undo(); return true;
                case Keys.Y: Redo(); return true;
                case Keys.D: DuplicateLine(); return true;
                case Keys.L: SelectLine(caret.Line); return true;
                case Keys.OemQuestion: case Keys.Divide: ToggleComment(); return true;
                case Keys.K: if (shift) { DeleteLines(); return true; } return false;
                case Keys.Space: ShowCompletion(); return true;
                case Keys.OemOpenBrackets: IndentLines(-1); return true;
                case Keys.OemCloseBrackets: IndentLines(1); return true;
                case Keys.Oemplus: case Keys.Add: Zoom(1); return true;
                case Keys.OemMinus: case Keys.Subtract: Zoom(-1); return true;
                case Keys.D0: case Keys.NumPad0: Zoom(0); return true;
            }
        }
        return false;
    }

    private const int WM_IME_STARTCOMPOSITION = 0x010D, WM_IME_COMPOSITION = 0x010F;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_IME_STARTCOMPOSITION || m.Msg == WM_IME_COMPOSITION) PlaceImeWindow();
        base.WndProc(ref m);
    }

    private void PlaceImeWindow()
    {
        IntPtr imc = Native.ImmGetContext(Handle);
        if (imc == IntPtr.Zero) return;
        try
        {
            var p = CaretPixel();
            var form = new Native.CompositionForm { Style = 0x0002, Position = new Point(p.X, p.Y + textOffsetY), Area = Rectangle.Empty };
            Native.ImmSetCompositionWindow(imc, ref form);
            var lf = new Native.LogFont();
            font.ToLogFont(lf);
            Native.ImmSetCompositionFontW(imc, lf);
        }
        finally { Native.ImmReleaseContext(Handle, imc); }
    }

    private int LineAt(int y) { return Math.Max(0, (int)((y + scrollY.Value) / Math.Max(1, lineH))); }

    public TextPos HitTest(Point p)
    {
        int line = Math.Min(lines.Count - 1, LineAt(p.Y));
        float cell = (p.X - textLeft + scrollX.Value) / cellW;
        return new TextPos(line, ColAtCell(line, Math.Max(0, cell), true));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        HidePopup();
        hoverDiag = null;
        if (e.Button == MouseButtons.Left)
        {
            if (VBarArea.Contains(e.Location) && MaxScrollY > 0)
            {
                var thumb = VThumb();
                if (thumb.Contains(e.Location)) { thumbDrag = true; thumbGrab = e.Y - thumb.Y; }
                else ScrollTo(scrollY.Target + Math.Sign(e.Y - thumb.Y) * (ClientSize.Height - lineH), true);
                Capture = true;
                ShowBarBriefly();
                return;
            }
            if (e.X < gutterW)
            {
                int line = LineAt(e.Y);
                if (line < lines.Count) ToggleBreakpoint(line);
                return;
            }
            if ((ModifierKeys & Keys.Control) != 0)
            {
                SetCaret(HitTest(e.Location), false);
                string word = WordAtCaret();
                if (!string.IsNullOrEmpty(word) && DefinitionRequested != null) DefinitionRequested(word);
                return;
            }
            double now = Animator.Now;
            if (now - lastClickTime < SystemInformation.DoubleClickTime &&
                Math.Abs(e.X - lastClickPoint.X) <= Ui.D(4) && Math.Abs(e.Y - lastClickPoint.Y) <= Ui.D(4))
                clickCount = clickCount % 3 + 1;
            else clickCount = 1;
            lastClickTime = now;
            lastClickPoint = e.Location;
            var pos = HitTest(e.Location);
            if (clickCount == 1)
            {
                SetCaret(pos, (ModifierKeys & Keys.Shift) != 0);
                dragging = true;
                Capture = true;
                Animator.AddTicker(dragTicker);
            }
            else if (clickCount == 2) SelectWordAt(pos);
            else SelectLine(pos.Line);
        }
        else if (e.Button == MouseButtons.Right)
        {
            var pos = HitTest(e.Location);
            if (!(HasSelection && pos >= SelStart && pos <= SelEnd)) SetCaret(pos, false);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (thumbDrag)
        {
            var thumb = VThumb();
            float travel = ClientSize.Height - (thumb.Height + Ui.D(4));
            if (travel > 0) ScrollTo((e.Y - thumbGrab) / travel * MaxScrollY, false);
            return;
        }
        if (dragging)
        {
            var p = new Point(Math.Max(0, Math.Min(ClientSize.Width, e.X)), Math.Max(0, Math.Min(ClientSize.Height - 1, e.Y)));
            var pos = HitTest(p);
            if (pos != caret) { caret = Clamp(pos); AfterCaretChange(); }
            return;
        }
        bool overBar = e.X >= ClientSize.Width - Ui.D(16);
        Cursor = e.X < gutterW || overBar ? Cursors.Arrow : Cursors.IBeam;
        if (overBar && MaxScrollY > 0) ShowBarBriefly();

        var diag = DiagnosticAt(e.Location);
        if (diag != hoverCandidate)
        {
            hoverCandidate = diag;
            hoverTimer.Stop();
            if (hoverDiag != null && diag == null) { hoverDiag = null; Invalidate(); }
            if (diag != null) { hoverPoint = e.Location; hoverTimer.Start(); }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragging = false;
        thumbDrag = false;
        Capture = false;
        Animator.RemoveTicker(dragTicker);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hoverTimer.Stop();
        hoverCandidate = null;
        if (hoverDiag != null) { hoverDiag = null; Invalidate(); }
    }

    private void AutoScrollWhileDragging()
    {
        if (!dragging) { Animator.RemoveTicker(dragTicker); return; }
        var p = PointToClient(Cursor.Position);
        float dy = 0;
        if (p.Y < 0) dy = p.Y;
        else if (p.Y > ClientSize.Height) dy = p.Y - ClientSize.Height;
        if (dy == 0) return;
        scrollY.Snap(Math.Max(0, Math.Min(MaxScrollY, scrollY.Value + dy * 0.35f)));
        var pos = HitTest(new Point(Math.Max(0, p.X), Math.Max(0, Math.Min(ClientSize.Height - 1, p.Y))));
        caret = Clamp(pos);
        Invalidate();
        if (CaretMoved != null) CaretMoved(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        HidePopup();
        if ((ModifierKeys & Keys.Control) != 0) { Zoom(Math.Sign(e.Delta)); return; }
        float notches = e.Delta / 120f;
        if ((ModifierKeys & Keys.Shift) != 0)
        {
            float x = Math.Max(0, Math.Min(MaxScrollX, scrollX.Target - notches * cellW * 12));
            scrollX.To(x, 140);
            return;
        }
        int linesPerNotch = Math.Max(1, SystemInformation.MouseWheelScrollLines);
        ScrollTo(scrollY.Target - notches * linesPerNotch * lineH, true);
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Touch(); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); HidePopup(); StopBlink(); caretAlpha = 1f; Invalidate(); }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        HidePopup();
        if (scrollY.Value > MaxScrollY) scrollY.Snap(MaxScrollY);
    }

    private void Touch()
    {
        lastActivity = blinkStart = Animator.Now;
        caretAlpha = 1f;
        if (Focused && !blinking) { blinking = true; Animator.AddTicker(blinkTicker); }
    }

    private void StopBlink()
    {
        if (!blinking) return;
        blinking = false;
        Animator.RemoveTicker(blinkTicker);
    }

    private void InvalidateCaret()
    {
        var p = CaretPixel();
        Invalidate(new Rectangle(p.X - Ui.D(2), p.Y, Ui.D(6), lineH));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopBlink();
            Animator.RemoveTicker(dragTicker);
            if (execTicker != null) Animator.RemoveTicker(execTicker);
            hoverTimer.Dispose();
            barFade.Dispose();
            if (font != null) font.Dispose();
            if (cjkFont != null) cjkFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private Diagnostic DiagnosticAt(Point p)
    {
        if (diagnostics.Count == 0) return null;
        int line = LineAt(p.Y);
        if (line >= lines.Count) return null;
        foreach (var d in diagnostics)
        {
            if (d.Line != line) continue;
            if (p.X < gutterW) return d;
            int a, b;
            DiagRange(d, out a, out b);
            float x1 = textLeft + CellOf(line, a) * cellW - scrollX.Value;
            float x2 = textLeft + CellOf(line, b) * cellW - scrollX.Value;
            if (p.X >= x1 - Ui.D(2) && p.X <= Math.Max(x1 + cellW, x2) + Ui.D(2)) return d;
        }
        return null;
    }

    private void DiagRange(Diagnostic d, out int a, out int b)
    {
        string s = d.Line >= 0 && d.Line < lines.Count ? lines[d.Line] : "";
        if (d.Col <= 0)
        {
            a = s.Length - s.TrimStart().Length;
            b = Math.Max(a + 1, s.TrimEnd().Length);
            return;
        }
        a = Math.Min(Math.Max(0, d.Col - 1), Math.Max(0, s.Length - 1));
        b = a + 1;
        if (a < s.Length && CppSyntax.IsIdentChar(s[a])) while (b < s.Length && CppSyntax.IsIdentChar(s[b])) b++;
        b = Math.Min(Math.Max(b, a + 1), Math.Max(s.Length, a + 1));
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.EditorBg);
        if (lineH <= 0) return;
        float sy = scrollY.Value, sx = scrollX.Value;
        int first = Math.Max(0, (int)(sy / lineH));
        int last = Math.Min(lines.Count - 1, (int)((sy + ClientSize.Height) / lineH) + 1);
        bool focused = Focused;
        var selA = SelStart; var selB = SelEnd;
        bool sel = HasSelection;

        StateAt(first);
        var guides = IndentGuides(first, last);

        for (int l = first; l <= last; l++)
        {
            int y = (int)Math.Round(l * lineH - sy);
            if (l == execLine)
            {
                double breath = Animator.Reduced ? 0.5 : 0.5 + 0.5 * Math.Sin(Animator.Now / 420.0);
                using (var b = new SolidBrush(Ui.Alpha(Theme.Orange, (int)(34 + 30 * breath)))) g.FillRectangle(b, gutterW, y, ClientSize.Width - gutterW, lineH);
                int glowW = Ui.D(180);
                using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(gutterW, y, glowW, lineH), Ui.Alpha(Theme.OrangeHot, (int)(70 + 60 * breath)), Ui.Alpha(Theme.OrangeHot, 0), 0f))
                    g.FillRectangle(lg, gutterW, y, glowW, lineH);
                if (execArrive.Value < 1f)
                {
                    float w = ClientSize.Width - gutterW, band = Ui.D(140);
                    float bx = gutterW - band + execArrive.Value * (w + band);
                    var blend = new System.Drawing.Drawing2D.ColorBlend(3) { Colors = new[] { Ui.Alpha(Theme.Gold, 0), Ui.Alpha(Theme.Gold, 120), Ui.Alpha(Theme.Gold, 0) }, Positions = new[] { 0f, 0.5f, 1f } };
                    using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(bx, y, band, lineH), Color.White, Color.White, 0f) { InterpolationColors = blend })
                        g.FillRectangle(lg, bx, y, band, lineH);
                }
            }
            else if (l == caret.Line && !sel)
                using (var b = new SolidBrush(Theme.CurrentLine)) g.FillRectangle(b, gutterW, y, ClientSize.Width - gutterW, lineH);

            if (sel && l >= selA.Line && l <= selB.Line)
            {
                int c1 = l == selA.Line ? CellOf(l, selA.Col) : 0;
                int c2 = l == selB.Line ? CellOf(l, selB.Col) : CellOf(l, lines[l].Length) + 1;
                float x1 = textLeft + c1 * cellW - sx, x2 = textLeft + c2 * cellW - sx;
                if (x2 > x1) Shapes.FillRound(g, focused ? Theme.Selection : Theme.SelectionInactive, new RectangleF(x1, y + Ui.D(1), x2 - x1, lineH - Ui.D(2)), Ui.D(3));
            }

            if (searchTerm.Length > 0)
                foreach (int i in MatchesIn(lines[l]))
                {
                    float x1 = textLeft + CellOf(l, i) * cellW - sx;
                    float x2 = textLeft + CellOf(l, i + searchTerm.Length) * cellW - sx;
                    Shapes.FillRound(g, Theme.FindMatch, new RectangleF(x1, y + Ui.D(2), x2 - x1, lineH - Ui.D(4)), Ui.D(3));
                }

            foreach (var bracket in new[] { bracketA, bracketB })
            {
                if (bracket.Line != l || bracketB.Line < 0) continue;
                float bx = textLeft + CellOf(l, bracket.Col) * cellW - sx;
                Shapes.StrokeRound(g, Theme.BracketMatch, new RectangleF(bx, y + Ui.D(2), cellW, lineH - Ui.D(4)), Ui.D(2), Math.Max(1f, Ui.Scale));
            }

            int indent = guides[l - first];
            for (int cell = 0; cell < indent; cell += TabSize)
            {
                float gx = textLeft + cell * cellW - sx;
                if (gx < textLeft) continue;
                using (var pen = new Pen(Ui.Alpha(Theme.Faint, 70), 1f)) g.DrawLine(pen, gx, y, gx, y + lineH);
            }

            DrawLineText(g, l, y, StateAt(l), sx);
        }

        foreach (var d in diagnostics)
        {
            if (d.Line < first || d.Line > last || d.Line >= lines.Count) continue;
            int a, b;
            DiagRange(d, out a, out b);
            float x1 = textLeft + CellOf(d.Line, a) * cellW - sx;
            float x2 = Math.Max(x1 + cellW, textLeft + CellOf(d.Line, b) * cellW - sx);
            int y = (int)Math.Round(d.Line * lineH - sy) + textOffsetY + textH + Ui.D(1);
            Squiggle(g, x1, x2, y, d.Severity == Severity.Error ? Theme.Red : d.Severity == Severity.Warning ? Theme.Gold : Theme.Blue);
        }

        if (focused)
        {
            // The caret glides to its new spot (rich animations); long jumps snap.
            float tx = CellOf(caret.Line, caret.Col) * cellW, ty = caret.Line * lineH;
            if (tx != glideTargetX || ty != glideTargetY)
            {
                bool jump = Animator.Reduced || glideTargetX < 0 || Math.Abs(ty - glideTargetY) > lineH * 3 || Math.Abs(tx - glideTargetX) > cellW * 40;
                glideTargetX = tx; glideTargetY = ty;
                if (jump) { glideX.Snap(tx); glideY.Snap(ty); } else { glideX.To(tx, 70); glideY.To(ty, 70); }
            }
            var p = new Point((int)Math.Round(textLeft + glideX.Value - sx), (int)Math.Round(glideY.Value - sy));
            if (p.X >= textLeft - 1 && caretAlpha > 0.02f)
                using (var b = new SolidBrush(Ui.Alpha(Theme.Orange, (int)(255 * caretAlpha))))
                    g.FillRectangle(b, p.X, p.Y + Ui.D(3), Math.Max(2, Ui.D(2)), lineH - Ui.D(6));
        }

        PaintGutter(g, first, last, sy);
        PaintScrollMarks(g);
        if (hoverDiag != null) PaintTooltip(g);
    }

    private int[] IndentGuides(int first, int last)
    {
        var result = new int[last - first + 1];
        int prev = 0;
        for (int l = first; l <= last; l++)
        {
            string s = lines[l];
            if (s.Trim().Length > 0) { prev = CellOf(l, LeadingWhitespace(s).Length); result[l - first] = prev; continue; }
            int next = 0;
            for (int k = l + 1; k < lines.Count && k < l + 60; k++)
                if (lines[k].Trim().Length > 0) { next = CellOf(k, LeadingWhitespace(lines[k]).Length); break; }
            result[l - first] = Math.Min(prev, next) + (Math.Min(prev, next) > 0 ? 1 : 0);
        }
        return result;
    }

    private void DrawLineText(Graphics g, int l, int y, int state, float sx)
    {
        string s = lines[l];
        if (s.Length == 0) return;
        CppSyntax.Tokenize(s, state, tokenBuffer);
        int ty = y + textOffsetY;
        float baseX = textLeft - sx;
        int right = ClientSize.Width;
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping;
        var run = new StringBuilder();
        int runCell = 0;
        Color runColor = Theme.SynPlain;
        int cell = 0, ti = 0;
        Action flush = () =>
        {
            if (run.Length == 0) return;
            float x = baseX + runCell * cellW;
            if (x + run.Length * cellW >= gutterW && x <= right)
                TextRenderer.DrawText(g, run.ToString(), font, new Point((int)Math.Round(x), ty), runColor, flags);
            run.Clear();
        };
        for (int i = 0; i < s.Length; i++)
        {
            while (ti < tokenBuffer.Count && tokenBuffer[ti].Start + tokenBuffer[ti].Length <= i) ti++;
            Color color = ti < tokenBuffer.Count && tokenBuffer[ti].Start <= i ? CppSyntax.ColorOf(tokenBuffer[ti].Kind) : Theme.SynPlain;
            char c = s[i];
            int w = CharCells(c, cell);
            if (c == '\t') { flush(); cell += w; continue; }
            if (w != 1 || c >= 0x80)
            {
                flush();
                string glyph = char.IsHighSurrogate(c) && i + 1 < s.Length ? s.Substring(i, 2) : c.ToString();
                float x = baseX + cell * cellW;
                if (x + w * cellW >= gutterW && x <= right)
                {
                    var f = w == 2 ? cjkFont : font;
                    var gs = TextRenderer.MeasureText(g, glyph, f, Size.Empty, flags);
                    float cx = x + (w * cellW - gs.Width) / 2f;
                    TextRenderer.DrawText(g, glyph, f, new Point((int)Math.Round(cx), ty + (textH - gs.Height) / 2), color, flags);
                }
                cell += Math.Max(w, 1);
                if (glyph.Length == 2) i++;
                if (baseX + cell * cellW > right) break;
                continue;
            }
            if (run.Length > 0 && color != runColor) flush();
            if (run.Length == 0) { runCell = cell; runColor = color; }
            run.Append(c);
            cell++;
            if (baseX + cell * cellW > right + cellW * 4) { flush(); break; }
        }
        flush();
    }

    private static void Squiggle(Graphics g, float x1, float x2, int y, Color color)
    {
        float amp = Math.Max(1f, Ui.Scale * 1.2f), period = Math.Max(3f, Ui.Scale * 3f);
        var pts = new List<PointF>();
        int k = 0;
        for (float x = x1; x <= x2; x += period / 2f, k++) pts.Add(new PointF(x, y + (k % 2 == 0 ? 0 : amp)));
        if (pts.Count < 2) return;
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pen = new Pen(color, Math.Max(1f, Ui.Scale * 0.9f))) g.DrawLines(pen, pts.ToArray());
        g.SmoothingMode = mode;
    }

    private void PaintGutter(Graphics g, int first, int last, float sy)
    {
        using (var b = new SolidBrush(Theme.EditorBg)) g.FillRectangle(b, 0, 0, gutterW, ClientSize.Height);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Right;
        int numRight = gutterW - Ui.D(14);
        var errorLines = new Dictionary<int, Severity>();
        foreach (var d in diagnostics)
        {
            Severity existing;
            if (!errorLines.TryGetValue(d.Line, out existing) || d.Severity < existing) errorLines[d.Line] = d.Severity;
        }
        for (int l = first; l <= last; l++)
        {
            int y = (int)Math.Round(l * lineH - sy);
            bool isCaretLine = l == caret.Line;
            TextRenderer.DrawText(g, (l + 1).ToString(), font, new Rectangle(0, y + textOffsetY, numRight, textH),
                isCaretLine ? Theme.TextDim : Theme.GutterText, flags);
            var dot = new Rectangle(Ui.D(7), y + (lineH - Ui.D(12)) / 2, Ui.D(12), Ui.D(12));
            if (breakpoints.Contains(l)) Glyphs.Draw(g, "breakpoint", dot, Theme.Red);
            if (l == execLine)
            {
                int nudge = Animator.Reduced ? 0 : (int)Math.Round((0.5 + 0.5 * Math.Sin(Animator.Now / 420.0)) * Ui.D(2));
                Glyphs.Draw(g, "arrow", new Rectangle(dot.X + nudge, dot.Y, dot.Width, dot.Height), Theme.OrangeHot);
            }
            foreach (var bm in bookmarks)
            {
                if (bm.Value != l) continue;
                var badge = new RectangleF(Ui.D(5), y + (lineH - Ui.D(15)) / 2f, Ui.D(15), Ui.D(15));
                Shapes.FillRound(g, Ui.Alpha(Theme.Gold, breakpoints.Contains(l) ? 120 : 220), badge, Ui.D(4));
                Shapes.Text(g, bm.Key.ToString(), Theme.UiSmall, Rectangle.Round(badge), Theme.Ink, Shapes.Center);
            }
            Severity sev;
            if (errorLines.TryGetValue(l, out sev))
                using (var b = new SolidBrush(sev == Severity.Error ? Theme.Red : sev == Severity.Warning ? Theme.Gold : Theme.Blue))
                    g.FillRectangle(b, gutterW - Ui.D(6), y + Ui.D(4), Ui.D(3), lineH - Ui.D(8));
        }
    }

    private void PaintScrollMarks(Graphics g)
    {
        int h = ClientSize.Height;
        if (lines.Count > 0 && (diagnostics.Count > 0 || searchTerm.Length > 0) && MaxScrollY > 0)
        {
            float scale = (float)h / Math.Max(1, lines.Count);
            foreach (var d in diagnostics)
                using (var b = new SolidBrush(d.Severity == Severity.Error ? Theme.Red : Theme.Gold))
                    g.FillRectangle(b, ClientSize.Width - Ui.D(5), d.Line * scale, Ui.D(4), Math.Max(2, Ui.D(2)));
            if (searchTerm.Length > 0 && lines.Count < 20000)
                for (int l = 0; l < lines.Count; l++)
                    if (MatchesIn(lines[l]).Any())
                        using (var b = new SolidBrush(Ui.Alpha(Theme.Gold, 160)))
                            g.FillRectangle(b, ClientSize.Width - Ui.D(11), l * scale, Ui.D(4), Math.Max(2, Ui.D(2)));
        }
        if (barAlpha.Value > 0.01f && MaxScrollY > 0)
        {
            var thumb = VThumb();
            Shapes.FillRound(g, Ui.Alpha(Theme.TextDim, (int)((thumbDrag ? 120 : 80) * barAlpha.Value)), thumb, Ui.D(4));
        }
    }

    private void PaintTooltip(Graphics g)
    {
        var d = hoverDiag;
        string title = d.Severity == Severity.Error ? L.T("Error", "错误") : d.Severity == Severity.Warning ? L.T("Warning", "警告") : L.T("Note", "提示");
        string body = d.Message + (string.IsNullOrEmpty(d.Hint) ? "" : "\n\n" + d.Hint);
        int maxW = Ui.D(460);
        var size = TextRenderer.MeasureText(g, body, Theme.Ui9, new Size(maxW - Ui.D(24), 2000), TextFormatFlags.WordBreak);
        int w = Math.Min(maxW, size.Width + Ui.D(24));
        int hgt = size.Height + Ui.D(40);
        int x = Math.Min(hoverPoint.X + Ui.D(8), ClientSize.Width - w - Ui.D(8));
        int y = hoverPoint.Y + Ui.D(18);
        if (y + hgt > ClientSize.Height) y = Math.Max(0, hoverPoint.Y - hgt - Ui.D(8));
        var r = new RectangleF(x, y, w, hgt);
        var accent = d.Severity == Severity.Error ? Theme.Red : d.Severity == Severity.Warning ? Theme.Gold : Theme.Blue;
        Shapes.FillRound(g, Theme.PanelHi, r, Ui.D(10));
        Shapes.StrokeRound(g, Ui.Alpha(accent, 150), r, Ui.D(10), Math.Max(1f, Ui.Scale * 0.75f));
        Glyphs.Draw(g, d.Severity == Severity.Error ? "error" : "warning", new Rectangle(x + Ui.D(12), y + Ui.D(10), Ui.D(12), Ui.D(12)), accent);
        Shapes.Text(g, title, Theme.UiSemi, new Rectangle(x + Ui.D(32), y + Ui.D(6), w - Ui.D(40), Ui.D(20)), accent, Shapes.Left);
        TextRenderer.DrawText(g, body, Theme.Ui9, new Rectangle(x + Ui.D(12), y + Ui.D(30), w - Ui.D(24), size.Height + Ui.D(4)), Theme.Text,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
    }
}
}
