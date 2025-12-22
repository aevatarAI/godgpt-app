namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Test agent acting as child
/// </summary>
public class ChildTestAgent : GAgentBase<TestAgentState>
{
    public string? ParentId { get; private set; }
    
    public override string GetDescription() => "ChildTestAgent";
    
    public async Task SetParentIdAsync(string parentId)
    {
        ParentId = parentId;
        await Task.CompletedTask;
    }
    
    public async Task ClearParentAsync()
    {
        ParentId = null;
        await Task.CompletedTask;
    }
    
    // NOTE: SendToParentAsync method removed - was a mock that only incremented counter
    // For real parent-child communication, see RealChildAgent in ParentChildCommunicationTests.cs
    // which uses PublishAsync(evt, EventDirection.Up)
}