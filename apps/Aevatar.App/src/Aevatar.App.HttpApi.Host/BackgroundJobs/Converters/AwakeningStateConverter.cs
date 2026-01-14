using System;
using System.Collections.Generic;
using Aevatar.Agents.GodGPT.Protos.Awakening;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// Converter for AwakeningGAgent State from old JSON format to new Protobuf format
/// </summary>
public class AwakeningStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?> oldState)
    {
        try
        {
            var newState = new AwakeningStateProto();

            // last_generated_timestamp (int64) - from CreatedAt or Status.LastUpdate
            if (oldState.TryGetValue("CreatedAt", out var createdAtObj) && createdAtObj != null)
            {
                var createdAt = ConvertToDateTime(createdAtObj);
                if (createdAt.HasValue)
                {
                    newState.LastGeneratedTimestamp = ((DateTimeOffset)createdAt.Value).ToUnixTimeSeconds();
                }
            }
            else if (oldState.TryGetValue("Status", out var statusObj) && statusObj is Dictionary<string, object?> status)
            {
                if (status.TryGetValue("LastUpdate", out var lastUpdateObj) && lastUpdateObj != null)
                {
                    var lastUpdate = ConvertToDateTime(lastUpdateObj);
                    if (lastUpdate.HasValue)
                    {
                        newState.LastGeneratedTimestamp = ((DateTimeOffset)lastUpdate.Value).ToUnixTimeSeconds();
                    }
                }
            }

            // awakening_level (int32) - default to 1 if not found
            newState.AwakeningLevel = ConvertToInt32(oldState.GetValueOrDefault("AwakeningLevel")) ?? 1;

            // awakening_message (string)
            newState.AwakeningMessage = ConvertToString(oldState.GetValueOrDefault("AwakeningMessage")) ?? string.Empty;

            // language (int32) - from Language.Code or Language enum
            if (oldState.TryGetValue("Language", out var languageObj))
            {
                if (languageObj is Dictionary<string, object?> langDict && langDict.TryGetValue("Code", out var codeObj))
                {
                    var code = ConvertToString(codeObj);
                    // Map language codes to int32 (0=English, 1=Chinese, 2=Spanish)
                    newState.Language = code switch
                    {
                        "en" or "en-US" => 0,
                        "zh" or "zh-CN" => 1,
                        "es" or "es-ES" => 2,
                        _ => 0
                    };
                }
                else
                {
                    newState.Language = ConvertToInt32(languageObj) ?? 0;
                }
            }

            // session_id (string)
            newState.SessionId = ConvertToString(oldState.GetValueOrDefault("SessionId")) ?? string.Empty;

            // created_at (Timestamp)
            if (oldState.TryGetValue("CreatedAt", out var createdObj) && createdObj != null)
            {
                var created = ConvertToDateTime(createdObj);
                if (created.HasValue)
                {
                    newState.CreatedAt = Timestamp.FromDateTime(created.Value.ToUniversalTime());
                }
            }

            // generation_attempts (int32)
            newState.GenerationAttempts = ConvertToInt32(oldState.GetValueOrDefault("GenerationAttempts")) ?? 0;

            // status (AwakeningStatusProto enum)
            if (oldState.TryGetValue("Status", out var statusObj2) && statusObj2 is Dictionary<string, object?> statusDict)
            {
                if (statusDict.TryGetValue("IsActive", out var isActiveObj) && ConvertToBool(isActiveObj) == true)
                {
                    newState.Status = AwakeningStatusProto.AwakeningStatusGenerating;
                }
                else
                {
                    newState.Status = AwakeningStatusProto.AwakeningStatusNotStarted;
                }
            }
            else
            {
                newState.Status = AwakeningStatusProto.AwakeningStatusUnspecified;
            }

            return newState;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AwakeningStateConverter] Conversion failed: {ex.Message}");
            return null;
        }
    }

    private static bool? ConvertToBool(object? value)
    {
        if (value == null) return null;
        if (value is bool b) return b;
        if (bool.TryParse(value.ToString(), out var result)) return result;
        return null;
    }

    private static string? ConvertToString(object? value)
    {
        if (value == null) return null;
        return value.ToString();
    }

    private static int? ConvertToInt32(object? value)
    {
        if (value == null) return null;
        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (int.TryParse(value.ToString(), out var result)) return result;
        return null;
    }

    private static DateTime? ConvertToDateTime(object? value)
    {
        if (value == null) return null;
        if (value is DateTime dt) return dt;
        if (value is string str && DateTime.TryParse(str, out var dt2)) return dt2;
        return null;
    }
}
