namespace Wms.Mobile.Services;

internal sealed class CachedMobileSessionStore(IMobileSessionStore persistentStore) : IMobileSessionStore
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _storageGate = new(1, 1);
    private MobileSession? _session;
    private bool _loaded;
    private long _clearRevision;

    public Task<MobileSession?> GetAsync()
    {
        lock (_sync)
        {
            if (_loaded) return Task.FromResult(_session);
        }
        return LoadAsync();
    }

    private async Task<MobileSession?> LoadAsync()
    {
        await _storageGate.WaitAsync().ConfigureAwait(false);
        try
        {
            long revision;
            lock (_sync)
            {
                if (_loaded) return _session;
                revision = _clearRevision;
            }
            var session = await persistentStore.GetAsync().ConfigureAwait(false);
            lock (_sync)
            {
                if (revision == _clearRevision)
                {
                    _session = session;
                    _loaded = true;
                }
                return _session;
            }
        }
        finally { _storageGate.Release(); }
    }

    public Task SaveAsync(MobileSession session) => SaveCoreAsync(session, expected: null);

    public Task<bool> TryRefreshAsync(MobileSession expected, MobileSession session) =>
        SaveCoreAsync(session, expected);

    private async Task<bool> SaveCoreAsync(MobileSession session, MobileSession? expected)
    {
        long clearRevision;
        lock (_sync) clearRevision = _clearRevision;
        await _storageGate.WaitAsync().ConfigureAwait(false);
        var writeStarted = false;
        try
        {
            lock (_sync)
            {
                if (clearRevision != _clearRevision
                    || expected is not null && !ReferenceEquals(_session, expected)) return false;
            }
            writeStarted = true;
            await persistentStore.SaveAsync(session).ConfigureAwait(false);
            lock (_sync)
            {
                if (clearRevision != _clearRevision) return false;
                _session = session;
                _loaded = true;
            }
            return true;
        }
        finally
        {
            try
            {
                // Clear is synchronous; a write already in progress can finish after it.
                // Repair persistence before allowing the next queued storage operation.
                lock (_sync)
                {
                    if (writeStarted && clearRevision != _clearRevision) persistentStore.Clear();
                }
            }
            finally { _storageGate.Release(); }
        }
    }

    public void Clear()
    {
        lock (_sync) ClearCore();
    }

    public void ClearIfCurrent(MobileSession expected)
    {
        lock (_sync)
            if (ReferenceEquals(_session, expected)) ClearCore();
    }

    private void ClearCore()
    {
        _clearRevision++;
        _session = null;
        _loaded = true;
        persistentStore.Clear();
    }
}
