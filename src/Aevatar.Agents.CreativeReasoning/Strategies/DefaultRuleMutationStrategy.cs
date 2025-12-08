using System.Text.Json;
using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

// ============================================================
//  Default Rule Mutation Strategy
//  T-UoT: Transformative Creative Reasoning
//  
//  Philosophy: The deepest creativity comes from questioning
//  the rules themselves. Hidden assumptions are invisible cages.
//  Breaking them requires first making them visible.
// ============================================================

/// <summary>
/// Default implementation of rule mutation strategy using structured JSON prompts.
/// </summary>
public class DefaultRuleMutationStrategy : IRuleMutationStrategy
{
    #region Step 1: Expose Rules

    public string BuildExplicitRuleExtractionPrompt(string problem, string? domainHint)
    {
        var domainContext = !string.IsNullOrEmpty(domainHint) 
            ? $"\nDomain Context: {domainHint}" 
            : "";

        return $$"""
            You are a constraint analyst. Your task is to expose all EXPLICIT RULES 
            and constraints that govern the problem space.
            
            ## Problem
            {{problem}}
            {{domainContext}}
            
            ## Your Task
            Identify all stated and obvious constraints, including:
            1. PHYSICAL: Natural/physical limitations
            2. TECHNICAL: Technology constraints
            3. ECONOMIC: Budget, cost, resource limits
            4. SOCIAL: Social norms, user expectations
            5. REGULATORY: Laws, regulations, policies
            6. CONVENTION: Industry standards, common practices
            
            ## Output Format (JSON)
            ```json
            {
                "explicit_rules": [
                    {
                        "id": "r_1",
                        "content": "The specific rule or constraint",
                        "type": "PHYSICAL|TECHNICAL|ECONOMIC|SOCIAL|REGULATORY|CONVENTION",
                        "constraint_strength": 0.9,
                        "domain": "The domain this rule belongs to",
                        "rationale": "Why this constraint exists"
                    }
                ]
            }
            ```
            
            Be thorough - identify 5-10 explicit rules.
            """;
    }

    public string BuildHiddenAssumptionExtractionPrompt(
        string problem, 
        IReadOnlyList<ParsedRule> explicitRules, 
        string? domainHint)
    {
        var explicitSummary = string.Join("\n", explicitRules.Take(10)
            .Select(r => $"- [{r.Type}] {r.Content}"));

        return $$"""
            You are an assumption detective. Your task is to expose HIDDEN ASSUMPTIONS
            that are NOT stated but implicitly constrain the solution space.
            
            ## Problem
            {{problem}}
            
            ## Explicit Rules (Already Identified)
            {{explicitSummary}}
            
            ## Your Task
            Identify HIDDEN assumptions - things everyone takes for granted but
            that could be challenged. These are the invisible cages of creativity.
            
            Look for assumptions about:
            1. WHO: Who is the user? Who makes decisions?
            2. HOW: How must this be done? What's the "normal" approach?
            3. WHEN: Timing assumptions, sequence assumptions
            4. WHERE: Location, context, environment assumptions
            5. WHY: Purpose assumptions, goal assumptions
            6. WHAT: Scope assumptions, definition assumptions
            
            CRITICAL: The most limiting assumptions are often so obvious
            that no one thinks to question them.
            
            ## Output Format (JSON)
            ```json
            {
                "hidden_assumptions": [
                    {
                        "id": "ha_1",
                        "content": "The hidden assumption",
                        "type": "WHO|HOW|WHEN|WHERE|WHY|WHAT",
                        "constraint_strength": 0.7,
                        "why_hidden": "Why this assumption isn't questioned",
                        "challenge_potential": "What happens if we challenge this"
                    }
                ]
            }
            ```
            
            Find at least 5 hidden assumptions. The less obvious, the better.
            """;
    }

    public IReadOnlyList<ParsedRule> ParseExplicitRules(string llmOutput)
    {
        return ParseRules(llmOutput, "explicit_rules", isExplicit: true);
    }

    public IReadOnlyList<ParsedRule> ParseHiddenAssumptions(string llmOutput)
    {
        return ParseRules(llmOutput, "hidden_assumptions", isExplicit: false);
    }

    #endregion

    #region Step 2: Rule Mutation

