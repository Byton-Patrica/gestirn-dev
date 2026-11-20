using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GestirnDev
{
// Turns GCC messages into plain language. Each rule gives two things: what went wrong, said simply
// (shown instead of the raw message when "simple error translation" is on), and how to fix it.
internal static class Hints
{
    private sealed class Rule
    {
        public Regex Re;
        public string PlainEn, PlainZh, FixEn, FixZh;
        public Func<Match, string[]> Custom;   // {plainEn, plainZh, fixEn, fixZh} computed from the match
    }

    public sealed class Translation { public string Plain, Fix; }

    private static readonly List<Rule> Rules = new List<Rule>();

    // Header that provides each commonly forgotten standard name.
    private static readonly Dictionary<string, string> HeaderOf = new Dictionary<string, string>
    {
        { "cout", "iostream" }, { "cin", "iostream" }, { "cerr", "iostream" }, { "endl", "iostream" }, { "ostream", "iostream" }, { "istream", "iostream" },
        { "string", "string" }, { "getline", "string" }, { "to_string", "string" }, { "stoi", "string" }, { "stod", "string" },
        { "vector", "vector" }, { "map", "map" }, { "multimap", "map" }, { "set", "set" }, { "multiset", "set" },
        { "unordered_map", "unordered_map" }, { "unordered_set", "unordered_set" },
        { "queue", "queue" }, { "priority_queue", "queue" }, { "stack", "stack" }, { "deque", "deque" }, { "list", "list" }, { "array", "array" },
        { "pair", "utility" }, { "make_pair", "utility" }, { "move", "utility" }, { "swap", "algorithm" },
        { "sort", "algorithm" }, { "max", "algorithm" }, { "min", "algorithm" }, { "reverse", "algorithm" }, { "find", "algorithm" },
        { "unique", "algorithm" }, { "lower_bound", "algorithm" }, { "upper_bound", "algorithm" }, { "binary_search", "algorithm" }, { "fill", "algorithm" },
        { "accumulate", "numeric" }, { "iota", "numeric" },
        { "setw", "iomanip" }, { "setprecision", "iomanip" }, { "fixed", "iomanip" },
        { "ifstream", "fstream" }, { "ofstream", "fstream" }, { "fstream", "fstream" },
        { "stringstream", "sstream" }, { "istringstream", "sstream" }, { "ostringstream", "sstream" },
        { "shared_ptr", "memory" }, { "unique_ptr", "memory" }, { "make_shared", "memory" }, { "make_unique", "memory" },
        { "function", "functional" }, { "thread", "thread" }, { "mutex", "mutex" },
        { "printf", "cstdio" }, { "scanf", "cstdio" }, { "puts", "cstdio" }, { "fopen", "cstdio" }, { "FILE", "cstdio" }, { "getchar", "cstdio" },
        { "sqrt", "cmath" }, { "pow", "cmath" }, { "abs", "cmath" }, { "sin", "cmath" }, { "cos", "cmath" }, { "floor", "cmath" }, { "ceil", "cmath" }, { "fabs", "cmath" }, { "log", "cmath" },
        { "memset", "cstring" }, { "strlen", "cstring" }, { "strcpy", "cstring" }, { "strcmp", "cstring" }, { "memcpy", "cstring" },
        { "rand", "cstdlib" }, { "srand", "cstdlib" }, { "malloc", "cstdlib" }, { "free", "cstdlib" }, { "exit", "cstdlib" }, { "system", "cstdlib" },
        { "time", "ctime" }, { "clock", "ctime" }, { "INT_MAX", "climits" }, { "INT_MIN", "climits" }, { "LLONG_MAX", "climits" },
        { "assert", "cassert" }, { "isdigit", "cctype" }, { "isalpha", "cctype" }, { "toupper", "cctype" }, { "tolower", "cctype" },
    };

    private static readonly HashSet<string> StdOnlyNames = new HashSet<string>(
        new[] { "cout", "cin", "cerr", "endl", "string", "vector", "map", "set", "queue", "stack", "deque", "list", "pair", "sort", "max", "min", "swap", "reverse",
                "getline", "to_string", "unordered_map", "unordered_set", "make_pair", "ifstream", "ofstream", "stringstream", "shared_ptr", "unique_ptr", "array", "priority_queue" });

    private static void R(string pattern, string plainEn, string plainZh, string fixEn, string fixZh)
    {
        Rules.Add(new Rule { Re = new Regex(pattern, RegexOptions.Compiled), PlainEn = plainEn, PlainZh = plainZh, FixEn = fixEn, FixZh = fixZh });
    }

    private static void C(string pattern, Func<Match, string[]> custom)
    {
        Rules.Add(new Rule { Re = new Regex(pattern, RegexOptions.Compiled), Custom = custom });
    }

    static Hints()
    {
        // ---------------------------------------------------------------- characters, punctuation, brackets
        R(@"^stray '\\\d{3}' in program|^stray '\\\d+'",
          "The code contains a character the compiler can't read, most likely a Chinese (full-width) symbol such as ； ， （ ） or a curly quote.",
          "代码里混进了编译器看不懂的字符，多半是中文标点，比如 ；，（）或弯引号。",
          "Retype that full-width symbol with the English keyboard (half-width). Look at the underlined spot.",
          "这是中文符号，请切到英文输入法（半角）重新输入，看红线标出的位置。");
        R(@"^stray '(.+)' in program",
          "A stray '{1}' is in the code where it doesn't belong.",
          "代码里多出了一个不该出现的 '{1}'。",
          "Delete it, or check whether a quote, bracket or operator next to it is missing.",
          "删掉它，或检查它旁边是不是少了引号、括号或运算符。");
        R(@"^expected ',' or ';' before '(.+)'|^expected ';' before '(.+)'",
          "A semicolon ; is missing right before '{1}{2}'.",
          "在 '{1}{2}' 前面少了一个分号（;）。",
          "Add ; at the end of the line above (the error is often reported one line late).",
          "在上一行末尾补上分号 ;（报错位置往往比真正出错的位置晚一行）。");
        R(@"^expected ',' or ';' at end of input|^expected ';' at end of input",
          "The file ends while a statement is still unfinished: a semicolon ; is missing.",
          "文件结束时还有一条语句没写完：少了分号 ;。",
          "Add ; after the last statement, and check that every { has a matching }.",
          "在最后一条语句后补上 ;，并检查每个 { 是否都有配对的 }。");
        R(@"^expected ';' after (.+)",
          "A semicolon ; is needed after the {1}.",
          "{1} 的后面需要加一个分号 ;。",
          "For struct, class and enum definitions the closing } must be followed by ;",
          "struct、class、enum 的定义，右大括号 } 后面要加分号 ;");
        R(@"^expected '\}' at end of input|^expected '\}' before",
          "A closing brace } is missing: braces don't pair up.",
          "少了一个右大括号 }：大括号没有配对。",
          "Count the { and }; every { needs its own }. Indenting the code (Ctrl+Shift+A) makes the gap easy to see.",
          "数一数 { 和 }，每个 { 都要有对应的 }。按 Ctrl+Shift+A 整理缩进，缺在哪里一眼就能看到。");
        R(@"^expected declaration before '\}' token",
          "There is one } too many.",
          "多了一个右大括号 }。",
          "Remove the extra } or add the missing { earlier in the file.",
          "删掉多余的 }，或者检查前面是不是少了一个 {。");
        R(@"^expected '\)' before '(.+)'",
          "A closing parenthesis ) is missing before '{1}'.",
          "在 '{1}' 前面少了一个右括号 )。",
          "Check that every ( has a matching ), including in if / for / while conditions.",
          "检查每个 ( 都有对应的 )，特别是 if / for / while 的条件里。");
        R(@"^expected '\(' before '(.+)'",
          "An opening parenthesis ( is missing before '{1}'.",
          "在 '{1}' 前面少了一个左括号 (。",
          "if, for, while and switch need their condition in parentheses.",
          "if、for、while、switch 的条件必须写在括号里。");
        R(@"^expected '(.+)' before '(.+)'",
          "The compiler expected {1} before '{2}'.",
          "编译器在 '{2}' 前面期待看到 {1}。",
          "Something is missing or in the wrong order near '{2}'. Check the line above too.",
          "'{2}' 附近缺了东西或顺序不对，也看看上一行。");
        R(@"^expected '(.+)' at end of input",
          "The file ends too early: {1} is missing.",
          "文件在最后突然结束了：还缺少 {1}。",
          "Add the missing {1}, and check for unclosed brackets or quotes above.",
          "补上缺少的 {1}，并检查前面有没有没关上的括号或引号。");
        R(@"^expected primary-expression before '(.+)' token",
          "A value or variable is missing before '{1}'.",
          "在 '{1}' 前面缺了一个值或变量。",
          "Typical causes: a doubled operator, a stray comma, or a missing variable name.",
          "常见原因：运算符写重了、多了逗号、漏了变量名。");
        R(@"^expected unqualified-id before '(.+)' token",
          "'{1}' appears where a name should be.",
          "这里应该是一个名字，却出现了 '{1}'。",
          "Look for a missing or extra ; { } ( ) just before it, or code written outside a function.",
          "看看它前面是不是少了或多了 ; { } ( )，或者语句写在了函数外面。");
        R(@"^expected initializer before '(.+)'",
          "This declaration breaks off before '{1}'.",
          "这条声明在 '{1}' 前面就断了。",
          "Usually a missing semicolon on the line above or a misspelled type name.",
          "通常是上一行少了分号，或者类型名拼错了。");
        R(@"^expected (?:class-name|type-specifier|nested-name-specifier|identifier|constructor, destructor, or type conversion) before '?(.+?)'?(?: token)?$",
          "A type or name is expected before '{1}'.",
          "在 '{1}' 前面应该有类型或名字。",
          "Check the spelling of the type and that its #include is present.",
          "检查类型名拼写，以及对应的 #include 有没有写。");
        R(@"^expected (?:a )?(?:statement|expression)",
          "A statement is incomplete here.",
          "这里的语句没写完整。",
          "Look at this line and the one before for a missing value, operator or ;",
          "看看这一行和上一行，是不是少了值、运算符或分号。");
        R(@"^missing terminating (?:"" )?character|^missing terminating (.)",
          "A string (or character) was opened with a quote but never closed.",
          "字符串（或字符）开了引号，却没有关上。",
          "Add the closing quote on the same line. Strings can't span lines.",
          "在同一行补上结尾的引号。字符串不能跨行。");
        R(@"^unterminated comment",
          "A /* comment was opened but never closed with */",
          "/* 注释开了头，没有用 */ 结束。",
          "Add */ where the comment should end.",
          "在注释应该结束的地方补上 */");
        R(@"^extra tokens at end of #include directive",
          "There is extra text after the #include line.",
          "#include 这一行后面多了别的内容。",
          "Put nothing after the closing > or quote of an #include.",
          "#include 后面的 > 或引号之后不要再写别的东西。");
        R(@"^invalid preprocessing directive #(\w+)",
          "'#{1}' is not a known # command.",
          "'#{1}' 不是有效的 # 指令。",
          "Check the spelling: it should be include, define, ifdef, ifndef, endif ...",
          "检查拼写：应该是 include、define、ifdef、ifndef、endif 等。");
        R(@"^#include expects",
          "The #include line is malformed.",
          "#include 这一行写得不对。",
          "Write #include <iostream> for system headers or #include \"mine.h\" for your own.",
          "系统头文件写成 #include <iostream>，自己的头文件写成 #include \"mine.h\"。");
        R(@"^unknown escape sequence: '(.+)'",
          "The backslash sequence \\{1} isn't valid inside a string.",
          "字符串里的 \\{1} 不是有效的转义写法。",
          "To print a backslash write two: \\\\  (e.g. file paths).",
          "要输出反斜杠需要写两个：\\\\（比如文件路径）。");
        R(@"^multi-character character constant|^character constant too long",
          "A character constant holds more than one character.",
          "单引号里写了多个字符。",
          "Single quotes hold exactly one character ('a'); use double quotes for text (\"abc\").",
          "单引号里只能放一个字符（'a'），文字要用双引号（\"abc\"）。");

        // ---------------------------------------------------------------- names and types
        C(@"^'(\w+)' (?:was not declared in this scope|has not been declared|is not a member of 'std')|^'(\w+)' undeclared \(first use in this function\)", m =>
        {
            string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (name == "nullptr" || name == "constexpr" || name == "decltype" || name == "static_assert" || name == "override" || name == "final")
                return new[] { "'" + name + "' is a newer C++ keyword that the selected C++ standard doesn't have.", "'" + name + "' 是较新的 C++ 关键字，当前选择的 C++ 标准里没有。",
                    "Open Settings > Compiler and set C++ standard to C++11 or newer.", "到 设置 > 编译器 里把 C++ 标准选成 C++11 或更新。" };
            string plainEn = "The compiler doesn't know '" + name + "': it was never declared (or is misspelled).";
            string plainZh = "编译器不认识 '" + name + "'：还没声明就用了，或者拼写错了。";
            string header;
            if (HeaderOf.TryGetValue(name, out header))
            {
                string use = StdOnlyNames.Contains(name) ? " and write std::" + name + " (or add 'using namespace std;')" : "";
                string useZh = StdOnlyNames.Contains(name) ? "，并写成 std::" + name + "（或加一句 using namespace std;）" : "";
                return new[] { plainEn, plainZh,
                    "'" + name + "' comes from the standard library: add #include <" + header + ">" + use + ".",
                    "'" + name + "' 属于标准库：请加上 #include <" + header + ">" + useZh + "。" };
            }
            return new[] { plainEn, plainZh,
                "Check the spelling and capital letters, and declare the name before its first use. A variable declared inside { } only exists inside those braces.",
                "检查拼写和大小写，并在第一次使用之前声明。在 { } 里声明的变量，出了这对大括号就不存在了。" };
        });
        R(@"^'(\w+)' does not name a type",
          "'{1}' isn't a type the compiler knows.",
          "编译器不认识 '{1}' 这个类型。",
          "Check the spelling, add the #include it needs (string → <string>, vector → <vector>) and write std:: in front.",
          "检查拼写，补上它需要的 #include（string → <string>，vector → <vector>），并在前面写 std::。");
        R(@"^'(.+)' is not a class, namespace, or enumeration|^'(.+)' is not a namespace-name",
          "'{1}{2}' can't be used with :: here.",
          "'{1}{2}' 不能这样用 :: 访问。",
          "Check the spelling of the name before ::, and that it is a class or namespace.",
          "检查 :: 前面的名字拼写，它必须是类或命名空间。");
        R(@"^implicit declaration of function '(\w+)'",
          "The function '{1}' is used before the compiler has seen it.",
          "函数 '{1}' 在编译器见到它之前就被调用了。",
          "Add the #include that declares it, or put a prototype above main.",
          "补上声明它的 #include，或在 main 上面先写出函数原型。");
        R(@"^unknown type name '(\w+)'",
          "'{1}' is not a known type.",
          "'{1}' 不是已知的类型。",
          "Check the spelling or the #include.",
          "检查拼写或 #include。");
        R(@"^ISO C\+\+ forbids declaration of '(\w+)' with no type",
          "'{1}' is declared without a type.",
          "'{1}' 声明时没有写类型。",
          "Write the type in front, for example int {1}; (or the name is misspelled).",
          "在前面写上类型，例如 int {1};（也可能是名字拼错了）。");
        R(@"^'(\w+)' redeclared as different kind of symbol|^conflicting declaration|^conflicting (?:types|return type)",
          "A name is declared twice with different meanings.",
          "同一个名字被声明了两次，而且含义不同。",
          "Rename one of them, or make the declarations identical.",
          "给其中一个改名，或让两处声明完全一致。");
        R(@"^redefinition of '(.+)'|^redeclaration of '(.+)'|^'(.+)' redeclared",
          "'{1}{2}{3}' is defined twice in the same place.",
          "'{1}{2}{3}' 在同一个范围里被定义了两次。",
          "Delete one of the definitions. For headers, add #pragma once at the top.",
          "删掉其中一个定义。头文件的开头加上 #pragma once。");
        R(@"^previous declaration of|^previously declared|^'(.+)' previously defined here",
          "This is the earlier place where the same name was declared.",
          "这是同一个名字之前被声明的位置。",
          null, null);
        R(@"^(?:invalid use of|use of) incomplete type '?(.+?)'?$|^invalid use of incomplete type",
          "'{1}' is only forward-declared, so the compiler doesn't know its contents yet.",
          "编译器只知道有 '{1}' 这个类型，却不知道它里面有什么。",
          "#include the header that defines it before using it.",
          "在用它之前，先 #include 定义它的头文件。");
        R(@"^'(.+)' is private|^'(.+)' is protected",
          "'{1}{2}' is private: only the class itself may use it.",
          "'{1}{2}' 是私有成员：只有这个类自己能使用。",
          "Make it public, or add a public function that gives access to it.",
          "把它改成 public，或者写一个 public 的函数来访问它。");
        R(@"^(?:'(.+)' )?has no member named '(\w+)'|^'(.+)' has no member named '(\w+)'",
          "{1}{3} has nothing called '{2}{4}'.",
          "{1}{3} 里没有叫 '{2}{4}' 的成员。",
          "Check the spelling and capitals of the member name (for example size, not Size).",
          "检查成员名的拼写和大小写（比如是 size，不是 Size）。");
        R(@"^request for member '(\w+)' in '(.+)', which is of (?:non-class|pointer) type '(.+)'",
          "'.{1}' was used on '{2}', but that is a plain {3}, not a struct or class object.",
          "对 '{2}' 用了 .{1}，但它只是普通的 {3}，不是结构体或类的对象。",
          "For a pointer use -> instead of . ; for numbers there are no members at all.",
          "指针要用 -> 而不是 . ；普通数字没有成员可以访问。");
        R(@"^invalid types '(.+)' for array subscript",
          "[] can't be used on '{1}'.",
          "'{1}' 不能用 [] 取下标。",
          "Only arrays, pointers and containers have [ ]. Check that the variable is what you think it is, and that the index is an integer.",
          "只有数组、指针和容器才能用 [ ]。检查变量类型，下标必须是整数。");

        // ---------------------------------------------------------------- calls and conversions
        R(@"^no matching function for call to '(.+)'",
          "Calling {1}: no version of that function accepts these arguments.",
          "调用 {1} 时，没有哪个版本的函数能接收这样的参数。",
          "Check how many arguments you pass and their types against the function's definition.",
          "对照函数的定义，检查传入参数的个数和类型。");
        R(@"^too many arguments to function|^too many arguments to '(.+)'",
          "The function is called with more arguments than it takes.",
          "调用函数时传的参数比它需要的多。",
          "Remove the extra arguments or change the function's parameter list.",
          "删掉多余的参数，或者修改函数的参数列表。");
        R(@"^too few arguments to function|^too few arguments to '(.+)'",
          "The function is called with fewer arguments than it needs.",
          "调用函数时传的参数比它需要的少。",
          "Pass every parameter the function declares.",
          "把函数声明的每个参数都传上。");
        R(@"^no match for 'operator(.+)' \(operand types are '(.+)' and '(.+)'\)|^no match for 'operator(.+)'",
          "The operator {1}{4} can't be used with these types ({2} and {3}).",
          "运算符 {1}{4} 不能用在这两种类型之间（{2} 和 {3}）。",
          "Check the types on both sides. To print or read your own class with << or >> you must define operator<< / operator>>.",
          "检查两边的类型。想用 << 或 >> 输出自己的类，需要自己写 operator<< / operator>>。");
        R(@"^invalid operands of types '(.+)' and '(.+)' to binary 'operator(.+)'",
          "The operator {3} can't be used between {1} and {2}.",
          "运算符 {3} 不能用在 {1} 和 {2} 之间。",
          "Convert one side so both have compatible types (for example turn text into a number with stoi).",
          "把其中一边转换成兼容的类型（比如用 stoi 把文字转成数字）。");
        R(@"^invalid conversion from '(.+)' to '(.+)'",
          "A {1} can't be turned into a {2} automatically.",
          "{1} 不能自动转换成 {2}。",
          "Convert explicitly, e.g. (int)x, or change the variable's type.",
          "手动转换，例如 (int)x，或者改变量的类型。");
        R(@"^cannot convert '(.+)' to '(.+)'",
          "A {1} can't be used where a {2} is needed.",
          "需要 {2} 的地方给了 {1}，不能转换。",
          "Pass a value of the type the function expects.",
          "传入函数需要的那种类型的值。");
        R(@"^(?:cannot|can't) convert '(.+)' to '(.+)' in (?:assignment|initialization|return)",
          "A {1} can't be stored in a {2}.",
          "不能把 {1} 存进 {2}。",
          "Change one of the types so they match.",
          "改一下类型，让两边一致。");
        R(@"^invalid initialization of (?:non-const )?reference of type '(.+)' from",
          "A reference of type {1} can't be set from that value.",
          "不能用这个值去初始化 {1} 类型的引用。",
          "A non-const reference needs a real variable, not a literal or a temporary.",
          "非 const 的引用必须绑定到真正的变量，不能绑定到字面量或临时值。");
        R(@"^void value not ignored as it ought to be",
          "The function returns nothing (void), but its result is used.",
          "这个函数没有返回值（void），你却在使用它的结果。",
          "Don't assign or print the result of a void function.",
          "不要对 void 函数的结果赋值或输出。");
        R(@"^return-statement with no value, in function returning '(.+)'",
          "This function must return a {1}, but 'return' has no value.",
          "这个函数必须返回 {1}，但 return 后面没有值。",
          "Write return <value>; with a value of that type.",
          "写成 return 值; 并返回对应类型的值。");
        R(@"^return-statement with a value, in function returning 'void'",
          "A void function can't return a value.",
          "void 函数不能返回值。",
          "Remove the value after return, or change void to the real return type.",
          "删掉 return 后面的值，或把 void 改成真正的返回类型。");
        R(@"^'main' must return 'int'|^return type of 'main' is not 'int'",
          "main must return an int.",
          "main 函数必须返回 int。",
          "Write int main() and end it with return 0;",
          "写成 int main()，结尾 return 0;");
        R(@"^'main' is usually a function",
          "'main' is being used as something other than the program's entry function.",
          "'main' 被当成了别的东西，而它应该是程序的入口函数。",
          "Rename your variable and keep int main() as the entry point.",
          "给你的变量改个名，让 int main() 做程序入口。");
        R(@"^deprecated conversion from string constant to 'char\*'|^ISO C\+\+ forbids converting a string constant to 'char\*'",
          "A text literal is assigned to a plain char*.",
          "把一段文字常量赋给了普通的 char*。",
          "Use const char* (or std::string) for text literals.",
          "文字常量请用 const char*（或 std::string）来保存。");
        R(@"^narrowing conversion of '(.+)' from '(.+)' to '(.+)'",
          "{1} ({2}) doesn't fit into a {3} without losing information.",
          "{1}（{2}）放进 {3} 会丢失信息。",
          "Use a cast like static_cast<{3}>(...) or pick a bigger type.",
          "用 static_cast<{3}>(...) 转换，或者换更大的类型。");
        R(@"^converting to '(.+)' from '(.+)' may (?:alter|change) its value",
          "A {2} is squeezed into a {1}; digits may be lost.",
          "把 {2} 放进 {1}，可能丢失小数或高位。",
          "Use the same type, or cast on purpose.",
          "用同一种类型，或者明确地强制转换。");
        R(@"^overflow in implicit constant conversion|^integer overflow in expression|^large integer implicitly truncated",
          "The number is too big for its type.",
          "这个数字超出了所用类型能表示的范围。",
          "Use a wider type such as long long, and write 123LL for big literals.",
          "改用更宽的类型，比如 long long，大数字写成 123LL。");
        R(@"^passing '(.+)' as 'this' argument (?:of '(.+)' )?discards qualifiers",
          "A const object is used to call a function that could change it.",
          "对 const 对象调用了可能修改它的函数。",
          "Mark that member function const, or don't make the object const.",
          "给那个成员函数加上 const，或者别把对象声明成 const。");
        R(@"^assignment of read-only (?:variable|location|reference) '?(.+?)'?$",
          "'{1}' is const (read-only), so it can't be changed.",
          "'{1}' 是 const（只读）的，不能被修改。",
          "Remove const from its declaration, or use a different variable.",
          "去掉它声明里的 const，或者换一个变量。");
        R(@"^increment of read-only|^decrement of read-only",
          "A const value can't be incremented or decremented.",
          "不能对 const 的值做自增或自减。",
          "Remove const or copy the value to a normal variable first.",
          "去掉 const，或先复制到普通变量里。");
        R(@"^lvalue required as left operand of assignment",
          "Only a variable can be on the left side of =.",
          "= 的左边只能是变量。",
          "Did you mean == to compare? Write == when you test whether two values are equal.",
          "你是不是想比较两个值？比较要写 ==。");
        R(@"^lvalue required as (?:increment|decrement|unary '&') operand",
          "++, -- and & only work on variables.",
          "++、-- 和 & 只能用在变量上。",
          "Apply it to a variable, not to a number or a calculated result.",
          "对变量使用，而不是对数字或计算结果使用。");
        R(@"^suggest parentheses around assignment used as truth value",
          "A single = (assignment) is used as a condition.",
          "条件里用的是单个 =，那是赋值，不是比较。",
          "To compare, write == (e.g. if (a == 5)).",
          "要比较，写成 ==（例如 if (a == 5)）。");
        R(@"^suggest parentheses around '(.+)' within '(.+)'|^suggest explicit braces to avoid ambiguous 'else'",
          "The compiler can't tell how this expression or if/else is grouped.",
          "编译器无法确定这个表达式（或 if/else）是怎么分组的。",
          "Add parentheses or { } to make your intention explicit.",
          "加上括号或 { }，把你的意图写清楚。");
        R(@"^invalid use of non-static member function|^cannot call member function '(.+)' without object",
          "A member function is called without an object.",
          "调用成员函数时没有指明是哪个对象。",
          "Create an object and call obj.function(), or make the function static.",
          "先创建对象再调用 obj.function()，或把函数声明为 static。");
        R(@"^cannot declare variable '(.+)' to be of abstract type|^invalid abstract (?:return|parameter) type",
          "'{1}' belongs to an abstract class, which can't be created directly.",
          "'{1}' 属于抽象类，不能直接创建对象。",
          "Implement all pure virtual functions in a derived class and create that instead.",
          "在派生类里实现全部纯虚函数，然后创建派生类的对象。");
        R(@"^use of deleted function",
          "That function was deleted, so it can't be used.",
          "这个函数被删除了，不能使用。",
          "Often copying a unique_ptr or a stream: move it or pass a reference.",
          "常见原因是拷贝了 unique_ptr 或流对象：改用移动或传引用。");
        R(@"^ISO C\+\+ forbids comparison between pointer and integer",
          "A pointer is compared with a number.",
          "把指针和整数放在一起比较了。",
          "For text compare with strcmp or use std::string; for characters use single quotes ('a').",
          "比较文字请用 strcmp 或 std::string；比较字符请用单引号 ('a')。");
        R(@"^ISO C\+\+ forbids variable length array|^variable-sized object may not be initialized|^size of array '(.+)' is not an integral constant",
          "An array size must be a constant known at compile time.",
          "数组长度必须是编译时就确定的常量。",
          "Use a const / #define size, or a std::vector when the size comes from input.",
          "用 const 常量或 #define 当长度；长度来自输入时改用 std::vector。");
        R(@"^jump to case label|^crosses initialization of",
          "A variable is declared inside a case without braces.",
          "在 case 里声明了变量，却没有加大括号。",
          "Wrap that case's code in { }.",
          "把这个 case 里的代码用 { } 括起来。");
        R(@"^'nullptr' was not declared|^'(?:auto|override|final|constexpr|nullptr|decltype)' .*(?:only available|requires)|(?:only available|requires) with -std=(?:c|gnu)\+\+(?:11|0x)|^(?:ISO C\+\+ forbids|non-static data member).*initialization|is only available with|^range-based 'for' loops are not allowed in C\+\+98 mode",
          "This feature needs a newer C++ standard than the one selected.",
          "这个写法需要比当前选择更新的 C++ 标准。",
          "Open Settings > Compiler and set C++ standard to C++11 or newer.",
          "到 设置 > 编译器 里把 C++ 标准选成 C++11 或更新。");
        R(@"^'for' loop initial declarations are only allowed in C99",
          "Declaring a variable inside for(...) needs a newer C standard.",
          "在 for(...) 里声明变量，需要更新的 C 语言标准。",
          "Declare the variable before the loop, or compile as C99/C11.",
          "把变量放到循环前面声明，或用 C99/C11 编译。");

        // ---------------------------------------------------------------- linker
        R(@"^undefined reference to 'WinMain|^undefined reference to 'main'",
          "The program has no main function (or it is misspelled).",
          "程序里没有 main 函数（或者拼错了）。",
          "Every program needs int main(). Check the name and that this file is part of the project.",
          "每个程序都需要 int main()。检查名字拼写，以及这个文件是否在项目里。");
        R(@"^undefined reference to 'vtable for (.+)'",
          "The class {1} has a virtual function without a body.",
          "类 {1} 里有虚函数没有写函数体。",
          "Define every virtual function (or make it = 0 to make the class abstract).",
          "给每个虚函数写上函数体（或写成 = 0 变成抽象类）。");
        R(@"^undefined reference to '(.+)'",
          "The linker can't find the code of '{1}': it is declared but never written.",
          "链接器找不到 '{1}' 的代码：只声明了，没有真正实现。",
          "Define the function, add its .cpp file to the project, or link the library that contains it.",
          "把函数实现写出来、把对应的 .cpp 加入项目，或链接包含它的库。");
        R(@"^multiple definition of '(.+)'",
          "'{1}' is defined in more than one file.",
          "'{1}' 在多个文件里被重复定义了。",
          "Define things only once: put definitions in one .cpp and only declarations in headers.",
          "每样东西只能定义一次：定义放在一个 .cpp 里，头文件里只放声明。");
        R(@"^cannot find -l(\w+)|^cannot find -l(.+)",
          "The library '{1}{2}' needed by the program can't be found.",
          "程序需要的库 '{1}{2}' 找不到。",
          "Check the name in Project Options > Parameters, or install that library.",
          "检查 项目属性 > 参数 里库的名字，或安装这个库。");
        R(@"^cannot open output file (.+?): (?:Permission denied|Device or resource busy)|^Permission denied",
          "The program file is locked: the program is still running.",
          "程序文件被占用：上一次运行的程序还没关闭。",
          "Close its window (or press Stop), then build again.",
          "关闭它的窗口（或点“停止”），再重新编译。");
        R(@"^cannot open output file|^cannot open linker script",
          "The program file can't be written.",
          "程序文件无法写入。",
          "Check that the folder exists and isn't read-only.",
          "检查文件夹是否存在、是否只读。");
        C(@"^(.+?): No such file or directory", m =>
        {
            string f = m.Groups[1].Value.Trim();
            string fixEn = "Check the file name and the folder it lives in; for your own headers use #include \"name.h\".";
            string fixZh = "检查文件名和所在文件夹；自己的头文件请写成 #include \"name.h\"。";
            string lower = f.ToLowerInvariant();
            if (lower == "iostream.h" || lower == "fstream.h" || lower == "iomanip.h")
            {
                string bare = lower.Replace(".h", "");
                fixEn = "That is an obsolete header: write #include <" + bare + "> (no .h) and add using namespace std;";
                fixZh = "这是过时的头文件写法：改成 #include <" + bare + ">（不带 .h），并加上 using namespace std;";
            }
            else if (lower == "stdafx.h" || lower == "pch.h")
            {
                fixEn = "stdafx.h belongs to Visual Studio projects. Delete that #include line.";
                fixZh = "stdafx.h 是 Visual Studio 工程专用的文件。删掉这一行 #include 即可。";
            }
            return new[] { "The file '" + f + "' can't be found.", "找不到文件 '" + f + "'。", fixEn, fixZh };
        });
        R(@"^ld returned (\d+) exit status|^collect2(?:\.exe)?: error: ld returned",
          "Linking failed: see the lines above for the real reason.",
          "链接失败：真正的原因在上面几行。",
          null, null);

        // ---------------------------------------------------------------- warnings
        R(@"^control reaches end of non-void function",
          "This function promises to return a value, but some path ends without one.",
          "这个函数承诺要返回一个值，但有的执行路径走到结尾也没有 return。",
          "Add a return at the end, and make sure every if/else branch returns.",
          "在结尾补上 return，并确保每个 if/else 分支都有 return。");
        R(@"^unused variable '(\w+)'",
          "The variable '{1}' is declared but never used.",
          "变量 '{1}' 声明了却从来没用过。",
          "Use it or delete it. Harmless, but often a sign of a typo elsewhere.",
          "用上它，或者删掉。不影响运行，但常常说明别处有拼写错误。");
        R(@"^variable '(\w+)' set but not used",
          "'{1}' gets a value but is never read afterwards.",
          "'{1}' 被赋了值，之后却没有被读取过。",
          "Print or use it, or remove it.",
          "输出或使用它，或者删掉。");
        R(@"^unused parameter '(\w+)'",
          "The parameter '{1}' is never used.",
          "参数 '{1}' 在函数里没有被用到。",
          "Harmless; remove the name (keep the type) if it's intentional.",
          "不影响运行；如果是有意的，可以只保留类型、去掉名字。");
        R(@"^'(\w+)' defined but not used",
          "'{1}' is defined but nothing uses it.",
          "'{1}' 定义了，却没有任何地方用到它。",
          "Delete it or call it.",
          "删掉它，或者去调用它。");
        R(@"^'(\w+)' (?:may be|is) used uninitialized(?: in this function)?",
          "The variable '{1}' may be read before it has been given a value.",
          "变量 '{1}' 可能在被赋值之前就被读取了。",
          "Give it an initial value where it is declared, e.g. int {1} = 0;",
          "声明时就给它一个初始值，例如 int {1} = 0;");
        R(@"^comparison between signed and unsigned|^comparison of integer expressions of different signedness",
          "A signed number is compared with an unsigned one, which can give surprising results.",
          "把有符号数和无符号数放在一起比较，结果可能出人意料。",
          "Use the same type on both sides (e.g. compare i with v.size() using size_t i).",
          "让两边类型一致（比如循环变量 i 用 size_t，去和 v.size() 比较）。");
        R(@"^format '(%[^']*)' expects (?:argument of )?type '(.+)', but argument (\d+) has type '(.+)'",
          "printf/scanf format {1} wants {2}, but argument {3} is {4}.",
          "printf/scanf 的格式 {1} 需要 {2}，但第 {3} 个参数是 {4}。",
          "Match the format to the type: %d int, %lld long long, %f float, %lf double (scanf), %c char, %s text.",
          "格式要和类型对应：%d int，%lld long long，%f float，scanf 读 double 用 %lf，%c char，%s 字符串。");
        R(@"^too (?:many|few) arguments for format|^format not a string literal|^format '(.+)' expects a matching",
          "The number of printf/scanf formats and arguments differ.",
          "printf/scanf 的格式个数和参数个数对不上。",
          "Each % in the format string needs exactly one argument.",
          "格式字符串里每个 % 都要有一个对应的参数。");
        R(@"^ignoring return value of '(.+)', declared with attribute warn_unused_result",
          "The result of {1} is ignored.",
          "{1} 的返回值被忽略了。",
          "Usually this is only a warning; check the result if the call can fail (e.g. scanf).",
          "通常只是警告；如果调用可能失败（比如 scanf），最好检查它的返回值。");
        R(@"^(?:reference|address) (?:to|of) local variable '(\w+)' returned",
          "The function returns the address of '{1}', which disappears when the function ends.",
          "函数返回了局部变量 '{1}' 的地址，函数一结束它就不存在了。",
          "Return the value itself, or use static / new / std::vector instead.",
          "直接返回值本身，或者改用 static、new、std::vector。");
        R(@"^array subscript is (?:above|below) array bounds|^array subscript .* out of bounds|out of bounds",
          "An index goes past the end (or before the start) of an array.",
          "下标超出了数组的范围。",
          "Valid indexes of an array of size N are 0 to N-1.",
          "长度为 N 的数组，下标只能是 0 到 N-1。");
        R(@"^division by zero",
          "A number is divided by zero.",
          "出现了除以 0。",
          "Check the divisor before dividing.",
          "除之前先检查除数是不是 0。");
        R(@"^statement has no effect|^value computed is not used|^right-hand operand of comma expression has no effect",
          "This statement does nothing.",
          "这条语句没有任何效果。",
          "Maybe == was written instead of =, or a function call lacks its ( ).",
          "是不是把 = 写成了 ==，或者调用函数时忘了写 ( )？");
        R(@"^empty body in an if-statement|^suggest braces around empty body",
          "An if (or else) is followed directly by ; so it controls nothing.",
          "if（或 else）后面直接跟了分号，它什么也没控制。",
          "Delete the ; right after the condition.",
          "删掉条件括号后面那个分号。");
        R(@"^this statement may fall through",
          "A case runs on into the next case because 'break;' is missing.",
          "这个 case 没有写 break;，会继续执行下一个 case。",
          "Add break; at the end of the case unless that is on purpose.",
          "除非有意如此，请在 case 末尾加上 break;。");
        R(@"^comparison is always (true|false)",
          "This comparison always gives the same answer.",
          "这个比较的结果永远是同一个。",
          "Check the types and the values you compare.",
          "检查比较的类型和数值。");
        R(@"^missing initializer|^too many initializers|^excess elements in",
          "The list of initial values doesn't match the array or struct.",
          "初始值的个数和数组（或结构体）对不上。",
          "Give no more values than there are elements.",
          "给的初始值不要比元素个数多。");
        R(@"^initializer-string for array of chars is too long",
          "The text is longer than the char array that should hold it.",
          "文字比用来保存它的 char 数组还长。",
          "Make the array one longer than the text (for the ending \\0), or use std::string.",
          "数组长度至少要比文字多 1（放结尾的 \\0），或者改用 std::string。");
    }

    // ------------------------------------------------------------------ lookup

    private static string Normalize(string message)
    {
        return (message ?? "").Replace('‘', '\'').Replace('’', '\'').Replace('`', '\'').Replace('“', '"').Replace('”', '"').Trim();
    }

    private static string Fill(string template, Match m)
    {
        if (template == null) return null;
        return Regex.Replace(template, @"\{(\d)\}", e =>
        {
            int i = int.Parse(e.Groups[1].Value);
            return i < m.Groups.Count ? m.Groups[i].Value : "";
        });
    }

    public static Translation Translate(string message)
    {
        string m = Normalize(message);
        foreach (var rule in Rules)
        {
            var match = rule.Re.Match(m);
            if (!match.Success) continue;
            string[] t = rule.Custom != null ? rule.Custom(match)
                : new[] { Fill(rule.PlainEn, match), Fill(rule.PlainZh, match), Fill(rule.FixEn, match), Fill(rule.FixZh, match) };
            return new Translation { Plain = L.T(t[0], t[1]), Fix = t[2] == null ? null : L.T(t[2], t[3]) };
        }
        return null;
    }

    // Switched by Settings > Learning aids > Simple error translation.
    public static bool SimpleMode = true;

    public static void Apply(Diagnostic d)
    {
        var t = Translate(d.Message);
        if (t == null) return;
        d.Plain = t.Plain;
        d.Hint = t.Fix;
    }

    // Kept for callers that only want the fix suggestion.
    public static string For(string message)
    {
        var t = Translate(message);
        return t != null ? t.Fix : null;
    }

    // How many rules exist (used by tests).
    public static int RuleCount { get { return Rules.Count; } }
}
}
