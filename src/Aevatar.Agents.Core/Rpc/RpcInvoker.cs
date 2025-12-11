using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Rpc;
using Aevatar.Agents.Rpc;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;

namespace Aevatar.Agents.Core.Rpc;

/// <summary>
/// Shared RPC invocation logic for all runtime implementations.
/// Handles method discovery, argument deserialization, invocation, and result serialization.
/// </summary>
public static class RpcInvoker
{
    private static readonly ConcurrentDictionary<(System.Type, string), MethodInfo> MethodCache = new();

    /// <summary>
    /// Invoke RPC method on Agent
    /// </summary>
    public static async Task<byte[]> InvokeAsync(IGAgent agent, byte[] requestBytes, ILogger? logger = null)
    {
        var request = RpcRequest.Parser.ParseFrom(requestBytes);
        var response = new RpcResponse
        {
            CorrelationId = request.CorrelationId,
            Success = false
        };

        try
        {
            if (agent == null)
                throw new InvalidOperationException("Agent not initialized");

            var method = GetCachedMethod(agent.GetType(), request.MethodName);
            var args = DeserializeArgs(request.Args, method.GetParameters());
            var result = method.Invoke(agent, args);

            // Handle async methods
            if (result is Task task)
            {
                await task;
                result = GetTaskResult(task);
            }

            if (result != null)
            {
                response.Result = ProtobufPacker.Pack(result);
            }
            response.Success = true;
        }
        catch (Exception ex)
        {
            var innerEx = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
            response.Error = new RpcError
            {
                ErrorType = innerEx.GetType().FullName ?? innerEx.GetType().Name,
                Message = innerEx.Message,
                StackTrace = innerEx.StackTrace ?? string.Empty
            };
            logger?.LogError(innerEx, "RPC method {Method} failed", request.MethodName);
        }

        return response.ToByteArray();
    }

    private static MethodInfo GetCachedMethod(System.Type agentType, string methodName)
    {
        return MethodCache.GetOrAdd((agentType, methodName), k =>
        {
            var method = k.Item1.GetMethod(k.Item2,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new InvalidOperationException($"Method '{k.Item2}' not found on type '{k.Item1.Name}'");

            // Validate method is defined in an interface
            var paramTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
            var isInInterface = k.Item1.GetInterfaces().Any(iface =>
                iface.GetMethod(method.Name, paramTypes) is { } m && m.ReturnType == method.ReturnType);

            if (!isInInterface)
            {
                throw new InvalidOperationException(
                    $"Method '{method.Name}' is not defined in any interface. " +
                    $"Only methods defined in interfaces can be called via RPC.");
            }

            return method;
        });
    }

    private static object?[] DeserializeArgs(
        Google.Protobuf.Collections.RepeatedField<Any> protoArgs,
        ParameterInfo[] paramInfos)
    {
        return paramInfos.Select((p, i) =>
            i < protoArgs.Count ? ProtobufPacker.Unpack(protoArgs[i], p.ParameterType)
            : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
    }

    private static object? GetTaskResult(Task task)
    {
        return task.GetType().IsGenericType
            ? task.GetType().GetProperty("Result")?.GetValue(task)
            : null;
    }
}