    public string BuildRuleMutationPrompt(
        IReadOnlyList<ParsedRule> allRules, 
        string problem, 
        int maxRuleSets, 
        int mutationsPerSet)
    {
        var rulesSummary = string.Join("\n", allRules.Select((r, i) => 
            $"{i + 1}. [{r.Id}] {(r.IsExplicit ? "[EXPLICIT]" : "[HIDDEN]")} {r.Content}"));

        return $$"""
            You are a creative rule-breaker. Your task is to generate MUTATED RULE SETS
            that open up new solution spaces.
            
            ## Original Problem
            {{problem}}
            
            ## Current Rules (Including Hidden Assumptions)
            {{rulesSummary}}
            
            ## Your Task
            Create {{maxRuleSets}} different mutated rule sets, each with {{mutationsPerSet}} mutations.
            
            Mutation types:
            - NEGATE: Reverse the rule entirely
            - WEAKEN: Make the constraint less strict
            - GENERALIZE: Broaden the rule's scope
            - SPECIALIZE: Narrow to specific cases
            - REMOVE: Eliminate the rule
            - COMBINE: Merge rules into new form
            
            IMPORTANT:
            - Prioritize mutating HIDDEN ASSUMPTIONS (they have highest creative leverage)
            - Don't violate physical laws (unless problem allows)
            - Each rule set should be internally coherent
            - Rate radicality: how transformative is this change?
            
            ## Output Format (JSON)
            ```json
            {
                "mutated_rule_sets": [
                    {
                        "id": "mrs_1",
                        "mutations": [
                            {
                                "rule_id": "r_1 or ha_1",
                                "mutation_type": "NEGATE|WEAKEN|GENERALIZE|SPECIALIZE|REMOVE|COMBINE",
                                "original_content": "Original rule",
                                "mutated_content": "New mutated rule",
                                "rationale": "Why this mutation"
                            }
                        ],
                        "mutation_rationale": "Overall theme of this rule set",
                        "radicality_score": 0.8,
                        "new_possibilities": "What this opens up"
                    }
                ]
            }
            ```
            
            Generate {{maxRuleSets}} diverse mutated rule sets.
            """;
    }

    public IReadOnlyList<ParsedMutatedRuleSet> ParseMutatedRuleSets(string llmOutput)
    {
        var results = new List<ParsedMutatedRuleSet>();
        
        try
        {
            var json = ExtractJson(llmOutput);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("mutated_rule_sets", out var ruleSets))
            {
                foreach (var ruleSet in ruleSets.EnumerateArray())
                {
                    var id = ruleSet.TryGetProperty("id", out var idProp) 
                        ? idProp.GetString() ?? Guid.NewGuid().ToString("N")[..8] 
                        : Guid.NewGuid().ToString("N")[..8];

                    var mutations = new List<ParsedRuleMutation>();
                    if (ruleSet.TryGetProperty("mutations", out var mutationsArray))
                    {
                        foreach (var mutation in mutationsArray.EnumerateArray())
                        {
                            var ruleId = mutation.TryGetProperty("rule_id", out var ruleIdProp) 
                                ? ruleIdProp.GetString() ?? "" 
                                : "";
                            var mutationType = ParseMutationType(
                                mutation.TryGetProperty("mutation_type", out var mutTypeProp) 
                                    ? mutTypeProp.GetString() ?? "" 
                                    : "");
                            var original = mutation.TryGetProperty("original_content", out var origProp) 
                                ? origProp.GetString() ?? "" 
                                : "";
                            var mutated = mutation.TryGetProperty("mutated_content", out var mutatedProp) 
                                ? mutatedProp.GetString() ?? "" 
                                : "";
                            var rationale = mutation.TryGetProperty("rationale", out var ratProp) 
                                ? ratProp.GetString() ?? "" 
                                : "";

                            mutations.Add(new ParsedRuleMutation(
                                ruleId, mutationType, original, mutated, rationale));
                        }
                    }

                    var setRationale = ruleSet.TryGetProperty("mutation_rationale", out var setRatProp) 
                        ? setRatProp.GetString() ?? "" 
                        : "";
                    var radicality = ruleSet.TryGetProperty("radicality_score", out var radProp) 
                        ? (float)radProp.GetDouble() 
                        : 0.5f;

                    results.Add(new ParsedMutatedRuleSet(
                        id,
                        OriginalRules: [], // Will be populated later
                        mutations,
                        ResultingRules: [], // Will be derived
                        setRationale,
                        radicality));
                }
            }
        }
        catch
        {
            // Return empty on parse failure
        }

