namespace Aevatar.Application.Grains.Agents.ChatManager;

// NOTE: ConfigurationBase was removed, fields inlined
[GenerateSerializer]
public class ManagerConfigDto
{
    // Base fields (from removed ConfigurationBase)
    [Id(0)] public string? ConfigId { get; set; }
    [Id(1)] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Id(2)] public DateTime? UpdatedAt { get; set; }
    
    // Specific field
    [Id(3)] public string SystemLLM { get; set; }
}
