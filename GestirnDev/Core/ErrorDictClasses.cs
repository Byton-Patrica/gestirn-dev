using System;
using System.Collections.Generic;
using System.Linq;

namespace GestirnDev
{
// Batch: function calls, classes and objects, templates and the standard library.
internal static partial class Hints
{
    private static void AddCallRules()
    {
        R(@"^candidate expects (\d+) arguments?, (\d+) provided", "candidate expects 2 arguments, 3 provided",
          "The function takes {1} argument(s), but the call gives {2}.", "这个函数需要 {1} 个参数，而你的调用给了 {2} 个。",
          "Make the number of arguments match the function's definition.", "让参数个数和函数的定义一致。");
        R(@"^candidate:? (.+)", "candidate: int add(int, int)",
          "One of the functions the compiler considered: {1}", "编译器考虑过的候选函数之一：{1}",
          "Compare its parameters with the ones in your call.", "把它的参数和你调用时写的对照一下。");
        R(@"^no matching function for call to '(\w+)::\1\(\)'", "no matching function for call to 'A::A()'",
          "The class {1} has no constructor without arguments, but an object is created without any.", "类 {1} 没有无参数的构造函数，但创建对象时没有给参数。",
          "Give the arguments when creating it (A a(1);), or add a constructor A() { } to the class.", "创建对象时把参数带上（A a(1);），或者在类里加一个无参构造函数 A() { }。");
        R(@"^no matching function for call to '(\w+)::\1\((.*)\)'", "no matching function for call to 'A::A(int, int)'",
          "No constructor of {1} accepts these arguments ({2}).", "类 {1} 的构造函数里没有一个能接收这些参数（{2}）。",
          "Check the constructors of {1}: their number of parameters and types must match what you pass.", "检查 {1} 的构造函数，参数的个数和类型要和你传的一致。");
        R(@"^no matching function for call to '(?:std::)?(?:vector|list|deque|set|map|basic_string|string)[^']*::(push_back|insert|push_front|emplace_back|find|erase|at|resize)\((.*)\)'", "no matching function for call to 'std::vector<int>::push_back(const char [2])'",
          "{1} doesn't accept these arguments ({2}).", "{1} 不接收这样的参数（{2}）。",
          "The element type of the container decides what you may add: a vector<int> takes ints, a vector<string> takes strings.", "容器的元素类型决定了能放什么：vector<int> 只能放整数，vector<string> 才能放字符串。");
        R(@"^no matching function for call to '(?:std::)?sort\((.*)\)'", "no matching function for call to 'sort(std::vector<int>&)'",
          "sort needs two iterators: where to start and where to stop.", "sort 需要两个位置：从哪里开始、到哪里结束。",
          "Write sort(v.begin(), v.end()); for a vector, or sort(a, a + n); for an array.", "vector 要写成 sort(v.begin(), v.end());，数组要写成 sort(a, a + n);。");
        R(@"^no matching function for call to '(?:std::)?(max|min)\((.*)\)'", "no matching function for call to 'max(int, double)'",
          "{1} needs both values to have the same type, but got ({2}).", "{1} 要求两个值类型相同，而你给的是（{2}）。",
          "Convert one so they match: max(a, (int)b), or write max<double>(a, b).", "把其中一个转换一下：max(a, (int)b)，或者写 max<double>(a, b)。");
        R(@"^no matching function for call to '(?:std::)?getline\((.*)\)'", "no matching function for call to 'getline(std::string&)'",
          "getline needs the input stream and the string: getline(cin, s).", "getline 需要同时给出输入流和字符串：getline(cin, s)。",
          "Write getline(cin, s); (and #include <string>).", "写成 getline(cin, s);（并 #include <string>）。");
        R(@"^no matching function for call to '(?:std::)?(swap|reverse|find|count|fill|accumulate|unique)\((.*)\)'", "no matching function for call to 'swap(int&)'",
          "The arguments of {1} are wrong ({2}).", "{1} 的参数不对（{2}）。",
          "swap takes two variables; reverse / find / count / fill need a start and an end position (v.begin(), v.end()).", "swap 需要两个变量；reverse / find / count / fill 需要起点和终点（v.begin(), v.end()）。");
        R(@"^call of overloaded '(.+)' is ambiguous", "call of overloaded 'f(long int)' is ambiguous",
          "The compiler can't decide which version of {1} to call.", "编译器没法决定该调用 {1} 的哪个版本。",
          "Make the argument type match one version exactly, with a cast such as f((int)x) or a suffix such as 1.0f.", "让参数类型和其中一个版本完全一致，可以强制转换 f((int)x)，或加后缀如 1.0f。");
        R(@"^default argument missing for parameter (\d+) of '(.+)'", "default argument missing for parameter 2 of 'int f(int, int)'",
          "Parameter {1} needs a default value because an earlier parameter has one.", "第 {1} 个参数也需要默认值，因为前面的参数有默认值了。",
          "Default values must be given from the right: int f(int a, int b = 1) is fine, int f(int a = 1, int b) is not.", "默认值必须从右往左给：int f(int a, int b = 1) 可以，int f(int a = 1, int b) 不行。");
        R(@"^invalid use of non-static data member '(.+)'", "invalid use of non-static data member 'A::x'",
          "The member '{1}' belongs to an object, but no object is given.", "成员 '{1}' 属于某个对象，可是没有指明是哪个对象。",
          "Use it through an object (a.x) or inside a non-static member function.", "要通过对象来用（a.x），或者在非静态的成员函数里用。");
        R(@"^cannot call member function '(.+)' without object", "cannot call member function 'void A::f()' without object",
          "The member function {1} is called without saying which object.", "调用成员函数 {1} 时没有说明是哪个对象。",
          "Create an object and call a.f(), or make the function static.", "先创建对象再写 a.f()，或者把函数改成 static。");
        R(@"^invalid use of non-static member function '?(.*?)'?$", "invalid use of non-static member function 'void A::f()'",
          "The member function {1} is used without an object (or without the parentheses).", "使用成员函数 {1} 时没有对象（或者漏了括号）。",
          "Call it as obj.f(); a function name alone is not a call.", "要写成 obj.f(); 光写函数名并不是调用。");
        R(@"^'(.+)' must take (?:either )?(?:exactly )?(zero|one|two|no) arguments?", "'void A::operator+(int, int)' must take exactly one argument",
          "{1} must take {2} argument(s).", "{1} 的参数个数只能是 {2} 个。",
          "A member operator already has the left side (this): binary operators take one parameter, unary ones none.", "成员运算符已经有了左边的对象（this）：二元运算符只写一个参数，一元运算符不写参数。");
        R(@"^'(.+)' takes only zero or two arguments", "'int main(int)' takes only zero or two arguments",
          "main can have no parameters or exactly two (argc and argv).", "main 要么没有参数，要么正好两个（argc 和 argv）。",
          "Write int main() or int main(int argc, char* argv[]).", "写成 int main() 或 int main(int argc, char* argv[])。");
        R(@"^new types may not be defined in a return type", "new types may not be defined in a return type",
          "A class, struct or enum is defined, and the next thing looks like part of it.", "刚定义了一个类、结构体或枚举，接下来的内容被当成了它的一部分。",
          "Put a semicolon after the closing } of the class / struct / enum definition: struct P { int x; };", "在类 / 结构体 / 枚举定义的右大括号 } 后面补上分号：struct P { int x; };");
        R(@"^two or more data types in declaration of '(.+)'", "two or more data types in declaration of 'main'",
          "Two types stand in front of '{1}'.", "'{1}' 前面连着写了两个类型。",
          "Usually a ; is missing after a struct / class / enum definition just above.", "通常是上面的 struct / class / enum 定义后面少了分号。");
        R(@"^declaration does not declare anything", "declaration does not declare anything [-fpermissive]",
          "This declaration declares nothing.", "这条声明什么也没有声明。",
          "A type name alone (typedef int; or struct P;) needs a new name after it.", "单独写个类型名（比如 typedef int;）是不够的，后面需要写新名字。");
        R(@"^return type specification for constructor invalid", "return type specification for constructor invalid",
          "A constructor was given a return type.", "构造函数被写上了返回类型。",
          "Constructors have no return type, not even void: write A() { } instead of void A() { }.", "构造函数没有返回类型，连 void 也不能写：写成 A() { }，不要写 void A() { }。");
        R(@"^request for member '(\w+)' in '(.+?)', which is of non-class type '(.+?)\(\)'", "request for member 'x' in 'a', which is of non-class type 'A()'",
          "'{2}' was declared as a function, not an object.", "'{2}' 被当成了函数声明，而不是对象。",
          "To create an object without arguments don't use parentheses: write A a; not A a();", "创建不带参数的对象时不要写括号：写 A a;，不要写 A a();");
    }

