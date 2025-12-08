using Aevatar.Agents.Cognitive.Primitives;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aevatar.Agents.Cognitive.Engine;

// ============================================================
//  工作流解析器 - YAML → WorkflowDefinition
// ============================================================

/// <summary>
/// 工作流解析器 - 将 YAML 文件解析为 WorkflowDefinition
/// </summary>
public class WorkflowParser
{
    private readonly IDeserializer _deserializer;
    
    public WorkflowParser()
    {
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }
    
    /// <summary>
    /// 从 YAML 字符串解析工作流
    /// </summary>
    public WorkflowDefinition Parse(string yaml)
    {
        var yamlDef = _deserializer.Deserialize<YamlWorkflowDefinition>(yaml);
        return ConvertToWorkflowDefinition(yamlDef);
    }
    
    /// <summary>
    /// 从文件解析工作流
    /// </summary>
    public WorkflowDefinition ParseFile(string filePath)
    {
        var yaml = File.ReadAllText(filePath);
        return Parse(yaml);
    }
    
    /// <summary>
    /// 从目录加载所有工作流
    /// </summary>
    public IEnumerable<WorkflowDefinition> ParseDirectory(string directoryPath)
    {
        var files = Directory.GetFiles(directoryPath, "*.yaml")
            .Concat(Directory.GetFiles(directoryPath, "*.yml"));
        
        foreach (var file in files)
        {
            WorkflowDefinition? workflow = null;
            try
            {
                workflow = ParseFile(file);
            }
            catch
            {
                // 跳过无效文件
            }
            
            if (workflow != null)
            {
                yield return workflow;
            }
        }
    }
    
    // ============================================================
    //  转换方法
    // ============================================================
    
    private static WorkflowDefinition ConvertToWorkflowDefinition(YamlWorkflowDefinition yaml)
    {
        return new WorkflowDefinition
        {
            Name = yaml.Name ?? "unnamed",
            Version = yaml.Version ?? "1.0",
            Description = yaml.Description ?? "",
            Inputs = yaml.Inputs?.Select(ConvertToInputParameter).ToList() ?? [],
            Steps = yaml.Steps?.Select(ConvertToStepDefinition).ToList() ?? [],
            Output = yaml.Output ?? new Dictionary<string, string>()
        };
    }
    
    private static InputParameter ConvertToInputParameter(YamlInputParameter yaml)
    {
        return new InputParameter
        {
            Name = yaml.Name ?? "",
            Type = yaml.Type ?? "string",
            Required = yaml.Required,
            DefaultValue = yaml.Default
        };
    }
    
    private static StepDefinition ConvertToStepDefinition(YamlStepDefinition yaml)
    {
        return new StepDefinition
        {
            Id = yaml.Id ?? Guid.NewGuid().ToString("N")[..8],
            Type = yaml.Type ?? "llm_call",
            Parameters = ConvertParameters(yaml),
            Store = yaml.Store,
            Condition = yaml.Condition,
            IfTrue = yaml.IfTrue?.Select(ConvertToStepDefinition).ToList(),
            IfFalse = yaml.IfFalse?.Select(ConvertToStepDefinition).ToList(),
            Generator = yaml.Generator != null ? ConvertToStepDefinition(yaml.Generator) : null,
            ForEach = yaml.ForEach,
            Step = yaml.Step != null ? ConvertToStepDefinition(yaml.Step) : null,
            Reduce = yaml.Reduce,
            MaxConcurrency = yaml.MaxConcurrency,
            Workflow = yaml.Workflow,
            Params = yaml.Params,
            MaxDepth = yaml.MaxDepth
        };
    }
    
    private static Dictionary<string, object?> ConvertParameters(YamlStepDefinition yaml)
    {
        var parameters = new Dictionary<string, object?>();
        
        // 添加类型特定参数
        if (!string.IsNullOrEmpty(yaml.Prompt))
            parameters["prompt"] = yaml.Prompt;
        
        if (!string.IsNullOrEmpty(yaml.System))
            parameters["system"] = yaml.System;
        
        if (!string.IsNullOrEmpty(yaml.Output))
            parameters["output"] = yaml.Output;
        
        if (yaml.K != null)
            parameters["k"] = yaml.K;
        
        if (yaml.MaxRounds != null)
            parameters["max_rounds"] = yaml.MaxRounds;
        
        if (yaml.Similarity != null)
            parameters["similarity"] = yaml.Similarity;
        
        if (yaml.Variables != null)
            parameters["variables"] = yaml.Variables;
        
        if (yaml.Steps != null)
            parameters["steps"] = yaml.Steps.Select(ConvertToStepDefinition).ToList();
        
        return parameters;
    }
}

// ============================================================
//  YAML 数据模型
// ============================================================

internal class YamlWorkflowDefinition
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public List<YamlInputParameter>? Inputs { get; set; }
    public List<YamlStepDefinition>? Steps { get; set; }
    public Dictionary<string, string>? Output { get; set; }
}

internal class YamlInputParameter
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool Required { get; set; }
    public object? Default { get; set; }
}

internal class YamlStepDefinition
{
    // 基础字段
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Store { get; set; }
    
    // llm_call 字段
    public string? Prompt { get; set; }
    public string? System { get; set; }
    public string? Output { get; set; }
    
    // conditional 字段
    public string? Condition { get; set; }
    public List<YamlStepDefinition>? IfTrue { get; set; }
    public List<YamlStepDefinition>? IfFalse { get; set; }
    
    // vote 字段 (支持模板变量如 "{{k}}")
    public object? K { get; set; }
    public object? MaxRounds { get; set; }
    public object? Similarity { get; set; }
    public YamlStepDefinition? Generator { get; set; }
    
    // fan_out 字段 (支持模板变量)
    public string? ForEach { get; set; }
    public YamlStepDefinition? Step { get; set; }
    public string? Reduce { get; set; }
    public object? MaxConcurrency { get; set; }
    
    // workflow_call 字段 (支持模板变量)
    public string? Workflow { get; set; }
    public Dictionary<string, object?>? Params { get; set; }
    public object? MaxDepth { get; set; }
    
    // checkpoint 字段
    public List<string>? Variables { get; set; }
    
    // parallel 字段
    public List<YamlStepDefinition>? Steps { get; set; }
}

