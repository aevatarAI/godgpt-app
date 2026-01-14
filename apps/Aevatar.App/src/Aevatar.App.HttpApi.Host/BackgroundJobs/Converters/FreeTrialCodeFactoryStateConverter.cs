using System;
using System.Collections.Generic;
using System.Linq;
using Aevatar.Agents.GodGPT.Protos.FreeTrialCode;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// Converter for FreeTrialCodeFactoryGAgent State from old JSON format to new Protobuf format
/// </summary>
public class FreeTrialCodeFactoryStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?> oldState)
    {
        try
        {
            var newState = new FreeTrialCodeFactoryState();

            // batch_id (optional int64) - from FactoryId if it's numeric
            if (oldState.TryGetValue("FactoryId", out var factoryIdObj))
            {
                var factoryId = ConvertToString(factoryIdObj);
                if (long.TryParse(factoryId?.Replace("factory-", ""), out var batchId))
                {
                    newState.BatchId = batchId;
                }
            }

            // operator_user_id (string) - default empty if not found
            newState.OperatorUserId = ConvertToString(oldState.GetValueOrDefault("OperatorUserId")) ?? string.Empty;

            // batch_config (optional BatchConfig) - create from available fields
            var batchConfig = new BatchConfig();
            batchConfig.TrialDays = ConvertToInt32(oldState.GetValueOrDefault("TrialDays")) ?? 7;
            batchConfig.ProductId = ConvertToString(oldState.GetValueOrDefault("ProductId")) ?? string.Empty;
            batchConfig.PlanType = FactoryPlanType.None; // Default: FACTORY_PLAN_TYPE_NONE = 0
            batchConfig.IsUltimate = ConvertToBool(oldState.GetValueOrDefault("IsUltimate")) ?? false;
            batchConfig.Platform = FactoryPaymentPlatform.Stripe; // Default: FACTORY_PAYMENT_PLATFORM_STRIPE = 0
            newState.BatchConfig = batchConfig;

            // creation_time (Timestamp) - from CreatedAt or use current time
            if (oldState.TryGetValue("CreatedAt", out var createdAtObj) && createdAtObj != null)
            {
                var createdAt = ConvertToDateTime(createdAtObj);
                if (createdAt.HasValue)
                {
                    newState.CreationTime = Timestamp.FromDateTime(createdAt.Value.ToUniversalTime());
                }
            }
            else
            {
                newState.CreationTime = Timestamp.FromDateTime(DateTime.UtcNow);
            }

            // status (FactoryStatus enum) - default to Active
            newState.Status = FactoryStatus.Active; // FACTORY_STATUS_ACTIVE = 1

            // generated_codes (repeated string) - from GeneratedCodes array
            if (oldState.TryGetValue("GeneratedCodes", out var generatedCodesObj) && generatedCodesObj is List<object> codesList)
            {
                foreach (var codeObj in codesList)
                {
                    if (codeObj is Dictionary<string, object?> codeDict && codeDict.TryGetValue("Code", out var codeValue))
                    {
                        var code = ConvertToString(codeValue);
                        if (!string.IsNullOrEmpty(code))
                        {
                            newState.GeneratedCodes.Add(code);
                        }
                    }
                    else
                    {
                        var code = ConvertToString(codeObj);
                        if (!string.IsNullOrEmpty(code))
                        {
                            newState.GeneratedCodes.Add(code);
                        }
                    }
                }
            }

            // used_codes (repeated string) - from GeneratedCodes where Used == true
            if (oldState.TryGetValue("GeneratedCodes", out var codesObj2) && codesObj2 is List<object> codesList2)
            {
                foreach (var codeObj in codesList2)
                {
                    if (codeObj is Dictionary<string, object?> codeDict && 
                        ConvertToBool(codeDict.GetValueOrDefault("Used")) == true &&
                        codeDict.TryGetValue("Code", out var codeValue))
                    {
                        var code = ConvertToString(codeValue);
                        if (!string.IsNullOrEmpty(code))
                        {
                            newState.UsedCodes.Add(code);
                        }
                    }
                }
            }

            // total_codes_generated (int32) - from TotalGenerated or count of GeneratedCodes
            if (oldState.TryGetValue("TotalGenerated", out var totalGeneratedObj))
            {
                newState.TotalCodesGenerated = ConvertToInt32(totalGeneratedObj) ?? newState.GeneratedCodes.Count;
            }
            else
            {
                newState.TotalCodesGenerated = newState.GeneratedCodes.Count;
            }

            // used_count (int32) - from TotalUsed or count of UsedCodes
            if (oldState.TryGetValue("TotalUsed", out var totalUsedObj))
            {
                newState.UsedCount = ConvertToInt32(totalUsedObj) ?? newState.UsedCodes.Count;
            }
            else
            {
                newState.UsedCount = newState.UsedCodes.Count;
            }

            // last_generation_time (Timestamp) - from GeneratedCodes[0].CreatedAt or use creation_time
            if (oldState.TryGetValue("GeneratedCodes", out var codesObj3) && codesObj3 is List<object> codesList3 && codesList3.Count > 0)
            {
                var firstCode = codesList3[0];
                if (firstCode is Dictionary<string, object?> codeDict && codeDict.TryGetValue("CreatedAt", out var codeCreatedAtObj))
                {
                    var codeCreatedAt = ConvertToDateTime(codeCreatedAtObj);
                    if (codeCreatedAt.HasValue)
                    {
                        newState.LastGenerationTime = Timestamp.FromDateTime(codeCreatedAt.Value.ToUniversalTime());
                    }
                }
            }
            
            if (newState.LastGenerationTime == null)
            {
                newState.LastGenerationTime = newState.CreationTime;
            }

            return newState;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FreeTrialCodeFactoryStateConverter] Conversion failed: {ex.Message}");
            return null;
        }
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
