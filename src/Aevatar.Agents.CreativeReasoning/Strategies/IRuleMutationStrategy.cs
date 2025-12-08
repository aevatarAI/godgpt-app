using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Strategies;

// ============================================================
//  Rule Mutation Strategy - T-UoT Core Mechanism
//  The most profound creative process: changing the rules
//  
//  Three steps:
//  1. Expose Rules (including hidden assumptions)
//  2. Mutate Rules to create new rule sets
//  3. Explore each new rule space for solutions
// ============================================================

/// <summary>
/// Strategy for transformative creativity through rule mutation.
/// T-UoT: The most radical form of creative reasoning.
/// </summary>
public interface IRuleMutationStrategy
{
    #region Step 1: Expose Rules
    
    /// <summary>
    /// Build prompt to expose explicit rules/constraints.
    /// These are the stated requirements and limitations.
    /// </summary>
    string BuildExplicitRuleExtractionPrompt(
        string problem,
        string? domainHint);
    
    /// <summary>
    /// Build prompt to expose hidden assumptions.
    /// CRITICAL: These unstated assumptions often limit creativity most.
    /// Examples: "Solutions must use existing technology",
    ///           "Users will behave rationally",
    ///           "Cost must stay within current budget norms"
    /// </summary>
    string BuildHiddenAssumptionExtractionPrompt(
        string problem,
        IReadOnlyList<ParsedRule> explicitRules,
        string? domainHint);
    
    /// <summary>
    /// Parse explicit rules from LLM output.
    /// </summary>
    IReadOnlyList<ParsedRule> ParseExplicitRules(string llmOutput);
    
    /// <summary>
    /// Parse hidden assumptions from LLM output.
    /// </summary>
    IReadOnlyList<ParsedRule> ParseHiddenAssumptions(string llmOutput);
    
    #endregion
    
    #region Step 2: Rule Mutation
    
    /// <summary>
    /// Build prompt to generate mutated rule sets.
    /// Each mutation should create a coherent alternative rule space.
    /// </summary>
    string BuildRuleMutationPrompt(
        IReadOnlyList<ParsedRule> allRules,
        string problem,
        int maxRuleSets,
        int mutationsPerSet);
    
    /// <summary>
    /// Parse mutated rule sets from LLM output.
    /// </summary>
    IReadOnlyList<ParsedMutatedRuleSet> ParseMutatedRuleSets(string llmOutput);
    
    #endregion
    
    #region Step 3: Explore New Rule Spaces
    
    /// <summary>
    /// Build prompt to explore solutions within a specific mutated rule space.
    /// Solutions should leverage the rule mutations for radical innovation.
    /// </summary>
    string BuildRuleSpaceExplorationPrompt(
        string problem,
        ParsedMutatedRuleSet ruleSet);
    
    /// <summary>
    /// Parse solutions discovered in the new rule space.
    /// </summary>
    IReadOnlyList<ParsedTransformativeSolution> ParseTransformativeSolutions(
        string llmOutput, 
        string ruleSetId);
    
    #endregion
    
    #region Evaluation
    
    /// <summary>
    /// Build prompt to evaluate a transformative solution.
    /// Must assess: feasibility (given rule changes), utility, novelty,
    /// and radicality (how transformative is it?).
    /// </summary>
    string BuildTransformativeEvaluationPrompt(
        ParsedTransformativeSolution solution,
        string originalProblem,
        IReadOnlyList<string> existingSolutions);
    
    /// <summary>
    /// Parse transformative evaluation result.
    /// </summary>
    ParsedTransformativeEvaluation ParseTransformativeEvaluation(string llmOutput);
    
    #endregion
}

/// <summary>
/// Parsed rule extracted from problem space.
/// </summary>
public record ParsedRule(
    string Id,
    string Content,
    RuleType Type,
    bool IsExplicit,
    float ConstraintStrength,
    string Domain);

/// <summary>
/// Parsed rule mutation.
/// </summary>
public record ParsedRuleMutation(
    string RuleId,
    MutationType MutationType,
    string OriginalContent,
    string MutatedContent,
    string Rationale);

/// <summary>
/// Parsed mutated rule set.
/// </summary>
public record ParsedMutatedRuleSet(
    string Id,
    IReadOnlyList<ParsedRule> OriginalRules,
    IReadOnlyList<ParsedRuleMutation> Mutations,
    IReadOnlyList<ParsedRule> ResultingRules,
    string MutationRationale,
    float RadicalityScore);

/// <summary>
/// Parsed solution in new rule space.
/// </summary>
public record ParsedTransformativeSolution(
    string Id,
    string Content,
    string RuleSetId,
    IReadOnlyList<string> ViolatedConventions);

/// <summary>
/// Parsed evaluation of transformative solution.
/// </summary>
public record ParsedTransformativeEvaluation(
    float Feasibility,
    float Utility,
    float Novelty,
    float Radicality,
    string Rationale);

