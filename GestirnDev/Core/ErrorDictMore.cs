using System;
using System.Collections.Generic;
using System.Linq;

namespace GestirnDev
{
// Batch: the preprocessor, the C language, the linker and the compiler warnings.
internal static partial class Hints
{
    private static string CHeader(string cxxHeader)
    {
        switch (cxxHeader)
        {
            case "cstdio": return "stdio.h";
            case "cstring": return "string.h";
            case "cstdlib": return "stdlib.h";
            case "cmath": return "math.h";
            case "ctime": return "time.h";
            case "cctype": return "ctype.h";
            case "climits": return "limits.h";
            case "cassert": return "assert.h";
            default: return cxxHeader;
        }
    }

    private static void AddPreprocessorRules()
    {
        R(@"^unterminated #(if|ifdef|ifndef|else|elif)", "unterminated #ifdef",
          "An #{1} has no matching #endif.", "有一个 #{1} 没有配对的 #endif。",
          "Add #endif where the conditional block should end.", "在条件块应该结束的地方补上 #endif。");
        R(@"^#(endif|else|elif) without #if", "#endif without #if",
          "An #{1} has no #if in front of it.", "这个 #{1} 前面没有配对的 #if。",
          "Remove it, or check that the #if / #ifdef / #ifndef above is spelled correctly.", "把它删掉，或者检查上面的 #if / #ifdef / #ifndef 有没有写对。");
        R(@"^no macro name given in #(\w+) directive", "no macro name given in #define directive",
          "#{1} must be followed by a name.", "#{1} 后面必须有名字。",
          "Write #define NAME value  (or #ifdef NAME).", "写成 #define 名字 值（或 #ifdef 名字）。");
        R(@"^macro names must be identifiers", "macro names must be identifiers",
          "A macro name must be an ordinary name (letters, digits, underscore).", "宏的名字必须是普通名字（字母、数字、下划线）。",
          "Don't start it with a digit and don't use symbols.", "不要以数字开头，也不要有符号。");
        R(@"^#error (.*)", "#error stop here",
          "The program itself stops the compilation on purpose: \"{1}\"", "程序里的 #error 主动中止了编译：“{1}”。",
          "Read the condition around that #error: it usually means a needed setting or compiler is missing.", "看看这条 #error 外面的条件：通常是缺少某个必要的设置或编译器。");
        R(@"^#warning (.*)", "#warning check this",
          "A message left in the code with #warning: \"{1}\"", "代码里用 #warning 留下的提示：“{1}”。",
          "It does not stop the build.", "它不会阻止编译。");
        R(@"^ignoring #pragma (.+)", "ignoring #pragma foo  [-Wunknown-pragmas]",
          "This compiler doesn't know the instruction #pragma {1}, so it is skipped.", "这个编译器不认识 #pragma {1} 这条指令，已经跳过它。",
          "Harmless. Such lines usually come from other compilers (for example Visual Studio's #pragma comment or #pragma region).", "不影响运行。这类语句通常来自别的编译器（比如 Visual Studio 的 #pragma comment、#pragma region）。");
        R(@"^#include nested too deeply", "#include nested too deeply",
          "Headers include each other in a circle.", "头文件互相 #include，绕成了圈。",
          "Put #pragma once (or an include guard #ifndef / #define / #endif) at the top of every header.", "给每个头文件开头加上 #pragma once（或 #ifndef / #define / #endif 保护）。");
        R(@"^missing terminating > character", "missing terminating > character",
          "The > that closes an #include <...> is missing.", "#include <...> 少了结尾的 >。",
          "Write #include <iostream> with both brackets.", "写成 #include <iostream>，两个尖括号都要有。");
        R(@"^macro ""(\w+)"" requires (\d+) arguments?, but only (\d+) given", "macro \"MAX\" requires 2 arguments, but only 1 given",
          "The macro {1} needs {2} argument(s) but got {3}.", "宏 {1} 需要 {2} 个参数，只给了 {3} 个。",
          "Give all of them, separated by commas.", "把参数补全，用逗号隔开。");
        R(@"^macro ""(\w+)"" passed (\d+) arguments?, but takes just (\d+)", "macro \"MAX\" passed 3 arguments, but takes just 2",
          "The macro {1} takes {3} argument(s) but got {2}.", "宏 {1} 只接受 {3} 个参数，却给了 {2} 个。",
          "Remove the extra argument. Commas inside the argument need extra parentheses.", "去掉多余的参数。参数里本身带逗号的话要多加一层括号。");
        R(@"^unterminated argument list invoking macro ""(\w+)""", "unterminated argument list invoking macro \"MAX\"",
          "A call of the macro {1} is missing its closing parenthesis.", "调用宏 {1} 时少了右括号。",
          "Add the ) at the end of the call.", "在调用的末尾补上 )。");
        R(@"^""(\w+)"" redefined", "\"MAX\" redefined [enabled by default]",
          "The macro {1} is defined a second time.", "宏 {1} 被定义了第二次。",
          "Keep only one #define, or rename yours (names like MAX / MIN clash with library macros).", "只保留一个 #define，或者给你的宏换个名字（MAX、MIN 这类名字容易和库里的宏撞车）。");
        R(@"^this is the location of the previous definition", "this is the location of the previous definition",
          "(Continues the error above) The earlier definition is here.", "（接上一条）之前的定义在这里。",
          "Delete one of the two.", "删掉两处中的一处。");
        R(@"^missing '\)' in expression|^missing binary operator before token ""(.+)""|^#if with no expression|^operator '(.+)' has no right operand|^token ""(.+)"" is not valid in preprocessor expressions", "operator '+' has no right operand",
          "The condition of an #if is written incorrectly.", "#if 后面的条件写得不对。",
          "Use constants and defined(NAME) only; no variables or function calls.", "只能用常量和 defined(名字)，不能有变量或函数调用。");
        R(@"^""(\w+)"" is not defined", "\"FOO\" is not defined [-Wundef]",
          "The macro {1} used in #if is not defined, so it counts as 0.", "#if 里用的宏 {1} 没有定义，被当成 0。",
          "Define it first, or test with #ifdef {1}.", "先 #define 它，或者改用 #ifdef {1}。");
        R(@"^__VA_ARGS__ can only appear", "__VA_ARGS__ can only appear in the expansion of a C99 variadic macro",
          "__VA_ARGS__ is used outside a variadic macro.", "__VA_ARGS__ 用在了不能用的地方。",
          "It only works inside #define NAME(...) with ... in the parameter list.", "它只能出现在参数列表带 ... 的 #define NAME(...) 里面。");
        R(@"^#pragma once in main file", "#pragma once in main file",
          "#pragma once is in a .cpp file, where it does nothing.", "#pragma once 写在了 .cpp 文件里，那里它没有作用。",
          "Use it only in header files (.h).", "只在头文件（.h）里使用它。");
        R(@"^missing terminating (?:"" )?character|^missing terminating (.) character", "missing terminating \" character",
          "A string (or character) was opened with a quote but never closed.", "字符串（或字符）开了引号，却没有关上。",
          "Add the closing quote on the same line. Strings can't span lines.", "在同一行补上结尾的引号。字符串不能跨行。");
    }

