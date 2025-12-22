using System.Collections.Concurrent;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local 运行时的 Stream 注册表
/// 管理所有 Agent 的 Message Stream
/// </summary>
public class LocalMessageStreamRegistry
{
    private readonly ConcurrentDictionary<string, LocalMessageStream> _streams = new();

    /// <summary>
    /// 获取或创建 Agent 的 Stream
    /// </summary>
    public LocalMessageStream GetOrCreateStream(string agentId, int capacity = 1000)
    {
        return _streams.GetOrAdd(agentId, _ => new LocalMessageStream(agentId, capacity));
    }

    /// <summary>
    /// 检查 Stream 是否已存在
    /// </summary>
    public bool StreamExists(string agentId)
    {
        return _streams.ContainsKey(agentId);
    }

    /// <summary>
    /// 移除 Agent 的 Stream
    /// </summary>
    public void RemoveStream(string agentId)
    {
        if (_streams.TryRemove(agentId, out var stream))
        {
            stream.Stop();
        }
    }

    /// <summary>
    /// 获取 Stream（如果存在）
    /// </summary>
    public LocalMessageStream? GetStream(string agentId)
    {
        _streams.TryGetValue(agentId, out var stream);
        return stream;
    }
}