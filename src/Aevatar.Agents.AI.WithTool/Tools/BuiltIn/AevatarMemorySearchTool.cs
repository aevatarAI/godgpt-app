using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Tools.BuiltIn;

/// <summary>
/// 内存搜索工具 - 内置AI工具（简化实现）
/// <para/>
/// 在Agent的内存中搜索相关信息，包括工作记忆、对话历史和长期记忆
/// </summary>
[AevatarTool(
    Name = "search_memory",
    Description = "Search agent memory for relevant information across working memory, conversation history, and long-term memory",
    Category = ToolCategory.Core,
    Version = "1.0.0",
    AutoRegister = true,
    Tags = ["memory", "search", "recall"]
)]
public class AevatarMemorySearchTool : AevatarToolBase
{
    private readonly ILogger<AevatarMemorySearchTool> _logger;

    public AevatarMemorySearchTool(ILogger<AevatarMemorySearchTool> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override string Name => "search_memory";
    public override string Description => "Search agent memory for relevant information";
    public override ToolCategory Category => ToolCategory.Core;
    public override string Version => "1.0.0";
    public override IList<string> Tags => new List<string> { "memory", "search", "recall" };

    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Required = new[] { "query" },
            Items = new Dictionary<string, ToolParameter>
            {
                ["query"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Search query to find relevant information in memory",
                    Required = true
                },
                ["maxResults"] = new ToolParameter
                {
                    Type = "integer",
                    Description = "Maximum number of results to return",
                    Required = false,
                    DefaultValue = 10
                },
                ["memoryType"] = new ToolParameter
                {
                    Type = "string",
                    Description = "Type of memory to search (all, working, conversation, longterm)",
                    Required = false,
                    DefaultValue = "all",
                    Enum = new[] { "all", "working", "conversation", "longterm" }
                }
            }
        };
    }

    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = parameters.GetValueOrDefault("query")?.ToString();
            var maxResultsObj = parameters.GetValueOrDefault("maxResults");
            var memoryType = parameters.GetValueOrDefault("memoryType")?.ToString() ?? "all";

            if (string.IsNullOrWhiteSpace(query))
            {
                _logger.LogWarning("Search query is required but not provided");
                throw new ArgumentException("Search query is required");
            }

            // 解析最大结果数
            var maxResults = 10; // 默认值
            if (maxResultsObj != null)
            {
                if (!int.TryParse(maxResultsObj.ToString(), out maxResults) || maxResults <= 0)
                {
                    _logger.LogWarning("Invalid maxResults value: {MaxResults}, using default 10", maxResultsObj);
                    maxResults = 10;
                }
            }

            // 验证内存类型
            var validMemoryTypes = new[] { "all", "working", "conversation", "longterm" };
            if (!validMemoryTypes.Contains(memoryType.ToLower()))
            {
                _logger.LogWarning("Invalid memory type: {MemoryType}, defaulting to 'all'", memoryType);
                memoryType = "all";
            }

            // ============================================================
            //  Real memory search (best-effort)
            //
            //  Priority:
            //  1) State snapshot (ToolContext.GetStateCallback): current history window + rolling summary
            //  2) External memory store (ToolContext.Memory): long-term / RAG memory (if wired)
            //
            //  NOTE:
            //  - If Memory is not wired yet, the tool still provides value by searching State.History + summary.
            // ============================================================
            var results = await SearchAsync(query, memoryType, maxResults, context, cancellationToken);

            _logger.LogInformation("Memory search completed: {Query} found {Count} results in {MemoryType} memory",
                query, results.Count, memoryType);

            var resultObj = new
            {
                results,
                count = results.Count,
                query,
                memoryType
            };
            
            var json = JsonSerializer.Serialize(resultObj);
            return JsonParser.Default.Parse<Struct>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory search failed for query: {Query}", parameters.GetValueOrDefault("query"));
            throw;
        }
    }

    public override ToolParameterValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        var result = new ToolParameterValidationResult { IsValid = true };

        if (!parameters.ContainsKey("query") || string.IsNullOrWhiteSpace(parameters["query"]?.ToString()))
        {
            result.IsValid = false;
            result.Errors.Add("Required parameter 'query' is missing or empty");
        }

        return result;
    }

    private async Task<List<MemoryItem>> SearchAsync(
        string query,
        string memoryType,
        int maxResults,
        ToolContext context,
        CancellationToken cancellationToken)
    {
        var allResults = new List<MemoryItem>();

        var type = memoryType.ToLowerInvariant();
        var includeConversation = type is "all" or "conversation";
        var includeLongTerm = type is "all" or "longterm";
        var includeWorking = type is "all" or "working";

        // 1) Conversation/working memory from agent State snapshot
        if (includeConversation || includeWorking)
        {
            try
            {
                var state = context.GetStateCallback?.Invoke() as AevatarAIAgentState;
                if (state != null)
                {
                    // Rolling summary (Layer 2) lives in state.Context["history_summary"]
                    if (state.Context != null &&
                        state.Context.TryGetValue("history_summary", out var summary) &&
                        !string.IsNullOrWhiteSpace(summary) &&
                        summary.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        allResults.Add(new MemoryItem
                        {
                            Id = "history_summary",
                            Type = "conversation_summary",
                            Content = summary,
                            Timestamp = DateTime.UtcNow,
                            Metadata = new Dictionary<string, object>
                            {
                                ["source"] = "state.context.history_summary"
                            }
                        });
                    }

                    // Recent history window (Layer 1) from state.History
                    if (state.History != null && state.History.Count > 0)
                    {
                        foreach (var msg in state.History)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var content = msg.Content ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(content)) continue;
                            if (!content.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

                            allResults.Add(new MemoryItem
                            {
                                Id = string.IsNullOrWhiteSpace(msg.Id) ? Guid.NewGuid().ToString("N") : msg.Id,
                                Type = "conversation",
                                Content = $"{msg.Role}: {content}",
                                Timestamp = msg.Timestamp?.ToDateTime() ?? DateTime.UtcNow,
                                Metadata = new Dictionary<string, object>
                                {
                                    ["role"] = msg.Role.ToString(),
                                    ["source"] = "state.history"
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "State-based memory search failed (best-effort)");
            }
        }

        // 2) Long-term memory (optional) via IAevatarAIMemory
        if (includeLongTerm && context.Memory != null)
        {
            try
            {
                var hits = await context.Memory.SearchAsync(query, topK: maxResults, cancellationToken: cancellationToken);
                foreach (var hit in hits)
                {
                    if (string.IsNullOrWhiteSpace(hit)) continue;
                    allResults.Add(new MemoryItem
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Type = "longterm",
                        Content = hit,
                        Timestamp = DateTime.UtcNow,
                        Metadata = new Dictionary<string, object>
                        {
                            ["source"] = "IAevatarAIMemory.SearchAsync"
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Long-term memory search failed (best-effort)");
            }
        }

        // Deduplicate + cap results (stable order: keep earlier matches first)
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var final = new List<MemoryItem>(Math.Min(maxResults, allResults.Count));
        foreach (var item in allResults)
        {
            if (final.Count >= maxResults) break;
            var key = $"{item.Type}:{item.Content}";
            if (!seen.Add(key)) continue;
            final.Add(item);
        }

        return final;
    }
}

/// <summary>
/// 内存项
/// </summary>
public class MemoryItem
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}