    private static void AddCRules()
    {
        C(@"^implicit declaration of function '(\w+)'", (m, ctx) =>
        {
            string name = m.Groups[1].Value, header;
            string hint = HeaderOf.TryGetValue(name, out header) ? "#include <" + CHeader(header) + ">" : null;
            return new[]
            {
                "The function '" + name + "' is used before the compiler has seen it.", "函数 '" + name + "' 在编译器见到它之前就被调用了。",
                hint != null ? "Add " + hint + " at the top." : "Add the #include that declares it, or put a prototype above main.", hint != null ? "在文件开头加上 " + hint + "。" : "补上声明它的 #include，或在 main 上面先写出函数原型。",
            };
        }, "implicit declaration of function 'printf' [-Wimplicit-function-declaration]");
        R(@"^incompatible implicit declaration of built-in function '(\w+)'", "incompatible implicit declaration of built-in function 'printf'",
          "The standard function '{1}' is used without its #include.", "用了标准函数 '{1}'，却没有 #include 它的头文件。",
          "printf / scanf / puts need #include <stdio.h>, strlen / strcpy <string.h>, malloc / exit <stdlib.h>, sqrt / pow <math.h>.", "printf / scanf / puts 需要 #include <stdio.h>，strlen / strcpy 需要 <string.h>，malloc / exit 需要 <stdlib.h>，sqrt / pow 需要 <math.h>。");
        R(@"^'(true|false|bool)' undeclared|^unknown type name 'bool'", "'true' undeclared (first use in this function)",
          "C has no bool, true and false built in.", "C 语言本身没有 bool、true、false。",
          "Add #include <stdbool.h>, or use int with 1 and 0.", "加上 #include <stdbool.h>，或者用 int 加 1 和 0。");
        R(@"^unknown type name 'string'", "unknown type name 'string'",
          "C has no string type.", "C 语言里没有 string 这种类型。",
          "Use a char array (char s[100];) or a char pointer (char* s). To use std::string save the file as .cpp.", "用字符数组（char s[100];）或字符指针（char* s）。想用 std::string 请把文件存成 .cpp。");
        R(@"^unknown type name '(\w+)'", "unknown type name 'size_t'",
          "'{1}' is not a known type.", "'{1}' 不是已知的类型。",
          "Check the spelling, or the #include that defines it (size_t: <stddef.h>, FILE: <stdio.h>).", "检查拼写，或定义它的 #include（size_t 在 <stddef.h>，FILE 在 <stdio.h>）。");
        R(@"^conflicting types for '(\w+)'", "conflicting types for 'f'",
          "The function {1} is declared and defined with different parameters or return types.", "函数 {1} 的声明和定义，参数或返回类型不一致。",
          "Make the prototype and the definition identical.", "让函数原型和函数定义完全一致。");
        R(@"^passing argument (\d+) of '(.+?)' makes pointer from integer without a cast", "passing argument 1 of 'f' makes pointer from integer without a cast",
          "Argument {1} of {2} must be a pointer (or array), but an integer was given.", "{2} 的第 {1} 个参数需要指针（或数组），却给了整数。",
          "Pass the array itself (a), not one element (a[0]), or pass the address (&x).", "传数组本身（a），不要传某一个元素（a[0]）；或传地址（&x）。");
        R(@"^passing argument (\d+) of '(.+?)' from incompatible pointer type", "passing argument 1 of 'f' from incompatible pointer type",
          "Argument {1} of {2} is a pointer to the wrong type.", "{2} 的第 {1} 个参数是指向错误类型的指针。",
          "Pass a pointer to exactly the type the function expects (int* for int*, char* for char*).", "传入函数需要的那种指针（int* 对 int*，char* 对 char*）。");
        R(@"^passing argument (\d+) of '(.+?)' discards '?(\w+)'? qualifier", "passing argument 1 of 'f' discards 'const' qualifier from pointer target type",
          "A read-only ({3}) value is passed to {2}, which might change it.", "把只读（{3}）的内容传给了可能会修改它的 {2}。",
          "Make the parameter const (const char* s) if the function doesn't change it.", "如果函数不会修改它，就把参数声明成 const（const char* s）。");
        R(@"^(assignment|initialization|return) makes (pointer|integer) from (integer|pointer) without a cast", "assignment makes pointer from integer without a cast",
          "A {3} is stored where a {2} is needed.", "把一个{3}存到了需要{2}的地方。",
          "Check the types of both sides: take an address with &, or dereference with *.", "检查两边的类型：取地址用 &，取内容用 *。");
        R(@"^(?:initialization|assignment|return) from incompatible pointer type", "initialization from incompatible pointer type",
          "A pointer of one type is stored into a pointer of another type.", "把一种类型的指针存进了另一种类型的指针里。",
          "Use matching pointer types (int* with int*, char* with char*), or cast on purpose.", "让指针类型一致（int* 对 int*，char* 对 char*），或有意地强制转换。");
        R(@"^'return' with no value, in function returning non-void", "'return' with no value, in function returning non-void",
          "This function must return a value, but return has none.", "这个函数必须返回一个值，但 return 后面没有值。",
          "Write return <value>;", "写成 return 值;");
        R(@"^'return' with a value, in function returning void", "'return' with a value, in function returning void",
          "A void function can't return a value.", "void 函数不能返回值。",
          "Remove the value after return, or change void to the real return type.", "删掉 return 后面的值，或把 void 改成真正的返回类型。");
        R(@"^incompatible types when (?:assigning|returning|initializing) (?:to type|type) '(.+?)' from type '(.+?)'", "incompatible types when assigning to type 'int' from type 'struct P'",
          "A {2} can't be stored in a {1}.", "不能把 {2} 存进 {1}。",
          "Assign member by member (p.x), or use a variable of the matching type.", "按成员逐个赋值（p.x），或使用类型一致的变量。");
        R(@"^invalid storage class for function '(\w+)'", "invalid storage class for function 'f'",
          "A function is defined inside another function.", "在一个函数里面定义了另一个函数。",
          "Move it outside; most likely the previous function is missing its closing }.", "把它挪到外面；更可能是上一个函数少了右大括号 }。");
        R(@"^variable '(\w+)' has initializer but incomplete type", "variable 'p' has initializer but incomplete type",
          "The type of '{1}' isn't defined yet at this point.", "'{1}' 的类型在这里还没有定义。",
          "#include the header that defines it, or define the struct before using it.", "#include 定义它的头文件，或者先定义这个结构体再使用。");
        R(@"^subscripted value is neither array nor pointer", "subscripted value is neither array nor pointer nor vector",
          "[ ] was used on something that is not an array or pointer.", "对不是数组或指针的东西用了 [ ]。",
          "Only arrays, pointers and strings can be indexed. Check the variable's type.", "只有数组、指针和字符串可以用下标。检查变量的类型。");
        R(@"^request for member '(\w+)' in something not a structure or union", "request for member 'x' in something not a structure or union",
          "'.{1}' was used on something that is not a struct.", "对不是结构体的东西用了 .{1}。",
          "For a pointer to a struct use ->; plain numbers have no members.", "指向结构体的指针要用 ->；普通数字没有成员。");
        R(@"^dereferencing pointer to incomplete type", "dereferencing pointer to incomplete type",
          "A pointer is used to reach inside a type the compiler doesn't know.", "通过指针访问了一个编译器不认识内容的类型。",
          "Define the struct (or #include its header) before this line.", "在这一行之前把结构体定义出来（或 #include 它的头文件）。");
        R(@"^storage size of '(\w+)' isn't constant|^variable-sized object '(\w+)' may not be initialized", "variable-sized object 'a' may not be initialized",
          "An array whose size is a variable can't be initialised.", "长度是变量的数组不能直接给初始值。",
          "Use a constant size, or fill the array with a loop (or memset) afterwards.", "用常量当长度，或者之后用循环（或 memset）来赋值。");
        R(@"^unknown field '(\w+)' specified in initializer", "unknown field 'z' specified in initializer",
          "The struct has no member called '{1}'.", "这个结构体里没有叫 '{1}' 的成员。",
          "Check the member names in the struct definition.", "对照结构体定义检查成员名。");
        R(@"^expected specifier-qualifier-list before '(.+)'", "expected specifier-qualifier-list before 'string'",
          "A struct member uses a type the compiler doesn't know ('{1}').", "结构体成员用了编译器不认识的类型（'{1}'）。",
          "Check the spelling of the type; in C there is no string or bool without #include <stdbool.h>.", "检查类型名拼写；C 里没有 string，用 bool 需要 #include <stdbool.h>。");
        R(@"^array initializer must be an initializer list", "array initializer must be an initializer list",
          "An array is initialised with something that isn't a { } list.", "用一个不是 { } 列表的东西去初始化数组。",
          "Write char s[] = \"text\"; or int a[] = {1, 2, 3};", "写成 char s[] = \"text\";，或者 int a[] = {1, 2, 3};");
        R(@"^missing braces around initializer", "missing braces around initializer [-Wmissing-braces]",
          "A nested array or struct is initialised without inner braces.", "给嵌套的数组或结构体赋初值时，里面少了大括号。",
          "Group the values: int a[2][2] = {{1, 2}, {3, 4}};", "把值分组：int a[2][2] = {{1, 2}, {3, 4}};");
        R(@"^function declaration isn't a prototype|^old-style function definition|^old-style parameter declarations", "function declaration isn't a prototype [-Wstrict-prototypes]",
          "A function is declared with empty parentheses.", "函数声明的括号里是空的。",
          "In C write void in the parentheses when there are no parameters: int f(void).", "C 语言里没有参数时要在括号里写 void：int f(void)。");
        R(@"^ISO C90 forbids mixed declarations and code", "ISO C90 forbids mixed declarations and code [-Wdeclaration-after-statement]",
          "A variable is declared after statements, which old C (C89/C90) doesn't allow.", "在语句后面才声明变量，老的 C 标准（C89/C90）不允许。",
          "Declare all variables at the top of the block, or compile as C99 / C11.", "把变量都放到块的开头声明，或者用 C99 / C11 编译。");
        R(@"^ISO C90 forbids variable length array '?(\w+)'?", "ISO C90 forbids variable length array 'a' [-Wvla]",
          "An array with a variable size isn't allowed in old C.", "老的 C 标准不允许数组长度是变量。",
          "Use a constant size, malloc, or compile as C99 / C11.", "用常量当长度、用 malloc，或者用 C99 / C11 编译。");
        R(@"^C\+\+ style comments are not allowed in ISO C90", "C++ style comments are not allowed in ISO C90",
          "// comments aren't allowed in old C (C89/C90).", "老的 C 标准（C89/C90）不允许 // 注释。",
          "Use /* ... */ comments, or compile as C99 / C11.", "改用 /* ... */ 注释，或者用 C99 / C11 编译。");
        R(@"^integer constant is too large for '(.+?)' type", "integer constant is too large for 'long' type",
          "The number is too big for a {1}.", "这个数字太大，{1} 装不下。",
          "Add the suffix LL (123456789012LL) or use long long / unsigned long long.", "加上后缀 LL（123456789012LL），或者改用 long long / unsigned long long。");
        R(@"^type of '(\w+)' defaults to 'int'|^return type defaults to 'int'", "return type defaults to 'int' [-Wreturn-type]",
          "A type is missing, so int is assumed.", "少写了类型，编译器默认当成 int。",
          "Write the type explicitly (int, double, void ...).", "把类型明确写出来（int、double、void 等）。");
    }

