using System.Text.Json;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

// ============================================================
//  Default Exploratory Strategy
//  E-UoT Step E: Exploratory Idea Expansion
//  
//  Philosophy: True creativity requires venturing beyond
//  the known solution space. We don't just recombine existing
//  thoughts - we actively explore the universe for new ones.
// ============================================================

/// <summary>
/// Default implementation of exploratory strategy using structured JSON prompts.
/// </summary>
public class DefaultExploratoryStrategy : IExploratoryStrategy
{
    public string BuildExplorationDirectionsPrompt(
        string originalProblem, 
        IReadOnlyList<ParsedThought> existingThoughts, 
        int maxDirections)
    {
        var thoughtSummary = existingThoughts.Count > 0
            ? string.Join("\n", existingThoughts.Take(10).Select(t => $"- {t.Content}"))
            : "(No existing thoughts yet)";

        return $$"""
            You are a creative exploration guide. Your task is to identify promising 
            EXPLORATION DIRECTIONS for finding novel ideas beyond the existing solution space.
            
            ## Original Problem
            {{originalProblem}}
            
            ## Existing Thoughts (Sample)
            {{thoughtSummary}}
            
            ## Your Task
            Identify {{maxDirections}} unexplored directions that could yield novel insights.
            These should be domains or perspectives NOT represented in existing thoughts.
            
            Think like:
            - What adjacent fields might have relevant patterns?
            - What distant analogies haven't been considered?
            - What unconventional perspectives could illuminate this problem?
            - What emerging fields might offer fresh approaches?
            
            ## Output Format (JSON)
            ```json
            {
                "exploration_directions": [
                    {
                        "direction": "Brief description of exploration direction",
                        "rationale": "Why this direction might yield novel insights",
                        "potential_domains": ["domain1", "domain2"]
                    }
                ]
            }
            ```
            
            Generate exactly {{maxDirections}} diverse exploration directions.
            """;
    }

    public string BuildOutsideThoughtDiscoveryPrompt(
        string originalProblem, 
        string explorationDirection, 
        IReadOnlyList<ParsedThought> existingThoughts, 
        int maxThoughts)
    {
        return $$"""
            You are discovering OUTSIDE THOUGHTS - novel conceptual primitives from 
            unexplored domains that could contribute to creative solutions.
            
            ## Original Problem
            {{originalProblem}}
            
            ## Exploration Direction
            {{explorationDirection}}
            
            ## Your Task
            Discover {{maxThoughts}} outside thoughts from this exploration direction.
            These should be:
            1. NOVEL: Not variations of existing thoughts
            2. TRANSFERABLE: Could potentially apply to the original problem
            3. CONCRETE: Specific enough to be actionable
            4. SURPRISING: Not obvious connections
            
            ## Output Format (JSON)
            ```json
            {
                "outside_thoughts": [
                    {
                        "id": "ot_1",
                        "content": "The specific thought/concept/mechanism",
                        "source_domain": "Where this thought comes from",
                        "discovery_method": "How this relates to exploration direction",
                        "transfer_potential": "How this might apply to original problem"
                    }
                ]
            }
            ```
            
            Generate {{maxThoughts}} outside thoughts.
            """;
    }

    public string BuildOutsideThoughtEvaluationPrompt(
        IReadOnlyList<ParsedOutsideThought> outsideThoughts, 
        string originalProblem, 
        IReadOnlyList<ParsedThought> existingThoughts)
    {
        var thoughtList = string.Join("\n", outsideThoughts.Select((t, i) => 
            $"{i + 1}. [{t.Id}] {t.Content} (from: {t.ExplorationSource})"));

        return $$"""
            You are evaluating outside thoughts for their creative potential.
            
            ## Original Problem
            {{originalProblem}}
            
            ## Outside Thoughts to Evaluate
            {{thoughtList}}
            
            ## Evaluation Criteria
            For each thought, assess:
            1. NOVELTY [0-1]: How different from conventional approaches?
            2. RELEVANCE [0-1]: How applicable to the original problem?
            
            Only thoughts with BOTH novelty > 0.5 AND relevance > 0.4 should be kept.
            
            ## Output Format (JSON)
            ```json
            {
                "evaluated_thoughts": [
                    {
                        "id": "ot_1",
                        "content": "The thought content",
                        "novelty_score": 0.8,
                        "relevance_score": 0.6,
                        "keep": true,
                        "rationale": "Why this thought is valuable"
                    }
                ]
            }
            ```
            """;
    }

