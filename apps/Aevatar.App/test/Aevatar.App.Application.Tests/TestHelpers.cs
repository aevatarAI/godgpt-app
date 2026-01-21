using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Rpc;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Aevatar.App;

/// <summary>
/// Test helper to create agents with InMemoryEventStore for unit testing.
/// Uses the framework's AgentEventStoreInjector for consistency.
/// Based on the Payment module's test pattern.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Create agent with InMemoryEventStore configured (for unit tests without Orleans)
    /// </summary>
    public static T CreateAgent<T>() where T : class, IGAgent, new()
    {
        var agent = new T();
        
        // Build a minimal service provider with InMemoryEventStore
        var services = new ServiceCollection();
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        var serviceProvider = services.BuildServiceProvider();
        
        // Use framework's injector
        AgentEventStoreInjector.InjectEventStore(agent, serviceProvider);
        
        return agent;
    }
    
    /// <summary>
    /// Setup mock IGAgentActor to handle RPC calls via As&lt;T&gt;() extension
    /// </summary>
    public static void SetupRpcMock<TInterface>(
        IGAgentActor mockActor, 
        string methodName, 
        IMessage response) where TInterface : class
    {
        mockActor.InvokeRpcAsync(Arg.Is<byte[]>(bytes => 
            RpcRequest.Parser.ParseFrom(bytes).MethodName == methodName))
            .Returns(callInfo =>
            {
                var rpcResponse = new RpcResponse
                {
                    Success = true,
                    Result = ProtobufPacker.Pack(response)
                };
                return Task.FromResult(rpcResponse.ToByteArray());
            });
    }
    
    /// <summary>
    /// Setup mock IGAgentActor to handle RPC calls that return primitive types
    /// </summary>
    public static void SetupRpcMock<TResult>(
        IGAgentActor mockActor, 
        string methodName, 
        TResult response)
    {
        mockActor.InvokeRpcAsync(Arg.Is<byte[]>(bytes => 
            RpcRequest.Parser.ParseFrom(bytes).MethodName == methodName))
            .Returns(callInfo =>
            {
                var rpcResponse = new RpcResponse
                {
                    Success = true,
                    Result = ProtobufPacker.Pack(response)
                };
                return Task.FromResult(rpcResponse.ToByteArray());
            });
    }
    
    /// <summary>
    /// Setup mock IGAgentActor to handle RPC calls that return void (Task)
    /// </summary>
    public static void SetupRpcMockVoid(
        IGAgentActor mockActor, 
        string methodName)
    {
        mockActor.InvokeRpcAsync(Arg.Is<byte[]>(bytes => 
            RpcRequest.Parser.ParseFrom(bytes).MethodName == methodName))
            .Returns(callInfo =>
            {
                var rpcResponse = new RpcResponse { Success = true };
                return Task.FromResult(rpcResponse.ToByteArray());
            });
    }
}