    private static void AddClassRules()
    {
        C(@"^'(.+) (?:\w+::)?(\w+)' is (private|protected)$", (m, ctx) =>
        {
            string member = m.Groups[2].Value, kind = m.Groups[3].Value;
            string zhKind = kind == "private" ? "私有（private）" : "受保护（protected）";
            return new[]
            {
                "The member '" + member + "' is " + kind + ", so code outside the class can't use it.", "成员 '" + member + "' 是" + zhKind + "的，类的外面不能直接使用。",
                "Move it under public:, or add a public function (a getter / setter) that gives access to it.", "把它放到 public: 下面，或者在类里写一个 public 的函数（getter / setter）来访问它。",
            };
        }, "'int A::x' is private");
        R(@"^within this context", "within this context",
          "(Continues the error above) This is the place where the private member is used.", "（接上一条）这就是违规使用的位置。",
          "See the previous message for what is not allowed here.", "具体原因看上一条报错。");
        R(@"^'(.+)' is inaccessible|^'(.+)' is not an accessible base of '(.+)'|^'(.+)' is an inaccessible base of '(.+)'", "'B' is not an accessible base of 'A'",
          "The base class can't be used here because it is inherited privately.", "基类在这里用不了，因为它是以私有方式继承的。",
          "Inherit publicly: class A : public B { ... };  (class defaults to private inheritance, struct to public).", "改成公有继承：class A : public B { ... };（class 默认是私有继承，struct 默认是公有继承）。");
        R(@"^because the following virtual functions are pure within '(.+)'", "because the following virtual functions are pure within 'A':",
          "The class {1} still has pure virtual functions (= 0), so no object of it can be made.", "类 {1} 里还有纯虚函数（= 0）没实现，所以不能创建它的对象。",
          "Create an object of a derived class that implements all of them, or use a pointer / reference to {1}.", "创建实现了全部纯虚函数的派生类的对象，或者只使用指向 {1} 的指针 / 引用。");
        R(@"^class '(.+)' does not have any field named '(\w+)'", "class 'A' does not have any field named 'y'",
          "The constructor's initializer list uses '{2}', but {1} has no member with that name.", "构造函数的初始化列表里用了 '{2}'，可 {1} 里没有这个成员。",
          "Check the spelling of '{2}' and that it is declared in the class.", "检查 '{2}' 的拼写，并确认它确实在类里声明过。");
        R(@"^type '(.+)' is not a direct base of '(.+)'", "type 'B' is not a direct base of 'A'",
          "{1} is not a direct parent of {2}.", "{1} 不是 {2} 的直接父类。",
          "Only name the classes written after the colon in class {2} : ...", "初始化列表里只能写 class {2} : ... 冒号后面列出的父类。");
        R(@"^'virtual' outside class declaration", "'virtual' outside class declaration",
          "virtual is written on a function defined outside the class.", "在类外面定义函数时也写了 virtual。",
          "Write virtual only inside the class; leave it out when the function is defined outside.", "virtual 只写在类里面；在类外面定义函数时不要再写。");
        R(@"^prototype for '(.+)' does not match any in class '(.+)'", "prototype for 'void A::f(int)' does not match any in class 'A'",
          "The definition of {1} doesn't match its declaration inside the class {2}.", "{1} 的定义和类 {2} 里的声明对不上。",
          "Make the parameters (types, number, const) and the return type identical in both places.", "让两处的参数（类型、个数、const）和返回类型完全一致。");
        R(@"^no '(.+)' member function declared in class '(.+)'", "no 'void A::f()' member function declared in class 'A'",
          "The class {2} has no member function that matches {1}.", "类 {2} 里没有和 {1} 对应的成员函数。",
          "Declare it inside the class first, with the same parameters.", "先在类里面声明它，参数要一致。");
        R(@"^'(.+)' has a previous declaration as '(.+)'", "'int A::f()' has a previous declaration as 'void A::f()'",
          "Two declarations of the same function differ only in the return type.", "同一个函数的两处声明，只有返回类型不一样。",
          "Overloads must differ in their parameters; make the return types identical.", "重载的函数必须靠参数区分；把返回类型改成一致。");
        R(@"^expected class-name before '\{' token|^expected class-name before '(.+)' token", "expected class-name before '{' token",
          "The base class name after the colon is missing or unknown.", "冒号后面的父类名缺失，或者不认识。",
          "Write class B : public A { ... }; and make sure A is defined (or #included) before B.", "写成 class B : public A { ... };，并确保 A 在 B 之前已经定义（或已 #include）。");
        R(@"^use of deleted function '(.+)'", "use of deleted function 'std::unique_ptr<int>::unique_ptr(const std::unique_ptr<int>&)'",
          "{1} has been deleted, so it can't be called.", "{1} 已经被删除，不能调用。",
          "Often this is a copy of something that can't be copied (unique_ptr, streams): use std::move or pass a reference.", "常见原因是拷贝了不能拷贝的东西（unique_ptr、流对象）：改用 std::move 或传引用。");
        R(@"^uninitialized (?:member|reference member|const member)|^uninitialized member '(\w+)' with 'const' type", "uninitialized member 'A::x' with 'const' type 'const int'",
          "A const (or reference) member must get its value in the constructor.", "const（或引用）类型的成员必须在构造函数里赋初值。",
          "Use the initializer list: A() : x(0) { }", "用初始化列表：A() : x(0) { }");
        R(@"^ISO C\+\+ forbids in-class initialization of non-const static member '(\w+)'", "ISO C++ forbids in-class initialization of non-const static member 'count'",
          "A static member can't get its value inside the class.", "静态成员不能在类的里面赋初值。",
          "Declare it as static int {1}; in the class and define it once outside: int A::{1} = 0;", "类里只写 static int {1};，然后在类外面定义一次：int A::{1} = 0;");
        R(@"^ISO C\+\+ forbids initialization of member '(\w+)'", "ISO C++ forbids initialization of member 'x'",
          "A member was given a value where it is declared, which the old C++ standard doesn't allow.", "在声明成员的地方直接给了初值，旧的 C++ 标准不允许这样。",
          "Set the C++ standard to C++11 or newer (Settings > Compiler), or assign it in the constructor.", "把 C++ 标准设成 C++11 或更新（设置 > 编译器），或者在构造函数里赋值。");
        R(@"^'(.+)' marked '?override'?,? but does not override", "'void B::f()' marked 'override', but does not override",
          "{1} is marked override but there is nothing to override.", "{1} 标了 override，可是父类里没有可以覆盖的函数。",
          "Make the name, parameters and const of the function identical to the virtual one in the base class.", "让函数名、参数、const 都和父类里那个 virtual 函数完全一致。");
        R(@"^'(.+)' declared as a (?:virtual )?(?:field|parameter|variable|type)", "'A::x' declared as a virtual field",
          "virtual was put on something that isn't a member function.", "virtual 被写在了不是成员函数的东西上。",
          "virtual only applies to member functions inside a class.", "virtual 只能用于类里面的成员函数。");
        R(@"^invalid use of 'class (\w+)'|^invalid use of '(\w+)::(\w+)'", "invalid use of 'class A'",
          "A class name is used as if it were a value or an object.", "把类名当成了值或对象来用。",
          "Create an object first (A a;) and use that, or call a static member as A::name.", "先创建对象（A a;）再使用它，或者用 A::名字 访问静态成员。");
        R(@"^forward declaration of '(.+)'", "forward declaration of 'class B'",
          "This is where the class was only announced (declared), not defined.", "这个类只在这里被预告了一下（前向声明），并没有真正定义。",
          "Define the class fully (or #include its header) before using its members.", "在用到它的成员之前，要把这个类完整定义出来（或 #include 它的头文件）。");
        R(@"^redefinition of '(?:class|struct|union|enum) (.+)'", "redefinition of 'class A'",
          "The class {1} is defined twice.", "类 {1} 被定义了两次。",
          "Delete one definition. For headers add #pragma once at the very top.", "删掉其中一个定义。头文件请在最开头加 #pragma once。");
        R(@"^'(\w+)' was not declared in this scope.*this->", "'x' was not declared in this scope; this->x",
          "A member of a base class template needs this->.", "基类模板的成员需要写 this->。",
          "Write this->{1} instead of {1}.", "把 {1} 写成 this->{1}。");
    }

