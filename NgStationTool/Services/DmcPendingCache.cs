using System.Collections.Concurrent;

namespace NgStationTool.Services;

public sealed class PendingItem
{
    public string Dmc { get; init; } = "";
    public DateTime EnqueuedAt { get; init; } = DateTime.Now;
    public string Source { get; init; } = "";
    public string? SourcePath { get; init; }
}

/// <summary>NG DMC 待确认池：超时删除；无票 log 不执行。</summary>
public sealed class DmcPendingCache
{
    private readonly ConcurrentDictionary<string, PendingItem> _map =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly AppLogger _log;
    private int _timeoutSec;

    public event Action? Changed;

    public DmcPendingCache(AppLogger log, int timeoutSec)
    {
        _log = log;
        _timeoutSec = Math.Max(1, timeoutSec);
    }

    public void SetTimeoutSec(int sec) => _timeoutSec = Math.Max(1, sec);

    public int Count => _map.Count;

    public bool TryEnqueue(string dmc, string source, string? sourcePath = null)
    {
        dmc = (dmc ?? "").Trim();
        if (string.IsNullOrEmpty(dmc)) return false;

        if (_map.ContainsKey(dmc))
        {
            _log.Skip("缓存", $"DMC 已在待确认池，忽略重复入队: {dmc}");
            return false;
        }

        var item = new PendingItem
        {
            Dmc = dmc,
            EnqueuedAt = DateTime.Now,
            Source = source,
            SourcePath = sourcePath
        };
        if (_map.TryAdd(dmc, item))
        {
            _log.Info("缓存", $"入队 DMC={dmc} 来源={source} 超时={_timeoutSec}s");
            Changed?.Invoke();
            return true;
        }
        return false;
    }

    public bool Contains(string dmc) => _map.ContainsKey((dmc ?? "").Trim());

    public bool TryRemove(string dmc, out PendingItem? item)
    {
        item = null;
        dmc = (dmc ?? "").Trim();
        if (_map.TryRemove(dmc, out var x))
        {
            item = x;
            Changed?.Invoke();
            return true;
        }
        return false;
    }

    public void ForceRemove(string dmc, string reason)
    {
        if (TryRemove(dmc, out _))
            _log.Warn("缓存", $"移除 DMC={dmc} 原因={reason}");
    }

    public List<PendingItem> Snapshot()
        => _map.Values.OrderBy(v => v.EnqueuedAt).ToList();

    /// <summary>清超时项；不按键。返回被清除的 DMC 列表，供归档匹配 log。</summary>
    public List<string> PurgeExpired()
    {
        var now = DateTime.Now;
        var removed = new List<string>();
        foreach (var kv in _map)
        {
            var age = (now - kv.Value.EnqueuedAt).TotalSeconds;
            if (age >= _timeoutSec)
            {
                if (_map.TryRemove(kv.Key, out _))
                {
                    removed.Add(kv.Key);
                    _log.Warn("缓存", $"超时清除 DMC={kv.Key} 已等待={age:F0}s（不按键，将尝试归档相关 log）");
                }
            }
        }
        if (removed.Count > 0) Changed?.Invoke();
        return removed;
    }

    public void ClearAll(string reason)
    {
        _map.Clear();
        _log.Warn("缓存", "清空全部: " + reason);
        Changed?.Invoke();
    }
}
