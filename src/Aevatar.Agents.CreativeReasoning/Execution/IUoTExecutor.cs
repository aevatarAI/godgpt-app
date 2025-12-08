using Aevatar.Agents.CreativeReasoning.Core;

namespace Aevatar.Agents.CreativeReasoning.Execution;

// ============================================================
//  UoT Executor Interface
//  Unified entry point for all three UoT modes
// ============================================================

/// <summary>
/// Executor interface for Universe of Thoughts creative reasoning.
/// Supports C-UoT, E-UoT, and T-UoT modes.
/// </summary>
public interface IUoTExecutor
{
    /// <summary>
    /// Execute UoT creative reasoning for a given problem.
    /// Mode is determined by options.Mode setting.
    /// </summary>
    /// <param name="problem">The problem to solve creatively</param>
    /// <param name="options">Configuration options (includes mode selection)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Creative reasoning result with ranked solutions</returns>
    Task<UoTResult> ExecuteAsync(
        string problem,
        UoTOptions options,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Execute T-UoT transformative reasoning for a given problem.
    /// Returns T-UoT specific result with rule mutation details.
    /// </summary>
    /// <param name="problem">The problem to solve transformatively</param>
    /// <param name="options">Configuration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transformative reasoning result with rule mutation trace</returns>
    Task<TUoTResult> ExecuteTransformativeAsync(
        string problem,
        UoTOptions options,
        CancellationToken cancellationToken = default);
}
