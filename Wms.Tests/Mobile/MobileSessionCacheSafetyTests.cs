using Wms.Mobile.Services;

namespace Wms.Tests.Mobile;

public sealed class MobileSessionCacheSafetyTests
{
    [Fact]
    public async Task Concurrent_restoration_shares_one_read_and_successful_refresh_replaces_cached_tokens()
    {
        var original = Session("original");
        var restored = new TaskCompletionSource<MobileSession?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new ControlledSessionStore(original) { PendingRead = restored };
        var cache = new CachedMobileSessionStore(storage);
        var reads = Enumerable.Range(0, 5).Select(_ => cache.GetAsync()).ToArray();
        Assert.Equal(1, storage.ReadCount);
        restored.SetResult(original);
        Assert.All(await Task.WhenAll(reads).WaitAsync(TimeSpan.FromSeconds(10)),
            session => Assert.Equal(original, session));

        var refreshed = Session("refreshed");
        Assert.True(await cache.TryRefreshAsync(original, refreshed));
        Assert.Equal(refreshed, await cache.GetAsync());
        Assert.Equal(refreshed, storage.Session);
        Assert.Equal(1, storage.ReadCount);
    }

    [Fact]
    public async Task Clear_during_restoration_cannot_restore_old_credentials()
    {
        var original = Session("original");
        var restored = new TaskCompletionSource<MobileSession?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new ControlledSessionStore(original) { PendingRead = restored };
        var cache = new CachedMobileSessionStore(storage);
        var pendingRead = cache.GetAsync();

        cache.Clear();
        Assert.Null(await cache.GetAsync());
        restored.SetResult(original);
        Assert.Null(await pendingRead.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(await cache.GetAsync());
        Assert.Null(storage.Session);
    }

    [Fact]
    public async Task Clear_during_save_removes_late_credentials_and_supersedes_queued_saves()
    {
        var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new ControlledSessionStore(Session("original"))
        {
            PendingWrite = written
        };
        var cache = new CachedMobileSessionStore(storage);
        var saving = cache.SaveAsync(Session("refreshed"));
        var queued = cache.SaveAsync(Session("queued"));

        cache.Clear();
        Assert.Null(await cache.GetAsync());
        written.SetResult();
        await saving.WaitAsync(TimeSpan.FromSeconds(10));
        await queued.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(await cache.GetAsync());
        Assert.Null(storage.Session);

        storage.PendingWrite = null;
        var nextLogin = Session("next-login");
        await cache.SaveAsync(nextLogin);
        Assert.Equal(nextLogin, await cache.GetAsync());
        Assert.Equal(nextLogin, storage.Session);
    }

    [Fact]
    public async Task Responses_for_a_logged_out_session_cannot_restore_it_or_clear_a_new_login()
    {
        var oldSession = Session("old");
        var storage = new ControlledSessionStore(oldSession);
        var cache = new CachedMobileSessionStore(storage);
        Assert.Same(oldSession, await cache.GetAsync());

        cache.Clear();
        var lateRefresh = Session("late-refresh");
        Assert.False(await cache.TryRefreshAsync(oldSession, lateRefresh));
        Assert.Null(await cache.GetAsync());
        Assert.Null(storage.Session);

        var newSession = Session("new-login");
        await cache.SaveAsync(newSession);
        Assert.False(await cache.TryRefreshAsync(oldSession, lateRefresh));
        cache.ClearIfCurrent(oldSession);
        Assert.Same(newSession, await cache.GetAsync());
        Assert.Same(newSession, storage.Session);
    }

    private static MobileSession Session(string token) =>
        new("Bearer", token, $"refresh-{token}", DateTimeOffset.UtcNow.AddHours(1));

    private sealed class ControlledSessionStore(MobileSession? session) : IMobileSessionStore
    {
        public MobileSession? Session { get; private set; } = session;
        public TaskCompletionSource<MobileSession?>? PendingRead { get; init; }
        public TaskCompletionSource? PendingWrite { get; set; }
        public int ReadCount { get; private set; }

        public Task<MobileSession?> GetAsync()
        {
            ReadCount++;
            return PendingRead?.Task ?? Task.FromResult(Session);
        }

        public async Task SaveAsync(MobileSession value)
        {
            if (PendingWrite is { } pending) await pending.Task;
            Session = value;
        }

        public void Clear() => Session = null;
    }
}
