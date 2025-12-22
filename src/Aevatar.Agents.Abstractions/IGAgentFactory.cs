namespace Aevatar.Agents.Abstractions;

public interface IGAgentFactory
{
    IGAgent CreateGAgent(string id, Type agentType, CancellationToken ct = default);
    
    TAgent CreateGAgent<TAgent>(string id, CancellationToken ct = default)
        where TAgent : IGAgent;

    TAgent CreateGAgent<TAgent>(CancellationToken ct = default)
        where TAgent : IGAgent;
}