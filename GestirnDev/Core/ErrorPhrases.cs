using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GestirnDev
{
// The safety net of the error dictionary. When no hand-written rule recognises a message, its fixed phrases are
// translated one by one (longest phrase first) and the names in quotes are kept as they are, so that no message is
// ever shown without a Chinese reading. The result is a literal translation with a category in front.
internal static class Phrases
{
    private sealed class Phrase { public Regex Re; public string Zh; public int Length; }

    private static readonly List<Phrase> Table = new List<Phrase>();

    private static void P(string english, string chinese)
    {
        string pattern = Regex.Escape(english).Replace("\\ ", "\\s+");
        bool wordStart = char.IsLetterOrDigit(english[0]), wordEnd = char.IsLetterOrDigit(english[english.Length - 1]);
        pattern = (wordStart ? "\\b" : "") + pattern + (wordEnd ? "\\b" : "");
        Table.Add(new Phrase { Re = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase), Zh = chinese, Length = english.Length });
    }

    public static int Count { get { return Table.Count; } }

    static Phrases()
    {
        // ---- declarations and names
        P("was not declared in this scope", "没有在这个范围内声明过");
        P("has not been declared", "还没有声明过");
        P("was not declared", "没有声明过");
        P("undeclared (first use in this function)", "未声明（在这个函数里第一次使用）");
        P("does not name a type", "不是一个类型名");
        P("is not a member of", "不属于");
        P("has no member named", "里没有叫这个名字的成员：");
        P("is not a class, namespace, or enumeration", "不是类、命名空间或枚举");
        P("is not a namespace-name", "不是命名空间的名字");
        P("is not a type", "不是类型");
        P("is not a template", "不是模板");
        P("is not a function", "不是函数");
        P("is not a variable", "不是变量");
        P("is not a constant", "不是常量");
        P("redeclared as different kind of symbol", "被重新声明成了另一种东西");
        P("conflicting declaration", "声明相互冲突");
        P("conflicting types for", "类型冲突：");
        P("redefinition of", "重复定义：");
        P("redeclaration of", "重复声明：");
        P("previous declaration of", "之前的声明：");
        P("previous definition of", "之前的定义：");
        P("previously declared here", "之前在这里声明过");
        P("previously defined here", "之前在这里定义过");
        P("first defined here", "第一次在这里定义");
        P("declared here", "在这里声明");
        P("defined here", "在这里定义");
        P("shadows a parameter", "遮住了同名的参数");
        P("shadows a previous local", "遮住了前面同名的局部变量");
        P("shadows", "遮住了");
        P("is private", "是私有的");
        P("is protected", "是受保护的");
        P("is inaccessible", "无法访问");
        P("within this context", "（在这个位置使用）");
        P("from this location", "（从这个位置调用）");
        P("in this context", "在这个位置");
        P("is deprecated", "已过时");
        P("is ambiguous", "有歧义");
        P("is a keyword", "是关键字");
        P("is reserved", "是保留字");
        P("undefined reference to", "链接时找不到：");
        P("multiple definition of", "被重复定义：");
        P("cannot find", "找不到");
        P("No such file or directory", "没有这个文件或文件夹");
        P("Permission denied", "权限不够或文件被占用");
        P("cannot open output file", "无法写入输出文件");
        P("cannot open source file", "无法打开源文件");
        P("cannot open", "无法打开");
        P("ld returned", "链接器返回");
        P("exit status", "退出状态");

        // ---- syntax
        P("expected primary-expression before", "这里应该有一个值或变量，却出现了");
        P("expected unqualified-id before", "这里应该有一个名字，却出现了");
        P("expected initializer before", "声明里缺了初始化部分，却出现了");
        P("expected declaration before", "这里应该是一条声明，却出现了");
        P("expected class-name before", "这里应该有类名，却出现了");
        P("expected type-specifier before", "这里应该有类型，却出现了");
        P("expected nested-name-specifier before", "这里应该有 :: 前面的名字，却出现了");
        P("expected template-name before", "这里应该有模板名，却出现了");
        P("expected identifier before", "这里应该有名字，却出现了");
        P("expected expression before", "这里应该有表达式，却出现了");
        P("expected statement before", "这里应该有一条语句，却出现了");
        P("expected constructor, destructor, or type conversion before", "这里应该是构造函数、析构函数或类型转换，却出现了");
        P("expected", "此处应该有");
        P("before numeric constant", "在数字常量之前");
        P("before string constant", "在字符串常量之前");
        P("before token", "在这个符号之前");
        P("before", "在前面的");
        P("after", "在后面的");
        P("at end of input", "到了文件末尾");
        P("at end of", "到了结尾处的");
        P("stray", "出现了不该有的");
        P("in program", "（在程序里）");
        P("missing terminating", "缺少结尾的");
        P("missing", "缺少");
        P("unterminated", "没有结束的");
        P("extraneous", "多余的");
        P("extra tokens at end of", "后面多出了内容：");
        P("unknown escape sequence", "未知的转义写法");
        P("invalid preprocessing directive", "无效的预处理指令");
        P("invalid suffix", "无效的后缀");
        P("invalid digit", "无效的数字");
        P("invalid character", "无效的字符");
        P("invalid", "无效的");
        P("illegal", "非法的");
        P("not allowed here", "不允许出现在这里");
        P("is not allowed", "是不允许的");
        P("may not", "不可以");
        P("cannot be used as a function", "不能当作函数来调用");
        P("cannot be used", "不能这样使用");
        P("cannot be", "不能是");
        P("cannot", "不能");
        P("can't", "不能");
        P("must be", "必须是");
        P("must have", "必须有");
        P("must return", "必须返回");
        P("must", "必须");
        P("should be", "应该是");
        P("without a previous", "前面没有对应的");
        P("without", "没有");
        P("outside of", "在……之外：");

        // ---- types and conversions
        P("invalid conversion from", "无效的类型转换，从");
        P("cannot convert", "无法转换");
        P("could not convert", "无法转换");
        P("conversion from", "从");
        P("conversion to", "转换为");
        P("converting to", "转换成");
        P("may alter its value", "可能改变它的值");
        P("incompatible types in", "类型不兼容，出现在");
        P("incompatible implicit declaration of built-in function", "隐式声明与内置函数不兼容：");
        P("incompatible", "不兼容的");
        P("invalid operands of types", "无效的运算对象类型：");
        P("invalid types", "无效的类型：");
        P("invalid type argument of", "无效的类型参数，用在");
        P("invalid type in declaration", "声明里的类型无效");
        P("invalid use of", "错误地使用了");
        P("invalid initialization of", "无效的初始化：");
        P("invalid cast from", "无效的强制转换，从");
        P("no match for", "没有可用的");
        P("no matching function for call to", "没有匹配的函数可以调用：");
        P("no matching constructor for", "没有匹配的构造函数：");
        P("no viable", "没有可用的");
        P("operand types are", "运算对象类型是");
        P("candidate expects", "候选函数需要");
        P("candidate:", "候选：");
        P("candidates are:", "候选有：");
        P("call of overloaded", "重载函数的调用");
        P("is ambiguous", "有歧义");
        P("too many arguments", "传入的参数太多");
        P("too few arguments", "传入的参数太少");
        P("too many initializers", "初始值太多");
        P("too many", "太多的");
        P("too few", "太少的");
        P("wrong number of template arguments", "模板参数的个数不对");
        P("missing template arguments", "缺少模板参数");
        P("template argument", "模板参数");
        P("template declaration of", "模板声明：");
        P("deduce", "推导");
        P("passing", "传入");
        P("as 'this' argument", "作为 this 参数");
        P("discards qualifiers", "丢掉了 const 等限定");
        P("discards", "丢掉了");
        P("makes pointer from integer without a cast", "把整数当成指针，没有强制转换");
        P("makes integer from pointer without a cast", "把指针当成整数，没有强制转换");
        P("makes", "把");
        P("without a cast", "没有强制转换");
        P("from incompatible pointer type", "来自不兼容的指针类型");
        P("incompatible pointer type", "不兼容的指针类型");
        P("assignment of read-only variable", "给只读变量赋值：");
        P("assignment of read-only location", "给只读位置赋值：");
        P("assignment of read-only", "给只读的内容赋值：");
        P("increment of read-only", "对只读变量做自增：");
        P("decrement of read-only", "对只读变量做自减：");
        P("read-only", "只读的");
        P("lvalue required as", "这里需要一个变量，用作");
        P("lvalue required", "这里需要一个变量");
        P("rvalue", "右值");
        P("lvalue", "左值");
        P("cannot bind", "无法绑定");
        P("to a reference", "到引用");
        P("scalar", "标量");
        P("non-scalar type", "非标量类型");
        P("non-class type", "非类类型");
        P("non-pointer type", "非指针类型");
        P("non-static member function", "非静态的成员函数");
        P("non-static", "非静态的");
        P("non-const", "非 const 的");
        P("non-void function", "有返回值的函数");
        P("incomplete type", "不完整的类型");
        P("abstract type", "抽象类");
        P("abstract class", "抽象类");
        P("pure virtual", "纯虚");
        P("virtual", "虚");
        P("expression cannot be used as a function", "表达式不能当函数使用");
        P("base operand of", "运算符的左边对象：");
        P("have", "现有");
        P("has type", "类型是");
        P("of type", "类型为");
        P("returning", "返回");
        P("argument", "参数");
        P("parameter", "参数");
        P("operand", "运算对象");
        P("operator", "运算符");
        P("unary", "一元");
        P("binary", "二元");
        P("expected pointer", "应该是指针");
        P("pointer", "指针");
        P("reference", "引用");
        P("array subscript", "数组下标");
        P("array", "数组");
        P("negative", "负数");
        P("size of", "大小：");
        P("storage size of", "存储大小：");
        P("isn't known", "不确定");
        P("is not captured", "没有被捕获");
        P("narrowing conversion of", "窄化转换：");
        P("function-definition", "函数定义");
        P("function", "函数");
        P("variable", "变量");
        P("constructor", "构造函数");
        P("destructor", "析构函数");
        P("member", "成员");
        P("namespace", "命名空间");
        P("template", "模板");
        P("class", "类");
        P("struct", "结构体");
        P("enumeration value", "枚举值");
        P("enumeration", "枚举");
        P("integer", "整数");
        P("constant", "常量");
        P("string", "字符串");
        P("character", "字符");
        P("type", "类型");
        P("value", "值");
        P("expression", "表达式");
        P("statement", "语句");
        P("declaration", "声明");
        P("definition", "定义");
        P("directive", "指令");
        P("initializer", "初始值");
        P("label", "标号");
        P("scope", "作用域");
        P("overloaded", "重载的");
        P("macro", "宏");
        P("keyword", "关键字");
        P("bit-field", "位域");
        P("offset", "偏移");

        // ---- flow and returns
        P("control reaches end of non-void function", "有返回值的函数走到结尾却没有 return");
        P("no return statement in function returning non-void", "有返回值的函数里没有 return");
        P("return-statement with no value", "return 后面没有值");
        P("return-statement with a value", "return 后面带了值");
        P("with no value", "没有值");
        P("jump to case label", "跳到 case 标号");
        P("crosses initialization of", "跳过了初始化：");
        P("case label", "case 标号");
        P("not within a switch statement", "不在 switch 语句里");
        P("switch quantity not an integer", "switch 的对象不是整数");
        P("duplicate case value", "case 的值重复");
        P("this statement may fall through", "这个 case 会继续执行下一个 case");
        P("statement has no effect", "这条语句不起任何作用");
        P("value computed is not used", "算出来的值没有被使用");
        P("unused variable", "没有用到的变量");
        P("unused parameter", "没有用到的参数");
        P("unused value", "没有用到的值");
        P("defined but not used", "定义了却没有使用");
        P("set but not used", "赋了值却没有使用");
        P("may be used uninitialized", "可能在赋值之前就被使用");
        P("is used uninitialized", "在赋值之前就被使用");
        P("uninitialized", "没有初始化的");
        P("comparison between signed and unsigned", "有符号数和无符号数在比较");
        P("comparison of integer expressions of different signedness", "符号不同的整数在比较");
        P("comparison is always", "比较的结果永远是");
        P("comparison with string literal results in unspecified behaviour", "和字符串常量比较，结果不确定");
        P("suggest parentheses around", "建议加上括号：");
        P("suggest explicit braces", "建议加上大括号");
        P("empty body in", "空的循环体或分支，出现在");
        P("division by zero", "除以零");
        P("array subscript is above array bounds", "数组下标超出了上界");
        P("array subscript is below array bounds", "数组下标低于下界");
        P("out of bounds", "越界");
        P("overflow in", "溢出，出现在");
        P("integer overflow", "整数溢出");
        P("format", "格式");
        P("expects argument of type", "需要的参数类型是");
        P("but argument", "但是第");
        P("has type", "个参数的类型是");
        P("ignoring return value of", "忽略了返回值：");
        P("ignoring", "忽略了");
        P("address of local variable", "局部变量的地址：");
        P("reference to local variable", "对局部变量的引用：");
        P("returned", "被返回了");
        P("deprecated conversion from string constant to", "把字符串常量转成了过时的类型");

        // ---- preprocessor, build
        P("#include expects", "#include 后面应该是");
        P("#error", "#error 指令：");
        P("unterminated #", "没有结束的 #");
        P("without #if", "前面没有对应的 #if");
        P("no macro name given in", "缺少宏名字，在");
        P("macro names must be identifiers", "宏的名字必须是标识符");
        P("ignoring #pragma", "忽略了 #pragma");
        P("unknown pragma", "未知的 #pragma");
        P("compilation terminated", "编译中止");
        P("is only available with", "只在以下条件下可用：");
        P("changes meaning in", "在新标准里含义不同：");
        P("please remove it", "请去掉它");
        P("-fpermissive", "-fpermissive");
        P("note:", "说明：");
        P("warning:", "警告：");
        P("error:", "错误：");

        // ---- small connecting words (applied last, only inside unquoted text)
        P("in function", "在函数");
        P("in file included from", "在包含自");
        P("at this point in file", "在文件的这个位置");
        P("here", "这里");
        P("and", "和");
        P("or", "或");
        P("not", "不");
        P("of", "的");
        P("for", "对于");
        P("to", "到");
        P("from", "从");
        P("in", "在");
        P("is", "是");
        P("are", "是");
        P("has", "有");
        P("no", "没有");
        P("only", "只能");
        P("zero", "零");
        P("two", "两个");
        P("one", "一个");
        P("or more", "或更多");
        P("data types", "数据类型");
        P("new types may not be defined in a return type", "不能在返回类型里定义新类型");
        P("two or more data types in declaration of", "声明里出现了两个以上的类型：");
        P("declaration does not declare anything", "这条声明什么也没有声明");
        P("a function-definition is not allowed here", "这里不允许定义函数");
        P("default argument missing for parameter", "缺少默认参数，参数序号：");
        P("takes only zero or two arguments", "只能有零个或两个参数");
        P("must return 'int'", "必须返回 int");
        P("hides overloaded virtual function", "遮住了重载的虚函数");
        P("will be initialized after", "会在……之后才初始化：");
        P("base class should be explicitly initialized in the copy constructor", "拷贝构造函数里应显式初始化基类");
        P("has virtual functions and accessible non-virtual destructor", "有虚函数，但析构函数不是虚的");

        // keep the longest phrases first
        Table.Sort((a, b) => b.Length.CompareTo(a.Length));
    }

    // Rough classification, used as the heading of a literal translation.
    private static string Category(string m, bool warning)
    {
        string l = m.ToLowerInvariant();
        if (warning) return "警告";
        if (Regex.IsMatch(l, "undefined reference|multiple definition|cannot find -l|ld returned|relocation truncated")) return "链接错误";
        if (Regex.IsMatch(l, "no such file|cannot open|permission denied")) return "文件问题";
        if (Regex.IsMatch(l, "^#|preprocessing|#include|#error|macro|pragma|#if|#endif")) return "预处理指令问题";
        if (Regex.IsMatch(l, "expected|stray|missing|unterminated|extraneous|before .* token|at end of input")) return "语法错误";
        if (Regex.IsMatch(l, "not declared|does not name a type|not a member|undeclared|has not been declared|no member named")) return "名字或类型没有声明";
        if (Regex.IsMatch(l, "invalid conversion|cannot convert|invalid operands|no match for|incompatible|invalid initialization|could not convert|narrowing")) return "类型不匹配";
        if (Regex.IsMatch(l, "no matching|too many arguments|too few arguments|call of overloaded|ambiguous")) return "函数调用问题";
        if (Regex.IsMatch(l, "redefinition|redeclaration|conflicting|previous definition|redeclared|shadows")) return "重复或冲突的定义";
        if (Regex.IsMatch(l, "private|protected|inaccessible")) return "访问权限问题";
        if (Regex.IsMatch(l, "template")) return "模板问题";
        if (Regex.IsMatch(l, "abstract|virtual|constructor|destructor|class|member function|base ")) return "类与对象问题";
        if (Regex.IsMatch(l, "read-only|lvalue|const")) return "只读或赋值问题";
        if (Regex.IsMatch(l, "array|subscript|pointer|dereference")) return "数组或指针问题";
        return "编译器报错";
    }

    private static readonly Dictionary<string, GestirnDev.Hints.Translation> memo = new Dictionary<string, GestirnDev.Hints.Translation>();

    public static GestirnDev.Hints.Translation Translate(string message)
    {
        if (!L.Zh || string.IsNullOrWhiteSpace(message)) return null;
        lock (memo)
        {
            GestirnDev.Hints.Translation known;
            if (memo.TryGetValue(message, out known)) return known;
            var t = TranslateNow(message);
            if (memo.Count > 3000) memo.Clear();
            memo[message] = t;
            return t;
        }
    }

    private static GestirnDev.Hints.Translation TranslateNow(string message)
    {
        bool warning = Regex.IsMatch(message, @"\[-W[\w+=-]+\]\s*$");
        var quoted = new List<string>();
        string s = Regex.Replace(message, "'[^']*'|\\[-W[\\w+=-]+\\]|\"[^\"]*\"", mm => { quoted.Add(mm.Value); return "\u0001" + (quoted.Count - 1) + "\u0002"; });
        foreach (var p in Table) s = p.Re.Replace(s, p.Zh);
        s = Regex.Replace(s, "\u0001(\\d+)\u0002", mm => quoted[int.Parse(mm.Groups[1].Value)]);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @" ?([，。：；（）]) ?", "$1");
        string category = Category(message, warning);
        return new GestirnDev.Hints.Translation
        {
            Plain = category + "：" + s,
            Fix = "对照红线标出的位置检查这一行和它的上一行：括号、分号、引号是否配对，名字和类型是否写对。",
            Approx = true,
        };
    }
}
}
