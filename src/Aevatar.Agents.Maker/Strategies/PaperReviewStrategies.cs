using System.Text;
using System.Text.Json;

namespace Aevatar.Agents.Maker;

// ============================================================
//  Paper Review Strategies - Two-Phase Architecture
//  
//  Phase 0: Orchestrator analyzes paper → generates dimensions
//  Phase 1: Each dimension runs MAKER independently
//  Phase 2: Orchestrator composes all consensus results
// ============================================================

/// <summary>
/// Paper analysis result - dimensions to review.
/// </summary>
public sealed record PaperAnalysis
{
    /// <summary>论文核心主题</summary>
    public string CoreTopic { get; init; } = "";
    
    /// <summary>论文领域</summary>
    public string Domain { get; init; } = "";
    
    /// <summary>评审维度列表</summary>
    public IReadOnlyList<ReviewDimension> Dimensions { get; init; } = [];
    
    /// <summary>论文摘要（用于后续 prompt）</summary>
    public string AbstractSummary { get; init; } = "";
}

/// <summary>
/// Single review dimension with specialized prompt.
/// </summary>
public sealed record ReviewDimension
{
    /// <summary>维度ID</summary>
    public string Id { get; init; } = "";
    
    /// <summary>维度名称</summary>
    public string Name { get; init; } = "";
    
    /// <summary>维度描述</summary>
    public string Description { get; init; } = "";
    
    /// <summary>评审重点</summary>
    public string Focus { get; init; } = "";
    
    /// <summary>评分权重 (0-1)</summary>
    public double Weight { get; init; } = 1.0;
    
    /// <summary>生成的专用提示词</summary>
    public string GeneratedPrompt { get; init; } = "";
}

/// <summary>
/// Orchestrator-level strategy for paper analysis and dimension generation.
/// </summary>
public interface IPaperAnalysisStrategy
{
    /// <summary>
    /// Build prompt for paper analysis (Phase 0).
    /// </summary>
    string BuildAnalysisPrompt(string paperContent, string venueType, string reviewType);
    
    /// <summary>
    /// Parse analysis result into dimensions.
    /// </summary>
    PaperAnalysis ParseAnalysis(string llmOutput, string paperContent);
    
    /// <summary>
    /// Build dimension-specific review prompt.
    /// </summary>
    string BuildDimensionPrompt(ReviewDimension dimension, string paperContent, string venueType);
    
    /// <summary>
    /// Compose final review from all dimension results.
    /// </summary>
    string ComposeFinalReview(
        PaperAnalysis analysis,
        IReadOnlyDictionary<string, string> dimensionResults,
        string paperTitle,
        string venueType);
}

