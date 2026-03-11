using System;
using System.Collections.Generic;
using System.Reflection;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.GodChat.Dtos;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents;

public class GodChatGAgentTests
{
    [Fact(DisplayName = "ExtractUserTimeContext should restore UTC user local time from retry metadata")]
    public void ExtractUserTimeContext_ShouldRestoreUtcUserLocalTimeFromRetryMetadata()
    {
        // Arrange
        var method = typeof(GodChatGAgent).GetMethod(
            "ExtractUserTimeContext",
            BindingFlags.NonPublic | BindingFlags.Static);

        var metadata = new Dictionary<string, object>
        {
            ["UserLocalTime"] = "2026-03-11T09:30:15Z",
            ["UserTimeZoneId"] = "Asia/Shanghai"
        };

        // Act
        var result = method!.Invoke(null, new object[] { metadata });

        // Assert
        result.ShouldNotBeNull();
        var userTimeContext = result.ShouldBeOfType<UserTimeContext>();
        userTimeContext.UserTimeZoneId.ShouldBe("Asia/Shanghai");
        userTimeContext.UserLocalTime.ShouldBe(DateTimeOffset.Parse("2026-03-11T09:30:15Z").UtcDateTime);
    }
}
