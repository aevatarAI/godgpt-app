using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using System.Reflection;

namespace Aevatar.Agents.Abstractions.Rpc;

/// <summary>
/// Shared Protobuf packing/unpacking utilities for RPC.
/// Used by both client-side (RpcExtensions) and server-side (RpcInvoker).
/// </summary>
public static class ProtobufPacker
{
    // Cache the generic Unpack method to avoid repeated reflection
    private static readonly MethodInfo? UnpackMethod = typeof(Any)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .FirstOrDefault(m => m.Name == "Unpack" && m.IsGenericMethod && m.GetParameters().Length == 0);

    /// <summary>
    /// Pack a value into Protobuf Any
    /// </summary>
    public static Any Pack(object? value)
    {
        // Handle null and void task results
        if (value == null || value.GetType().FullName == "System.Threading.Tasks.VoidTaskResult")
        {
            return Any.Pack(new Empty());
        }

        return value switch
        {
            int i => Any.Pack(new Int32Value { Value = i }),
            long l => Any.Pack(new Int64Value { Value = l }),
            string s => Any.Pack(new StringValue { Value = s }),
            bool b => Any.Pack(new BoolValue { Value = b }),
            double d => Any.Pack(new DoubleValue { Value = d }),
            float f => Any.Pack(new FloatValue { Value = f }),
            decimal m => Any.Pack(new DoubleValue { Value = (double)m }), // decimal -> double
            byte[] bytes => Any.Pack(new BytesValue { Value = ByteString.CopyFrom(bytes) }),
            IMessage msg => Any.Pack(msg),
            _ => throw new NotSupportedException($"Type {value.GetType()} is not supported for Protobuf packing")
        };
    }

    /// <summary>
    /// Unpack a Protobuf Any to target type
    /// </summary>
    public static object? Unpack(Any any, System.Type targetType)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        var actualType = underlyingType ?? targetType;
        var isNullable = underlyingType != null;

        // Handle Protobuf message types first
        if (typeof(IMessage).IsAssignableFrom(actualType) && UnpackMethod != null)
        {
            var genericUnpack = UnpackMethod.MakeGenericMethod(actualType);
            return genericUnpack.Invoke(any, null);
        }

        // Handle primitive types
        return actualType switch
        {
            var t when t == typeof(int) => isNullable ? (int?)any.Unpack<Int32Value>().Value : any.Unpack<Int32Value>().Value,
            var t when t == typeof(long) => isNullable ? (long?)any.Unpack<Int64Value>().Value : any.Unpack<Int64Value>().Value,
            var t when t == typeof(string) => isNullable && string.IsNullOrEmpty(any.Unpack<StringValue>().Value) ? null : any.Unpack<StringValue>().Value,
            var t when t == typeof(bool) => isNullable ? (bool?)any.Unpack<BoolValue>().Value : any.Unpack<BoolValue>().Value,
            var t when t == typeof(double) => isNullable ? (double?)any.Unpack<DoubleValue>().Value : any.Unpack<DoubleValue>().Value,
            var t when t == typeof(float) => isNullable ? (float?)any.Unpack<FloatValue>().Value : any.Unpack<FloatValue>().Value,
            var t when t == typeof(decimal) => isNullable ? (decimal?)Convert.ToDecimal(any.Unpack<DoubleValue>().Value) : Convert.ToDecimal(any.Unpack<DoubleValue>().Value),
            var t when t == typeof(byte[]) => any.Unpack<BytesValue>().Value.ToByteArray(),
            _ => throw new NotSupportedException($"Type {targetType} is not supported for Protobuf unpacking")
        };
    }

    /// <summary>
    /// Unpack a Protobuf Any to generic type T
    /// </summary>
    public static T Unpack<T>(Any any)
    {
        return (T)Unpack(any, typeof(T))!;
    }
}

