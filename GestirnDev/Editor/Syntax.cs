using System;
using System.Collections.Generic;
using System.Drawing;

namespace GestirnDev
{
internal enum Tok : byte { Plain, Keyword, Type, String, Number, Comment, Preproc, Function, Operator, Include }

internal struct Token
{
    public int Start, Length;
    public Tok Kind;
    public Token(int start, int length, Tok kind) { Start = start; Length = length; Kind = kind; }
}

// Line-based C/C++ scanner. A line's start state carries block comments and
// preprocessor continuations from the previous line.
internal static class CppSyntax
{
    public const int Normal = 0, BlockComment = 1, PreprocContinue = 2;

    public static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "break", "case", "catch", "class", "compl",
        "concept", "const", "consteval", "constexpr", "constinit", "const_cast", "continue", "co_await", "co_return", "co_yield",
        "decltype", "default", "delete", "do", "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "final",
        "for", "friend", "goto", "if", "import", "inline", "module", "mutable", "namespace", "new", "noexcept", "not", "not_eq",
        "nullptr", "operator", "or", "or_eq", "override", "private", "protected", "public", "register", "reinterpret_cast",
        "requires", "return", "sizeof", "static", "static_assert", "static_cast", "struct", "switch", "template", "this",
        "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union", "using", "virtual", "volatile",
        "while", "xor", "xor_eq", "NULL", "TRUE", "FALSE",
    };

    public static readonly HashSet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "bool", "char", "char8_t", "char16_t", "char32_t", "double", "float", "int", "long", "short", "signed", "unsigned",
        "void", "wchar_t", "size_t", "ptrdiff_t", "int8_t", "int16_t", "int32_t", "int64_t", "uint8_t", "uint16_t", "uint32_t",
        "uint64_t", "std", "string", "vector", "map", "set", "multimap", "multiset", "unordered_map", "unordered_set", "pair",
        "tuple", "queue", "priority_queue", "stack", "deque", "list", "array", "bitset", "ostream", "istream", "iostream",
        "stringstream", "istringstream", "ostringstream", "ifstream", "ofstream", "fstream", "FILE", "BOOL", "DWORD", "HANDLE",
        "HWND", "LPSTR", "WORD", "BYTE", "LL", "ll", "ull",
    };

    public static bool IsIdentStart(char c) { return c == '_' || char.IsLetter(c); }
    public static bool IsIdentChar(char c) { return c == '_' || char.IsLetterOrDigit(c); }

    private static bool IsOperatorChar(char c)
    {
        return !IsIdentChar(c) && c != ' ' && c != '\t' && c != '"' && c != '\'' && c != '/' && c != '#';
    }

    public static int Tokenize(string s, int state, List<Token> output)
    {
        output.Clear();
        int i = 0, n = s.Length;
        bool preproc = state == PreprocContinue;

        if (state == BlockComment)
        {
            int end = s.IndexOf("*/", StringComparison.Ordinal);
            if (end < 0) { if (n > 0) output.Add(new Token(0, n, Tok.Comment)); return BlockComment; }
            output.Add(new Token(0, end + 2, Tok.Comment));
            i = end + 2;
        }

        bool includeMode = false;
        if (state != BlockComment)
        {
            int k = i;
            while (k < n && (s[k] == ' ' || s[k] == '\t')) k++;
            if (k < n && s[k] == '#' && !preproc)
            {
                int j = k + 1;
                while (j < n && (s[j] == ' ' || s[j] == '\t')) j++;
                int wordStart = j;
                while (j < n && IsIdentChar(s[j])) j++;
                output.Add(new Token(k, j - k, Tok.Preproc));
                string directive = s.Substring(wordStart, j - wordStart);
                includeMode = directive == "include" || directive == "include_next" || directive == "import";
                preproc = true;
                i = j;
            }
        }

        while (i < n)
        {
            char c = s[i];
            if (c == ' ' || c == '\t') { i++; continue; }

            if (c == '/' && i + 1 < n && s[i + 1] == '/') { output.Add(new Token(i, n - i, Tok.Comment)); i = n; break; }
            if (c == '/' && i + 1 < n && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) { output.Add(new Token(i, n - i, Tok.Comment)); return BlockComment; }
                output.Add(new Token(i, end + 2 - i, Tok.Comment));
                i = end + 2;
                continue;
            }
            if (includeMode && c == '<')
            {
                int end = s.IndexOf('>', i + 1);
                int len = (end < 0 ? n : end + 1) - i;
                output.Add(new Token(i, len, Tok.Include));
                i += len;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (s[j] == '\\') { j += 2; continue; }
                    if (s[j] == c) { j++; break; }
                    j++;
                }
                if (j > n) j = n;
                output.Add(new Token(i, j - i, includeMode ? Tok.Include : Tok.String));
                i = j;
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])))
            {
                int j = i + 1;
                while (j < n && (IsIdentChar(s[j]) || s[j] == '.' || s[j] == '\'' ||
                       ((s[j] == '+' || s[j] == '-') && (s[j - 1] == 'e' || s[j - 1] == 'E') && !(i + 1 < n && (s[i + 1] == 'x' || s[i + 1] == 'X')))))
                    j++;
                output.Add(new Token(i, j - i, Tok.Number));
                i = j;
                continue;
            }
            if (IsIdentStart(c))
            {
                int j = i + 1;
                while (j < n && IsIdentChar(s[j])) j++;
                string word = s.Substring(i, j - i);
                Tok kind;
                if (Keywords.Contains(word)) kind = Tok.Keyword;
                else if (Types.Contains(word)) kind = Tok.Type;
                else
                {
                    int m = j;
                    while (m < n && (s[m] == ' ' || s[m] == '\t')) m++;
                    kind = m < n && s[m] == '(' ? Tok.Function : Tok.Plain;
                }
                output.Add(new Token(i, j - i, kind));
                i = j;
                continue;
            }
            int k2 = i + 1;
            while (k2 < n && IsOperatorChar(s[k2])) k2++;
            output.Add(new Token(i, k2 - i, Tok.Operator));
            i = k2;
        }

        if (preproc && n > 0 && s[n - 1] == '\\') return PreprocContinue;
        return Normal;
    }

    public static Color ColorOf(Tok kind)
    {
        switch (kind)
        {
            case Tok.Keyword: return Theme.SynKeyword;
            case Tok.Type: return Theme.SynType;
            case Tok.String: return Theme.SynString;
            case Tok.Include: return Theme.SynString;
            case Tok.Number: return Theme.SynNumber;
            case Tok.Comment: return Theme.SynComment;
            case Tok.Preproc: return Theme.SynPreproc;
            case Tok.Function: return Theme.SynFunction;
            case Tok.Operator: return Theme.SynOperator;
            default: return Theme.SynPlain;
        }
    }
}
}
