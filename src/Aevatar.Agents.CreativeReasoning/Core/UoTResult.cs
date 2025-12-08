using Aevatar.Agents.CreativeReasoning.Messages;

namespace Aevatar.Agents.CreativeReasoning.Core;

// ============================================================
//  UoT Result Types
//  Shared by C-UoT and E-UoT (similar output structure)
//  T-UoT has its own result type due to different data model
// ============================================================

/// <summary>
/// Result of C-UoT or E-UoT creative reasoning execution.
/// </summary>
public class UoTResult
{
    /// <summary>Whether execution completed successfully</summary>
    public required bool Success { get; init; }
    
    /// <summary>The best creative solution found</summary>
    public CandidateSolution? BestSolution { get; init; }
    
    /// <summary>All candidate solutions, ranked by composite score</summary>
    public IReadOnlyList<CandidateSolution> AllCandidates { get; init; } = [];
    
    /// <summary>Error message if failed</summary>
    public string? Error { get; init; }
    
    /// <summary>Execution trace for analysis</summary>
    public required UoTResultTrace Trace { get; init; }
}

/// <summary>
/// Execution trace for C-UoT and E-UoT.
/// </summary>
public class UoTResultTrace
{
    public required string ExecutionId { get; init; }
    public required string OriginalProblem { get; init; }
    public int AnalogiesExplored { get; init; }
    public int ThoughtsExtracted { get; init; }
    public int CandidatesGenerated { get; init; }
    public int CandidatesPassedFeasibility { get; init; }
    public int TotalLLMCalls { get; init; }
    public long TotalTokens { get; init; }
    public TimeSpan Duration { get; init; }
    
    /// <summary>Analogies explored (for debugging)</summary>
    public IReadOnlyList<AnalogousProblem> Analogies { get; init; } = [];
}

// ============================================================
//  T-UoT Result Types
//  Transformative reasoning has fundamentally different output
// ============================================================

/// <summary>
/// Result of T-UoT transformative creative reasoning.
/// </summary>
public class TUoTResult
{
    /// <summary>Whether execution completed successfully</summary>
    public required bool Success { get; init; }
    
    /// <summary>The best transformative solution found</summary>
    public TransformativeSolution? BestSolution { get; init; }
    
    /// <summary>All transformative solutions, ranked by composite score</summary>
    public IReadOnlyList<TransformativeSolution> AllSolutions { get; init; } = [];
    
    /// <summary>Error message if failed</summary>
    public string? Error { get; init; }
    
    /// <summary>Execution trace for analysis</summary>
    public required TUoTResultTrace Trace { get; init; }
}

/// <summary>
/// Execution trace for T-UoT transformative reasoning.
/// </summary>
public class TUoTResultTrace
{
    public required string ExecutionId { get; init; }
    public required string OriginalProblem { get; init; }
    
    /// <summary>Number of explicit rules exposed</summary>
    public int RulesExposed { get; init; }
    
    /// <summary>Number of hidden assumptions discovered</summary>
    public int HiddenAssumptionsFound { get; init; }
    
    /// <summary>Number of mutated rule sets explored</summary>
    public int RuleSetsExplored { get; init; }
    
    /// <summary>Number of transformative solutions generated</summary>
    public int SolutionsGenerated { get; init; }
    
    public int TotalLLMCalls { get; init; }
    public long TotalTokens { get; init; }
    public TimeSpan Duration { get; init; }
    
    /// <summary>Explicit rules exposed</summary>
    public IReadOnlyList<Rule> ExposedRules { get; init; } = [];
    
    /// <summary>Hidden assumptions discovered</summary>
    public IReadOnlyList<Rule> HiddenAssumptions { get; init; } = [];
    
    /// <summary>Mutated rule sets explored</summary>
    public IReadOnlyList<MutatedRuleSet> MutatedRuleSets { get; init; } = [];
}

