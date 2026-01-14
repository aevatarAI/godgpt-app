using System;
using System.Collections.Generic;
using Aevatar.Agents.GodGPT.Protos;
using Google.Protobuf;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// Converter for ConfigurationGAgent State from old JSON format to new Protobuf format
/// </summary>
public class ConfigurationStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?> oldState)
    {
        try
        {
            var newState = new ConfigurationState();

            // Extract from ConfigValue if it exists, otherwise use direct fields
            if (oldState.TryGetValue("ConfigValue", out var configValueObj) && configValueObj is Dictionary<string, object?> configValue)
            {
                // system_llm (string) - from ConfigValue.Setting1 or nested fields
                newState.SystemLlm = ExtractStringFromConfig(configValue, "SystemLLM", "system_llm", "Setting1") ?? string.Empty;

                // prompt (string) - from ConfigValue.Setting2 or nested fields
                newState.Prompt = ExtractStringFromConfig(configValue, "Prompt", "prompt", "Setting2") ?? string.Empty;

                // streaming_mode_enabled (bool) - from ConfigValue or nested fields
                newState.StreamingModeEnabled = ExtractBoolFromConfig(configValue, "StreamingModeEnabled", "streaming_mode_enabled") ?? false;

                // user_profile_prompt (string) - from ConfigValue.Nested or nested fields
                if (configValue.TryGetValue("Nested", out var nestedObj) && nestedObj is Dictionary<string, object?> nested)
                {
                    newState.UserProfilePrompt = ConvertToString(nested.GetValueOrDefault("Key")) ?? string.Empty;
                }
                else
                {
                    newState.UserProfilePrompt = ExtractStringFromConfig(configValue, "UserProfilePrompt", "user_profile_prompt") ?? string.Empty;
                }
            }
            else
            {
                // Fallback to direct fields
                newState.SystemLlm = ConvertToString(oldState.GetValueOrDefault("SystemLLM")) ?? string.Empty;
                newState.Prompt = ConvertToString(oldState.GetValueOrDefault("Prompt")) ?? string.Empty;
                newState.StreamingModeEnabled = ConvertToBool(oldState.GetValueOrDefault("StreamingModeEnabled")) ?? false;
                newState.UserProfilePrompt = ConvertToString(oldState.GetValueOrDefault("UserProfilePrompt")) ?? string.Empty;
            }

            return newState;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigurationStateConverter] Conversion failed: {ex.Message}");
            return null;
        }
    }

    private static string? ExtractStringFromConfig(Dictionary<string, object?> config, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (config.TryGetValue(key, out var value) && value != null)
            {
                return ConvertToString(value);
            }
        }
        return null;
    }

    private static bool? ExtractBoolFromConfig(Dictionary<string, object?> config, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (config.TryGetValue(key, out var value) && value != null)
            {
                return ConvertToBool(value);
            }
        }
        return null;
    }

    private static string? ConvertToString(object? value)
    {
        if (value == null) return null;
        return value.ToString();
    }

    private static bool? ConvertToBool(object? value)
    {
        if (value == null) return null;
        if (value is bool b) return b;
        if (bool.TryParse(value.ToString(), out var result)) return result;
        return null;
    }
}
