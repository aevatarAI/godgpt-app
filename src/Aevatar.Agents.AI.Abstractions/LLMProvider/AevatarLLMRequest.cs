namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// LLM请求
/// </summary>
public class AevatarLLMRequest
{
    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? SystemPrompt { get; set; }
    
    /// <summary>
    /// 用户提示词
    /// </summary>
    public string UserPrompt { get; set; } = string.Empty;
    
    /// <summary>
    /// 对话历史
    /// </summary>
    public IList<AevatarChatMessage> Messages { get; set; } = new List<AevatarChatMessage>();
    
    /// <summary>
    /// 模型设置
    /// </summary>
    public AevatarLLMSettings Settings { get; set; } = new();
    
    /// <summary>
    /// 函数/工具定义（用于Function Calling）
    /// </summary>
    public IList<AevatarFunctionDefinition>? Functions { get; set; }
    
    /// <summary>
    /// 上下文窗口中的额外信息
    /// </summary>
    public Dictionary<string, object>? Context { get; set; }
    
    /// <summary>
    /// Images for multimodal input (already resolved with data)
    /// </summary>
    public IList<AevatarImageData>? Images { get; set; }
}

/// <summary>
/// Resolved image data for LLM multimodal requests
/// </summary>
public class AevatarImageData
{
    /// <summary>
    /// Original key from blob storage
    /// </summary>
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// Raw image bytes
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; set; }
    
    /// <summary>
    /// MIME type (e.g., "image/jpeg", "image/png")
    /// </summary>
    public string MediaType { get; set; } = "image/jpeg";
}