using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aevatar.Agents.Cognitive.Template;

// ============================================================
//  输出解析器接口
// ============================================================

/// <summary>
/// 输出解析器接口
/// </summary>
public interface IOutputParser
{
    /// <summary>解析 LLM 输出</summary>
    object? Parse(string content);
}

/// <summary>
/// 泛型输出解析器接口
/// </summary>
public interface IOutputParser<out T> : IOutputParser
{
    /// <summary>解析 LLM 输出</summary>
    new T? Parse(string content);
    
    object? IOutputParser.Parse(string content) => Parse(content);
}

// ============================================================
//  输出解析器工厂
// ============================================================

/// <summary>
/// 输出解析器工厂
/// </summary>
public partial class OutputParserFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    /// <summary>
    /// 根据输出类型创建解析器
    /// </summary>
    /// <param name="outputType">输出类型字符串，如 "text", "json", "json_array", "regex(...)"</param>
    public IOutputParser Create(string outputType)
    {
        // 解析类型字符串
        var trimmed = outputType.Trim().ToLowerInvariant();
        
        // text
        if (trimmed == "text")
            return new TextOutputParser();
        
        // first_line
        if (trimmed == "first_line")
            return new FirstLineOutputParser();
        
        // code_block
        if (trimmed == "code_block")
            return new CodeBlockOutputParser();
        
        // json 或 json<TypeName>
        if (trimmed.StartsWith("json"))
        {
            // json_array<TypeName>
            if (trimmed.StartsWith("json_array"))
                return new JsonArrayOutputParser();
            
            // json<TypeName> 或 json
            return new JsonOutputParser();
        }
        
        // regex("pattern")
        var regexMatch = RegexTypePattern().Match(outputType);
        if (regexMatch.Success)
        {
            var pattern = regexMatch.Groups[1].Value;
            return new RegexOutputParser(pattern);
        }
        
        // 默认返回文本解析器
        return new TextOutputParser();
    }
    
    /// <summary>
    /// 创建泛型解析器
    /// </summary>
    public IOutputParser<T> Create<T>()
    {
        var type = typeof(T);
        
        if (type == typeof(string))
            return (IOutputParser<T>)(object)new TextOutputParser();
        
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            return (IOutputParser<T>)(object)new JsonArrayOutputParser();
        
        return (IOutputParser<T>)(object)new JsonOutputParser();
    }
    
    [GeneratedRegex(@"regex\([""'](.+?)[""']\)")]
    private static partial Regex RegexTypePattern();
}

// ============================================================
//  具体解析器实现
// ============================================================

/// <summary>
/// 文本解析器 - 直接返回原文
/// </summary>
public class TextOutputParser : IOutputParser<string>
{
    public string? Parse(string content) => content?.Trim();
}

/// <summary>
/// 首行解析器 - 返回第一行
/// </summary>
public class FirstLineOutputParser : IOutputParser<string>
{
    public string? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[0].Trim() : null;
    }
}

