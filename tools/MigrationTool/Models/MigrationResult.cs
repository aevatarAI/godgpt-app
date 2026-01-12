namespace MigrationTool.Models;

/// <summary>
/// Result of a migration operation
/// </summary>
public class MigrationResult
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public List<string> FailedIds { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
    
    public double SuccessRate => TotalCount > 0 
        ? (double)SuccessCount / TotalCount * 100 
        : 0;
}

/// <summary>
/// Result of a verification operation
/// </summary>
public class VerificationResult
{
    public int TotalVerified { get; set; }
    public int MatchCount { get; set; }
    public List<string> MismatchIds { get; set; } = new();
    public List<string> MissingInNew { get; set; } = new();
    public List<string> ErrorIds { get; set; } = new();
    
    public double MatchRate => TotalVerified > 0 
        ? (double)MatchCount / TotalVerified * 100 
        : 0;
}

/// <summary>
/// Old state wrapper for deserialization
/// </summary>
public class OldGrainStateDocument
{
    public string Id { get; set; } = string.Empty;
    public string GrainType { get; set; } = string.Empty;
    public object? State { get; set; }
}