    private static void AddTemplateRules()
    {
        R(@"^wrong number of template arguments \((\d+), should be (\d+)\)", "wrong number of template arguments (1, should be 4)",
          "The template got {1} type argument(s), but it needs {2}.", "模板给了 {1} 个类型参数，而它需要 {2} 个。",
          "map needs two (map<int, string>), vector one (vector<int>), pair two (pair<int, int>).", "map 需要两个（map<int, string>），vector 一个（vector<int>），pair 两个（pair<int, int>）。");
        R(@"^provided for '(template<.*)", "provided for 'template<class _Key, class _Tp, class _Compare, class _Alloc> class std::map'",
          "(Continues the error above) This is the declaration of the template that was used wrongly.", "（接上一条）这是被用错的那个模板的声明。",
          "Count the type arguments in <...> against it.", "对照它数一数 <...> 里的类型参数个数。");
        R(@"^invalid type in declaration before '(.+)' token", "invalid type in declaration before ';' token",
          "The type written before this name is not valid.", "写在这个名字前面的类型无效。",
          "Look at the line above: it is usually the template error just reported (wrong number of type arguments).", "看上一条报错：通常是模板用错了（类型参数个数不对）。");
        R(@"^missing template arguments before '(.+)'", "missing template arguments before 'v'",
          "A template type is used without its type argument.", "用了模板类型，却没写它的类型参数。",
          "Write the element type in <...>: vector<int> v; map<string, int> m;", "在 <...> 里写上元素类型：vector<int> v; map<string, int> m;");
        R(@"^invalid use of template-name '(.+)' without an argument list", "invalid use of template-name 'vector' without an argument list",
          "{1} is a template and needs its type in <...>.", "{1} 是模板，需要在 <...> 里写上类型。",
          "For example {1}<int>.", "例如 {1}<int>。");
        R(@"^expected template-name before '<' token", "expected template-name before '<' token",
          "A < follows a name that isn't a template.", "< 前面的名字不是模板。",
          "Check the name (vector, map, set ...), its #include and std:: in front.", "检查名字（vector、map、set 等）、#include，以及前面的 std::。");
        R(@"^need 'typename' before '(.+)' because '(.+)' is a dependent scope", "need 'typename' before 'std::vector<T>::iterator' because 'std::vector<T>' is a dependent scope",
          "A type that depends on the template parameter needs the word typename.", "依赖模板参数的类型前面需要写 typename。",
          "Write typename {1} it;", "写成 typename {1} it;");
        R(@"^no type named '(\w+)' in '(.+)'", "no type named 'iterator' in 'class std::vector<int>'",
          "{2} has no type called '{1}'.", "{2} 里没有叫 '{1}' 的类型。",
          "Check the spelling, and the #include of the container.", "检查拼写，以及容器的 #include。");
        R(@"^couldn't deduce template parameter '(.+)'", "couldn't deduce template parameter 'T'",
          "The compiler can't work out what {1} should be from the arguments.", "编译器没法从参数推断出 {1} 应该是什么类型。",
          "Give the type explicitly: f<int>(...), or pass arguments that determine it.", "把类型明确写出来：f<int>(...)，或者传入能决定它的参数。");
        R(@"^template argument deduction/substitution failed", "template argument deduction/substitution failed:",
          "(Continues the error above) The template could not be used with these arguments.", "（接上一条）这个模板不能用这些参数。",
          "Make the arguments the same type, or give the type in <...>.", "让参数类型一致，或者在 <...> 里写明类型。");
        R(@"^'(?:class |struct )?(?:std::)?(?:vector|basic_string|string|list|deque)[^']*' has no member named '(length|Size|Length|len|add|append|push|count|empty_|clear_|insert_back)'", "'class std::vector<int>' has no member named 'length'",
          "That name doesn't exist for this container: '{1}'.", "这个容器里没有叫 '{1}' 的函数。",
          "Use size() for the number of elements (length() works only for string), push_back(x) to add, empty() to test for emptiness.", "元素个数用 size()（length() 只有 string 才有），添加用 push_back(x)，判断是否为空用 empty()。");
        R(@"^'auto' changes meaning in C\+\+11", "'auto' changes meaning in C++11; please remove it [-Wc++0x-compat]",
          "The C++ standard in use is too old for auto (variable type deduction).", "当前选择的 C++ 标准太旧，不支持 auto 自动推断类型。",
          "Set the C++ standard to C++11 or newer in Settings > Compiler.", "到 设置 > 编译器 把 C++ 标准选成 C++11 或更新。");
        R(@"^identifier '(\w+)' is a keyword in C\+\+11", "identifier 'nullptr' is a keyword in C++11 [-Wc++0x-compat]",
          "'{1}' is a keyword in newer C++, but the standard selected is older.", "'{1}' 在新的 C++ 里是关键字，但当前选的标准比较旧。",
          "Set the C++ standard to C++11 or newer (Settings > Compiler).", "把 C++ 标准设成 C++11 或更新（设置 > 编译器）。");
        R(@"^lambda expressions only available with|^range-based 'for' loops (?:are not allowed|only available)|^(?:'(?:auto|nullptr|constexpr|decltype|override|final)' (?:only|is only) available)|^(?:scoped|strongly typed) enums|^non-static data member initializers|^(?:delegating|inheriting) constructors", "lambda expressions only available with -std=c++11 or -std=gnu++11",
          "This feature needs a newer C++ standard than the one selected.", "这个写法需要比当前选择更新的 C++ 标准。",
          "Open Settings > Compiler and set the C++ standard to C++11 or newer.", "到 设置 > 编译器 把 C++ 标准选成 C++11 或更新。");
        R(@"^use of '(\w+)' in lambda capture|^lambda-expression .*|^expected '\]' before", "lambda-expression in unevaluated context",
          "The lambda ( [ ] ( ) { } ) is written incorrectly.", "lambda 表达式（[ ] ( ) { }）的写法不对。",
          "A lambda looks like [capture](parameters) { body }; check every bracket and semicolon.", "lambda 的格式是 [捕获](参数) { 函数体 }，检查每个括号和分号。");
    }
}
}
