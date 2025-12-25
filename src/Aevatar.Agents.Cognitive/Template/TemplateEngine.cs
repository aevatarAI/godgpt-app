using System.Text.Encodings.Web;
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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Human-readable JSON in prompts:
        // - Avoid \uXXXX for non-ASCII (Chinese / math symbols like ℋ, Φ, ⊗)
        // - Safe here because this JSON is used for LLM prompts, not for HTML/JS embedding.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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
        var result = scribanTemplate.Render(context);
        
        // DEBUG: 如果渲染结果中包含空的 Task，输出调试信息
        if (result.Contains("Task:") && result.Contains("Task: \n"))
        {
            System.Diagnostics.Debug.WriteLine($"[TemplateEngine] Empty Task detected!");
            System.Diagnostics.Debug.WriteLine($"[TemplateEngine] Variables: {string.Join(", ", variables.Keys)}");
            System.Diagnostics.Debug.WriteLine($"[TemplateEngine] task in vars: {variables.ContainsKey("task")}");
            if (variables.ContainsKey("task"))
            {
                var taskVal = variables["task"];
                System.Diagnostics.Debug.WriteLine($"[TemplateEngine] task type: {taskVal?.GetType().Name ?? "null"}");
                System.Diagnostics.Debug.WriteLine($"[TemplateEngine] task length: {taskVal?.ToString()?.Length ?? 0}");
            }
        }
        
        return result;
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
    /// 预处理模板：将各种模板语法转换为 Scriban 的统一语法
    /// 支持: Mustache ({{var}}), Handlebars ({{#if}}), Liquid/Jinja2 ({% if %})
    /// </summary>
    private static string PreprocessTemplate(string template)
    {
        var result = template;
        
        // ─────────────────────────────────────────────────────────
        //  1. Liquid/Jinja2 风格 {% if %} / {% endif %}
        //     转换为 Scriban 的 {{ if }} / {{ end }}
        // ─────────────────────────────────────────────────────────
        result = LiquidIfRegex().Replace(result, "{{ if $1 }}");
        result = LiquidElseRegex().Replace(result, "{{ else }}");
        result = LiquidEndIfRegex().Replace(result, "{{ end }}");
        result = LiquidForRegex().Replace(result, "{{ for $1 }}");
        result = LiquidEndForRegex().Replace(result, "{{ end }}");
        
        // ─────────────────────────────────────────────────────────
        //  2. Handlebars 风格 {{#if}} / {{/if}}
        // ─────────────────────────────────────────────────────────
        result = IfBlockRegex().Replace(result, "{{ if $1 }}");
        result = EachBlockRegex().Replace(result, "{{ for item in $1 }}");
        result = EndBlockRegex().Replace(result, "{{ end }}");
        result = ElseBlockRegex().Replace(result, "{{ else }}");
        
        // ─────────────────────────────────────────────────────────
        //  3. Liquid 过滤器参数语法 `| filter: arg` → `| filter arg`
        // ─────────────────────────────────────────────────────────
        result = LiquidFilterArgRegex().Replace(result, "$1 ");
        
        // ─────────────────────────────────────────────────────────
        //  4. 管道过滤器和简单变量
        // ─────────────────────────────────────────────────────────
        result = PipeFilterRegex().Replace(result, "{{ $1 | $2 }}");
        result = SimpleVarRegex().Replace(result, "{{ $1 }}");
        
        return result;
    }
    
    /// <summary>
    /// 转换值为 Scriban 兼容格式
    /// 关键：字典必须转换为 ScriptObject 才能支持 item.property 语法
    /// </summary>
    private static object? ConvertValue(object? value)
    {
        if (value == null) return null;
        
        // 优先检查字典类型（必须在 IEnumerable 之前，因为字典也实现 IEnumerable）
        if (value is IDictionary<string, object> dict)
            return ConvertDictToScriptObject(dict);
        
        // 检查非泛型字典（兼容不同的字典类型）
        if (value is System.Collections.IDictionary nonGenericDict)
            return ConvertNonGenericDictToScriptObject(nonGenericDict);
        
        return value switch
        {
            string s => s,
            bool b => b,
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            decimal m => (double)m,
            DateTime dt => dt,
            ScriptObject so => so, // 已经是 ScriptObject，直接返回
            IEnumerable<object> list => list.Select(ConvertValue).ToList(),
            System.Collections.IEnumerable enumerable => enumerable.Cast<object>().Select(ConvertValue).ToList(),
            _ => ConvertComplexObject(value)
        };
    }
    
    /// <summary>
    /// 将非泛型字典转换为 ScriptObject
    /// </summary>
    private static ScriptObject ConvertNonGenericDictToScriptObject(System.Collections.IDictionary dict)
    {
        var scriptObj = new ScriptObject();
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            var key = entry.Key?.ToString() ?? "";
            scriptObj[key] = ConvertValue(entry.Value);
        }
        return scriptObj;
    }
    
    /// <summary>
    /// 将字典转换为 Scriban ScriptObject，支持 item.property 语法
    /// </summary>
    private static ScriptObject ConvertDictToScriptObject(IDictionary<string, object> dict)
    {
        var scriptObj = new ScriptObject();
        foreach (var (key, val) in dict)
        {
            scriptObj[key] = ConvertValue(val);
        }
        return scriptObj;
    }
    
    /// <summary>
    /// 转换复杂对象为 ScriptObject
    /// </summary>
    private static ScriptObject ConvertComplexObject(object value)
    {
        var type = value.GetType();
        var scriptObj = new ScriptObject();
        
        foreach (var prop in type.GetProperties())
        {
            if (prop.CanRead)
            {
                var propValue = prop.GetValue(value);
                scriptObj[ToCamelCase(prop.Name)] = ConvertValue(propValue);
            }
        }
        
        return scriptObj;
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
    
    // Liquid/Jinja2 风格: {% if %}, {% else %}, {% endif %}, {% for %}, {% endfor %}
    [GeneratedRegex(@"\{%\s*if\s+(.+?)\s*%\}")]
    private static partial Regex LiquidIfRegex();
    
    [GeneratedRegex(@"\{%\s*else\s*%\}")]
    private static partial Regex LiquidElseRegex();
    
    [GeneratedRegex(@"\{%\s*endif\s*%\}")]
    private static partial Regex LiquidEndIfRegex();
    
    [GeneratedRegex(@"\{%\s*for\s+(.+?)\s*%\}")]
    private static partial Regex LiquidForRegex();
    
    [GeneratedRegex(@"\{%\s*endfor\s*%\}")]
    private static partial Regex LiquidEndForRegex();
    
    // Handlebars 风格: {{#if}}, {{/if}}
    [GeneratedRegex(@"\{\{#if\s+(.+?)\}\}")]
    private static partial Regex IfBlockRegex();
    
    [GeneratedRegex(@"\{\{#each\s+(.+?)\}\}")]
    private static partial Regex EachBlockRegex();
    
    [GeneratedRegex(@"\{\{/(if|each)\}\}")]
    private static partial Regex EndBlockRegex();
    
    [GeneratedRegex(@"\{\{else\}\}")]
    private static partial Regex ElseBlockRegex();
    
    // NOTE:
    // - `|` in Scriban is used for filter / pipe expressions (value | filter).
    // - `||` is a boolean operator, and MUST NOT be treated as a pipe.
    // - Our previous regex matched `||` accidentally (because it allowed 0 whitespace), which
    //   caused expressions like `a || b` to be rewritten and crash with errors like:
    //   "Invalid target function `True` (bool)".
    [GeneratedRegex(@"\{\{(.+?)\s*(?<!\|)\|(?!\|)\s*(.+?)\}\}")]
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

