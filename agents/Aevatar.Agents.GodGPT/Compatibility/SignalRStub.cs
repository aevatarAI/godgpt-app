// Stub for SignalR types that are referenced but not migrated

namespace Aevatar.SignalR
{
    /// <summary>
    /// Stub interface for SignalR client manager
    /// </summary>
    public interface ISignalRClientManager
    {
        Task SendToUserAsync(string userId, string method, object data);
        Task SendToGroupAsync(string groupId, string method, object data);
    }
}

