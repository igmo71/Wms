using System.ComponentModel;
using System.Diagnostics;
using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

internal sealed class MobilePerformanceDiagnostics : INotifyPropertyChanged
{
    private static readonly AsyncLocal<Measurement?> Current = new();
    private static System.Action<System.Action> _dispatch = static update => update();
    private long _loadVersion;
    private long _actionVersion;
    private string _loadRequestsText = string.Empty;
    private string _actionRequestsText = string.Empty;

    public static MobilePerformanceDiagnostics Instance { get; } = new();
    public static bool Enabled { get; set; }
    public static HttpRequestOptionsKey<TimeSpan> SessionDurationKey { get; } =
        new("Wms.Mobile.SessionDuration");
    public static HttpRequestOptionsKey<bool> SessionRefreshKey { get; } =
        new("Wms.Mobile.SessionRefresh");
    public event PropertyChangedEventHandler? PropertyChanged;
    public string LoadText { get; private set; } = $"Диагностика · {BuildMode}";
    public string ActionText { get; private set; } = string.Empty;
    public bool HasAction => ActionText.Length > 0;
    public bool ShowRequests { get; private set; }
    public string RequestsToggleText => ShowRequests ? "Скрыть HTTP-запросы" : "Показать HTTP-запросы";
    public string RequestsText => string.Join("\n\n",
        new[] { _loadRequestsText, _actionRequestsText }.Where(x => x.Length > 0));

    public static void SetDispatcher(System.Action<System.Action> dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        _dispatch = dispatch;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void ToggleRequests()
    {
        ShowRequests = !ShowRequests;
        OnPropertyChanged(nameof(ShowRequests));
        OnPropertyChanged(nameof(RequestsToggleText));
    }

    private static string BuildMode
    {
        get
        {
#if DEBUG
            const string configuration = "Debug";
#else
            const string configuration = "Release";
#endif
            return Debugger.IsAttached ? $"{configuration} · отладчик" : configuration;
        }
    }

    public static IDisposable? Load(string name) => Enabled ? new Measurement(name, isAction: false) : null;
    public static IDisposable? Action(string name) => Enabled ? new Measurement(name, isAction: true) : null;
    public static Measurement? CaptureHttpOperation() => Enabled ? Current.Value : null;

    private void Publish(bool isAction, long version, string text, string requests)
    {
        void Update()
        {
            if (version != (isAction
                ? Interlocked.Read(ref _actionVersion)
                : Interlocked.Read(ref _loadVersion))) return;
            if (isAction)
            {
                ActionText = text;
                _actionRequestsText = requests;
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(HasAction));
            }
            else
            {
                LoadText = text;
                _loadRequestsText = requests;
                OnPropertyChanged(nameof(LoadText));
            }
            OnPropertyChanged(nameof(RequestsText));
        }

        _dispatch(Update);
    }

    internal sealed class Measurement : IDisposable
    {
        private readonly string _name;
        private readonly bool _isAction;
        private readonly Measurement? _parent;
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly long _version;
        private readonly List<HttpMeasurement> _requests = [];
        private int _pending = 1;
        private int _disposed;

        public Measurement(string name, bool isAction)
        {
            _name = name;
            _isAction = isAction;
            var parent = Current.Value;
            _parent = parent?.TryAddChild() == true ? parent : null;
            Current.Value = this;

            // A nested load belongs to the outer load (e.g. opening an assembly).
            // Loads within an action also have their own visible load measurement.
            if (HasAncestorOfSameKind()) return;
            _version = isAction
                ? Interlocked.Increment(ref Instance._actionVersion)
                : Interlocked.Increment(ref Instance._loadVersion);
            Instance.Publish(isAction, _version, $"{name}: … · {BuildMode}",
                $"{name}:\nЗапросы выполняются…");
        }

        private bool HasAncestorOfSameKind()
        {
            for (var ancestor = _parent; ancestor is not null; ancestor = ancestor._parent)
                if (ancestor._isAction == _isAction) return true;
            return false;
        }

        private bool TryAddChild()
        {
            var pending = Volatile.Read(ref _pending);
            while (pending > 0)
            {
                var actual = Interlocked.CompareExchange(ref _pending, pending + 1, pending);
                if (actual == pending) return true;
                pending = actual;
            }
            return false;
        }

        public void RecordHttp(string method, string route, long started,
            TimeSpan elapsed, int? statusCode, bool failed,
            TimeSpan? sessionDuration, TimeSpan? serverDuration, bool sessionRefreshed)
        {
            var request = new HttpMeasurement(started, DescribeRequest(method, route),
                elapsed, statusCode, failed, sessionDuration, serverDuration, sessionRefreshed);
            for (Measurement? operation = this; operation is not null; operation = operation._parent)
            {
                lock (operation._requests) operation._requests.Add(request);
            }
        }

        private static string DescribeRequest(string method, string route)
        {
            var path = route.Split(new[] { '?', '#' }, 2)[0];
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri)) path = uri.AbsolutePath;
            if (path.StartsWith(MobileApiRoutes.Base + "/", StringComparison.Ordinal))
                path = path[MobileApiRoutes.Base.Length..];
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => Guid.TryParse(segment, out _) ? "{id}" : segment);
            return $"{method} /{string.Join('/', segments)}";
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Current.Value = _parent;
            CompletePart();
        }

        private void CompletePart()
        {
            // OnAppearing is async void: navigation can return before its load ends.
            // Keep the enclosing action open until registered child loads finish.
            if (Interlocked.Decrement(ref _pending) != 0) return;
            if (_version != 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(_started);
                HttpMeasurement[] requests;
                lock (_requests) requests = _requests.OrderBy(x => x.Started).ToArray();
                var http = TimeSpan.FromTicks(requests.Sum(x => x.Elapsed.Ticks));
                var failureCount = requests.Count(x => x.Failed);
                var failures = failureCount == 0 ? string.Empty : $" · ошибок HTTP: {failureCount}";
                var details = requests.Length == 0 ? "Запросов API нет."
                    : string.Join("\n", requests.Select((request, index) =>
                        $"{index + 1}. {request.Name}: {request.Elapsed.TotalSeconds:0.00} с · "
                        + (request.StatusCode is int code ? $"HTTP {code}" : "нет ответа")
                        + $"\n   Сессия ТСД: {FormatDuration(request.SessionDuration)}"
                        + (request.SessionRefreshed ? " (обновление токена)" : string.Empty)
                        + $" · API: {FormatDuration(request.ServerDuration)}"));
                Instance.Publish(_isAction, _version,
                    $"{_name}: {elapsed.TotalSeconds:0.00} с · HTTP {requests.Length} / Σ {http.TotalSeconds:0.00} с{failures} · {BuildMode}",
                    $"{_name}:\n{details}");
            }
            _parent?.CompletePart();
        }

        private static string FormatDuration(TimeSpan? duration) =>
            duration is { } elapsed ? $"{elapsed.TotalSeconds:0.00} с" : "—";

        private sealed record HttpMeasurement(long Started, string Name,
            TimeSpan Elapsed, int? StatusCode, bool Failed,
            TimeSpan? SessionDuration, TimeSpan? ServerDuration, bool SessionRefreshed);
    }
}
