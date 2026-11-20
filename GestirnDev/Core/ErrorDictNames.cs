using System;
using System.Collections.Generic;
using System.Linq;

namespace GestirnDev
{
// Batch: names, declarations, scope, and type conversions.
internal static partial class Hints
{
    private static void AddNameRules()
    {
        C(@"^'(string|vector|map|set|list|queue|stack|deque|pair|array|unordered_map|unordered_set|priority_queue|ifstream|ofstream|stringstream|istringstream|ostringstream|shared_ptr|unique_ptr|function|thread|mutex)' does not name a type", (m, ctx) =>
        {
            string name = m.Groups[1].Value, header;
            HeaderOf.TryGetValue(name, out header);
            header = header ?? name;
            return new[]
            {
                "'" + name + "' isn't a type the compiler knows yet.", "编译器还不认识 '" + name + "' 这个类型。",
                "Add #include <" + header + "> at the top and write std::" + name + " (or add using namespace std;).", "在文件开头加上 #include <" + header + ">，并写成 std::" + name + "（或者加一句 using namespace std;）。",
            };
        }, "'string' does not name a type");
        R(@"^redeclaration of '[\w:<>*&, ]+ (\w+)'", "redeclaration of 'int a'",
          "The variable '{1}' is declared twice in the same place.", "变量 '{1}' 在同一个范围里被声明了两次。",
          "Declare it once. To change its value later just write {1} = ...; without the type in front.", "只声明一次。以后要改它的值，直接写 {1} = ...;，前面不要再写类型。");
        R(@"^'(\w+)' redeclared as different kind of symbol", "'count' redeclared as different kind of symbol",
          "The name '{1}' already means something else here (a function, class or variable).", "名字 '{1}' 在这里已经有别的含义了（函数、类或变量）。",
          "Rename your variable. Names such as count, max, min, size, list or map clash with library names when using namespace std; is on.", "给你的变量换个名字。写了 using namespace std; 时，count、max、min、size、list、map 这类名字很容易和标准库撞车。");
        R(@"^declaration of '[\w:<>*&, ]+ (\w+)' shadows a parameter", "declaration of 'int a' shadows a parameter",
          "The variable '{1}' has the same name as a parameter of this function.", "变量 '{1}' 和这个函数的参数重名了。",
          "Use another name, or just assign to the parameter ({1} = ...;).", "换一个名字，或者直接给参数赋值（{1} = ...;）。");
        R(@"^previous (?:declaration|definition) of '(.+)'", "previous definition of 'class A'",
          "This is where '{1}' was defined the first time.", "'{1}' 第一次是在这里定义的。",
          "Keep one definition. Headers need #pragma once (or an include guard) so they aren't read twice.", "只保留一个定义。头文件要加 #pragma once（或头文件保护宏），免得被读两遍。");
        R(@"^'(\w+)' undeclared here \(not in a function\)", "'x' undeclared here (not in a function)",
          "'{1}' is used outside any function and was never declared.", "'{1}' 用在了函数外面，而且从没声明过。",
          "Declare it first (e.g. int {1};) or check the spelling.", "先声明它（比如 int {1};），或检查拼写。");
        R(@"^variable or field '(\w+)' declared void", "variable or field 'f' declared void",
          "'{1}' was declared with the type void, which usually means a parameter type is unknown.", "'{1}' 被声明成了 void，多半是参数里有个类型名编译器不认识。",
          "Check the types in the parameter list: a misspelled or not-included type (string, vector ...) causes this.", "检查参数列表里的类型：类型名拼错或没有 #include（string、vector 等）就会这样。");
        R(@"^'(\w+)' cannot be used as a function", "'x' cannot be used as a function",
          "'{1}' is a variable, but it is used like a function ({1}(...)).", "'{1}' 是个变量，却被当成函数来调用了（{1}(...)）。",
          "Maybe a * is missing (write a * (b + c), not a (b + c)), or a variable has the same name as the function.", "可能少写了乘号（应写成 a * (b + c)，不是 a (b + c)），或者变量和函数重名了。");
        R(@"^expression cannot be used as a function", "expression cannot be used as a function",
          "Something that isn't a function is followed by parentheses.", "不是函数的东西后面跟了括号，被当成函数调用。",
          "Check the name before the ( ): a missing operator or a wrong name is the usual cause.", "检查括号前面的名字：多半是漏了运算符，或名字写错了。");
        R(@"^'(.+)' cannot appear in a constant-expression", "'n' cannot appear in a constant-expression",
          "'{1}' is not a constant, but a constant is required here (array size, case value ...).", "这里需要常量，而 '{1}' 不是常量（比如数组长度、case 的值）。",
          "Use a literal, a const variable with a fixed value, or a #define. For a size known only at run time use std::vector.", "改用具体的数字、有固定值的 const 变量或 #define。大小要到运行时才知道的，请用 std::vector。");
        R(@"^the value of '(\w+)' is not usable in a constant expression", "the value of 'n' is not usable in a constant expression",
          "'{1}' is not a compile-time constant.", "'{1}' 不是编译时就确定的常量。",
          "Declare it const with a literal value, or use #define / constexpr.", "把它声明成 const 并赋一个固定的值，或使用 #define / constexpr。");
        R(@"^size of array '(\w+)' is negative|^size of array is negative", "size of array 'a' is negative",
          "An array can't have a negative size.", "数组的长度不能是负数。",
          "Check the number between [ ].", "检查 [ ] 里的数字。");
        R(@"^size of array '(\w+)' is not an integral constant-expression|^size of array is not an integral constant-expression|^array bound is not an integer constant", "size of array 'a' is not an integral constant-expression",
          "The array size must be a whole-number constant.", "数组长度必须是整数常量。",
          "Use a literal or a const int. For a size that changes at run time use std::vector<int> v(n);", "用具体数字或 const int。长度运行时才知道的，请用 std::vector<int> v(n);");
        R(@"^storage size of '(\w+)' isn't known", "storage size of 'a' isn't known",
          "The array '{1}' has no size and nothing to infer it from.", "数组 '{1}' 没有写长度，也没有初始值可以推断长度。",
          "Write the size (int {1}[10];) or give it values (int {1}[] = {1, 2, 3};).", "写上长度（int {1}[10];），或者给初始值（int {1}[] = {1, 2, 3};）。");
        R(@"^array size missing in '(.+)'", "array size missing in 'a'",
          "An array is declared without a size.", "声明数组时没有写长度。",
          "Write the size between [ ], or initialise it with values.", "在 [ ] 里写上长度，或者给它初始值。");
        R(@"^field '(\w+)' has incomplete type '(.+)'", "field 'b' has incomplete type 'B'",
          "The member '{1}' uses the type {2}, which isn't fully defined at this point.", "成员 '{1}' 用到的类型 {2} 在这里还没有完整定义。",
          "Define {2} before this class, or make the member a pointer ({2}* {1};).", "把 {2} 的定义放到这个类前面，或者把成员改成指针（{2}* {1};）。");
        R(@"^declaration of '(.+)' has no initializer", "declaration of 'auto a' has no initializer",
          "'{1}' needs a starting value so the compiler can tell its type.", "'{1}' 需要一个初始值，编译器才能判断它的类型。",
          "Write the type explicitly, or give it a value: auto a = 1;", "把类型写明白，或者给它一个值：auto a = 1;");
        R(@"^uninitialized const '(\w+)'", "uninitialized const 'a'",
          "The constant '{1}' has no value.", "常量 '{1}' 没有给初始值。",
          "A const must get its value where it is declared: const int {1} = 5;", "const 常量必须在声明时就赋值：const int {1} = 5;");
        R(@"^'(\w+)' is not captured", "'a' is not captured",
          "The lambda uses '{1}' from outside without capturing it.", "lambda 里用到了外面的变量 '{1}'，却没有捕获它。",
          "Put it in the brackets: [{1}] copies it, [&{1}] shares it, [=] or [&] capture everything.", "把它写进方括号：[{1}] 是复制，[&{1}] 是共享，[=] 或 [&] 会捕获所有用到的变量。");
        R(@"^'this' is unavailable for static member functions|^invalid use of 'this' in non-member function", "'this' is unavailable for static member functions",
          "this can't be used here.", "这里不能使用 this。",
          "Static functions and plain functions belong to no object, so they have no this.", "静态函数和普通函数不属于任何对象，所以没有 this。");
        R(@"^invalid use of member '(.+)' in static member function", "invalid use of member 'A::x' in static member function",
          "A static function is using the object member '{1}'.", "静态函数里用到了属于对象的成员 '{1}'。",
          "Static functions have no object. Make the member static too, or make the function non-static.", "静态函数没有对象。把这个成员也改成 static，或者把函数改成非静态的。");
        R(@"^'(\w+)' in namespace '(.+)' does not name a(?: template)? type", "'strng' in namespace 'std' does not name a type",
          "'{1}' doesn't exist in {2}.", "{2} 里没有叫 '{1}' 的类型。",
          "Check the spelling and capital letters ('string', not 'String' or 'strng') and the #include.", "检查拼写和大小写（是 string，不是 String 或 strng），以及 #include。");
        R(@"^'(\w+)' is not a member of '(.+)'", "'push' is not a member of 'std::vector<int>'",
          "{2} has nothing called '{1}'.", "{2} 里没有叫 '{1}' 的东西。",
          "Check the spelling and capitals (push_back, size, begin ...). Different containers have different functions.", "检查拼写和大小写（push_back、size、begin 等）。不同容器的函数并不一样。");
        R(@"^'(\w+)' is not a template", "'foo' is not a template",
          "'{1}' is not a template, so <...> can't follow it.", "'{1}' 不是模板，后面不能跟 <...>。",
          "Remove the <...>, or check that you meant a template (vector, map ...) and included its header.", "去掉 <...>；或者确认你想用的是模板（vector、map 等）并 #include 了头文件。");
        R(@"^'(\w+)' is not a namespace-name|^expected namespace-name before", "'std2' is not a namespace-name",
          "The namespace name is unknown.", "这个命名空间的名字不认识。",
          "Check the spelling (using namespace std;) and the #include.", "检查拼写（using namespace std;）和 #include。");
        R(@"^'(\w+)' is not a type", "'x' is not a type",
          "'{1}' is a variable or function, but a type is expected here.", "'{1}' 是变量或函数，而这里需要的是类型。",
          "Write a type name, or check that '{1}' isn't hiding the type you meant.", "写一个类型名，或检查 '{1}' 是不是把你想用的类型遮住了。");
        R(@"^'(\w+)' has (?:an )?incomplete type|^invalid use of incomplete type", "'p' has incomplete type",
          "The compiler only knows that this type exists, not what is inside.", "编译器只知道这个类型存在，却不知道里面有什么。",
          "#include the header that defines it before using it.", "在用它之前，先 #include 定义它的头文件。");
    }

