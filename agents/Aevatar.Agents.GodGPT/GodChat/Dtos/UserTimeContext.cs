using Orleans;

namespace Aevatar.Application.Grains.GodChat.Dtos;

[GenerateSerializer]
public class UserTimeContext
{
    [Id(0)] public DateTime? UserLocalTime { get; set; }
    [Id(1)] public string? UserTimeZoneId { get; set; }
}
