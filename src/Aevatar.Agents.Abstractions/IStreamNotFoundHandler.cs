using System;
using System.Threading.Tasks;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Stream Not Found Handler Interface
/// Used to handle cases where a message stream is not found (e.g., to activate the target Actor)
/// </summary>
public interface IStreamNotFoundHandler
{
    /// <summary>
    /// Handle stream not found event
    /// </summary>
    /// <param name="streamId">Stream ID (Agent ID, format varies by runtime)</param>
    Task HandleStreamNotFoundAsync(string streamId);
}

