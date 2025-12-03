// Stub interfaces for excluded services (GoogleAuth, Twitter)
// These services are not migrated but are referenced by other agents
// TODO: Remove these stubs when the dependent code is refactored

using Orleans;

namespace Aevatar.Application.Grains.GoogleAuth
{
    /// <summary>
    /// Stub interface for GoogleAuth GAgent - not migrated
    /// </summary>
    public interface IGoogleAuthGAgent : IGrainWithGuidKey
    {
        Task<Dtos.GoogleAuthStatusDto?> GetAuthStatusAsync();
    }
}

namespace Aevatar.Application.Grains.GoogleAuth.Dtos
{
    /// <summary>
    /// Stub DTO for GoogleAuth status
    /// </summary>
    [GenerateSerializer]
    public class GoogleAuthStatusDto
    {
        [Id(0)] public bool IsAuthenticated { get; set; }
        [Id(1)] public string? Email { get; set; }
    }
}

namespace Aevatar.Application.Grains.Twitter
{
    /// <summary>
    /// Stub interface for Twitter Auth GAgent - not migrated
    /// </summary>
    public interface ITwitterAuthGAgent : IGrainWithGuidKey
    {
        Task<TwitterBindStatusDto?> GetBindStatusAsync();
    }

    /// <summary>
    /// Stub DTO for Twitter bind status
    /// </summary>
    [GenerateSerializer]
    public class TwitterBindStatusDto
    {
        [Id(0)] public bool IsBound { get; set; }
        [Id(1)] public string? TwitterHandle { get; set; }
    }
}