    private static void AddLinkerRules()
    {
        R(@"^undefined reference to '(?:std::|__gxx_|_Unwind_|operator (?:new|delete))", "undefined reference to 'std::cout'",
          "C++ code was linked as if it were C.", "C++ 代码被当成 C 来链接了。",
          "Save the file as .cpp (so g++ is used), or add -lstdc++ to the linker options.", "把文件存成 .cpp（这样会用 g++），或在链接选项里加 -lstdc++。");
        R(@"^undefined reference to '(?:typeinfo|vtable) for (.+)'", "undefined reference to 'typeinfo for A'",
          "The class {1} is declared but some of its virtual functions have no body.", "类 {1} 里有虚函数只声明了、没有写函数体。",
          "Define every virtual function (or make it = 0 to make the class abstract).", "给每个虚函数写上函数体（或写成 = 0 变成抽象类）。");
        R(@"^undefined reference to '(\w+)::(\w+)\(.*\)'", "undefined reference to 'A::f()'",
          "The member function {1}::{2} is declared in the class but never written.", "成员函数 {1}::{2} 在类里声明了，却没有写函数体。",
          "Define it: return_type {1}::{2}(...) { ... } — don't forget the {1}:: in front.", "把它实现出来：返回类型 {1}::{2}(...) { ... }，别忘了前面的 {1}::。");
        R(@"^undefined reference to '(\w+)::(\w+)'$", "undefined reference to 'A::count'",
          "The static member {1}::{2} has no definition.", "静态成员 {1}::{2} 没有定义。",
          "Define it once outside the class (in a .cpp): int {1}::{2} = 0;", "在类的外面（某个 .cpp 里）定义一次：int {1}::{2} = 0;");
        R(@"^undefined reference to '(?:pthread_\w+|sem_\w+)'", "undefined reference to 'pthread_create'",
          "The thread library isn't linked.", "没有链接线程库。",
          "Add -lpthread to the linker options (Project Options > Parameters).", "在链接选项里加上 -lpthread（项目属性 > 参数）。");
        R(@"^undefined reference to '(sin|cos|tan|asin|acos|atan|atan2|sqrt|pow|exp|log|log10|floor|ceil|fabs|fmod|sinh|cosh|tanh)'", "undefined reference to 'sqrt'",
          "The math library isn't linked, so {1} can't be found.", "没有链接数学库，所以找不到 {1}。",
          "Add -lm to the linker options. (In C++ include <cmath>.)", "在链接选项里加上 -lm。（C++ 里请 #include <cmath>。）");
        R(@"^undefined reference to '_?_?imp_?_?(\w+?)(?:@\d+)?'", "undefined reference to '__imp_MessageBoxA@16'",
          "The Windows function {1} comes from a library that isn't linked.", "Windows 函数 {1} 所在的库没有被链接。",
          "Add the library to the linker options: -luser32 (windows, dialogs), -lgdi32 (drawing), -lws2_32 (sockets), -lwinmm (sound/timers).", "把对应的库加到链接选项：-luser32（窗口、对话框）、-lgdi32（绘图）、-lws2_32（网络）、-lwinmm（声音、定时器）。");
        R(@"^multiple definition of 'main'", "multiple definition of 'main'",
          "Two files in the project both contain a main function.", "项目里有两个文件都写了 main 函数。",
          "Keep main in one file only; rename or remove the other.", "只保留一个 main；把另一个改名或删掉。");
        R(@"^first defined here", "first defined here",
          "(Continues the error above) The first definition is here.", "（接上一条）第一次定义在这里。",
          "Keep only one definition: bodies go into one .cpp, headers only get declarations.", "只保留一个定义：函数体写在一个 .cpp 里，头文件里只放声明。");
        R(@"^skipping incompatible (.+) when searching for -l(\w+)", "skipping incompatible C:/lib/libfoo.a when searching for -lfoo",
          "The library {2} was built for a different kind of program (32-bit vs 64-bit).", "库 {2} 是为另一种程序（32 位 / 64 位）编译的。",
          "Use the 64-bit version of the library with this 64-bit compiler.", "这个编译器是 64 位的，请使用库的 64 位版本。");
        R(@"^relocation truncated to fit", "relocation truncated to fit: R_X86_64_32S against '.bss'",
          "The program has more fixed data than the compiler can address (about 2 GB).", "程序里固定大小的数据太多，超出了编译器能寻址的范围（约 2 GB）。",
          "Don't use huge global arrays. Allocate big data at run time with new / std::vector, or use -mcmodel=medium.", "不要用特别大的全局数组。大块数据请在运行时用 new / std::vector 分配，或者加 -mcmodel=medium。");
        R(@"^unrecognized command line option '-std=(.+)'", "unrecognized command line option '-std=c++20'",
          "This g++ is too old for the C++ standard '{1}'.", "这个 g++ 太旧，不支持 '{1}' 这个 C++ 标准。",
          "Open Settings > Compiler and choose an older standard (C++14 is safe), or use a newer compiler.", "到 设置 > 编译器 选一个更旧的标准（C++14 最稳妥），或者换新一点的编译器。");
        R(@"^unrecognized command line option '(.+)'", "unrecognized command line option '-Wfoo'",
          "The compiler doesn't know the option '{1}'.", "编译器不认识选项 '{1}'。",
          "Fix or delete it in Settings > Compiler > Extra flags (or Project Options).", "到 设置 > 编译器 > 额外编译选项（或项目属性）里改正或删掉它。");
        R(@"^cannot find -l(.+)", "cannot find -lfoo",
          "The library '{1}' needed by the program can't be found.", "程序需要的库 '{1}' 找不到。",
          "Check the name in Project Options > Parameters, or install that library.", "检查 项目属性 > 参数 里库的名字，或安装这个库。");
        R(@"^out of memory allocating (\d+) bytes", "out of memory allocating 2147483648 bytes",
          "The compiler ran out of memory.", "编译器内存不够了。",
          "Look for a huge array or deeply recursive template, and reduce it.", "找找有没有特别大的数组或嵌套很深的模板，把它缩小。");
        R(@"^The system cannot find the (?:file|path) specified", "The system cannot find the file specified",
          "Windows can't find a program or file the build needs.", "Windows 找不到构建需要的某个程序或文件。",
          "Check the compiler path in Settings > Compiler.", "检查 设置 > 编译器 里的 g++ 路径。");
    }

