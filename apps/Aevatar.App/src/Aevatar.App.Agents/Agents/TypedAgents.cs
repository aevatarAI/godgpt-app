using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Business.Server;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Agents.Agents;

/// <summary>
/// Base TypedAgent for multi-topic benchmark tests.
/// </summary>
public class TypedAgent : GAgentBase<TypedAgentState>
{
    public string AgentType
    {
        get => State.AgentType;
        set => State.AgentType = value;
    }

    public string StreamNamespace
    {
        get => State.StreamNamespace;
        set => State.StreamNamespace = value;
    }

    public TypedAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"TypedAgent [{State.AgentType}] - Processed: {State.ProcessedCount}");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.AgentId = Id.ToString();
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
    }

    public new TypedAgentState GetState() => State;

    [EventHandler]
    public Task HandleBusinessMessage(BusinessMessageEvent evt)
    {
        State.ProcessedCount++;
        State.LastMessage = evt.Message;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);

        Logger.LogDebug("TypedAgent [{Type}] processed message: {Msg}",
            State.AgentType, evt.Message);

        return Task.CompletedTask;
    }
}

/// <summary>
/// TypeA agent for multi-topic benchmark - routes to TypeA topic
/// </summary>
[StreamTopic("AevatarAgents-TypeA")]
public class TypeAAgent : TypedAgent
{
    public TypeAAgent() : base()
    {
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.AgentType = "TypeA";
        State.StreamNamespace = "AevatarAgents-TypeA";
    }
}

/// <summary>
/// TypeB agent for multi-topic benchmark - routes to TypeB topic
/// </summary>
[StreamTopic("AevatarAgents-TypeB")]
public class TypeBAgent : TypedAgent
{
    public TypeBAgent() : base()
    {
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.AgentType = "TypeB";
        State.StreamNamespace = "AevatarAgents-TypeB";
    }
}

/// <summary>
/// TypeC agent for multi-topic benchmark - routes to TypeC topic
/// </summary>
[StreamTopic("AevatarAgents-TypeC")]
public class TypeCAgent : TypedAgent
{
    public TypeCAgent() : base()
    {
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.AgentType = "TypeC";
        State.StreamNamespace = "AevatarAgents-TypeC";
    }
}

/// <summary>
/// TypeD agent for multi-topic benchmark - routes to TypeD topic
/// </summary>
[StreamTopic("AevatarAgents-TypeD")]
public class TypeDAgent : TypedAgent
{
    public TypeDAgent() : base()
    {
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.AgentType = "TypeD";
        State.StreamNamespace = "AevatarAgents-TypeD";
    }
}

/// <summary>
/// TypeE agent for multi-topic benchmark - routes to TypeE topic
/// </summary>
[StreamTopic("AevatarAgents-TypeE")]
public class TypeEAgent : TypedAgent
{
    public TypeEAgent() : base()
    {
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.AgentType = "TypeE";
        State.StreamNamespace = "AevatarAgents-TypeE";
    }
}

