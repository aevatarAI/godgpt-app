using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents.Rpc;
using Google.Protobuf;

namespace Aevatar.Agents.Abstractions.Extensions;

/// <summary>
/// Extension methods for RPC calls on IGAgentActor
/// </summary>
public static class RpcExtensions
{
    /// <summary>
    /// Invoke RPC method with typed result
    /// </summary>
    public static async Task<TResult> InvokeRpcAsync<TResult>(
        this IGAgentActor actor,
        string methodName,
        params object?[] args)
    {
        var response = await InvokeRpcCoreAsync(actor, methodName, args);
        return ProtobufPacker.Unpack<TResult>(response.Result);
    }

    /// <summary>
    /// Invoke RPC method without return value
    /// </summary>
    public static async Task InvokeRpcAsync(
        this IGAgentActor actor,
        string methodName,
        params object?[] args)
    {
        await InvokeRpcCoreAsync(actor, methodName, args);
    }

    private static async Task<RpcResponse> InvokeRpcCoreAsync(
        IGAgentActor actor,
        string methodName,
        object?[] args)
    {
        var request = new RpcRequest
        {
            MethodName = methodName,
            CorrelationId = Guid.NewGuid().ToString()
        };

        foreach (var arg in args)
        {
            request.Args.Add(ProtobufPacker.Pack(arg));
        }

        var responseBytes = await actor.InvokeRpcAsync(request.ToByteArray());
        var response = RpcResponse.Parser.ParseFrom(responseBytes);

        if (!response.Success)
        {
            throw new InvalidOperationException(
                $"RPC call failed: {response.Error?.Message ?? "Unknown error"}");
        }

        return response;
    }
}
