using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MigrationTool.Converters;
using MigrationTool.Models;
using MigrationTool.Readers;
using MigrationTool.Writers;
using Newtonsoft.Json;

namespace MigrationTool.Services;

/// <summary>
/// Verification service to validate migrated data
/// </summary>
/// <typeparam name="TOldState">Old C# state type</typeparam>
/// <typeparam name="TNewState">New Protobuf state type</typeparam>
public class VerificationService<TOldState, TNewState>
    where TNewState : class, IMessage<TNewState>, new()
{
    private readonly IOldStateReader<TOldState> _oldReader;
    private readonly INewStateWriter<TNewState> _newWriter;
    private readonly IStateConverter<TOldState, TNewState> _converter;
    private readonly ILogger _logger;

    public VerificationService(
        IOldStateReader<TOldState> oldReader,
        INewStateWriter<TNewState> newWriter,
        IStateConverter<TOldState, TNewState> converter,
        ILogger logger)
    {
        _oldReader = oldReader;
        _newWriter = newWriter;
        _converter = converter;
        _logger = logger;
    }

    /// <summary>
    /// Verify a sample of migrated data
    /// </summary>
    public async Task<VerificationResult> VerifySampleAsync(
        int sampleSize = 100,
        CancellationToken ct = default)
    {
        var result = new VerificationResult();
        
        _logger.LogInformation("Starting verification of {SampleSize} records", sampleSize);
        
        var sampleIds = await _oldReader.GetSampleIdsAsync(sampleSize);
        
        foreach (var agentId in sampleIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var verifyResult = await VerifyOneAsync(agentId);
                
                switch (verifyResult)
                {
                    case VerifyOneResult.Match:
                        result.MatchCount++;
                        break;
                    case VerifyOneResult.Mismatch:
                        result.MismatchIds.Add(agentId);
                        break;
                    case VerifyOneResult.MissingInNew:
                        result.MissingInNew.Add(agentId);
                        break;
                    case VerifyOneResult.Error:
                        result.ErrorIds.Add(agentId);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Verification error for {AgentId}", agentId);
                result.ErrorIds.Add(agentId);
            }

            result.TotalVerified++;
        }

        _logger.LogInformation(
            "Verification completed: {Match}/{Total} matched ({Rate:F1}%), " +
            "{Mismatch} mismatched, {Missing} missing, {Errors} errors",
            result.MatchCount, result.TotalVerified, result.MatchRate,
            result.MismatchIds.Count, result.MissingInNew.Count, result.ErrorIds.Count);

        return result;
    }

    /// <summary>
    /// Verify all migrated data
    /// </summary>
    public async Task<VerificationResult> VerifyAllAsync(
        CancellationToken ct = default,
        IProgress<int>? progress = null)
    {
        var result = new VerificationResult();
        var count = 0;
        
        _logger.LogInformation("Starting full verification");

        await foreach (var (agentId, oldState) in _oldReader.ReadAllAsync())
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var newState = await _newWriter.ReadAsync(agentId);
                if (newState == null)
                {
                    result.MissingInNew.Add(agentId);
                    continue;
                }

                // Convert new back to old and compare
                var convertedBack = _converter.ConvertBack(newState);
                var isMatch = CompareStates(oldState, convertedBack);

                if (isMatch)
                {
                    result.MatchCount++;
                }
                else
                {
                    result.MismatchIds.Add(agentId);
                    if (result.MismatchIds.Count <= 5)
                    {
                        LogMismatchDetails(agentId, oldState, convertedBack);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Verification failed for {AgentId}", agentId);
                result.ErrorIds.Add(agentId);
            }

            count++;
            result.TotalVerified = count;
            progress?.Report(count);
        }

        return result;
    }

    /// <summary>
    /// Verify a single agent
    /// </summary>
    public async Task<VerifyOneResult> VerifyOneAsync(string agentId)
    {
        try
        {
            var oldState = await _oldReader.ReadAsync(agentId);
            if (oldState == null)
            {
                _logger.LogWarning("Old state not found for {AgentId}", agentId);
                return VerifyOneResult.Error;
            }

            var newState = await _newWriter.ReadAsync(agentId);
            if (newState == null)
            {
                _logger.LogWarning("New state not found for {AgentId}", agentId);
                return VerifyOneResult.MissingInNew;
            }

            // Convert new back to old and compare
            var convertedBack = _converter.ConvertBack(newState);
            var isMatch = CompareStates(oldState, convertedBack);

            if (isMatch)
            {
                _logger.LogDebug("Verification passed for {AgentId}", agentId);
                return VerifyOneResult.Match;
            }
            else
            {
                LogMismatchDetails(agentId, oldState, convertedBack);
                return VerifyOneResult.Mismatch;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verification error for {AgentId}", agentId);
            return VerifyOneResult.Error;
        }
    }

    /// <summary>
    /// Compare two states using JSON serialization
    /// </summary>
    private bool CompareStates(TOldState old, TOldState converted)
    {
        var oldJson = JsonConvert.SerializeObject(old, Formatting.None);
        var convertedJson = JsonConvert.SerializeObject(converted, Formatting.None);
        return oldJson == convertedJson;
    }

    /// <summary>
    /// Log details of mismatched states
    /// </summary>
    private void LogMismatchDetails(string agentId, TOldState old, TOldState converted)
    {
        var oldJson = JsonConvert.SerializeObject(old, Formatting.Indented);
        var convertedJson = JsonConvert.SerializeObject(converted, Formatting.Indented);
        
        _logger.LogWarning(
            "Mismatch for {AgentId}:\nOld: {OldJson}\nConverted: {ConvertedJson}",
            agentId, oldJson, convertedJson);
    }
}

public enum VerifyOneResult
{
    Match,
    Mismatch,
    MissingInNew,
    Error
}
