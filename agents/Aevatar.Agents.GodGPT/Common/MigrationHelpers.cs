using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Enum = System.Enum;

namespace Aevatar.Agents.GodGPT.Common;

/// <summary>
/// Migration helper methods for converting between C# types and Protobuf types.
/// These helpers simplify the migration of large agents to the new framework.
/// </summary>
public static class MigrationHelpers
{
    #region Repeated Field Operations
    
    /// <summary>
    /// Replace all items in a Protobuf RepeatedField with new items.
    /// Use this instead of direct assignment since RepeatedField is read-only.
    /// </summary>
    /// <example>
    /// // Instead of: State.PaymentHistory = newList;
    /// State.PaymentHistory.ReplaceWith(newList.Select(ToProto));
    /// </example>
    public static void ReplaceWith<T>(this RepeatedField<T> field, IEnumerable<T> items)
    {
        field.Clear();
        field.AddRange(items);
    }
    
    /// <summary>
    /// Find index of item in RepeatedField by predicate.
    /// Returns -1 if not found.
    /// </summary>
    public static int FindIndex<T>(this RepeatedField<T> field, Func<T, bool> predicate)
    {
        for (int i = 0; i < field.Count; i++)
        {
            if (predicate(field[i]))
                return i;
        }
        return -1;
    }
    
    /// <summary>
    /// Find first item in RepeatedField by predicate.
    /// Returns default if not found.
    /// </summary>
    public static T? FindFirst<T>(this RepeatedField<T> field, Func<T, bool> predicate) where T : class
    {
        for (int i = 0; i < field.Count; i++)
        {
            if (predicate(field[i]))
                return field[i];
        }
        return null;
    }
    
    /// <summary>
    /// Remove item from RepeatedField by predicate.
    /// Returns true if item was removed.
    /// </summary>
    public static bool RemoveFirst<T>(this RepeatedField<T> field, Func<T, bool> predicate)
    {
        var index = field.FindIndex(predicate);
        if (index >= 0)
        {
            field.RemoveAt(index);
            return true;
        }
        return false;
    }
    
    #endregion
    
    #region Timestamp Conversions
    
    // /// <summary>
    // /// Convert DateTime to Protobuf Timestamp (handles UTC conversion).
    // /// </summary>
    // public static Timestamp ToProtoTimestamp(this DateTime dateTime)
    // {
    //     // Ensure UTC before conversion
    //     var utc = dateTime.Kind == DateTimeKind.Utc 
    //         ? dateTime 
    //         : dateTime.ToUniversalTime();
    //     return Timestamp.FromDateTime(utc);
    // }
    
    /// <summary>
    /// Convert nullable DateTime to Protobuf Timestamp.
    /// Returns null if input is null.
    /// </summary>
    public static Timestamp? ToProtoTimestamp(this DateTime? dateTime)
    {
        return dateTime?.ToProtoTimestamp();
    }
    
    /// <summary>
    /// Convert Protobuf Timestamp to DateTime.
    /// Returns DateTime.MinValue if null.
    /// </summary>
    public static DateTime ToDateTime(this Timestamp? timestamp)
    {
        return timestamp?.ToDateTime() ?? DateTime.MinValue;
    }
    
    /// <summary>
    /// Convert Protobuf Timestamp to nullable DateTime.
    /// </summary>
    public static DateTime? ToNullableDateTime(this Timestamp? timestamp)
    {
        return timestamp?.ToDateTime();
    }
    
    #endregion
    
    #region Guid Conversions
    
    /// <summary>
    /// Convert Guid to string for Protobuf storage.
    /// </summary>
    public static string ToProtoString(this Guid guid)
    {
        return guid.ToString();
    }
    
    /// <summary>
    /// Convert string to Guid (from Protobuf).
    /// Returns Guid.Empty if parsing fails.
    /// </summary>
    public static Guid ToGuid(this string? str)
    {
        return Guid.TryParse(str, out var guid) ? guid : Guid.Empty;
    }
    
    #endregion
    
    #region Decimal Conversions (Protobuf uses double)
    
    /// <summary>
    /// Convert decimal to double for Protobuf storage.
    /// </summary>
    public static double ToProtoDouble(this decimal value)
    {
        return (double)value;
    }
    
    /// <summary>
    /// Convert nullable decimal to double for Protobuf storage.
    /// </summary>
    public static double ToProtoDouble(this decimal? value)
    {
        return value.HasValue ? (double)value.Value : 0;
    }
    
    /// <summary>
    /// Convert double to decimal (from Protobuf).
    /// </summary>
    public static decimal ToDecimal(this double value)
    {
        return (decimal)value;
    }
    
    #endregion
    
    #region Enum Conversions
    
    /// <summary>
    /// Convert C# enum to int for Protobuf storage.
    /// </summary>
    public static int ToProtoInt<TEnum>(this TEnum value) where TEnum : struct, Enum
    {
        return Convert.ToInt32(value);
    }
    
    /// <summary>
    /// Convert int to C# enum (from Protobuf).
    /// </summary>
    public static TEnum ToEnum<TEnum>(this int value) where TEnum : struct, Enum
    {
        return (TEnum)(object)value;
    }
    
    #endregion
    
    #region Map Field Operations
    
    /// <summary>
    /// Replace all entries in a Protobuf MapField.
    /// </summary>
    public static void ReplaceWith<TKey, TValue>(
        this MapField<TKey, TValue> field, 
        IDictionary<TKey, TValue> items)
    {
        field.Clear();
        foreach (var kvp in items)
        {
            field[kvp.Key] = kvp.Value;
        }
    }
    
    /// <summary>
    /// Convert HashSet to comma-separated string for map storage.
    /// </summary>
    public static string ToCommaSeparated(this HashSet<string> set)
    {
        return string.Join(",", set);
    }
    
    /// <summary>
    /// Convert comma-separated string back to HashSet.
    /// </summary>
    public static HashSet<string> ToHashSet(this string? str)
    {
        if (string.IsNullOrEmpty(str))
            return new HashSet<string>();
        return str.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    }
    
    #endregion
}

