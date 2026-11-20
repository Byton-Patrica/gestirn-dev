using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GestirnDev
{
// The error dictionary, in batches. Every rule has a sample message; Hints.SelfTest() proves that the sample is
// caught by that very rule, so a rule can never be silently shadowed by an earlier one.
internal static partial class Hints
{
    private static void AddBatches()
    {
        AddSyntaxRules();
        AddNameRules();
        AddTypeRules();
        AddCallRules();
        AddClassRules();
        AddTemplateRules();
        AddPreprocessorRules();
        AddCRules();
        AddLinkerRules();
        AddWarningRules();
    }

    // Full-width and "smart" characters beginners type by accident, with the ASCII character they should be.
    private static readonly Dictionary<char, string> Lookalikes = new Dictionary<char, string>
    {
        { '；', ";" }, { '，', "," }, { '（', "(" }, { '）', ")" }, { '“', "\"" }, { '”', "\"" }, { '‘', "'" }, { '’', "'" },
        { '：', ":" }, { '。', "." }, { '！', "!" }, { '？', "?" }, { '、', "," }, { '【', "[" }, { '】', "]" }, { '｛', "{" }, { '｝', "}" },
        { '＝', "=" }, { '＋', "+" }, { '－', "-" }, { '＊', "*" }, { '／', "/" }, { '＜', "<" }, { '＞', ">" }, { '｜', "|" }, { '＆', "&" },
        { '％', "%" }, { '＃', "#" }, { '《', "<" }, { '》', ">" }, { '—', "-" }, { '…', "..." }, { '·', "." },
    };

    private static string[] StrayHint(Ctx ctx)
    {
        if (ctx != null && !string.IsNullOrEmpty(ctx.Line))
        {
            var found = new List<string>();
            var foundZh = new List<string>();
            bool invisible = false;
            foreach (char c in ctx.Line)
            {
                string ascii;
                if (Lookalikes.TryGetValue(c, out ascii))
                {
                    string item = "'" + c + "' → '" + ascii + "'";
                    if (!found.Contains(item)) { found.Add(item); foundZh.Add("“" + c + "”应写成 " + ascii); }
                }
                else if (c == '　' || c == ' ' || c == '​' || c == '﻿') invisible = true;
            }
            if (found.Count > 0)
                return new[]
                {
                    "This line contains full-width (Chinese) symbols: " + string.Join(", ", found) + ".",
                    "这一行里混进了中文符号：" + string.Join("；", foundZh) + "。",
                    "Retype these full-width symbols with the English keyboard (half-width): switch the input method to English, then type them again.",
                    "这些是中文符号：先把输入法切到英文（半角），再把它们重新打一遍。",
                };
            if (invisible)
                return new[]
                {
                    "This line contains an invisible special character (such as a full-width space).",
                    "这一行里有看不见的特殊字符（比如全角空格）。",
                    "Delete the whitespace around the underlined spot and type a normal space.",
                    "把红线附近的空白删掉，重新敲一个普通空格。",
                };
        }
        return new[]
        {
            "The code contains a character the compiler can't read, most likely a Chinese (full-width) symbol such as ; , ( ) or a curly quote.",
            "代码里混进了编译器看不懂的字符，多半是中文标点，比如 ；，（）或弯引号。",
            "Retype that full-width symbol with the English keyboard (half-width). Look at the underlined spot.",
            "这是中文符号，请切到英文输入法（半角）重新输入，看红线标出的位置。",
        };
    }

    private static void AddSyntaxRules()
    {
        C(@"^stray '\\\d+' in program", (m, ctx) => StrayHint(ctx), "stray '\\357' in program");
        C(@"^expected ',' or ';' before '(.+)'|^expected ';' before '(.+)'", (m, ctx) =>
        {
            string next = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            string prev = ctx != null && !string.IsNullOrWhiteSpace(ctx.Prev) ? ctx.Prev.Trim() : null;
            bool prevLooksOpen = prev != null && !prev.EndsWith(";") && !prev.EndsWith("{") && !prev.EndsWith("}") && !prev.EndsWith(":") && !prev.StartsWith("#") && !prev.StartsWith("//");
            string fixEn = prevLooksOpen ? "Add ; at the end of the previous line: " + prev + "   (the error is often reported one line late)" : "Add ; at the end of the line above (the error is often reported one line late).";
            string fixZh = prevLooksOpen ? "在上一行的末尾补上分号 ;：" + prev + "   （报错位置往往比真正出错的位置晚一行）" : "在上一行末尾补上分号 ;（报错位置往往比真正出错的位置晚一行）。";
            return new[] { "A semicolon ; is missing right before '" + next + "'.", "在 '" + next + "' 前面少了一个分号（;）。", fixEn, fixZh };
        }, "expected ';' before 'std'");
        C(@"^expected primary-expression before '(.+?)'(?: token)?$", (m, ctx) =>
        {
            string tok = m.Groups[1].Value;
            if (ctx != null && ctx.Line != null && ctx.Line.Any(c => Lookalikes.ContainsKey(c))) return StrayHint(ctx);
            bool argHole = tok == ")" || tok == ",";
            return new[]
            {
                "A value or variable is missing before '" + tok + "'.", "在 '" + tok + "' 前面缺了一个值或变量。",
                argHole ? "Something that should be there is empty, e.g. f(1, ) or f(, 2): fill it in or remove the extra comma." : "Typical causes: a doubled operator, a stray comma, or a missing variable name.",
                argHole ? "括号或逗号旁边少了内容，比如 f(1, ) 或 f(, 2)：把值补上，或删掉多余的逗号。" : "常见原因：运算符写重了、多了逗号、漏了变量名。",
            };
        }, "expected primary-expression before ')' token");
        R(@"^'(?:::)?main' must return 'int'", "'::main' must return 'int'",
          "main must return an int.", "main 函数必须返回 int。",
          "Write int main() and end it with return 0;", "写成 int main()，结尾 return 0;");
        R(@"^from this location", "from this location",
          "(Continues the error above) This is the place where it happens.", "（接上一条）问题就出在这个位置。",
          "See the previous message for what is wrong.", "具体原因看上一条报错。");
        R(@"^expected ',' or ';' before numeric constant|^expected '[^']+' or '[^']+' before numeric constant", "expected ',' or ';' before numeric constant",
          "Two numbers are next to each other with nothing between them.", "两个数字挨在一起，中间少了东西。",
          "Put an operator or a comma between them, for example 2 + 3. A variable name can't start with a digit either.", "在它们之间补上运算符或逗号，比如 2 + 3。变量名也不能以数字开头。");
        R(@"^expected unqualified-id before numeric constant", "expected unqualified-id before numeric constant",
          "A number appears where a name should be.", "这里应该是名字，却出现了数字。",
          "Names can't start with a digit (1a is wrong, a1 is fine). Check the underlined spot.", "名字不能以数字开头（1a 不行，a1 可以）。看红线标出的位置。");
        R(@"^expected unqualified-id before '(class|new|delete|this|template|typename|namespace|private|public|protected|virtual|operator|switch|case|default|for|while|do|if|else|return|break|continue|goto|try|catch|throw|sizeof|typedef|using|struct|union|enum|const|static|auto|register|volatile|inline|friend|explicit|bool|char|short|int|long|float|double|void|signed|unsigned)'",
          "expected unqualified-id before 'new'",
          "'{1}' is a C++ keyword, so it can't be used as a name.", "'{1}' 是 C++ 的关键字，不能拿来当变量名或函数名。",
          "Pick another name, for example {1}_value or my_{1}.", "换一个名字，比如 {1}_value 或 my_{1}。");
        R(@"^expected unqualified-id before '(.+)' token", "expected unqualified-id before ')' token",
          "'{1}' appears where a name should be.", "这里应该是一个名字，却出现了 '{1}'。",
          "Look for a missing or extra ; { } ( ) just before it, or code written outside a function.", "看看它前面是不是少了或多了 ; { } ( )，或者语句写在了函数外面。");
        R(@"^'else' without a previous 'if'", "'else' without a previous 'if'",
          "This else has no if in front of it.", "这个 else 前面没有配对的 if。",
          "Check the braces and semicolons above: a ; right after if (...) or an unbalanced { } cuts the if off from its else. Also write else if with a space.", "检查上面的大括号和分号：if (...) 后面多了 ; 或大括号不配对，都会让 else 找不到 if。另外 else if 中间要有空格。");
        R(@"^(?:break|continue) statement not within (?:a )?(?:loop|switch)", "break statement not within loop or switch",
          "break (or continue) is used outside a loop or switch.", "break（或 continue）用在了循环或 switch 的外面。",
          "They only make sense inside for / while / do / switch. To leave a function use return.", "它们只能写在 for / while / do / switch 里面。想结束函数请用 return。");
        R(@"^(?:case|default) label '?(.*?)'? not within a switch statement", "case label '1' not within a switch statement",
          "A case label is outside any switch.", "case 标号不在 switch 语句里面。",
          "Put the case inside switch (x) { ... } and check that its braces match.", "case 必须写在 switch (x) { ... } 的大括号里面，检查大括号是否配对。");
        R(@"^a function-definition is not allowed here", "a function-definition is not allowed here before '{' token",
          "A function is being defined inside another function.", "在一个函数里面又定义了另一个函数。",
          "C++ doesn't allow that. Move the inner function out, or close the previous function with } first (a missing } is the usual cause).", "C++ 不允许这样。把里面的函数挪到外面；更常见的原因是上一个函数少了右大括号 }，先把它补上。");
        R(@"^expected expression before '(.+)'", "expected expression before 'int'",
          "An expression is expected, but '{1}' showed up.", "这里应该是一个表达式，却出现了 '{1}'。",
          "A declaration such as int x can't be used as a value here. Declare the variable on its own line first.", "像 int x 这样的声明不能直接当值用。先单独写一行把变量声明好。");
        R(@"^expected declaration specifiers or '\.\.\.' before '(.+)'", "expected declaration specifiers or '...' before 'cout'",
          "A statement was written outside of any function.", "语句写到了函数的外面。",
          "Only declarations may stand outside functions. Put the statement inside main() or another function.", "函数外面只能写声明。把这条语句挪到 main() 或别的函数里面。");
        R(@"^expected identifier or '\(' before '(.+)'", "expected identifier or '(' before '{' token",
          "A name or ( is expected before '{1}'.", "在 '{1}' 前面应该有名字或左括号 (。",
          "A function definition, struct or enum is incomplete here. Check the line above for a missing name or ;", "这里的函数、结构体或枚举没写完整。看看上一行是不是少了名字或分号。");
        R(@"^expected '=', ',', ';', 'asm' or '__attribute__' before '(.+)'", "expected '=', ',', ';', 'asm' or '__attribute__' before 'cout'",
          "The C compiler doesn't understand this line.", "C 编译器看不懂这一行。",
          "Either the line above lacks a ; or the line is C++ (cout, class, bool ...). Save the file as .cpp to use C++.", "要么上一行少了分号，要么这行用的是 C++ 的写法（cout、class、bool 等）。想用 C++ 请把文件存成 .cpp。");
        R(@"^empty character constant", "empty character constant",
          "A pair of single quotes has nothing in it.", "单引号里面什么也没有。",
          "Put one character between the quotes ('a'). For an empty text use two double quotes \"\".", "单引号里要放一个字符，比如 'a'。想表示空字符串请用两个双引号 \"\"。");
        R(@"^invalid suffix '(.+)' on integer constant", "invalid suffix 'a' on integer constant",
          "A number is followed by letters ('{1}').", "数字后面跟了不该有的字母 '{1}'。",
          "Names can't start with a digit (1a). If you meant a variable, rename it; for big numbers write 123L or 123LL.", "变量名不能以数字开头（比如 1a）。如果是变量请改名；表示大整数可以写 123L 或 123LL。");
        R(@"^invalid digit ""?(\d)""? in octal constant", "invalid digit \"8\" in octal constant",
          "A number that starts with 0 is read as octal, and {1} is not an octal digit.", "以 0 开头的数字会被当成八进制，而八进制里没有数字 {1}。",
          "Remove the leading 0 (write 8 instead of 08).", "把前面的 0 去掉（写 8，不要写 08）。");
        R(@"^too many decimal points in number", "too many decimal points in number",
          "A number has more than one decimal point.", "一个数字里有多个小数点。",
          "Check the number, for example 1.2.3 should be 1.23.", "检查这个数字，比如 1.2.3 应该是 1.23。");
        R(@"^extra qualification '(.+)' on member '(.+)'", "extra qualification 'A::' on member 'f'",
          "The member '{2}' is written with its class name ('{1}') inside the class itself.", "在类的里面声明成员 '{2}' 时又写了类名 '{1}'。",
          "Inside the class body just write the member name; use Class::name only when defining outside the class.", "在类的大括号里直接写成员名就行；只有在类的外面定义时才写 类名::成员名。");
        R(@"^label '(\w+)' used but not defined", "label 'done' used but not defined",
          "goto jumps to '{1}', but there is no such label.", "goto 要跳到 '{1}'，可是没有这个标号。",
          "Write {1}: before the statement you want to jump to (with a colon).", "在要跳转到的语句前面写上 {1}:（带冒号）。");
        R(@"^expected '\)' before ';' token", "expected ')' before ';' token",
          "A closing parenthesis ) is missing at the end of this statement.", "这条语句的右括号 ) 没有写全。",
          "Count the ( and ) on the underlined line; every ( needs its ).", "数一数红线这一行的 ( 和 )，每个 ( 都要有对应的 )。");
        R(@"^expected '\{' before '(.+)'", "expected '{' before 'return'",
          "A { is missing before '{1}'.", "在 '{1}' 前面少了左大括号 {。",
          "A function, class, if or for body must start with {. A ; after the ) of a function definition also causes this.", "函数、类、if、for 的内容要用 { 开头。函数定义的 ) 后面多了分号 ; 也会引起这个错误。");
        R(@"^expected '(?:\}|\))' at end of input", "expected '}' at end of input",
          "The file ends while a bracket is still open.", "文件到结尾了，还有括号没有关上。",
          "Add the missing } or ). Format the code (Ctrl+Shift+A) to see where the indentation breaks.", "补上缺少的 } 或 )。按 Ctrl+Shift+A 整理缩进，就能看出哪里断了。");
        R(@"^expected 'while' before", "expected 'while' before 'return'",
          "A do { ... } block must end with while (...);", "do { ... } 的后面必须接 while (...);",
          "Add while (condition); after the closing brace of do.", "在 do 的右大括号后面补上 while (条件);");
        R(@"^expected '(.+)' before '(.+)' token", "expected ':' before ')' token",
          "{1} is expected before '{2}'.", "在 '{2}' 前面应该有 {1}。",
          "Something is missing or in the wrong order near '{2}'. Check the line above too.", "'{2}' 附近缺了东西或顺序不对，也看看上一行。");
        R(@"^expected '(.+)' before '(.+)'", "expected ',' before 'int'",
          "{1} is expected before '{2}'.", "在 '{2}' 前面应该有 {1}。",
          "Something is missing or in the wrong order near '{2}'. Check the line above too.", "'{2}' 附近缺了东西或顺序不对，也看看上一行。");
        R(@"^expected (?:a )?type", "expected type-specifier",
          "A type name is expected here.", "这里应该写一个类型名。",
          "Write a type such as int, double or string. Check the spelling of the type before it.", "写一个类型，比如 int、double、string。也检查前面类型名的拼写。");
        R(@"^ISO C\+\+ forbids declaration of '(\w+)' with no type", "ISO C++ forbids declaration of 'f' with no type",
          "'{1}' is declared without a type.", "'{1}' 声明时没有写类型。",
          "Write the type in front, for example int {1}; (or the name is misspelled).", "在前面写上类型，例如 int {1};（也可能是名字拼错了）。");
        R(@"^name lookup of '(\w+)' changed", "name lookup of 'i' changed for ISO 'for' scoping",
          "The loop variable '{1}' is used after its loop.", "循环变量 '{1}' 在循环结束之后又被用到了。",
          "A variable declared in for (int i...) exists only inside the loop. Declare it before the loop to use it afterwards.", "在 for (int i...) 里声明的变量只在循环里面有效。想在循环后用它，请在循环前面声明。");
    }
}
}
