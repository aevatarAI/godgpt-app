using System.Text.Json;
using System.Text.RegularExpressions;
using Scriban;
using Scriban.Runtime;

namespace Aevatar.Agents.Cognitive.Template;

// ============================================================
//  模板引擎 - 基于 Scriban
// ============================================================

/// <summary>
/// 模板引擎，用于渲染 prompt 模板
/// 支持 Mustache/Handlebars 风格语法
/// </summary>
public partial class TemplateEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    /// <summary>
    /// 渲染模板
    /// </summary>
    public string Render(string template, Dictionary<string, object> variables)
    {
        // 预处理：将 {{var}} 转换为 Scriban 的 {{ var }}
        var processedTemplate = PreprocessTemplate(template);
        
        // 解析模板
        var scribanTemplate = Scriban.Template.Parse(processedTemplate);
        if (scribanTemplate.HasErrors)
        {
            throw new TemplateParseException(
                $"Template parse error: {string.Join(", ", scribanTemplate.Messages)}");
        }
        
        // 创建上下文
        var scriptObject = new ScriptObject();
        
        // 注入变量
        foreach (var (key, value) in variables)
        {
            scriptObject[key] = ConvertValue(value);
        }
        
        // 注入内置函数
        RegisterBuiltinFunctions(scriptObject);
        
        var context = new TemplateContext();
        context.PushGlobal(scriptObject);
        
        // 渲染
        return scribanTemplate.Render(context);
    }
    
    /// <summary>
    /// 解析表达式（用于条件判断）
    /// </summary>
    public object? Evaluate(string expression, Dictionary<string, object> variables)
    {
        // 检查表达式是否已被 {{ }} 包裹
        var trimmed = expression.Trim();
        var template = trimmed.StartsWith("{{") && trimmed.EndsWith("}}")
            ? trimmed                              // 已包裹，直接使用
            : $"{{{{ {expression} }}}}";           // 未包裹，添加包裹
        
        var result = Render(template, variables);
        
        // 尝试解析为 bool
        if (bool.TryParse(result.Trim(), out var boolResult))
            return boolResult;
        
        // 尝试解析为数字
        if (double.TryParse(result.Trim(), out var numResult))
            return numResult;
        
        return result;
    }
    
    /// <summary>
    /// 解析变量引用（如 {{items}} → 变量名 "items"）
    /// </summary>
    public object? ResolveValue(object? value, Dictionary<string, object> variables)
    {
        if (value is not string strValue)
            return value;
        
        // 检查是否是简单的变量引用
        var match = SimpleVariableRegex().Match(strValue);
        if (match.Success)
        {
            var varName = match.Groups[1].Value.Trim();
            if (variables.TryGetValue(varName, out var resolved))
                return resolved;
        }
        
        // 否则作为模板渲染
        return Render(strValue, variables);
    }
    
    // ============================================================
    //  私有方法
    // ============================================================
    
    /// <summary>
    /// 预处理模板：将 {{var}} 风格转换为 Scriban 的 {{ var }}
    /// </summary>
    private static string PreprocessTemplate(string template)
    {
        // Scriban 需要空格，但我们允许紧凑语法
        // {{var}} → {{ var }}
        // {{#if condition}} → {{ if condition }}
        // {{#each items}} → {{ for item in items }}
        // {{/if}} → {{ end }}
        // {{/each}} → {{ end }}
        
        var result = template;
        
        // 转换 if/each/end
        result = IfBlockRegex().Replace(result, "{{ if $1 }}");
        result = EachBlockRegex().Replace(result, "{{ for item in $1 }}");
        result = EndBlockRegex().Replace(result, "{{ end }}");
        result = ElseBlockRegex().Replace(result, "{{ else }}");
        
        // 转换 Liquid 风格的过滤器参数 `| filter: arg` → `| filter arg`
        // 例如: {{atomic_check | contains: 'ATOMIC'}} → {{ atomic_check | contains 'ATOMIC' }}
        result = LiquidFilterArgRegex().Replace(result, "$1 ");
        
        // 转换管道过滤器
        result = PipeFilterRegex().Replace(result, "{{ $1 | $2 }}");
        
        // 转换简单变量 {{var}} → {{ var }}
        result = SimpleVarRegex().Replace(result, "{{ $1 }}");
        
        return result;
    }
    
    /// <summary>
    /// 转换值为 Scriban 兼容格式
    /// </summary>
    private static object? ConvertValue(object? value)
    {
        return value switch
        {
            null => null,
            string s => s,
            bool b => b,
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            decimal m => (double)m,
            DateTime dt => dt,
            IEnumerable<object> list => list.Select(ConvertValue).ToList(),
            IDictionary<string, object> dict => dict.ToDictionary(kv => kv.Key, kv => ConvertValue(kv.Value)),
            _ => ConvertComplexObject(value)
        };
    }
    
    /// <summary>
    /// 转换复杂对象为字典
    /// </summary>
    private static object ConvertComplexObject(object value)
    {
        // 使用反射转换为字典
        var type = value.GetType();
        var dict = new Dictionary<string, object?>();
        
        foreach (var prop in type.GetProperties())
        {
            if (prop.CanRead)
            {
                var propValue = prop.GetValue(value);
                dict[ToCamelCase(prop.Name)] = ConvertValue(propValue);
            }
        }
        
        return dict;
    }
    
    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
    
    /// <summary>
    /// 注册内置函数
    /// </summary>
    private static void RegisterBuiltinFunctions(ScriptObject scriptObject)
    {
        // json 过滤器
        scriptObject.Import("json", new Func<object?, string>(obj => 
            JsonSerializer.Serialize(obj, JsonOptions)));
        
        // length 过滤器
        scriptObject.Import("length", new Func<object?, int>(obj => obj switch
        {
            string s => s.Length,
            ICollection<object> c => c.Count,
            IEnumerable<object> e => e.Count(),
            _ => 0
        }));
        
        // truncate 过滤器
        scriptObject.Import("truncate", new Func<string?, int, string>((s, len) => 
            s == null ? "" : s.Length <= len ? s : s[..len] + "..."));
        
        // uppercase/lowercase 过滤器
        scriptObject.Import("uppercase", new Func<string?, string>(s => s?.ToUpperInvariant() ?? ""));
        scriptObject.Import("lowercase", new Func<string?, string>(s => s?.ToLowerInvariant() ?? ""));
        
        // is_atomic 函数（用于 MAKER 判断任务是否原子）
        scriptObject.Import("is_atomic", new Func<string?, bool>(task => 
            task != null && task.Length < 200 && !task.Contains("and") && !task.Contains("then")));
        
        // consensus_k 函数（根据可靠性返回 K 值）
        scriptObject.Import("consensus_k", new Func<string?, int>(reliability => reliability?.ToLowerInvariant() switch
        {
            "low" => 1,
            "medium" => 2,
            "high" => 3,
            _ => 2
        }));
        
        // contains 函数 - 字符串包含检查
        // 用法: {{ atomic_check | contains 'ATOMIC' }}
        scriptObject.Import("contains", new Func<string?, string?, bool>((str, substring) => 
            str != null && substring != null && str.Contains(substring, StringComparison.OrdinalIgnoreCase)));
        
        // default 函数 - 默认值
        // 用法: {{ value | default 'fallback' }}
        scriptObject.Import("default", new Func<object?, object?, object?>((value, defaultValue) => 
            value is null or "" ? defaultValue : value));
        
        // size 函数 - 集合大小
        // 用法: {{ items | size }}
        scriptObject.Import("size", new Func<object?, int>(obj => obj switch
        {
            string s => s.Length,
            System.Collections.ICollection c => c.Count,
            System.Collections.IEnumerable e => e.Cast<object>().Count(),
            _ => 0
        }));
    }
    
    // ============================================================
    //  正则表达式
    // ============================================================
    
    [GeneratedRegex(@"\{\{#if\s+(.+?)\}\}")]
    private static partial Regex IfBlockRegex();
    
    [GeneratedRegex(@"\{\{#each\s+(.+?)\}\}")]
    private static partial Regex EachBlockRegex();
    
    [GeneratedRegex(@"\{\{/(if|each)\}\}")]
    private static partial Regex EndBlockRegex();
    
    [GeneratedRegex(@"\{\{else\}\}")]
    private static partial Regex ElseBlockRegex();
    
    [GeneratedRegex(@"\{\{(.+?)\s*\|\s*(.+?)\}\}")]
    private static partial Regex PipeFilterRegex();
    
    // 转换 Liquid 风格的过滤器参数: `filter: arg` → `filter arg`
    // 匹配: `contains: 'ATOMIC'` → `contains 'ATOMIC'`
    [GeneratedRegex(@"(\|\s*\w+):\s*")]
    private static partial Regex LiquidFilterArgRegex();
    
    [GeneratedRegex(@"\{\{([^#/][^}]*?)\}\}")]
    private static partial Regex SimpleVarRegex();
    
    [GeneratedRegex(@"^\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}$")]
    private static partial Regex SimpleVariableRegex();
}

// ============================================================
//  异常
// ============================================================

public class TemplateParseException : Exception
{
    public TemplateParseException(string message) : base(message) { }
}