/// <summary>
/// 代码块解析器 - 提取 ``` 包裹的内容
/// </summary>
public partial class CodeBlockOutputParser : IOutputParser<string>
{
    public string? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        var match = CodeBlockPattern().Match(content);
        return match.Success ? match.Groups[1].Value.Trim() : content.Trim();
    }
    
    [GeneratedRegex(@"```(?:\w+)?\s*([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex CodeBlockPattern();
}

/// <summary>
/// JSON 对象解析器
/// 返回 Dictionary 以便模板引擎可以访问属性
/// </summary>
public partial class JsonOutputParser : IOutputParser<object>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public object? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        // 尝试提取 JSON（可能包裹在代码块中）
        var json = ExtractJson(content);
        
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(json, Options);
            return ConvertJsonElement(element);
        }
        catch (JsonException)
        {
            // 解析失败：尝试修复常见的 LLM “伪 JSON”问题（尤其是 proof 里的 LaTeX 反斜杠）
            // NOTE:
            // - 不能把失败的 JSON 当成 string 返回，否则下游会把 state 当字符串继续跑，
            //   最终在 conditional 访问 state.xxx 时崩溃（表现为“原因不明中断”）
            var repaired = TryRepairJson(json);
            if (repaired != null)
            {
                try
                {
                    var element2 = JsonSerializer.Deserialize<JsonElement>(repaired, Options);
                    return ConvertJsonElement(element2);
                }
                catch (JsonException)
                {
                    // fall through
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Best-effort JSON repair for LLM outputs.
    /// Handles common issues:
    /// - Unescaped backslashes inside JSON string literals (e.g. LaTeX "\mathcal{H}")
    /// - Raw newline/tab characters inside JSON string literals
    /// </summary>
    private static string? TryRepairJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        // Only attempt repair for JSON objects
        var trimmed = json.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            return null;

        var sb = new System.Text.StringBuilder(trimmed.Length + 32);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];

            if (!inString)
            {
                if (c == '"')
                {
                    inString = true;
                    escaped = false;
                }
                sb.Append(c);
                continue;
            }

            // in string
            if (escaped)
            {
                // preserve the escape as-is
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                // Check if this is a valid JSON escape. If not, escape the backslash itself.
                var next = i + 1 < trimmed.Length ? trimmed[i + 1] : '\0';
                var valid = next is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u';
                if (!valid)
                {
                    sb.Append("\\\\"); // turn "\" into "\\"
                }
                else
                {
                    sb.Append('\\');
                    escaped = true;
                }
                continue;
            }

            if (c == '"')
            {
                inString = false;
                sb.Append(c);
                continue;
            }

            // Raw control chars are illegal inside JSON strings; escape them.
            if (c == '\n')
            {
                sb.Append("\\n");
                continue;
            }
            if (c == '\r')
            {
                sb.Append("\\r");
                continue;
            }
            if (c == '\t')
            {
                sb.Append("\\t");
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
    
    /// <summary>
    /// 将 JsonElement 转换为 CLR 类型（字典/列表/原始值）
    /// </summary>
    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList(),
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString()
        };
    }
    
    private static string ExtractJson(string content)
    {
        // 尝试提取代码块中的 JSON
        var codeBlockMatch = JsonCodeBlockPattern().Match(content);
        if (codeBlockMatch.Success)
            return codeBlockMatch.Groups[1].Value.Trim();
        
        // 尝试找到 JSON 对象边界
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        
        if (start >= 0 && end > start)
            return content[start..(end + 1)];
        
        return content.Trim();
    }
    
    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex JsonCodeBlockPattern();
}