    public IReadOnlyList<string> ParseExplorationDirections(string llmOutput)
    {
        try
        {
            var json = ExtractJson(llmOutput);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("exploration_directions", out var directions))
            {
                return directions.EnumerateArray()
                    .Select(d => d.GetProperty("direction").GetString() ?? "")
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .ToList();
            }
        }
        catch
        {
            // Fallback: extract direction-like sentences
        }

        return ExtractFallbackDirections(llmOutput);
    }

    public IReadOnlyList<ParsedOutsideThought> ParseOutsideThoughts(string llmOutput, string explorationDirection)
    {
        var results = new List<ParsedOutsideThought>();
        
        try
        {
            var json = ExtractJson(llmOutput);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("outside_thoughts", out var thoughts))
            {
                var index = 0;
                foreach (var thought in thoughts.EnumerateArray())
                {
                    var id = thought.TryGetProperty("id", out var idProp) 
                        ? idProp.GetString() ?? $"ot_{index}" 
                        : $"ot_{index}";
                    var content = thought.TryGetProperty("content", out var contentProp) 
                        ? contentProp.GetString() ?? "" 
                        : "";
                    var source = thought.TryGetProperty("source_domain", out var sourceProp) 
                        ? sourceProp.GetString() ?? explorationDirection 
                        : explorationDirection;
                    var method = thought.TryGetProperty("discovery_method", out var methodProp) 
                        ? methodProp.GetString() ?? "exploration" 
                        : "exploration";

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        results.Add(new ParsedOutsideThought(
                            id,
                            content,
                            source,
                            method,
                            NoveltyScore: 0.7f, // Will be evaluated later
                            RelevanceScore: 0.5f));
                    }
                    index++;
                }
            }
        }
        catch
        {
            // Return empty on parse failure
        }

        return results;
    }

    public IReadOnlyList<ParsedOutsideThought> ParseEvaluatedOutsideThoughts(string llmOutput)
    {
        var results = new List<ParsedOutsideThought>();
        
        try
        {
            var json = ExtractJson(llmOutput);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("evaluated_thoughts", out var thoughts))
            {
                foreach (var thought in thoughts.EnumerateArray())
                {
                    var keep = thought.TryGetProperty("keep", out var keepProp) && keepProp.GetBoolean();
                    if (!keep) continue;

                    var id = thought.TryGetProperty("id", out var idProp) 
                        ? idProp.GetString() ?? "" 
                        : "";
                    var content = thought.TryGetProperty("content", out var contentProp) 
                        ? contentProp.GetString() ?? "" 
                        : "";
                    var novelty = thought.TryGetProperty("novelty_score", out var noveltyProp) 
                        ? (float)noveltyProp.GetDouble() 
                        : 0.5f;
                    var relevance = thought.TryGetProperty("relevance_score", out var relevanceProp) 
                        ? (float)relevanceProp.GetDouble() 
                        : 0.5f;

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        results.Add(new ParsedOutsideThought(
                            id,
                            content,
                            ExplorationSource: "evaluated",
                            ExplorationMethod: "evaluation",
                            novelty,
                            relevance));
                    }
                }
            }
        }
        catch
        {
            // Return empty on parse failure
        }

        return results;
    }

    #region Helpers

    private static string ExtractJson(string text)
    {
        // Find JSON block in markdown code fence
        var start = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start = text.IndexOf('\n', start) + 1;
            var end = text.IndexOf("```", start, StringComparison.Ordinal);
            if (end > start)
            {
                return text[start..end].Trim();
            }
        }

        // Try to find raw JSON object
        var braceStart = text.IndexOf('{');
        var braceEnd = text.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
        {
            return text[braceStart..(braceEnd + 1)];
        }

        return text;
    }

    private static List<string> ExtractFallbackDirections(string text)
    {
        // Simple fallback: extract numbered items or bullet points
        var lines = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 10)
            .Where(l => l.StartsWith('-') || l.StartsWith('*') || 
                       (l.Length > 2 && char.IsDigit(l[0]) && l[1] == '.'))
            .Select(l => l.TrimStart('-', '*', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ' '))
            .Where(l => l.Length > 5)
            .Take(5)
            .ToList();

        return lines.Count > 0 ? lines : ["General domain exploration"];
    }

    #endregion
}
