using Aevatar.Agents.AI.Core;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.Agents;

// ============================================================
//  MAKER Provider Validator
//  Handles LLM provider discovery, validation, and selection
//  Part of MakerCoordinatorGAgent (partial class)
// ============================================================

public partial class MakerCoordinatorGAgent
{
    // ============================================================
    //  Provider Discovery and Validation
    // ============================================================

    /// <summary>
    /// Discover and validate LLM providers based on configuration.
    /// 
    /// Logic:
    /// 1. If UseMultipleProviders = true, auto-discover all configured providers
    /// 2. Validate each provider with a simple test request
    /// 3. Only use providers that pass validation
    /// 4. Determine Coordinator's provider (dedicated or round-robin)
    /// </summary>
    /// <returns>
    /// Tuple of (validProviders for workers, coordinatorProvider)
    /// </returns>
    private async Task<(List<string> ValidProviders, string CoordinatorProvider)> DiscoverAndValidateProvidersAsync(
        bool useMultipleProviders,
        string? coordinatorProviderName,
        string defaultProviderName,
        string executionId)
    {
        // Step 1: Discover all candidate providers
        List<string> candidateProviders;
        var availableProviders = RequireLLMProviderFactory().GetAvailableProviderNames().ToList();

        if (useMultipleProviders)
        {
            // Auto-discover all configured providers
            candidateProviders = availableProviders;

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = executionId,
                Message =
                    $"Multi-provider mode: Discovered {candidateProviders.Count} configured provider(s): {string.Join(", ", candidateProviders)}",
                Depth = 0
            });
        }
        else
        {
            // Single provider mode - auto-fallback if specified provider doesn't exist
            if (availableProviders.Contains(defaultProviderName))
            {
                candidateProviders = [defaultProviderName];
            }
            else if (availableProviders.Count > 0)
            {
                // Fallback: use first available provider instead of non-existent "default"
                var fallbackProvider = availableProviders[0];
                Logger.LogInformation(
                    "Provider '{DefaultProvider}' not found, using '{FallbackProvider}' instead. Available: {Available}",
                    defaultProviderName, fallbackProvider, string.Join(", ", availableProviders));
                
                ReportProgress(new MakerProgress
                {
                    Phase = MakerPhase.Starting,
                    TaskId = executionId,
                    Message = $"Provider '{defaultProviderName}' not configured, auto-selecting '{fallbackProvider}'",
                    Depth = 0
                });
                
                candidateProviders = [fallbackProvider];
            }
            else
            {
                // No providers available at all
                candidateProviders = [defaultProviderName]; // Will fail in validation with clear error
            }
        }

        // Add coordinator provider to candidates if specified and not already included
        if (!string.IsNullOrEmpty(coordinatorProviderName) && !candidateProviders.Contains(coordinatorProviderName))
        {
            candidateProviders.Add(coordinatorProviderName);
        }

        // Step 2: Validate all providers
        var validProviders = new List<string>();
        var failedProviders = new List<(string Provider, string Error)>();

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = executionId,
            Message = $"Validating {candidateProviders.Count} LLM provider(s)...",
            Depth = 0
        });

        foreach (var providerName in candidateProviders)
        {
            var (isValid, error) = await ValidateSingleProviderAsync(providerName, executionId);
            if (isValid)
            {
                validProviders.Add(providerName);
            }
            else
            {
                failedProviders.Add((providerName, error ?? "Unknown error"));
            }
        }

        // Step 3: Check if we have any valid providers
        if (validProviders.Count == 0)
        {
            var errorMessage = $"No valid LLM providers found. Checked: {string.Join(", ", candidateProviders)}";
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Failed,
                TaskId = executionId,
                Message = errorMessage,
                Depth = 0
            });
            throw new InvalidOperationException(errorMessage);
        }

        // Step 4: Determine Coordinator's provider
        string coordinatorProvider;
        if (!string.IsNullOrEmpty(coordinatorProviderName) && validProviders.Contains(coordinatorProviderName))
        {
            // Use dedicated Coordinator provider
            coordinatorProvider = coordinatorProviderName;
            Logger.LogInformation("Coordinator using dedicated provider: {Provider}", coordinatorProvider);

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = executionId,
                Message = $"Coordinator using dedicated provider: {coordinatorProvider}",
                Depth = 0
            });
        }
        else
        {
            // Coordinator participates in round-robin (use first valid provider)
            coordinatorProvider = validProviders[0];
            Logger.LogInformation("Coordinator participating in round-robin, using: {Provider}", coordinatorProvider);
        }

        // Summary with detailed error reasons
        if (failedProviders.Count > 0)
        {
            var failedDetails = string.Join(", ", failedProviders.Select(f => $"{f.Provider}({f.Error})"));
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = executionId,
                Message = $"⚠️ {failedProviders.Count} provider(s) failed: {failedDetails}",
                Depth = 0
            });
        }

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = executionId,
            Message =
                $"✓ {validProviders.Count} valid provider(s) for workers: {string.Join(", ", validProviders)} | Coordinator: {coordinatorProvider}",
            Depth = 0
        });

        return (validProviders, coordinatorProvider);
    }

    /// <summary>
    /// Validate a single LLM provider with a test request.
    /// Returns (IsValid, ErrorReason) tuple.
    /// </summary>
    private async Task<(bool IsValid, string? Error)> ValidateSingleProviderAsync(
        string providerName,
        string executionId)
    {
        try
        {
            Logger.LogDebug("Validating LLM provider: {Provider}", providerName);

            var provider = await RequireLLMProviderFactory().GetProviderAsync(providerName);

            var testRequest = new Aevatar.Agents.AI.Abstractions.AevatarLLMRequest
            {
                SystemPrompt = "You are a test assistant.",
                Settings = new Aevatar.Agents.AI.Abstractions.AevatarLLMSettings
                {
                    Temperature = 0.1f,
                    MaxTokens = 10
                },
                Messages =
                [
                    new AI.AevatarChatMessage
                    {
                        Role = AI.AevatarChatRole.User,
                        Content = "Reply with 'OK'"
                    }
                ]
            };

            var response = await provider.GenerateAsync(testRequest);

            if (string.IsNullOrWhiteSpace(response.Content))
            {
                // Log detailed response info for debugging
                var debugInfo = $"Content='{response.Content ?? "null"}', " +
                                $"StopReason={response.AevatarStopReason}, " +
                                $"PromptTokens={response.Usage?.PromptTokens}, " +
                                $"CompletionTokens={response.Usage?.CompletionTokens}";
                Logger.LogWarning("Provider {Provider} returned empty response. Details: {Debug}", providerName,
                    debugInfo);

                // Provide actionable error message based on stop reason
                var error = response.AevatarStopReason switch
                {
                    AI.Abstractions.AevatarStopReason.ContentFilter => "Content filtered by safety policy",
                    AI.Abstractions.AevatarStopReason.MaxTokens => "Response truncated (max tokens too low)",
                    AI.Abstractions.AevatarStopReason.Complete => "Empty response (model returned nothing)",
                    AI.Abstractions.AevatarStopReason.Error => "API returned error",
                    AI.Abstractions.AevatarStopReason.Timeout => "Request timeout",
                    AI.Abstractions.AevatarStopReason.RateLimitReached => "Rate limited",
                    _ => $"Empty response (stop_reason: {response.AevatarStopReason})"
                };

                ReportProgress(new MakerProgress
                {
                    Phase = MakerPhase.Starting,
                    TaskId = executionId,
                    Message = $"✗ Provider '{providerName}': {error}",
                    Depth = 0
                });
                return (false, error);
            }

            Logger.LogInformation("Provider {Provider} validated successfully", providerName);
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = executionId,
                Message = $"✓ Provider '{providerName}' is available",
                Depth = 0
            });
            return (true, null);
        }
        catch (Exception ex)
        {
            // Extract concise error message
            var error = ExtractConciseError(ex);
            Logger.LogWarning(ex, "Provider {Provider} validation failed: {Message}", providerName, error);
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = executionId,
                Message = $"✗ Provider '{providerName}': {error}",
                Depth = 0
            });
            return (false, error);
        }
    }

    /// <summary>
    /// [Legacy] Validate all LLM providers - throws on failure.
    /// Kept for backward compatibility.
    /// </summary>
    private async Task ValidateProvidersAsync(List<string> providerNames, string executionId)
    {
        var uniqueProviders = providerNames.Distinct().ToList();

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = executionId,
            Message = $"Validating {uniqueProviders.Count} LLM provider(s): {string.Join(", ", uniqueProviders)}",
            Depth = 0
        });

        var failedProviders = new List<(string Provider, string Error)>();

        foreach (var providerName in uniqueProviders)
        {
            var (isValid, error) = await ValidateSingleProviderAsync(providerName, executionId);
            if (!isValid)
            {
                failedProviders.Add((providerName, error ?? "Unknown error"));
            }
        }

        if (failedProviders.Count > 0)
        {
            var errorDetails = string.Join("\n", failedProviders.Select(f => $"  - {f.Provider}: {f.Error}"));
            var errorMessage = $"LLM provider validation failed:\n{errorDetails}";

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Failed,
                TaskId = executionId,
                Message = errorMessage,
                Depth = 0
            });

            throw new InvalidOperationException(errorMessage);
        }

        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Starting,
            TaskId = executionId,
            Message = $"All {uniqueProviders.Count} LLM provider(s) validated successfully",
            Depth = 0
        });
    }

    /// <summary>
    /// Extract a concise, user-friendly error message from exception.
    /// </summary>
    private static string ExtractConciseError(Exception ex)
    {
        var msg = ex.Message;

        // Common API error patterns
        if (msg.Contains("401") || msg.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
            return "Invalid API key";
        if (msg.Contains("403") || msg.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
            return "Access denied (check API key permissions)";
        if (msg.Contains("404") || msg.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
            return "Endpoint not found (check base URL)";
        if (msg.Contains("429") || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            return "Rate limited";
        if (msg.Contains("500") || msg.Contains("Internal Server Error", StringComparison.OrdinalIgnoreCase))
            return "Provider server error";
        if (msg.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "Connection timeout";
        if (msg.Contains("connection", StringComparison.OrdinalIgnoreCase) &&
            msg.Contains("refused", StringComparison.OrdinalIgnoreCase))
            return "Connection refused";

        // Truncate if too long
        return msg.Length > 80 ? msg[..77] + "..." : msg;
    }
}