    private static void AddWarningRules()
    {
        R(@"^comparison of unsigned expression < 0 is always false", "comparison of unsigned expression < 0 is always false [-Wtype-limits]",
          "An unsigned number is never below zero, so this test never succeeds.", "无符号数永远不会小于 0，这个判断永远不成立。",
          "Use a signed type (int), or test differently. for (unsigned i = n; i >= 0; i--) loops forever.", "改用有符号类型（int），或换种判断。for (unsigned i = n; i >= 0; i--) 会无限循环。");
        R(@"^comparison of unsigned expression >= 0 is always true", "comparison of unsigned expression >= 0 is always true [-Wtype-limits]",
          "An unsigned number is always at least zero, so this test always succeeds.", "无符号数永远不小于 0，这个判断永远成立。",
          "Use a signed type (int) for countdown loops.", "倒着数的循环请用有符号类型（int）。");
        R(@"^suggest parentheses around '(.+)' (?:inside|in) '(.+)'", "suggest parentheses around '+' inside '<<' [-Wparentheses]",
          "It is unclear how '{1}' and '{2}' combine.", "'{1}' 和 '{2}' 怎么组合不够清楚。",
          "Add parentheses to say what you mean, e.g. (a + b) << 2.", "加上括号表明你的意思，比如 (a + b) << 2。");
        R(@"^suggest parentheses around arithmetic in operand of '(.+)'|^suggest parentheses around comparison in operand of '(.+)'", "suggest parentheses around comparison in operand of '=='",
          "The order of evaluation here may not be what you expect.", "这里的运算顺序可能和你想的不一样。",
          "Add parentheses to make it explicit.", "加上括号把顺序写清楚。");
        R(@"^this '(if|for|while|else)' clause does not guard", "this 'if' clause does not guard... [-Wmisleading-indentation]",
          "The indentation suggests the next line belongs to the {1}, but it doesn't.", "缩进让人以为下一行属于这个 {1}，其实不是。",
          "Put the statements in braces { } so the {1} covers all of them.", "把语句放进大括号 { } 里，这样 {1} 才能管到它们。");
        R(@"^array subscript is outside array bounds|^array subscript (\d+) is (?:above|below|outside) array bounds", "array subscript is outside array bounds of 'int [3]' [-Warray-bounds]",
          "An index goes past the end (or before the start) of an array.", "下标超出了数组的范围。",
          "An array of size N has the indexes 0 to N-1.", "长度为 N 的数组，下标只能是 0 到 N-1。");
        R(@"^no return statement in function returning non-void", "no return statement in function returning non-void [-Wreturn-type]",
          "The function should return a value but has no return.", "函数应该返回一个值，却没有 return。",
          "Add return <value>; at the end.", "在结尾补上 return 值;。");
        R(@"^format '(%[^']*)' expects a matching '(.+?)' argument", "format '%d' expects a matching 'int' argument [-Wformat=]",
          "The format {1} has no value to print.", "格式 {1} 后面没有对应的值可以输出。",
          "Give one argument for every % in the format string.", "格式字符串里每个 % 都要有一个对应的参数。");
        R(@"^too many arguments for format", "too many arguments for format [-Wformat-extra-args]",
          "More values than % formats.", "给的值比 % 格式多。",
          "Add the missing % in the format string, or remove the extra value.", "在格式字符串里补上缺的 %，或删掉多余的值。");
        R(@"^unknown conversion type character '?(.+?)'? in format", "unknown conversion type character 'k' in format [-Wformat=]",
          "The format character '{1}' after % does not exist.", "% 后面的格式字符 '{1}' 不存在。",
          "Common ones: %d int, %lld long long, %f float, %lf double (scanf), %c char, %s text.", "常用的有：%d int，%lld long long，%f float，scanf 读 double 用 %lf，%c char，%s 字符串。");
        R(@"^format not a string literal and no format arguments", "format not a string literal and no format arguments [-Wformat-security]",
          "printf was given a variable as its format string.", "printf 的格式字符串是个变量。",
          "Write printf(\"%s\", text); instead of printf(text);", "写成 printf(\"%s\", text);，不要写 printf(text);");
        R(@"^left-hand operand of comma expression has no effect", "left-hand operand of comma expression has no effect [-Wunused-value]",
          "A comma is used where something else was meant.", "这里用了逗号，可能想写的是别的。",
          "In a for loop use ; between the parts. For several values write one statement each.", "for 循环里各部分之间用分号 ;。多个值请各写一条语句。");
        R(@"^statement is a reference, not call, to function '(\w+)'", "statement is a reference, not call, to function 'f' [-Waddress]",
          "'{1}' is named but not called.", "只写了函数名 '{1}'，并没有调用它。",
          "Add the parentheses: {1}();", "加上括号：{1}();");
        R(@"^the address of '(.+?)' will always evaluate as 'true'", "the address of 'int f()' will always evaluate as 'true' [-Waddress]",
          "A function name is used as a condition, which is always true.", "把函数名当成了条件，它永远为真。",
          "To test the result, call it: if (f()).", "想判断结果，要调用它：if (f())。");
        R(@"^comparison with string literal results in unspecified behaviour", "comparison with string literal results in unspecified behaviour [-Waddress]",
          "Text is compared with == which compares addresses, not letters.", "用 == 比较了文字，它比的是地址，不是字母。",
          "Use strcmp(a, b) == 0, or std::string, which compares letters with ==.", "用 strcmp(a, b) == 0，或者改用 std::string（它的 == 比较的是字母）。");
        R(@"^comparison between pointer and zero character constant", "comparison between pointer and zero character constant [-Wpointer-compare]",
          "A pointer is compared with '\\0'.", "把指针和 '\\0' 比较了。",
          "Compare the character it points to: *p == '\\0', or p[0] == '\\0'.", "要比较的是它指向的字符：*p == '\\0'，或 p[0] == '\\0'。");
        R(@"^(?:left|right) shift count (?:is negative|>= width of type)", "left shift count >= width of type [enabled by default]",
          "The number of bits to shift is out of range.", "移位的位数超出了范围。",
          "Shift by less than the width of the type (31 for int, 63 for long long).", "移位位数要小于类型的位数（int 最多 31，long long 最多 63）。");
        R(@"^conversion to '(.+?)' from '(.+?)' may (?:alter|change) its value", "conversion to 'int' from 'double' may alter its value [-Wconversion]",
          "A {2} is squeezed into a {1}; digits may be lost.", "把 {2} 放进 {1}，可能丢失小数或高位。",
          "Use the same type, or cast on purpose: (int)x.", "用同一种类型，或者明确地强制转换：(int)x。");
        R(@"^enumeration value '(\w+)' not handled in switch", "enumeration value 'GREEN' not handled in switch [-Wswitch]",
          "The switch has no case for {1}.", "这个 switch 没有处理 {1}。",
          "Add case {1}: or a default: branch.", "补上 case {1}: 或者 default: 分支。");
        R(@"^case value '(.+?)' not in enumerated type", "case value '3' not in enumerated type 'Color' [-Wswitch]",
          "{1} isn't one of the values of the enum.", "{1} 不是这个枚举里的值。",
          "Use the enum's own names in the case labels.", "case 里请使用枚举自己的名字。");
        R(@"^array subscript has type 'char'", "array subscript has type 'char' [-Wchar-subscripts]",
          "A char is used as an array index.", "用 char 当了数组下标。",
          "A char may be negative; convert it: a[(unsigned char)c] or a[c - 'a'].", "char 可能是负数，请转换：a[(unsigned char)c] 或 a[c - 'a']。");
        R(@"^declaration of '(\w+)' shadows a (?:global declaration|member of 'this'|previous local|parameter)", "declaration of 'a' shadows a global declaration [-Wshadow]",
          "The variable '{1}' hides another one with the same name.", "变量 '{1}' 遮住了另一个同名的变量。",
          "Rename one of them so you don't use the wrong one by mistake.", "给其中一个改名，免得不小心用错。");
        R(@"^'(.+?)' will be initialized after", "'A::y' will be initialized after 'int A::x' [-Wreorder]",
          "The initializer list is in a different order than the members.", "初始化列表的顺序和成员声明的顺序不一样。",
          "Write the initializers in the same order as the members are declared.", "让初始化列表的顺序和成员声明的顺序一致。");
        R(@"^base class '(.+?)' should be explicitly initialized in the copy constructor", "base class 'B' should be explicitly initialized in the copy constructor [-Wextra]",
          "The copy constructor doesn't copy the base class part.", "拷贝构造函数没有复制基类的那部分。",
          "Add : B(other) to its initializer list.", "在它的初始化列表里加上 : B(other)。");
        R(@"^'(.+?)' has virtual functions and accessible non-virtual destructor|^deleting object of polymorphic class type '(.+?)' which has non-virtual destructor", "'A' has virtual functions and accessible non-virtual destructor [-Wnon-virtual-dtor]",
          "A class with virtual functions needs a virtual destructor.", "有虚函数的类，析构函数也应该是 virtual 的。",
          "Write virtual ~A() { } in the base class, otherwise delete through a base pointer leaks.", "在基类里写 virtual ~A() { }，否则通过基类指针 delete 会漏掉子类的清理。");
        R(@"^(?:unused label|label) '(\w+)'(?: defined but not used)?", "label 'done' defined but not used [-Wunused-label]",
          "The label '{1}' is never used.", "标号 '{1}' 从来没有被用到。",
          "Delete it, or add the goto that jumps to it.", "删掉它，或者补上跳到它的 goto。");
        R(@"^multi-line comment", "multi-line comment [-Wcomment]",
          "A // comment ends with a backslash, so the next line is a comment too.", "// 注释的行尾有反斜杠，下一行也被当成了注释。",
          "Remove the \\ at the end of the comment line.", "去掉注释行末尾的 \\。");
        R(@"^""/\*"" within comment", "\"/*\" within comment [-Wcomment]",
          "A /* comment contains another /*.", "一个 /* 注释里面又出现了 /*。",
          "Comments can't be nested: close the first one with */ before opening another.", "注释不能嵌套：先用 */ 结束前一个，再开新的。");
        R(@"^no newline at end of file", "no newline at end of file",
          "The file doesn't end with an empty line.", "文件末尾没有留一个空行。",
          "Harmless; press Enter once at the end of the file to remove the message.", "不影响运行；在文件末尾按一次回车就没有这个提示了。");
        R(@"^NULL used in arithmetic|^converting 'false' to pointer type|^passing NULL to non-pointer argument", "NULL used in arithmetic [-Wpointer-arith]",
          "NULL (a null pointer) is mixed with numbers.", "把 NULL（空指针）和数字混在一起用了。",
          "Use 0 for numbers; NULL / nullptr only for pointers.", "数字请用 0；NULL / nullptr 只用于指针。");
        R(@"^'(gets)' is deprecated|^the 'gets' function is dangerous", "the 'gets' function is dangerous and should not be used.",
          "gets can overflow your buffer, so it has been removed from modern C and C++.", "gets 会让缓冲区溢出，现代 C 和 C++ 已经把它删除了。",
          "Use fgets(s, size, stdin), cin >> s, or getline(cin, s).", "改用 fgets(s, 大小, stdin)、cin >> s 或 getline(cin, s)。");
        R(@"^'(\w+)' is deprecated", "'sprintf' is deprecated",
          "'{1}' is outdated.", "'{1}' 已经过时。",
          "It still works, but a safer or newer replacement exists.", "它还能用，但已经有更安全或更新的替代品。");
        R(@"^'(\w+)' is initialized and declared 'extern'", "'x' is initialized and declared 'extern'",
          "A variable is both declared extern and given a value.", "变量既声明成 extern，又给了初始值。",
          "Remove extern (to define it here) or the = value (to just declare it).", "去掉 extern（在这里定义它），或去掉 = 值（只声明它）。");
        R(@"^function returns address of local variable", "function returns address of local variable [-Wreturn-local-addr]",
          "The function returns the address of a variable that disappears when it ends.", "函数返回了一个局部变量的地址，函数一结束它就没了。",
          "Return the value itself, or use static / new / std::vector.", "直接返回值本身，或者用 static / new / std::vector。");
        R(@"^typedef '(\w+)' locally defined but not used", "typedef 'T' locally defined but not used [-Wunused-local-typedefs]",
          "A type alias '{1}' is defined but never used.", "类型别名 '{1}' 定义了却没有使用。",
          "Delete it.", "删掉它。");
        R(@"^'(.+?)' defined but not used", "'int f()' defined but not used [-Wunused-function]",
          "The function {1} is defined but never called.", "函数 {1} 定义了，却从来没有被调用。",
          "Delete it, or call it. Harmless, but often a sign of a typo elsewhere.", "删掉它，或者去调用它。不影响运行，但常常说明别处有拼写错误。");
        R(@"^'(.+?)' declared 'static' but never defined", "'void f()' declared 'static' but never defined [-Wunused-function]",
          "The static function {1} is declared but has no body.", "静态函数 {1} 只声明了，没有函数体。",
          "Write its body, or delete the declaration.", "把函数体写出来，或者删掉这条声明。");
        R(@"^invalid suffix on literal", "invalid suffix on literal; C++11 requires a space between literal and identifier [-Wliteral-suffix]",
          "A text literal sits right next to a name or macro.", "字符串常量紧挨着一个名字或宏。",
          "Put a space between them: \"text\" NAME.", "在它们之间加一个空格：\"text\" NAME。");
    }
}
}