/// <summary>
/// JSON 数组解析器
/// 返回 List&lt;Dictionary&gt; 以便模板引擎可以访问属性（如 item.description）
/// </summary>
public partial class JsonArrayOutputParser : IOutputParser<List<object>>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public List<object>? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        // 尝试提取 JSON 数组
        var json = ExtractJsonArray(content);
        
        try
        {
            var array = JsonSerializer.Deserialize<JsonElement>(json, Options);
            if (array.ValueKind == JsonValueKind.Array)
            {
                // 转换为字典列表，以便模板引擎可以访问属性
                return array.EnumerateArray()
                    .Select(ConvertJsonElement)
                    .ToList();
            }
            return [ConvertJsonElement(array)];
        }
        catch (JsonException)
        {
            // 解析失败：尝试修复常见的 LLM “伪 JSON”问题（尤其是 LaTeX 反斜杠）
            // NOTE:
            // - json_array 常用于 proposed_b / candidate pools；这些字符串经常包含 "\mathcal{H}" 之类的内容
            // - 若不修复，会导致 strict_parse=true 时直接 redflag-parse-null，流程在 conditional 内“看似无关地”失败
            var repaired = TryRepairJson(json);
            if (repaired != null)
            {
                try
                {
                    var array2 = JsonSerializer.Deserialize<JsonElement>(repaired, Options);
                    if (array2.ValueKind == JsonValueKind.Array)
                    {
                        return array2.EnumerateArray()
                            .Select(ConvertJsonElement)
                            .ToList();
                    }
                    return [ConvertJsonElement(array2)];
                }
                catch (JsonException)
                {
                    // fall through
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Best-effort JSON repair for LLM outputs (array/object).
    /// Handles common issues:
    /// - Unescaped backslashes inside JSON string literals (e.g. LaTeX "\mathcal{H}")
    /// - Raw newline/tab characters inside JSON string literals
    /// </summary>
    private static string? TryRepairJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        var trimmed = json.Trim();
        if (!((trimmed.StartsWith('[') && trimmed.EndsWith(']')) ||
              (trimmed.StartsWith('{') && trimmed.EndsWith('}'))))
        {
            return null;
        }

        var sb = new System.Text.StringBuilder(trimmed.Length + 32);
        var inString = false;
        var escaped = false;

        static bool IsValidJsonEscape(char c)
            => c is '"' or '\\' or '/' or 'b' or 'f' or 'n' or 'r' or 't' or 'u';

        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];

            if (!inString)
            {
                if (c == '"')
                {
                    inString = true;
                    escaped = false;
                }
                sb.Append(c);
                continue;
            }

            // In string
            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '"')
            {
                inString = false;
                sb.Append(c);
                continue;
            }

            if (c == '\\')
            {
                var next = i + 1 < trimmed.Length ? trimmed[i + 1] : '\0';
                if (IsValidJsonEscape(next))
                {
                    sb.Append(c);
                    escaped = true;
                }
                else
                {
                    // Invalid escape like "\m" → repair as "\\m"
                    sb.Append("\\\\");
                }
                continue;
            }

            // Raw control chars in string
            if (c == '\n')
            {
                sb.Append("\\n");
                continue;
            }
            if (c == '\r')
            {
                sb.Append("\\r");
                continue;
            }
            if (c == '\t')
            {
                sb.Append("\\t");
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
    
    /// <summary>
    /// 将 JsonElement 转换为 CLR 类型（字典/列表/原始值）
    /// </summary>
    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElement)
                .ToList(),
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString()
        };
    }
    
    private static string ExtractJsonArray(string content)
    {
        // 尝试提取代码块中的 JSON
        var codeBlockMatch = JsonCodeBlockPattern().Match(content);
        if (codeBlockMatch.Success)
            return codeBlockMatch.Groups[1].Value.Trim();
        
        // 尝试找到 JSON 数组边界
        var start = content.IndexOf('[');
        var end = content.LastIndexOf(']');
        
        if (start >= 0 && end > start)
            return content[start..(end + 1)];
        
        return content.Trim();
    }
    
    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex JsonCodeBlockPattern();
}

/// <summary>
/// 正则表达式解析器
/// </summary>
public class RegexOutputParser : IOutputParser<string>
{
    private readonly Regex _pattern;
    
    public RegexOutputParser(string pattern)
    {
        _pattern = new Regex(pattern, RegexOptions.Compiled);
    }
    
    public string? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        var match = _pattern.Match(content);
        if (!match.Success)
            return null;
        
        // 如果有捕获组，返回第一个捕获组
        // 否则返回整个匹配
        return match.Groups.Count > 1 
            ? match.Groups[1].Value 
            : match.Value;
    }
}

/// <summary>
/// 回退解析器 - 按顺序尝试多个解析器
/// </summary>
public class FallbackOutputParser : IOutputParser
{
    private readonly IOutputParser[] _parsers;
    
    public FallbackOutputParser(params IOutputParser[] parsers)
    {
        _parsers = parsers;
    }
    
    public object? Parse(string content)
    {
        foreach (var parser in _parsers)
        {
            try
            {
                var result = parser.Parse(content);
                if (result != null)
                    return result;
            }
            catch
            {
                // 继续尝试下一个解析器
            }
        }
        
        return content;
    }
}

