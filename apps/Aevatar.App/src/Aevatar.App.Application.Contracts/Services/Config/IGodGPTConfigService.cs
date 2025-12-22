using System.Threading.Tasks;
using Aevatar.Quantum;

namespace Aevatar.App.Application.Contracts.Services.Config;

/// <summary>
/// Service interface for managing GodGPT system configuration.
/// </summary>
public interface IGodGPTConfigService
{
    /// <summary>
    /// Gets the current system prompt configuration.
    /// </summary>
    /// <returns>The system prompt string</returns>
    Task<string> GetSystemPromptAsync();

    /// <summary>
    /// Updates the system prompt configuration.
    /// </summary>
    /// <param name="godGptConfigurationDto">The configuration DTO containing the new system prompt</param>
    Task UpdateSystemPromptAsync(GodGPTConfigurationDto godGptConfigurationDto);
}
