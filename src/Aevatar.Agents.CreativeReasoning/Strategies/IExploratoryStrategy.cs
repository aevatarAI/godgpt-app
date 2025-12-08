namespace Aevatar.Agents.CreativeReasoning.Strategies;

// ============================================================
//  Exploratory Strategy - E-UoT Step E: Exploratory Idea Expansion
//  Discovers novel "outside thoughts" beyond existing solutions
//  Key insight: Don't just recombine existing thoughts, actively
//  explore the universe for new conceptual primitives
// ============================================================

/// <summary>
/// Strategy for discovering outside thoughts through exploration.
/// E-UoT Step E: Exploratory Idea Expansion (after Step 2, before Step 3).
/// </summary>
public interface IExploratoryStrategy
{
    /// <summary>
    /// Identify promising exploration directions based on existing thoughts.
    /// These directions guide where to look for outside thoughts.
    /// </summary>
    string BuildExplorationDirectionsPrompt(
        string originalProblem,
        IReadOnlyList<ParsedThought> existingThoughts,
        int maxDirections);
    
    /// <summary>
    /// Build prompt to discover outside thoughts in a specific direction.
    /// Outside thoughts come from domains NOT yet explored.
    /// </summary>
    string BuildOutsideThoughtDiscoveryPrompt(
        string originalProblem,
        string explorationDirection,
        IReadOnlyList<ParsedThought> existingThoughts,
        int maxThoughts);
    
    /// <summary>
    /// Evaluate relevance and novelty of discovered outside thoughts.
    /// Filter to keep only thoughts that are both novel AND potentially useful.
    /// </summary>
    string BuildOutsideThoughtEvaluationPrompt(
        IReadOnlyList<ParsedOutsideThought> outsideThoughts,
        string originalProblem,
        IReadOnlyList<ParsedThought> existingThoughts);
    
    /// <summary>
    /// Parse LLM output to extract exploration directions.
    /// </summary>
    IReadOnlyList<string> ParseExplorationDirections(string llmOutput);
    
    /// <summary>
    /// Parse LLM output to extract outside thoughts.
    /// </summary>
    IReadOnlyList<ParsedOutsideThought> ParseOutsideThoughts(string llmOutput, string explorationDirection);
    
    /// <summary>
    /// Parse evaluation results to filter and score outside thoughts.
    /// </summary>
    IReadOnlyList<ParsedOutsideThought> ParseEvaluatedOutsideThoughts(string llmOutput);
}

/// <summary>
/// Outside thought discovered through exploration.
/// </summary>
public record ParsedOutsideThought(
    string Id,
    string Content,
    string ExplorationSource,
    string ExplorationMethod,
    float NoveltyScore,
    float RelevanceScore);

