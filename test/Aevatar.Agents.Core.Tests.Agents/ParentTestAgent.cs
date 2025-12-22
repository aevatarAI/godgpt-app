namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Test agent acting as parent
/// </summary>
public class ParentTestAgent : GAgentBase<TestAgentState>
{
    public List<string> Children { get; } = new();

    public override string GetDescription() => "ParentTestAgent";

    public async Task AddChildAsync(string childId)
    {
        Children.Add(childId);
        await Task.CompletedTask;
    }

    public async Task RemoveChildAsync(string childId)
    {
        Children.Remove(childId);
        await Task.CompletedTask;
    }
}