    private static void AddTypeRules()
    {
        C(@"^no match for 'operator<<' \(operand types are '(.+?)' and '(.+?)'\)", (m, ctx) =>
        {
            string stream = m.Groups[1].Value, type = m.Groups[2].Value;
            bool output = stream.Contains("ostream") || stream.Contains("cout");
            return new[]
            {
                "'" + type + "' can't be printed with << directly.", "'" + type + "' 不能直接用 << 输出。",
                "Print its members one by one (cout << p.x), or define operator<< for it.", "把它的成员一个一个输出（cout << p.x），或者为它写一个 operator<<。",
            };
        }, "no match for 'operator<<' (operand types are 'std::ostream {aka std::basic_ostream<char>}' and 'P')");
        R(@"^no match for 'operator>>' \(operand types are '(.+?)' and '(.+?)'\)", "no match for 'operator>>' (operand types are 'std::istream {aka std::basic_istream<char>}' and 'P')",
          "'{2}' can't be read with >> directly.", "'{2}' 不能直接用 >> 读入。",
          "Read into its members one by one (cin >> p.x), or define operator>>. cin >> only works on variables, not on constants.", "把它的成员一个一个读入（cin >> p.x），或者为它写 operator>>。cin >> 后面只能跟变量，不能跟常量。");
        R(@"^no match for 'operator\+' \(operand types are 'const char \[\d+\]' and 'const char \[\d+\]'\)|^invalid operands of types 'const char \[\d+\]' and 'const char \[\d+\]' to binary 'operator\+'", "invalid operands of types 'const char [2]' and 'const char [2]' to binary 'operator+'",
          "Two text literals can't be joined with +.", "两个字符串常量不能用 + 连接。",
          "Make one of them a std::string first: string(\"a\") + \"b\", or write them as one literal \"ab\".", "先把其中一个变成 std::string：string(\"a\") + \"b\"，或者直接写成一个字符串 \"ab\"。");
        R(@"^no match for 'operator\[\]' \(operand types are '(.+?)' and '(.+?)'\)", "no match for 'operator[]' (operand types are 'std::vector<int>' and 'const char [2]')",
          "{1} can't be indexed with a {2}.", "{1} 不能用 {2} 当下标。",
          "The index of an array, vector or string must be an integer (v[0], v[i]).", "数组、vector、string 的下标必须是整数（v[0]、v[i]）。");
        R(@"^no match for 'operator(==|!=|<|>|<=|>=)' \(operand types are '(.+?)' and '(.+?)'\)", "no match for 'operator==' (operand types are 'P' and 'P')",
          "{2} and {3} can't be compared with {1}.", "{2} 和 {3} 不能用 {1} 比较。",
          "Compare their members instead (a.x == b.x), or define operator{1} for the type.", "改为比较它们的成员（a.x == b.x），或为这个类型写 operator{1}。");
        R(@"^no match for 'operator=' \(operand types are '(.+?)' and '(.+?)'\)", "no match for 'operator=' (operand types are 'std::vector<int>' and 'int')",
          "A {2} can't be assigned to a {1}.", "不能把 {2} 赋给 {1}。",
          "Use the container's functions (push_back, assign, resize) or assign another {1}.", "用容器自己的函数（push_back、assign、resize），或者赋另一个 {1}。");
        R(@"^no match for 'operator(.+?)' \(operand types are '(.+?)' and '(.+?)'\)", "no match for 'operator+' (operand types are 'P' and 'P')",
          "The operator {1} can't be used between {2} and {3}.", "运算符 {1} 不能用在 {2} 和 {3} 之间。",
          "Check the types on both sides, or define operator{1} for your own type.", "检查两边的类型，或者为你自己的类型写 operator{1}。");
        R(@"^no match for 'operator(.+?)' in '(.+)'", "no match for 'operator[]' in 'a[1.5e+0]'",
          "The operator {1} doesn't work on this expression.", "运算符 {1} 不能用在这个表达式上。",
          "Check the types of the operands.", "检查运算对象的类型。");
        R(@"^invalid operands of types '(?:double|float|long double)' and '(.+?)' to binary 'operator%'|^invalid operands of types '(.+?)' and '(?:double|float|long double)' to binary 'operator%'", "invalid operands of types 'double' and 'int' to binary 'operator%'",
          "% (remainder) only works on whole numbers, not on decimals.", "% 取余只能用在整数上，不能用在小数上。",
          "Use int, or fmod(a, b) from <cmath> for decimals.", "用 int，或者小数用 <cmath> 里的 fmod(a, b)。");
        R(@"^invalid operands of types '(.+?)' and '(.+?)' to binary 'operator(.+)'", "invalid operands of types 'int' and 'const char [3]' to binary 'operator+'",
          "The operator {3} can't be used between {1} and {2}.", "运算符 {3} 不能用在 {1} 和 {2} 之间。",
          "Convert one side so both have compatible types (for example turn text into a number with stoi).", "把其中一边转换成兼容的类型（比如用 stoi 把文字转成数字）。");
        R(@"^invalid operands to binary (\S+) \(have '(.+?)' and '(.+?)'\)", "invalid operands to binary + (have 'char *' and 'char *')",
          "The operator {1} can't be used between {2} and {3}.", "运算符 {1} 不能用在 {2} 和 {3} 之间。",
          "In C, text is not joined with +; use strcat or sprintf. % needs whole numbers.", "C 语言里不能用 + 连接字符串，请用 strcat 或 sprintf。% 只能用在整数上。");
        R(@"^invalid type argument of unary '\*' \(have '(.+)'\)", "invalid type argument of unary '*' (have 'int')",
          "* (dereference) was used on a {1}, which is not a pointer.", "对一个 {1} 使用了 *（取内容），但它不是指针。",
          "Only pointers can be dereferenced. Remove the * (or declare the variable as a pointer: int* p).", "只有指针才能用 * 取内容。去掉 *（或把变量声明成指针：int* p）。");
        R(@"^invalid type argument of '->' \(have '(.+)'\)", "invalid type argument of '->' (have 'P')",
          "-> was used on a {1}, which is not a pointer.", "对一个 {1} 使用了 ->，但它不是指针。",
          "For an ordinary object write a dot: obj.member. Use -> only with pointers.", "普通对象请用点：obj.成员。只有指针才用 ->。");
        R(@"^base operand of '->' has non-pointer type '(.+)'", "base operand of '->' has non-pointer type 'P'",
          "-> was used on a {1}, which is not a pointer.", "对一个 {1} 使用了 ->，但它不是指针。",
          "For an ordinary object write a dot: obj.member. Use -> only with pointers.", "普通对象请用点：obj.成员。只有指针才用 ->。");
        R(@"^request for member '(\w+)' in '(.+?)', which is of pointer type '(.+?)' \(maybe you meant to use '->'", "request for member 'x' in 'p', which is of pointer type 'P*' (maybe you meant to use '->' ?)",
          "'{2}' is a pointer, so its member '{1}' is reached with ->.", "'{2}' 是指针，要用 -> 访问它的成员 '{1}'。",
          "Write {2}->{1} instead of {2}.{1}", "把 {2}.{1} 改成 {2}->{1}");
        R(@"^cannot bind '(.+?)' lvalue to '(.+?)'", "cannot bind 'std::istream {aka std::basic_istream<char>}' lvalue to 'std::basic_istream<char>&&'",
          "A stream can't be used this way: >> or << was given something it can't work with.", "流（cin / cout）不能这样用：>> 或 << 后面跟了它处理不了的东西。",
          "cin >> must be followed by a variable (not a constant), and cout << by something printable. Custom types need their own operator.", "cin >> 后面必须是变量（不能是常量），cout << 后面要是能输出的东西。自定义类型需要自己写运算符。");
        R(@"^cannot convert '(.+?)' to '(.+?)' for argument '(\d+)' to '(.+)'", "cannot convert 'const char*' to 'int' for argument '1' to 'int f(int)'",
          "Argument {3} of the call to {4} is a {1}, but a {2} is needed.", "调用 {4} 时，第 {3} 个参数是 {1}，函数需要的是 {2}。",
          "Pass a value of the type the function expects, or convert it first.", "传入函数需要的那种类型的值，或先做转换。");
        R(@"^could not convert '(.+?)' from '(.+?)' to '(.+?)'", "could not convert '\"a\"' from 'const char [2]' to 'int'",
          "A {2} can't be turned into a {3}.", "{2} 不能转成 {3}。",
          "Use a value of the right type. A string like \"5\" becomes a number with stoi(\"5\").", "使用类型正确的值。像 \"5\" 这样的文字，可以用 stoi(\"5\") 变成数字。");
        R(@"^conversion from '(.+?)' to non-scalar type '(.+?)' requested", "conversion from 'int' to non-scalar type 'std::vector<int>' requested",
          "A {1} can't be used to create a {2} this way.", "不能这样用 {1} 去创建 {2}。",
          "Use the constructor form: vector<int> v(5); (5 elements) or vector<int> v = {5};", "用构造的写法：vector<int> v(5);（5 个元素）或 vector<int> v = {5};");
        R(@"^invalid (?:static_cast|cast|reinterpret_cast|const_cast|dynamic_cast) from type '(.+?)' to type '(.+?)'", "invalid static_cast from type 'int' to type 'P'",
          "A {1} can't be cast to a {2}.", "不能把 {1} 强制转换成 {2}。",
          "These types are unrelated. Convert by calling a constructor or writing a conversion function.", "这两种类型没有关系。请通过构造函数或自己写转换函数来转换。");
        R(@"^comparison between pointer and integer", "comparison between pointer and integer ('char *' and 'int')",
          "A text (pointer) is compared with a number.", "把文字（指针）和数字放在一起比较了。",
          "To compare characters use single quotes ('a'); to compare text use strcmp or std::string.", "比较字符请用单引号 ('a')；比较文字请用 strcmp 或 std::string。");
        R(@"^incompatible types in assignment of '(.+?)' to '(.+?)'", "incompatible types in assignment of 'const char [4]' to 'char [5]'",
          "A whole array or text can't be assigned with =.", "不能用 = 给整个数组或字符串赋值。",
          "For C text use strcpy(dest, \"abc\"); or use std::string, which can be assigned with =.", "C 风格字符串请用 strcpy(目标, \"abc\");，或者改用可以直接用 = 赋值的 std::string。");
        R(@"^invalid array assignment", "invalid array assignment",
          "An array can't be assigned to another array with =.", "数组不能用 = 整体赋值给另一个数组。",
          "Copy element by element in a loop, use std::copy, or use std::vector / std::array which can be assigned.", "用循环一个一个复制，或用 std::copy；也可以改用能直接赋值的 std::vector / std::array。");
        R(@"^array must be initialized with a brace-enclosed initializer", "array must be initialized with a brace-enclosed initializer",
          "An array needs its values in braces { }.", "数组的初始值要用大括号 { } 括起来。",
          "Write int a[3] = {1, 2, 3};", "写成 int a[3] = {1, 2, 3};");
        R(@"^type '(.+?)' argument given to 'delete', expected pointer", "type 'int' argument given to 'delete', expected pointer",
          "delete was used on a {1}, which is not a pointer.", "对一个 {1} 使用了 delete，但它不是指针。",
          "delete only works on things created with new. Ordinary variables are cleaned up automatically.", "delete 只能用在用 new 创建出来的东西上。普通变量会自动清理，不用 delete。");
        R(@"^taking address of temporary", "taking address of temporary [-fpermissive]",
          "The address of a temporary value was taken.", "对一个临时值取了地址。",
          "Store the value in a variable first, then take the address of the variable.", "先把值存进一个变量，再对变量取地址。");
        R(@"^cannot pass objects of non-trivially-copyable type '(.+?)' through '\.\.\.'", "cannot pass objects of non-trivially-copyable type 'std::string {aka class std::basic_string<char>}' through '...'",
          "A {1} can't be given to printf (or another ... function) directly.", "{1} 不能直接交给 printf 这样的函数。",
          "For std::string use s.c_str() with %s, or print it with cout << s.", "std::string 请配合 %s 使用 s.c_str()，或者直接用 cout << s 输出。");
        R(@"^switch quantity not an integer", "switch quantity not an integer",
          "switch only works on whole numbers, characters and enums, not on text or decimals.", "switch 只能用于整数、字符和枚举，不能用于文字或小数。",
          "For text use if / else if with ==, or compare a character of it: switch (s[0]).", "文字请改用 if / else if 加 ==，或者取其中一个字符来判断：switch (s[0])。");
        R(@"^case label does not reduce to an integer constant|^case label value is not an integer constant", "case label does not reduce to an integer constant",
          "A case value must be a constant whole number or character.", "case 后面的值必须是常量整数或字符。",
          "Use a literal (case 1:), a character (case 'a':) or a const / enum value, not a variable.", "用具体的数字（case 1:）、字符（case 'a':）或 const / 枚举值，不能用变量。");
        R(@"^duplicate case value", "duplicate case value",
          "Two cases in the same switch have the same value.", "同一个 switch 里有两个 case 的值一样。",
          "Remove or change one of them.", "删掉或改掉其中一个。");
        R(@"^invalid use of void expression", "invalid use of void expression",
          "The result of a function that returns nothing (void) is being used.", "使用了一个没有返回值（void）的函数的结果。",
          "Don't assign or print it; just call the function on its own line.", "不要给它赋值或输出，单独写一行调用它就行。");
        R(@"^too many initializers for '(.+?)'", "too many initializers for 'int [2]'",
          "More initial values were given than {1} can hold.", "给 {1} 的初始值比它能装的还多。",
          "Remove the extra values, or make the array bigger.", "删掉多余的值，或者把数组开大一些。");
        R(@"^excess elements in (?:array|struct|scalar) initializer", "excess elements in array initializer",
          "More initial values were given than there are elements.", "给的初始值比元素个数还多。",
          "Remove the extra values, or make the array bigger.", "删掉多余的值，或者把数组开大一些。");
        R(@"^braces around scalar initializer|^scalar object '(\w+)' requires one element in initializer", "braces around scalar initializer",
          "A single variable was given a list of values in braces.", "给单个变量写了一串花括号里的值。",
          "A plain variable takes one value: int a = 5; Use an array for several values.", "普通变量只能有一个值：int a = 5;。要放多个值请用数组。");
        R(@"^invalid initialization of non-const reference of type '(.+?)' from an rvalue of type '(.+?)'", "invalid initialization of non-const reference of type 'int&' from an rvalue of type 'int'",
          "A reference parameter ({1}) was given a constant or a calculated value.", "引用参数（{1}）收到了常量或计算出来的值。",
          "Pass a real variable, or change the parameter to const int& (or plain int).", "传入一个真正的变量，或者把参数改成 const int&（或直接用 int）。");
        R(@"^invalid initialization of reference of type '(.+?)' from expression of type '(.+?)'", "invalid initialization of reference of type 'int&' from expression of type 'double'",
          "A reference of type {1} can't refer to a {2}.", "{1} 类型的引用不能指向 {2}。",
          "Use a variable of exactly the right type, or a const reference.", "使用类型完全一致的变量，或者用 const 引用。");
        R(@"^cannot convert '(.+?)' to '(.+?)' in (?:initialization|assignment|return|argument passing)", "cannot convert 'const char*' to 'int' in initialization",
          "A {1} can't be stored in a {2}.", "不能把 {1} 存进 {2}。",
          "Change one of the types so they match, or convert explicitly.", "改一下类型让两边一致，或者手动转换。");
        R(@"^invalid conversion from '(.+?)' to '(.+?)'", "invalid conversion from 'const char*' to 'int' [-fpermissive]",
          "A {1} can't be turned into a {2} automatically.", "{1} 不能自动转换成 {2}。",
          "Convert explicitly, e.g. (int)x or stoi(text), or change the variable's type.", "手动转换，例如 (int)x 或 stoi(文字)，或改变量的类型。");
    }
}
}
