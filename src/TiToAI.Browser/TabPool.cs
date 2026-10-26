using System.Collections.Concurrent;
using TiToAI.Browser.Abstractions;
using TiToAI.Browser.Models;

namespace TiToAI.Browser;

/// <summary>
/// Quản lý Tab Pool cho profile trình duyệt.
/// Với Shared AI: Giới hạn tối đa 3 role tabs ("Script", "Image", "VideoPrompt").
/// Mỗi tab tách biệt hoàn toàn ngữ cảnh hội thoại và tối đa 1 active job/tab.
/// </summary>
public sealed class TabPool : ITabPool
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, BrowserTabHandle>> _profileTabs = new();

    public Task<IBrowserTabHandle> AcquireTabAsync(
        string profileId,
        string role,
        string jobId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        // Quy tắc Shared AI: chỉ cho phép 3 role tab hợp lệ
        if (string.Equals(profileId, "shared_ai", StringComparison.OrdinalIgnoreCase))
        {
            if (!SharedAiTabRole.IsValid(role))
            {
                throw new ArgumentException(
                    $"Role '{role}' không hợp lệ cho Shared AI. Chỉ chấp nhận: {string.Join(", ", SharedAiTabRole.AllRoles)}.",
                    nameof(role));
            }
        }

        var tabs = _profileTabs.GetOrAdd(profileId, _ => new ConcurrentDictionary<string, BrowserTabHandle>());

        var newHandle = new BrowserTabHandle(this, profileId, role, jobId);
        if (!tabs.TryAdd(role, newHandle))
        {
            var existing = tabs[role];
            throw new InvalidOperationException(
                $"Role tab '{role}' của profile '{profileId}' đang được sử dụng bởi job '{existing.JobId}'.");
        }

        return Task.FromResult<IBrowserTabHandle>(newHandle);
    }

    public IReadOnlyList<string> GetActiveTabs(string profileId)
    {
        if (_profileTabs.TryGetValue(profileId, out var tabs))
        {
            return tabs.Keys.ToList();
        }
        return [];
    }

    public int GetActiveJobCount(string profileId)
    {
        if (_profileTabs.TryGetValue(profileId, out var tabs))
        {
            return tabs.Count;
        }
        return 0;
    }

    public bool IsRoleActive(string profileId, string role)
    {
        if (_profileTabs.TryGetValue(profileId, out var tabs))
        {
            return tabs.ContainsKey(role);
        }
        return false;
    }

    public IReadOnlyList<IBrowserTabHandle> GetAllActiveTabs(string profileId)
    {
        if (_profileTabs.TryGetValue(profileId, out var tabs))
        {
            return tabs.Values.Cast<IBrowserTabHandle>().ToList();
        }
        return [];
    }

    public Task ReleaseTabAsync(string profileId, string role, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (_profileTabs.TryGetValue(profileId, out var tabs))
        {
            tabs.TryRemove(role, out _);
        }

        return Task.CompletedTask;
    }

    public Task ReleaseTabAsync(string profileId, string role, string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        if (_profileTabs.TryGetValue(profileId, out var tabs))
        {
            if (tabs.TryGetValue(role, out var handle) && string.Equals(handle.JobId, jobId, StringComparison.Ordinal))
            {
                tabs.TryRemove(new KeyValuePair<string, BrowserTabHandle>(role, handle));
            }
        }

        return Task.CompletedTask;
    }

    public Task ResetTabContextAsync(string profileId, string role, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        // Xóa context nghiệp vụ trong tab pool
        if (_profileTabs.TryGetValue(profileId, out var tabs))
        {
            if (tabs.TryGetValue(role, out var handle))
            {
                handle.IsSideEffectSubmitted = false;
            }
        }

        return Task.CompletedTask;
    }

    private sealed class BrowserTabHandle : IBrowserTabHandle
    {
        private readonly TabPool _pool;
        private int _isDisposed;

        public string ProfileId { get; }
        public string Role { get; }
        public string JobId { get; }
        public bool IsSideEffectSubmitted { get; set; }
        public DateTime AcquiredAtUtc { get; }

        public BrowserTabHandle(TabPool pool, string profileId, string role, string jobId)
        {
            _pool = pool;
            ProfileId = profileId;
            Role = role;
            JobId = jobId;
            AcquiredAtUtc = DateTime.UtcNow;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                await _pool.ReleaseTabAsync(ProfileId, Role, JobId);
            }
        }
    }
}
