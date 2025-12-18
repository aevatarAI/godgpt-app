using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Aevatar.Agents.Core.Tests")]
[assembly: InternalsVisibleTo("Aevatar.Agents.AI.Core")]
[assembly: InternalsVisibleTo("Aevatar.Agents.AI.Core.Tests")]

namespace Aevatar.Agents.Core.StateProtection;

/// <summary>
/// Context tracker for State and Config protection mechanism.
/// Tracks whether current execution is within an event handler or initialization context.
/// </summary>
internal static class StateProtectionContext
{
    /// <summary>
    /// AsyncLocal storage to track event handler execution context per async flow.
    /// This ensures thread-safe context tracking in async operations.
    /// </summary>
    private static readonly AsyncLocal<bool> IsStateOrConfigModifiable = new();

    /// <summary>
    /// Gets whether current execution is within a modifiable context (event handler or initialization).
    /// </summary>
    public static bool IsModifiable => IsStateOrConfigModifiable.Value;

    /// <summary>
    /// Unified scope for state modification contexts.
    /// Used for both event handlers and initialization - the behavior is identical.
    /// </summary>
    public sealed class StateModificationScope : IDisposable
    {
        private readonly bool _previousValue;
        private bool _disposed;

        internal StateModificationScope()
        {
            _previousValue = IsStateOrConfigModifiable.Value;
            IsStateOrConfigModifiable.Value = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            IsStateOrConfigModifiable.Value = _previousValue;
        }
    }

    /// <summary>
    /// Begins an event handler execution scope.
    /// State modifications are only allowed within this scope.
    /// </summary>
    /// <returns>A disposable scope that must be disposed when event handling completes.</returns>
    public static StateModificationScope BeginEventHandlerScope() => new();

    /// <summary>
    /// Creates a special scope for agent initialization where State setup is allowed.
    /// Should only be used in OnActivateAsync or similar initialization methods.
    /// </summary>
    /// <returns>A disposable scope for initialization.</returns>
    public static StateModificationScope BeginInitializationScope() => new();

    /// <summary>
    /// Checks if current context allows State modification.
    /// Throws InvalidOperationException if not in event handler context.
    /// </summary>
    /// <param name="operationName">Name of the operation attempting to modify state</param>
    /// <exception cref="InvalidOperationException">Thrown when not in event handler context</exception>
    public static void EnsureModifiable(string operationName = "State modification")
    {
        if (!IsModifiable)
        {
            throw new InvalidOperationException(
                $"{operationName} is not allowed outside of event handlers. " +
                "State must only be modified within event handler methods to ensure consistency through the event stream. " +
                "RPC methods cannot directly modify State. " +
                "For EventSourcing mode: Use RaiseEvent + ConfirmEventsAsync. " +
                "For StateStore mode: Publish an event and handle it in an [EventHandler] method instead.");
        }
    }
}
