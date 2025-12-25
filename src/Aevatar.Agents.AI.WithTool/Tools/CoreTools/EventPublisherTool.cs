using System.Text.Json;
using System.Text.Json.Nodes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Tools.CoreTools;

/// <summary>
/// 事件发布工具实现
/// 用于发布事件到Agent流
/// </summary>
public class EventPublisherTool : AevatarToolBase
{
    /// <inheritdoc />
    public override string Name => "publish_event";

    /// <inheritdoc />
    public override string Description => "Publish an event to the agent stream";

    /// <inheritdoc />
    public override ToolCategory Category => ToolCategory.Core;

    /// <inheritdoc />
    public override string Version => "1.0.0";

    /// <inheritdoc />
    public override IList<string> Tags => new List<string> { "core", "event", "communication" };

    /// <inheritdoc />
    protected override bool RequiresInternalAccess() => true;

    /// <inheritdoc />
    protected override bool CanBeOverridden() => false;

    /// <inheritdoc />
    public override ToolParameters CreateParameters()
    {
        return new ToolParameters
        {
            Items = new Dictionary<string, ToolParameter>
            {
                ["event_type"] = new()
                {
                    Type = "string",
                    Required = true,
                    Description = "The type of event to publish"
                },
                ["payload"] = new()
                {
                    Type = "object",
                    Required = true,
                    Description = "The event payload data"
                },
                ["direction"] = new()
                {
                    Type = "string",
                    Enum = new[] { "up", "down", "both" },
                    Description = "The direction to publish the event",
                    DefaultValue = "both"
                }
            },
            Required = new[] { "event_type", "payload" }
        };
    }

    /// <inheritdoc />
    public override async Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default)
    {
        // 验证参数
        var validation = ValidateParameters(ToNullableParameters(parameters));
        if (!validation.IsValid)
        {
            logger?.LogWarning("Invalid parameters: {Errors}", string.Join(", ", validation.Errors));
            var errorResult = new { success = false, errors = validation.Errors };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult));
        }

        if (context.PublishEventCallback == null)
        {
            logger?.LogWarning("PublishEventCallback not provided, cannot publish event");
            var errorResult = new { success = false, error = "Event publishing not available" };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult));
        }

        try
        {
            var eventType = parameters["event_type"]?.ToString();
            var payload = parameters["payload"];
            var direction = parameters.GetValueOrDefault("direction", "both")?.ToString();

            logger?.LogInformation("Publishing event {EventType} with direction {Direction}",
                eventType, direction);

            // Create event message
            var eventId = Guid.NewGuid().ToString();
            var eventMessage = CreateEventMessage(eventId, eventType, payload, context.AgentId, logger);

            // Publish the event
            if (eventMessage != null)
            {
                await context.PublishEventCallback(eventMessage);
            }

            var result = new
            {
                success = true,
                eventId = eventId,
                eventType = eventType,
                direction = direction,
                published = eventMessage != null
            };
            
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(result));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error publishing event");
            var errorResult = new { success = false, error = ex.Message };
            return JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(errorResult));
        }
    }

    /// <summary>
    /// 创建事件消息
    /// </summary>
    private static IMessage? CreateEventMessage(
        string eventId,
        string? eventType,
        object payload,
        string publisherId,
        ILogger? logger)
    {
        try
        {
            // 如果 payload 已经是 IMessage 类型，直接返回
            if (payload is IMessage message)
            {
                return message;
            }

            // 序列化 payload
            var jsonPayload = SerializePayload(payload);

            // 创建事件信封
            return new EventEnvelope
            {
                Id = eventId,
                Message = eventType ?? "GenericEvent",
                Payload = Any.Pack(new StringValue { Value = jsonPayload }),
                Timestamp = Aevatar.Agents.Abstractions.Helpers.TimestampHelper.GetUtcNow(),
                Version = 1,
                PublisherId = publisherId,
                CorrelationId = Guid.NewGuid().ToString(),
                Direction = EventDirection.Both
            };
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to create event message");
            return null;
        }
    }

    /// <summary>
    /// 序列化事件载荷
    /// </summary>
    private static string SerializePayload(object payload)
    {
        return payload switch
        {
            string str => str,
            JsonNode jNode => jNode.ToJsonString(),
            JsonElement jElement => jElement.GetRawText(),
            _ => JsonSerializer.Serialize(payload)
        };
    }
}