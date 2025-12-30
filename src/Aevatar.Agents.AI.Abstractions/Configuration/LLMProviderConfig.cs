using System.Collections.Generic;

namespace Aevatar.Agents.AI.Abstractions.Configuration;

/// <summary>
/// LLM提供商配置
/// <para/>
/// 在appsettings.json中配置多个LLM提供商
/// </summary>
public class LLMProviderConfig
{
    /// <summary>
    /// 提供商名称（唯一标识，如：openai-gpt4, azure-gpt35, local-llama等）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 提供商类型（OpenAI, AzureOpenAI, Local, etc）
    /// </summary>
    public string ProviderType { get; set; } = string.Empty;

    /// <summary>
    /// API密钥
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 模型名称（如：gpt-4, gpt-3.5-turbo, llama2-70b等）
    /// </summary>
    public string Model { get; set; } = "gpt-4";

    /// <summary>
    /// API终结点（可选，用于Azure或本地模型）
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// 部署名称（Azure OpenAI专用）
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// 温度参数（0-2）
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// 最大令牌数
    /// </summary>
    public int MaxTokens { get; set; } = 2000;

    /// <summary>
    /// 超时时间（毫秒）
    /// </summary>
    public int TimeoutMilliseconds { get; set; } = 60000;

    /// <summary>
    /// 是否启用流式响应
    /// </summary>
    public bool EnableStreaming { get; set; } = false;

    /// <summary>
    /// 是否启用工具/函数调用（默认false，某些代理模型不支持）
    /// </summary>
    public bool EnableTools { get; set; } = false;

    /// <summary>
    /// 提供商特定设置
    /// </summary>
    public Dictionary<string, object> ProviderSpecificSettings { get; set; } = new();

    /// <summary>
    /// Embedding 通道配置（可选）
    /// </summary>
    public LLMEmbeddingConfig? Embeddings { get; set; }
}

/// <summary>
/// LLM提供商集合配置
/// <para/>
/// 在appsettings.json中的配置示例：
/// <code>
/// {
///   "LLMProviders": {
///     "default": "openai-gpt4",
///     "providers": {
///       "openai-gpt4": {
///         "providerType": "OpenAI",
///         "apiKey": "${OPENAI_API_KEY}",
///         "model": "gpt-4",
///         "temperature": 0.7
///       },
///       "azure-gpt35": {
///         "providerType": "AzureOpenAI",
///         "apiKey": "${AZURE_API_KEY}",
///         "endpoint": "https://your-resource.openai.azure.com",
///         "deploymentName": "gpt-35-turbo",
///         "model": "gpt-3.5-turbo"
///       },
///       "local-llama": {
///         "providerType": "Ollama",
///         "endpoint": "http://localhost:11434",
///         "model": "llama2:70b"
///       }
///     }
///   }
/// }
/// </code>
/// </summary>
public class LLMProvidersConfig
{
    /// <summary>
    /// 默认提供商名称
    /// </summary>
    public string Default { get; set; } = "openai-gpt4";

    /// <summary>
    /// 提供商字典（key: 提供商名称, value: 配置）
    /// </summary>
    public Dictionary<string, LLMProviderConfig> Providers { get; set; } = new();
}

/// <summary>
/// Embedding 配置，用于驱动 IEmbeddingGenerator
/// </summary>
public class LLMEmbeddingConfig
{
    /// <summary>
    /// 是否启用 Embedding 通道（默认 true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Embedding 提供商类型（OpenAI, AzureOpenAI, Ollama 等）。
    /// 若未指定，默认沿用主配置的 ProviderType。
    /// </summary>
    public string? ProviderType { get; set; }

    /// <summary>
    /// Embedding 模型名称（OpenAI 场景）
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Embedding 部署名称（Azure OpenAI 场景）
    /// </summary>
    public string? DeploymentName { get; set; }

    /// <summary>
    /// 可覆盖的 Endpoint
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// 可覆盖的 API Key
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 维度（部分模型需要手动指定）
    /// </summary>
    public int? Dimensions { get; set; }

    /// <summary>
    /// 提供商特定扩展字段
    /// </summary>
    public Dictionary<string, object> ProviderSpecificSettings { get; set; } = new();
}