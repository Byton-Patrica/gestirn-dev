using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GestirnDev
{
// A small JSON reader/writer: enough for the chat-completions protocol, no extra assemblies needed.
internal static class Json
{
    public static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    // Returns Dictionary<string, object>, List<object>, string, double, bool or null.
    public static object Parse(string text)
    {
        int i = 0;
        try
        {
            var v = Value(text, ref i);
            return v;
        }
        catch (FormatException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

    private static object Value(string s, ref int i)
    {
        Ws(s, ref i);
        char c = s[i];
        if (c == '{')
        {
            var d = new Dictionary<string, object>();
            i++; Ws(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                Ws(s, ref i);
                string key = Str(s, ref i);
                Ws(s, ref i);
                if (s[i++] != ':') throw new FormatException();
                d[key] = Value(s, ref i);
                Ws(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException();
            }
        }
        if (c == '[')
        {
            var l = new List<object>();
            i++; Ws(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i));
                Ws(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException();
            }
        }
        if (c == '"') return Str(s, ref i);
        if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
        if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
        if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
        int start = i;
        while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
        double n;
        if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out n)) throw new FormatException();
        return n;
    }

    private static string Str(string s, ref int i)
    {
        if (s[i] != '"') throw new FormatException();
        i++;
        var sb = new StringBuilder();
        while (true)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
    }

    // Walks a path such as "choices", 0, "message", "content".
    public static object At(object root, params object[] path)
    {
        object cur = root;
        foreach (var step in path)
        {
            if (step is string)
            {
                var d = cur as Dictionary<string, object>;
                object next;
                if (d == null || !d.TryGetValue((string)step, out next)) return null;
                cur = next;
            }
            else
            {
                var l = cur as List<object>;
                int k = (int)step;
                if (l == null || k < 0 || k >= l.Count) return null;
                cur = l[k];
            }
        }
        return cur;
    }
}

// "AI fix" for compile errors (experimental): sends the file and the compiler messages to an
// OpenAI-compatible chat-completions endpoint the user configured, and returns an explanation plus fixed code.
internal static class AiFix
{
    public sealed class Result
    {
        public string Explanation, Code, Error, Raw;
        public bool Ok { get { return Error == null; } }
    }

    public static bool Configured(AppSettings s)
    {
        Uri u;
        return s.AiFixEnabled && !string.IsNullOrWhiteSpace(s.AiKey) && !string.IsNullOrWhiteSpace(s.AiModel)
            && Uri.TryCreate(s.AiBaseUrl ?? "", UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
    }

    public static string Endpoint(string baseUrl)
    {
        string b = (baseUrl ?? "").Trim().TrimEnd('/');
        return b.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? b : b + "/chat/completions";
    }

    public static string HostOf(string baseUrl)
    {
        Uri u;
        return Uri.TryCreate(baseUrl ?? "", UriKind.Absolute, out u) ? u.Host : "";
    }

    public static string BuildRequest(string model, string system, string user)
    {
        return "{\"model\":" + Json.Quote(model) + ",\"messages\":[{\"role\":\"system\",\"content\":" + Json.Quote(system) +
               "},{\"role\":\"user\",\"content\":" + Json.Quote(user) + "}]}";
    }

    public static string SystemPrompt(bool chinese)
    {
        return "You are a careful C/C++ tutor built into a beginner-friendly IDE. The user's program failed to compile. " +
               "Fix it with the smallest changes that make it compile and keep its intended behaviour. " +
               "Reply in " + (chinese ? "Simplified Chinese" : "English") + ". " +
               "First explain in at most four short sentences what was wrong and what you changed. " +
               "Then give the COMPLETE corrected file in exactly one fenced code block (```cpp). Never leave parts of the file out.";
    }

    public static string UserPrompt(string fileName, string code, string compilerMessages)
    {
        if (compilerMessages.Length > 6000) compilerMessages = compilerMessages.Substring(0, 6000) + "\n...";
        if (code.Length > 60000) code = code.Substring(0, 60000);
        return "File: " + fileName + "\n\nCompiler messages:\n```\n" + compilerMessages + "\n```\n\nSource code:\n```cpp\n" + code + "\n```";
    }

    // Splits a reply into the explanation (text outside code blocks) and the biggest code block.
    public static Result ParseReply(string content)
    {
        var r = new Result { Raw = content };
        if (string.IsNullOrWhiteSpace(content)) { r.Error = L.T("The model sent an empty reply.", "模型返回了空内容。"); return r; }
        var blocks = Regex.Matches(content, "```[^\\r\\n]*\\r?\\n(.*?)```", RegexOptions.Singleline).Cast<Match>().ToList();
        if (blocks.Count > 0)
        {
            var best = blocks.OrderByDescending(b => b.Groups[1].Length).First();
            r.Code = best.Groups[1].Value.TrimEnd();
            r.Explanation = Regex.Replace(content, "```[^\\r\\n]*\\r?\\n.*?```", "", RegexOptions.Singleline).Trim();
        }
        else r.Explanation = content.Trim();
        return r;
    }

    public static Task<Result> Ask(AppSettings s, string fileName, string code, string compilerMessages, int timeoutMs = 90000)
    {
        return Task.Run(() => Send(s, SystemPrompt(L.Zh), UserPrompt(fileName, code, compilerMessages), timeoutMs));
    }

    // A tiny request used by "Test connection" in Settings.
    public static Task<Result> Ping(AppSettings s)
    {
        return Task.Run(() => Send(s, "Reply with the single word OK.", "ping", 30000));
    }

    private static Result Send(AppSettings s, string system, string user, int timeoutMs)
    {
        var result = new Result();
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(Endpoint(s.AiBaseUrl));
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Accept = "application/json";
            req.UserAgent = "GestirnDev";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.Headers["Authorization"] = "Bearer " + s.AiKey.Trim();
            byte[] body = Encoding.UTF8.GetBytes(BuildRequest(s.AiModel.Trim(), system, user));
            req.ContentLength = body.Length;
            using (var stream = req.GetRequestStream()) stream.Write(body, 0, body.Length);
            string text;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                text = reader.ReadToEnd();
            var content = Json.At(Json.Parse(text), "choices", 0, "message", "content") as string;
            if (content == null)
            {
                result.Error = L.T("The reply was not in the expected format.", "返回的内容格式不对。");
                result.Raw = text;
                return result;
            }
            return ParseReply(content);
        }
        catch (WebException ex)
        {
            result.Error = Describe(ex);
        }
        catch (Exception ex) when (ex is IOException || ex is UriFormatException || ex is NotSupportedException || ex is InvalidOperationException)
        {
            result.Error = ex.Message;
        }
        return result;
    }

    private static string Describe(WebException ex)
    {
        var resp = ex.Response as HttpWebResponse;
        if (resp != null)
        {
            string text = "";
            try { using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) text = r.ReadToEnd(); }
            catch (IOException) { }
            string detail = Json.At(Json.Parse(text), "error", "message") as string;
            int code = (int)resp.StatusCode;
            string hint = code == 401 ? L.T(" Check the API key.", " 请检查 API Key。")
                        : code == 404 ? L.T(" Check the API address and model name.", " 请检查接口地址和模型名称。")
                        : code == 429 ? L.T(" Rate limit or no credit left.", " 请求太频繁或余额不足。") : "";
            return "HTTP " + code + (string.IsNullOrEmpty(detail) ? "" : ": " + detail) + hint;
        }
        if (ex.Status == WebExceptionStatus.Timeout) return L.T("The request timed out.", "请求超时。");
        return L.T("Couldn't reach the server: ", "无法连接服务器：") + ex.Message;
    }
}
}