        return results;
    }

    #endregion

    #region Step 3: Explore New Rule Spaces

    public string BuildRuleSpaceExplorationPrompt(string problem, ParsedMutatedRuleSet ruleSet)
    {
        var mutationsSummary = string.Join("\n", ruleSet.Mutations.Select(m => 
            $"- {m.MutationType}: '{m.OriginalContent}' → '{m.MutatedContent}'"));

        return $$"""
            You are exploring a NEW SOLUTION SPACE created by rule mutations.
            Your task is to discover solutions that were IMPOSSIBLE before.
            
            ## Original Problem
            {{problem}}
            
            ## Rule Mutations Applied
            {{mutationsSummary}}
            
            ## Mutation Theme
            {{ruleSet.MutationRationale}}
            
            ## Your Task
            Generate 2-3 TRANSFORMATIVE solutions that leverage these rule changes.
            These solutions should:
            1. Be IMPOSSIBLE under original rules
            2. Take full advantage of the new freedoms
            3. Be creative and surprising
            4. Still solve the core problem
            
            ## Output Format (JSON)
            ```json
            {
                "transformative_solutions": [
                    {
                        "id": "ts_1",
                        "content": "Detailed solution description",
                        "how_rules_enable": "How the mutated rules make this possible",
                        "violated_conventions": ["Convention 1", "Convention 2"],
                        "innovation_type": "What kind of innovation this represents"
                    }
                ]
            }
            ```
            
            Be bold! The rules have changed - think accordingly.
            """;
    }

    public IReadOnlyList<ParsedTransformativeSolution> ParseTransformativeSolutions(
        string llmOutput, 
        string ruleSetId)
    {
        var results = new List<ParsedTransformativeSolution>();
        
        try
        {
            var json = ExtractJson(llmOutput);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("transformative_solutions", out var solutions))
            {
                var index = 0;
                foreach (var solution in solutions.EnumerateArray())
                {
                    var id = solution.TryGetProperty("id", out var idProp) 
                        ? idProp.GetString() ?? $"ts_{index}" 
                        : $"ts_{index}";
                    var content = solution.TryGetProperty("content", out var contentProp) 
                        ? contentProp.GetString() ?? "" 
                        : "";

                    var violatedConventions = new List<string>();
                    if (solution.TryGetProperty("violated_conventions", out var vcArray))
                    {
                        foreach (var vc in vcArray.EnumerateArray())
                        {
                            var vcStr = vc.GetString();
                            if (!string.IsNullOrWhiteSpace(vcStr))
                                violatedConventions.Add(vcStr);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        results.Add(new ParsedTransformativeSolution(
                            id, content, ruleSetId, violatedConventions));
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

    #endregion

    #region Evaluation

    public string BuildTransformativeEvaluationPrompt(
        ParsedTransformativeSolution solution, 
        string originalProblem, 
        IReadOnlyList<string> existingSolutions)
    {
        var existingSummary = existingSolutions.Count > 0
            ? string.Join("\n", existingSolutions.Take(5).Select((s, i) => $"{i + 1}. {s[..Math.Min(200, s.Length)]}..."))
            : "(No existing solutions for comparison)";

        var violatedStr = string.Join(", ", solution.ViolatedConventions);

        return $$"""
            You are evaluating a TRANSFORMATIVE solution that challenges conventions.
            
            ## Original Problem
            {{originalProblem}}
            
            ## Transformative Solution
            {{solution.Content}}
            
            ## Conventions Violated
            {{violatedStr}}
            
            ## Existing Solutions (for comparison)
            {{existingSummary}}
            
            ## Evaluation Criteria
            Rate each dimension [0-1]:
            
            1. FEASIBILITY: Can this actually be implemented?
               - Even if rules changed, is it physically possible?
               - Are required resources obtainable?
               
            2. UTILITY: How well does this solve the core problem?
               - Does it address the fundamental need?
               - What value does it create?
               
            3. NOVELTY: How different from existing solutions?
               - Is this genuinely new?
               - Does it open new possibilities?
               
            4. RADICALITY: How transformative is this?
               - How fundamentally does it change the approach?
               - How many conventions does it challenge?
            
            ## Output Format (JSON)
            ```json
            {
                "evaluation": {
                    "feasibility": 0.7,
                    "utility": 0.8,
                    "novelty": 0.9,
                    "radicality": 0.85,
                    "rationale": "Detailed evaluation rationale",
                    "key_strengths": ["strength1", "strength2"],
                    "key_risks": ["risk1", "risk2"]
                }
            }
            ```
            """;
    }

    public ParsedTransformativeEvaluation ParseTransformativeEvaluation(string llmOutput)
    {
        try
        {
            var json = ExtractJson(llmOutput);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("evaluation", out var eval))
            {
                return new ParsedTransformativeEvaluation(
                    Feasibility: eval.TryGetProperty("feasibility", out var fProp) 
                        ? (float)fProp.GetDouble() : 0.5f,
                    Utility: eval.TryGetProperty("utility", out var uProp) 
                        ? (float)uProp.GetDouble() : 0.5f,
                    Novelty: eval.TryGetProperty("novelty", out var nProp) 
                        ? (float)nProp.GetDouble() : 0.5f,
                    Radicality: eval.TryGetProperty("radicality", out var rProp) 
                        ? (float)rProp.GetDouble() : 0.5f,
                    Rationale: eval.TryGetProperty("rationale", out var ratProp) 
                        ? ratProp.GetString() ?? "" : "");
            }
        }
        catch
        {
            // Return default on parse failure
        }

        return new ParsedTransformativeEvaluation(0.5f, 0.5f, 0.5f, 0.5f, "Parse failed");
    }

    #endregion

    #region Helpers

    private IReadOnlyList<ParsedRule> ParseRules(string llmOutput, string propertyName, bool isExplicit)
    {
        var results = new List<ParsedRule>();
        
        try
        {
            var json = ExtractJson(llmOutput);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty(propertyName, out var rules))
            {
                foreach (var rule in rules.EnumerateArray())
                {
                    var id = rule.TryGetProperty("id", out var idProp) 
                        ? idProp.GetString() ?? Guid.NewGuid().ToString("N")[..8] 
                        : Guid.NewGuid().ToString("N")[..8];
                    var content = rule.TryGetProperty("content", out var contentProp) 
                        ? contentProp.GetString() ?? "" 
                        : "";
                    var typeStr = rule.TryGetProperty("type", out var typeProp) 
                        ? typeProp.GetString() ?? "" 
                        : "";
                    var strength = rule.TryGetProperty("constraint_strength", out var strengthProp) 
                        ? (float)strengthProp.GetDouble() 
                        : 0.5f;
                    var domain = rule.TryGetProperty("domain", out var domainProp) 
                        ? domainProp.GetString() ?? "" 
                        : "";

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        results.Add(new ParsedRule(
                            id, content, ParseRuleType(typeStr), isExplicit, strength, domain));
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

    private static RuleType ParseRuleType(string typeStr)
    {
        return typeStr.ToUpperInvariant() switch
        {
            "PHYSICAL" => RuleType.Physical,
            "TECHNICAL" => RuleType.Technical,
            "ECONOMIC" => RuleType.Economic,
            "SOCIAL" => RuleType.Social,
            "REGULATORY" or "CONVENTION" => RuleType.Convention,
            "ASSUMPTION" or "WHO" or "HOW" or "WHEN" or "WHERE" or "WHY" or "WHAT" => RuleType.Assumption,
            _ => RuleType.Unknown
        };
    }

    private static MutationType ParseMutationType(string typeStr)
    {
        return typeStr.ToUpperInvariant() switch
        {
            "NEGATE" => MutationType.Negate,
            "WEAKEN" => MutationType.Weaken,
            "STRENGTHEN" => MutationType.Strengthen,
            "GENERALIZE" => MutationType.Generalize,
            "SPECIALIZE" => MutationType.Specialize,
            "COMBINE" => MutationType.Combine,
            "REMOVE" => MutationType.Remove,
            _ => MutationType.Unknown
        };
    }

    private static string ExtractJson(string text)
    {
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

        var braceStart = text.IndexOf('{');
        var braceEnd = text.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
        {
            return text[braceStart..(braceEnd + 1)];
        }

        return text;
    }

    #endregion
}