/// <summary>
/// Default paper analysis strategy with adaptive dimension generation.
/// </summary>
public sealed class DefaultPaperAnalysisStrategy : IPaperAnalysisStrategy
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <inheritdoc />
    public string BuildAnalysisPrompt(string paperContent, string venueType, string reviewType)
    {
        var reviewFocus = reviewType switch
        {
            "Quick" => "Focus on 2-3 major aspects only.",
            "Standard" => "Cover 4-5 key aspects.",
            "Detailed" => "Comprehensive coverage of 5-6 aspects.",
            "Rigorous" => "Thorough analysis of 6-7 aspects with deep scrutiny.",
            "Critical" => "Exhaustive analysis of all aspects with maximum rigor.",
            _ => "Cover key aspects appropriate for the venue."
        };

        return $$"""
            You are a Senior Editor at a top-tier {{venueType}}. Your task is to analyze this paper and design a structured review plan.
            
            {{reviewFocus}}
            
            ═══════════════════════════════════════════════════════════════
            ANALYSIS TASK
            ═══════════════════════════════════════════════════════════════
            
            Read the paper carefully and:
            1. Identify the paper's core contribution and domain
            2. Design review dimensions tailored to THIS SPECIFIC paper
            3. For each dimension, specify what aspects to focus on
            
            ═══════════════════════════════════════════════════════════════
            OUTPUT FORMAT (JSON)
            ═══════════════════════════════════════════════════════════════
            
            ```json
            {
              "core_topic": "Brief description of main contribution",
              "domain": "e.g., Machine Learning, Systems, Theory",
              "abstract_summary": "2-3 sentence summary for context",
              "dimensions": [
                {
                  "id": "D1",
                  "name": "Technical Soundness",
                  "description": "What this dimension evaluates",
                  "focus": "Specific aspects to check for THIS paper",
                  "weight": 0.25
                },
                ...
              ]
            }
            ```
            
            ═══════════════════════════════════════════════════════════════
            DIMENSION DESIGN GUIDELINES
            ═══════════════════════════════════════════════════════════════
            
            Common dimensions (adapt based on paper type):
            - Technical Soundness: Mathematical rigor, algorithm correctness, proof validity
            - Novelty & Contribution: Originality, significance of contribution
            - Experimental Evaluation: Methodology, baselines, statistical validity
            - Clarity & Presentation: Writing quality, organization, figures
            - Related Work: Coverage, positioning, fair comparison
            - Reproducibility: Code availability, implementation details
            - Broader Impact: Ethical considerations, societal implications
            
            For theoretical papers: emphasize proofs, assumptions, tightness of bounds
            For empirical papers: emphasize experiments, ablations, real-world applicability
            For systems papers: emphasize design choices, scalability, practicality
            
            Weights should sum to 1.0.
            
            ═══════════════════════════════════════════════════════════════
            PAPER TO ANALYZE
            ═══════════════════════════════════════════════════════════════
            
            {{paperContent}}
            
            ═══════════════════════════════════════════════════════════════
            END OF PAPER
            ═══════════════════════════════════════════════════════════════
            
            Output ONLY the JSON, no other text.
            """;
    }

    /// <inheritdoc />
    public PaperAnalysis ParseAnalysis(string llmOutput, string paperContent)
    {
        var json = ExtractJson(llmOutput);
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateFallbackAnalysis();
        }

        try
        {
            using var doc = JsonDocument.Parse(json, JsonOptions);
            var root = doc.RootElement;

            var dimensions = new List<ReviewDimension>();
            if (root.TryGetProperty("dimensions", out var dims) && dims.ValueKind == JsonValueKind.Array)
            {
                var index = 1;
                foreach (var dim in dims.EnumerateArray())
                {
                    dimensions.Add(new ReviewDimension
                    {
                        Id = GetString(dim, "id") ?? $"D{index}",
                        Name = GetString(dim, "name") ?? $"Dimension {index}",
                        Description = GetString(dim, "description") ?? "",
                        Focus = GetString(dim, "focus") ?? "",
                        Weight = GetDouble(dim, "weight", 1.0 / Math.Max(1, dims.GetArrayLength()))
                    });
                    index++;
                }
            }

            if (dimensions.Count == 0)
            {
                return CreateFallbackAnalysis();
            }

            return new PaperAnalysis
            {
                CoreTopic = GetString(root, "core_topic") ?? "Research contribution",
                Domain = GetString(root, "domain") ?? "Computer Science",
                AbstractSummary = GetString(root, "abstract_summary") ?? "",
                Dimensions = dimensions
            };
        }
        catch (JsonException)
        {
            return CreateFallbackAnalysis();
        }
    }

    /// <inheritdoc />
    public string BuildDimensionPrompt(ReviewDimension dimension, string paperContent, string venueType)
    {
        return $$"""
            You are a specialist reviewer for {{venueType}}, focusing exclusively on: **{{dimension.Name}}**
            
            ═══════════════════════════════════════════════════════════════
            YOUR TASK: {{dimension.Name}}
            ═══════════════════════════════════════════════════════════════
            
            {{dimension.Description}}
            
            FOCUS SPECIFICALLY ON:
            {{dimension.Focus}}
            
            ═══════════════════════════════════════════════════════════════
            OUTPUT FORMAT
            ═══════════════════════════════════════════════════════════════
            
            ## {{dimension.Name}}
            
            ### Assessment
            [Provide detailed evaluation of this dimension - 200-400 words]
            - Be specific: cite sections, equations, figures by number
            - For each point, explain WHY it matters
            - Distinguish major issues from minor ones
            
            ### Strengths (for this dimension)
            - [Strength 1 with specific evidence]
            - [Strength 2 with specific evidence]
            - ...
            
            ### Weaknesses (for this dimension)
            - [Weakness 1 with specific evidence and suggestion]
            - [Weakness 2 with specific evidence and suggestion]
            - ...
            
            ### Questions
            - [Clarifying question 1]
            - [Clarifying question 2]
            
            ### Dimension Score: [1-5] (1=Major issues, 5=Excellent)
            [One sentence justification]
            
            ═══════════════════════════════════════════════════════════════
            PAPER CONTENT
            ═══════════════════════════════════════════════════════════════
            
            {{paperContent}}
            
            ═══════════════════════════════════════════════════════════════
            END OF PAPER
            ═══════════════════════════════════════════════════════════════
            
            Remember: Focus ONLY on {{dimension.Name}}. Other dimensions are handled separately.
            """;
    }

    /// <inheritdoc />
    public string ComposeFinalReview(
        PaperAnalysis analysis,
        IReadOnlyDictionary<string, string> dimensionResults,
        string paperTitle,
        string venueType)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"# Paper Review: {paperTitle}");
        sb.AppendLine();
        sb.AppendLine($"**Venue**: {venueType}");
        sb.AppendLine($"**Domain**: {analysis.Domain}");
        sb.AppendLine();
        
        // Summary section
        sb.AppendLine("## Executive Summary");
        sb.AppendLine();
        sb.AppendLine($"**Core Contribution**: {analysis.CoreTopic}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(analysis.AbstractSummary))
        {
            sb.AppendLine(analysis.AbstractSummary);
            sb.AppendLine();
        }
        
        sb.AppendLine("---");
        sb.AppendLine();
        
        // Dimension results
        foreach (var dim in analysis.Dimensions)
        {
            if (dimensionResults.TryGetValue(dim.Id, out var result))
            {
                sb.AppendLine(result.Trim());
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }
        }
        
        // Overall recommendation
        sb.AppendLine("## Overall Recommendation");
        sb.AppendLine();
        sb.AppendLine("_Based on the multi-dimensional analysis above, the final recommendation should consider:_");
        sb.AppendLine();
        
        foreach (var dim in analysis.Dimensions)
        {
            sb.AppendLine($"- **{dim.Name}** (weight: {dim.Weight:P0})");
        }
        
        sb.AppendLine();
        sb.AppendLine("_Each dimension was reviewed independently with consensus voting to ensure reliability._");
        
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────

    private static PaperAnalysis CreateFallbackAnalysis()
    {
        return new PaperAnalysis
        {
            CoreTopic = "Research contribution",
            Domain = "Computer Science",
            AbstractSummary = "",
            Dimensions =
            [
                new ReviewDimension
                {
                    Id = "D1",
                    Name = "Technical Soundness",
                    Description = "Correctness of methods, proofs, and algorithms",
                    Focus = "Mathematical rigor, logical consistency, correctness of claims",
                    Weight = 0.30
                },
                new ReviewDimension
                {
                    Id = "D2",
                    Name = "Novelty & Significance",
                    Description = "Originality and importance of contribution",
                    Focus = "What is new, why it matters, comparison with prior art",
                    Weight = 0.25
                },
                new ReviewDimension
                {
                    Id = "D3",
                    Name = "Experimental Evaluation",
                    Description = "Quality of experiments and validation",
                    Focus = "Baselines, metrics, statistical significance, reproducibility",
                    Weight = 0.25
                },
                new ReviewDimension
                {
                    Id = "D4",
                    Name = "Presentation Quality",
                    Description = "Clarity, organization, and writing",
                    Focus = "Readability, logical flow, figure quality, grammar",
                    Weight = 0.20
                }
            ]
        };
    }

    private static string ExtractJson(string content)
    {
        // Try to find JSON in code block first
        var codeBlockStart = content.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (codeBlockStart >= 0)
        {
            var jsonStart = content.IndexOf('{', codeBlockStart);
            var codeBlockEnd = content.IndexOf("```", codeBlockStart + 7);
            if (jsonStart >= 0 && codeBlockEnd > jsonStart)
            {
                return content[jsonStart..codeBlockEnd].Trim();
            }
        }

        // Try raw JSON
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return content[start..(end + 1)];
        }

        return content.Trim();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static double GetDouble(JsonElement element, string propertyName, double defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            return prop.ValueKind switch
            {
                JsonValueKind.Number => prop.GetDouble(),
                JsonValueKind.String when double.TryParse(prop.GetString(), out var d) => d,
                _ => defaultValue
            };
        }
        return defaultValue;
    }
}
