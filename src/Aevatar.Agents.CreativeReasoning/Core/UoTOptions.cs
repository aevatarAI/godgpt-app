namespace Aevatar.Agents.CreativeReasoning.Core;

// ============================================================
//  UoT Mode Selection
// ============================================================

/// <summary>
/// Universe of Thoughts reasoning modes.
/// Each mode represents a different level of creative exploration.
/// </summary>
public enum UoTMode
{
    /// <summary>
    /// Combinational UoT (C-UoT): Combines existing thoughts from analogous problems.
    /// Fastest, most practical, good for incremental innovation.
    /// </summary>
    Combinational,
    
    /// <summary>
    /// Exploratory UoT (E-UoT): Extends C-UoT with outside thought discovery.
    /// Medium complexity, explores beyond known solution space.
    /// </summary>
    Exploratory,
    
    /// <summary>
    /// Transformative UoT (T-UoT): Challenges rules and hidden assumptions.
    /// Most radical, highest creativity, may produce disruptive innovations.
    /// </summary>
    Transformative
}

// ============================================================
//  Shared Options
// ============================================================

/// <summary>
/// Configuration options for Universe of Thoughts (UoT) creative reasoning.
/// Works for C-UoT, E-UoT, and T-UoT with mode-specific fields.
/// </summary>
public class UoTOptions
{
    /// <summary>UoT reasoning mode (default: Combinational)</summary>
    public UoTMode Mode { get; set; } = UoTMode.Combinational;
    
    /// <summary>LLM provider name (required)</summary>
    public string ProviderName { get; set; } = string.Empty;
    
    /// <summary>Optional domain hint for better analogy/rule retrieval</summary>
    public string? DomainHint { get; set; }
    
    /// <summary>Progress callback</summary>
    public Action<UoTProgress>? OnProgress { get; set; }
    
    // ============================================================
    //  C-UoT Options (also used by E-UoT)
    // ============================================================
    
    /// <summary>Maximum analogous problems to retrieve (default: 5)</summary>
    public int MaxAnalogies { get; set; } = 5;
    
    /// <summary>Solutions to generate per analogous problem (default: 3)</summary>
    public int SolutionsPerAnalogy { get; set; } = 3;
    
    /// <summary>Maximum candidate solutions to synthesize (default: 10)</summary>
    public int MaxCandidates { get; set; } = 10;
    
    /// <summary>Minimum feasibility score to pass (hard constraint, default: 0.6)</summary>
    public float FeasibilityThreshold { get; set; } = 0.6f;
    
    /// <summary>Semantic similarity threshold for embedding comparisons (default: 0.7)</summary>
    public float SemanticSimilarityThreshold { get; set; } = 0.7f;
    
    /// <summary>Weight for utility in composite score (default: 0.5)</summary>
    public float UtilityWeight { get; set; } = 0.5f;
    
    /// <summary>Weight for novelty in composite score (default: 0.5)</summary>
    public float NoveltyWeight { get; set; } = 0.5f;
    
    /// <summary>Distance threshold for "far" donors in Far-then-Analogical selection (default: 0.6)</summary>
    public float FarDistanceThreshold { get; set; } = 0.6f;
    
    /// <summary>Number of far donors to consider (default: 3)</summary>
    public int FarDonorsCount { get; set; } = 3;
    
    // ============================================================
    //  E-UoT Specific Options
    // ============================================================
    
    /// <summary>Maximum outside thoughts to discover (E-UoT, default: 10)</summary>
    public int MaxOutsideThoughts { get; set; } = 10;
    
    /// <summary>Number of exploration directions to pursue (E-UoT, default: 3)</summary>
    public int ExplorationDirections { get; set; } = 3;
    
    /// <summary>Minimum relevance score for outside thoughts (E-UoT, default: 0.4)</summary>
    public float OutsideThoughtRelevance { get; set; } = 0.4f;
    
    // ============================================================
    //  T-UoT Specific Options
    // ============================================================
    
    /// <summary>Maximum mutated rule sets to explore (T-UoT, default: 3)</summary>
    public int MaxRuleSets { get; set; } = 3;
    
    /// <summary>Number of mutations per rule set (T-UoT, default: 3)</summary>
    public int MutationsPerSet { get; set; } = 3;
    
    /// <summary>Minimum radicality score for solutions (T-UoT, default: 0.5)</summary>
    public float MinRadicality { get; set; } = 0.5f;
    
    /// <summary>Allow violating physical rules (T-UoT, default: false)</summary>
    public bool AllowPhysicalRuleViolation { get; set; } = false;
}

// ============================================================
//  Progress Reporting
// ============================================================

/// <summary>
/// Progress information during UoT execution.
/// </summary>
public class UoTProgress
{
    public required UoTPhase Phase { get; init; }
    public required string Message { get; init; }
    public float ProgressPercent { get; init; }
    public int AnalogiesFound { get; init; }
    public int ThoughtsExtracted { get; init; }
    public int CandidatesGenerated { get; init; }
    public int CandidatesPassed { get; init; }
}

public enum UoTPhase
{
    Starting,
    RetrievingAnalogies,
    DecomposingThoughts,
    ExploringIdeas,      // E-UoT specific
    ExposingRules,       // T-UoT specific
    MutatingRules,       // T-UoT specific
    ExploringRuleSpaces, // T-UoT specific
    SelectingHost,
    SelectingDonors,
    Synthesizing,
    Evaluating,
    Completed,
    Failed
}

