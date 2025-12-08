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
            return JsonSerializer.Deserialize<JsonElement>(json, Options);
        }
        catch (JsonException)
        {
            // 解析失败，返回原文
            return content;
        }
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
                return array.EnumerateArray()
                    .Select(e => (object)e)
                    .ToList();
            }
            return [array];
        }
        catch (JsonException)
        {
            return null;
        }
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

