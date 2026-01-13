namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Marks a method as safe for concurrent execution.
/// When used on interface methods, RpcProxy will route calls through
/// InvokeReadOnlyRpcAsync which has [AlwaysInterleave] on Orleans Grain.
/// 
/// Use this for read-only operations that don't modify Agent state.
/// 
/// Note: This is NOT Orleans' [ReadOnly] attribute. Orleans' [ReadOnly] only works
/// on Grain implementation methods. Since our RPC mechanism goes through a single
/// InvokeRpcAsync entry point, Orleans cannot see the business method's attributes.
/// This attribute enables our framework to make the routing decision.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class ConcurrentSafeAttribute : Attribute
{
}
