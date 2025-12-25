namespace Aevatar.Agents.AI.Abstractions;

// ============================================================
//  LLM Call Policy
//  Configuration for retry, circuit breaker, and timeout.
//
//  Design Philosophy:
//  - Network is unreliable, prepare for failures
//  - Exponential backoff respects rate limits
//  - Circuit breaker prevents cascade failures
//  - Every failure is a learning opportunity
// ============================================================

/// <summary>
/// Configuration for LLM call resilience behavior.
/// </summary>
public sealed record LLMCallPolicy
{
    /// <summary>Maximum retry attempts for transient failures.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Initial delay before first retry (exponentially increases).</summary>
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Maximum delay between retries.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Backoff multiplier for exponential delay.</summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>Add jitter to prevent thundering herd.</summary>
    public bool EnableJitter { get; init; } = true;

    /// <summary>Number of failures before circuit opens.</summary>
    public int CircuitBreakerThreshold { get; init; } = 5;

    /// <summary>Duration to keep circuit open before testing.</summary>
    public TimeSpan CircuitBreakerDuration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Timeout for individual LLM calls (10 min for large token generation).</summary>
    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Default policy with sensible defaults.</summary>
    public static LLMCallPolicy Default => new();

    /// <summary>No retry policy for testing or known-stable scenarios.</summary>
    public static LLMCallPolicy NoRetry => new() { MaxRetries = 0 };

    /// <summary>Aggressive retry for critical operations.</summary>
    public static LLMCallPolicy Aggressive => new()
    {
        MaxRetries = 5,
        InitialRetryDelay = TimeSpan.FromMilliseconds(500),
        MaxRetryDelay = TimeSpan.FromSeconds(60),
        CircuitBreakerThreshold = 10
    };
}

/// <summary>
/// Circuit breaker states.
/// </summary>
public enum CircuitState
{
    /// <summary>Normal operation - requests pass through.</summary>
    Closed,

    /// <summary>Rejecting all requests - service deemed unhealthy.</summary>
    Open,

    /// <summary>Testing if service recovered - allowing single request.</summary>
    HalfOpen
}

/// <summary>
/// Exception thrown when circuit breaker is open.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public string ProviderName { get; }
    public DateTime OpenUntil { get; }

    public CircuitBreakerOpenException(string providerName, DateTime openUntil)
        : base($"Circuit breaker OPEN for provider '{providerName}' until {openUntil:HH:mm:ss}")
    {
        ProviderName = providerName;
        OpenUntil = openUntil;
    }
}

/// <summary>
/// Exception thrown when LLM call fails after all retries.
/// </summary>
public sealed class LLMCallException : Exception
{
    public string ProviderName { get; }
    public int AttemptsUsed { get; }
    public TimeSpan TotalDuration { get; }
    public bool WasCircuitBroken { get; }

    public LLMCallException(
        string message,
        string providerName,
        int attemptsUsed,
        TimeSpan totalDuration,
        bool wasCircuitBroken,
        Exception? innerException)
        : base(message, innerException)
    {
        ProviderName = providerName;
        AttemptsUsed = attemptsUsed;
        TotalDuration = totalDuration;
        WasCircuitBroken = wasCircuitBroken;
    }
}

/// <summary>
/// Health status for a provider.
/// </summary>
public sealed record ProviderHealthStatus
{
    public required string ProviderName { get; init; }
    public CircuitState State { get; init; }
    public int FailureCount { get; init; }
    public DateTime? LastSuccess { get; init; }
    public DateTime? LastFailure { get; init; }
    public DateTime OpenUntil { get; init; }
}

