using System.Diagnostics;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MigrationTool.Converters;
using MigrationTool.Models;
using MigrationTool.Readers;
using MigrationTool.Writers;

namespace MigrationTool.Services;

/// <summary>
/// Generic migration service for any agent type
/// </summary>
/// <typeparam name="TOldState">Old C# state type</typeparam>
/// <typeparam name="TNewState">New Protobuf state type</typeparam>
public class MigrationService<TOldState, TNewState>
    where TNewState : class, IMessage<TNewState>, new()
{
    private readonly IOldStateReader<TOldState> _reader;
    private readonly IStateConverter<TOldState, TNewState> _converter;
    private readonly INewStateWriter<TNewState> _writer;
    private readonly ILogger _logger;

    public MigrationService(
        IOldStateReader<TOldState> reader,
        IStateConverter<TOldState, TNewState> converter,
        INewStateWriter<TNewState> writer,
        ILogger logger)
    {
        _reader = reader;
        _converter = converter;
        _writer = writer;
        _logger = logger;
    }

    /// <summary>
    /// Migrate all agents
    /// </summary>
    public async Task<MigrationResult> MigrateAllAsync(
        CancellationToken ct = default,
        IProgress<(int Current, int Total)>? progress = null)
    {
        var result = new MigrationResult();
        var stopwatch = Stopwatch.StartNew();
        
        var total = await _reader.GetCountAsync();
        var processed = 0;

        _logger.LogInformation("Starting migration of {Total} records", total);

        await foreach (var (agentId, oldState) in _reader.ReadAllAsync())
        {
            if (ct.IsCancellationRequested) 
            {
                _logger.LogWarning("Migration cancelled at {Processed}/{Total}", processed, total);
                break;
            }

            try
            {
                var newState = _converter.Convert(oldState);
                await _writer.WriteAsync(agentId, newState);
                result.SuccessCount++;
                
                if (result.SuccessCount % 100 == 0)
                {
                    _logger.LogDebug("Migrated {Count} records...", result.SuccessCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate {AgentId}", agentId);
                result.FailedIds.Add(agentId);
                result.Errors.Add($"{agentId}: {ex.Message}");
            }

            processed++;
            progress?.Report((processed, total));
        }

        stopwatch.Stop();
        result.TotalCount = total;
        result.Duration = stopwatch.Elapsed;
        
        _logger.LogInformation(
            "Migration completed in {Duration}: {Success}/{Total} succeeded ({Rate:F1}%), {Failed} failed",
            result.Duration, result.SuccessCount, result.TotalCount, 
            result.SuccessRate, result.FailedIds.Count);

        return result;
    }

    /// <summary>
    /// Migrate a single agent
    /// </summary>
    public async Task<bool> MigrateOneAsync(string agentId)
    {
        try
        {
            _logger.LogInformation("Migrating single agent: {AgentId}", agentId);
            
            var oldState = await _reader.ReadAsync(agentId);
            if (oldState == null)
            {
                _logger.LogWarning("No state found for {AgentId}", agentId);
                return false;
            }

            var newState = _converter.Convert(oldState);
            await _writer.WriteAsync(agentId, newState);
            
            _logger.LogInformation("Successfully migrated {AgentId}", agentId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate {AgentId}", agentId);
            return false;
        }
    }

    /// <summary>
    /// Migrate a batch of agents
    /// </summary>
    public async Task<MigrationResult> MigrateBatchAsync(
        IEnumerable<string> agentIds,
        CancellationToken ct = default)
    {
        var result = new MigrationResult();
        var stopwatch = Stopwatch.StartNew();
        var idList = agentIds.ToList();
        
        _logger.LogInformation("Migrating batch of {Count} agents", idList.Count);

        foreach (var agentId in idList)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var oldState = await _reader.ReadAsync(agentId);
                if (oldState == null)
                {
                    result.FailedIds.Add(agentId);
                    result.Errors.Add($"{agentId}: State not found");
                    continue;
                }

                var newState = _converter.Convert(oldState);
                await _writer.WriteAsync(agentId, newState);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailedIds.Add(agentId);
                result.Errors.Add($"{agentId}: {ex.Message}");
            }
        }

        stopwatch.Stop();
        result.TotalCount = idList.Count;
        result.Duration = stopwatch.Elapsed;

        return result;
    }
}
