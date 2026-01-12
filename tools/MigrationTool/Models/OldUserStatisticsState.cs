namespace MigrationTool.Models;

/// <summary>
/// Old UserStatistics state model (copied from old architecture)
/// Used for deserialization from Orleans Grain Storage
/// </summary>
public class OldUserStatisticsState
{
    public Guid UserId { get; set; }
    public bool IsInitialized { get; set; } = false;
    public Dictionary<string, OldAppRatingInfo> AppRatings { get; set; } = new();
    public bool IsRealUser { get; set; } = true;
}

/// <summary>
/// Old AppRatingInfo model
/// </summary>
public class OldAppRatingInfo
{
    public string Platform { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public DateTime FirstRatingTime { get; set; }
    public DateTime LastRatingTime { get; set; }
    public int RatingCount { get; set; }
}
