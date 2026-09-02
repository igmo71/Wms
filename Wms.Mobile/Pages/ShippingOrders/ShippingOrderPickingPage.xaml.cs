using System.Collections.ObjectModel;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ShippingOrderPickingPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private MobileShippingOrderDetailsResponse? _details;
    private PickingPageMode _mode = PickingPageMode.Ready;
    private MobileStorageLocationResponse? _scannedLocation;
    private string? _scannedLocationBarcode;
    private Guid? _pendingStartRequestId;
    private Guid? _pendingDeleteRequestId;
    private Guid? _pendingDeleteMovementId;
    private Guid? _pendingCompletionRequestId;
    private int? _accentedLineNumber;
    private Guid? _accentedMovementId;
    private bool _deviationConfirmed;
    private int _searchVersion;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;

    public ShippingOrderPickingPage(
        MobileApiClient apiClient,
        IOperationalBarcodeScanner scanner,
        IServiceProvider services)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
        _services = services;
        CameraScannerView.Configure(scanner);
    }

    public ObservableCollection<ShippingOrderPickingLineViewState> LineStates { get; } = [];
    public IReadOnlyList<MobileShippingOrderLineCandidateResponse> ScanCandidates { get; private set; } = [];
    public IReadOnlyList<MobileShippingOrderLineCandidateResponse> SearchCandidates { get; private set; } = [];

    private MobileShippingOrderDetailsResponse Details =>
        _details ?? throw new InvalidOperationException("Расходный ордер не загружен.");

    private bool IsEditable => Details.Order.Status is
        MobileShippingOrderStatus.ReadyForPicking or
        MobileShippingOrderStatus.ReadyForVerification or
        MobileShippingOrderStatus.InVerification or
        MobileShippingOrderStatus.Verified;

    private bool HasPendingCommand => _pendingStartRequestId is not null
        || _pendingDeleteRequestId is not null
        || _pendingCompletionRequestId is not null;

    private bool HasPickingDeviation =>
        Details.Order.Progress.FactQuantity != Details.Order.Progress.PlanQuantity;

    private bool CanStartMovement => IsEditable
        && !_busy
        && !HasPendingCommand
        && _mode == PickingPageMode.Scanning;

    private bool IsScanExpected => !_busy
        && !HasPendingCommand
        && (_mode == PickingPageMode.LocationScanning
            || _mode == PickingPageMode.Scanning);

    public void Show(MobileShippingOrderDetailsResponse details)
    {
        _scannedLocation = null;
        _scannedLocationBarcode = null;
        _pendingStartRequestId = null;
        _pendingDeleteRequestId = null;
        _pendingDeleteMovementId = null;
        _pendingCompletionRequestId = null;
        _accentedLineNumber = null;
        _accentedMovementId = null;
        _deviationConfirmed = false;
        ApplyDetails(details);
        SetMode(details.Order.Status == MobileShippingOrderStatus.Prepared
            ? PickingPageMode.Ready
            : PickingPageMode.Scanning);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isVisible = true;
        if (!_scannerSubscribed)
        {
            _scanner.ScanReceived += OnScanReceived;
            _scannerSubscribed = true;
        }

        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        _searchVersion++;
        LineSearchEntry.Unfocus();
        CameraScannerView.Stop();
        if (_scannerSubscribed)
        {
            _scanner.ScanReceived -= OnScanReceived;
            _scannerSubscribed = false;
        }

        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_busy || HasPendingCommand)
        {
            ErrorLabel.Text = HasPendingCommand
                ? "Сначала повторите незавершённую операцию."
                : "Дождитесь завершения операции.";
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private async void OnStartPickingClicked(object? sender, EventArgs e)
    {
        if (_busy
            || HasPendingCommand
            || Details.Order.Status != MobileShippingOrderStatus.Prepared)
        {
            return;
        }

        ErrorLabel.Text = string.Empty;
        SetMode(PickingPageMode.LocationScanning);
        await UpdateCameraAsync();
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scanEvent) =>
        MainThread.BeginInvokeOnMainThread(async () => await HandleScanAsync(scanEvent.Value));

    private async Task HandleScanAsync(string barcode)
    {
        if (!IsScanExpected)
        {
            return;
        }

        if (_mode == PickingPageMode.LocationScanning)
        {
            await ResolveShippingLocationAsync(barcode);
        }
        else
        {
            await ResolveSkuAsync(barcode);
        }
    }

    private async Task ResolveShippingLocationAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var location = await _apiClient.ResolveStorageLocationAsync(
                barcode,
                Details.Order.WarehouseId,
                MobileStorageLocationContext.Shipping);
            _scannedLocation = location;
            _scannedLocationBarcode = barcode;
            ScannedLocationLabel.Text = $"{location.Address} · {location.Name}";
            SetMode(PickingPageMode.LocationConfirmation);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование позиции.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnConfirmLocationClicked(object? sender, EventArgs e)
    {
        if (_busy || _scannedLocation is null || _scannedLocationBarcode is null)
        {
            return;
        }

        _pendingStartRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.StartShippingOrderPickingAsync(
                Details.Order.Id,
                _scannedLocationBarcode,
                _pendingStartRequestId.Value);
            _pendingStartRequestId = null;
            _scannedLocation = null;
            _scannedLocationBarcode = null;
            ConfirmLocationButton.Text = "Начать";
            ApplyDetails(response.Details);
            SetMode(PickingPageMode.Scanning);
        }
        catch (MobileApiException exception)
        {
            _pendingStartRequestId = null;
            ConfirmLocationButton.Text = "Начать";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ConfirmLocationButton.Text = "Повторить начало";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите начало с той же позицией.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnCancelLocationClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_pendingStartRequestId is not null)
        {
            ErrorLabel.Text = "Сначала повторите начало отбора с той же позицией.";
            return;
        }

        _scannedLocation = null;
        _scannedLocationBarcode = null;
        ConfirmLocationButton.Text = "Начать";
        SetMode(PickingPageMode.LocationScanning);
        await UpdateCameraAsync();
    }

    private async Task ResolveSkuAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var candidates = await _apiClient.ResolveShippingOrderSkuAsync(
                Details.Order.Id,
                barcode);
            if (candidates.Count == 1)
            {
                await OpenMovementPageAsync(candidates[0].LineNumber);
                return;
            }

            ScanCandidates = candidates;
            OnPropertyChanged(nameof(ScanCandidates));
            SetMode(PickingPageMode.CandidateSelection);
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен. Повторите сканирование товара.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnScanCandidateTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || e.Parameter is not MobileShippingOrderLineCandidateResponse candidate)
        {
            return;
        }

        ScanCandidates = [];
        OnPropertyChanged(nameof(ScanCandidates));
        SetMode(PickingPageMode.Scanning);
        await OpenMovementPageAsync(candidate.LineNumber);
    }

    private async void OnCancelCandidatesClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        ScanCandidates = [];
        OnPropertyChanged(nameof(ScanCandidates));
        SetMode(PickingPageMode.Scanning);
        await UpdateCameraAsync();
    }

    private async void OnAddLineMovementTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is ShippingOrderPickingLineViewState line
            && CanStartMovement
            && line.RemainingQuantity > 0)
        {
            await OpenMovementPageAsync(line.LineNumber);
        }
    }

    private async Task OpenMovementPageAsync(int? lineNumber)
    {
        var line = lineNumber is int number
            ? Details.Lines.Single(x => x.LineNumber == number)
            : null;
        var page = _services.GetRequiredService<ShippingOrderPickingMovementPage>();
        page.Show(Details, line, ApplyMovementResult);
        await Navigation.PushAsync(page);
    }

    private void ApplyMovementResult(MobileShippingOrderCommandResponse response)
    {
        _accentedLineNumber = response.ChangedLineNumber;
        _accentedMovementId = response.ChangedMovementId;
        ApplyDetails(response.Details);
    }

    private void OnOpenLineSearchTapped(object? sender, TappedEventArgs e)
    {
        if (!CanStartMovement)
        {
            return;
        }

        SetMode(PickingPageMode.Searching);
        CameraScannerView.Stop();
        Dispatcher.Dispatch(() => LineSearchEntry.Focus());
    }

    private async void OnCancelLineSearchTapped(object? sender, TappedEventArgs e)
    {
        await ClearSearchAsync();
        SetMode(PickingPageMode.Scanning);
        await UpdateCameraAsync();
    }

    private async void OnLineSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var version = ++_searchVersion;
        SetSearchBusy(false);
        SearchCandidates = [];
        OnPropertyChanged(nameof(SearchCandidates));
        if (_mode != PickingPageMode.Searching)
        {
            return;
        }

        var query = e.NewTextValue?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            LineSearchStatusLabel.Text = "Введите не менее двух символов.";
            return;
        }

        try
        {
            await Task.Delay(300);
            if (!IsCurrentSearch(version))
            {
                return;
            }

            SetSearchBusy(true);
            var result = await _apiClient.SearchShippingOrderLinesAsync(
                Details.Order.Id,
                query);
            if (!IsCurrentSearch(version))
            {
                return;
            }

            SearchCandidates = result.Items;
            OnPropertyChanged(nameof(SearchCandidates));
            LineSearchStatusLabel.Text = result.HasMore
                ? "Показаны первые результаты. Уточните запрос."
                : $"Найдено: {result.Items.Count}.";
        }
        catch (MobileApiException exception)
        {
            if (IsCurrentSearch(version))
            {
                LineSearchStatusLabel.Text = exception.Message;
            }
        }
        catch (HttpRequestException)
        {
            if (IsCurrentSearch(version))
            {
                LineSearchStatusLabel.Text = "Сервер WMS недоступен.";
            }
        }
        finally
        {
            if (IsCurrentSearch(version))
            {
                SetSearchBusy(false);
            }
        }
    }

    private bool IsCurrentSearch(int version) =>
        version == _searchVersion && _mode == PickingPageMode.Searching;

    private async void OnSearchCandidateTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not MobileShippingOrderLineCandidateResponse candidate)
        {
            return;
        }

        await ClearSearchAsync();
        SetMode(PickingPageMode.Scanning);
        await OpenMovementPageAsync(candidate.LineNumber);
    }

    private async Task ClearSearchAsync()
    {
        _searchVersion++;
        LineSearchEntry.Text = string.Empty;
        LineSearchEntry.Unfocus();
        await LineSearchEntry.HideSoftInputAsync(CancellationToken.None);
        SearchCandidates = [];
        OnPropertyChanged(nameof(SearchCandidates));
        LineSearchStatusLabel.Text = "Введите не менее двух символов.";
    }

    private async void OnDeleteMovementTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ShippingOrderPickingMovementViewState movement || _busy)
        {
            return;
        }

        if (_pendingDeleteMovementId is Guid pendingId && pendingId != movement.Id)
        {
            ErrorLabel.Text = "Сначала повторите удаление предыдущего движения.";
            return;
        }

        if (HasPendingCommand && _pendingDeleteRequestId is null)
        {
            ErrorLabel.Text = "Сначала повторите незавершённую операцию.";
            return;
        }

        if (_pendingDeleteRequestId is null)
        {
            SetBusy(true);
            var confirmed = await DisplayAlertAsync(
                "Удалить движение",
                $"Удалить черновик «{movement.SourceText}», {movement.QuantityText.ToLowerInvariant()}?",
                "Удалить",
                "Отмена");
            if (!confirmed)
            {
                SetBusy(false);
                return;
            }
        }
        else
        {
            SetBusy(true);
        }

        _pendingDeleteMovementId = movement.Id;
        _pendingDeleteRequestId ??= Guid.NewGuid();
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.DeleteShippingOrderPickingMovementAsync(
                Details.Order.Id,
                movement.Id,
                _pendingDeleteRequestId.Value);
            _pendingDeleteRequestId = null;
            _pendingDeleteMovementId = null;
            _accentedLineNumber = movement.LineNumber;
            _accentedMovementId = null;
            ApplyDetails(response.Details);
        }
        catch (MobileApiException exception)
        {
            _pendingDeleteRequestId = null;
            _pendingDeleteMovementId = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторите удаление этого же движения.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private void OnCompletePickingClicked(object? sender, EventArgs e)
    {
        if (_busy || !IsEditable || HasPendingCommand || _mode != PickingPageMode.Scanning)
        {
            return;
        }

        _deviationConfirmed = !HasPickingDeviation;
        ErrorLabel.Text = string.Empty;
        SetMode(PickingPageMode.Completion);
    }

    private void OnConfirmDeviationClicked(object? sender, EventArgs e)
    {
        if (_busy || _mode != PickingPageMode.Completion || !HasPickingDeviation)
        {
            return;
        }

        _deviationConfirmed = true;
        ErrorLabel.Text = string.Empty;
        RefreshActionAvailability();
    }

    private async void OnCancelCompletionClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_pendingCompletionRequestId is not null)
        {
            ErrorLabel.Text = "Сначала повторите завершение отбора.";
            return;
        }

        _deviationConfirmed = false;
        ConfirmCompletionButton.Text = "Завершить";
        ErrorLabel.Text = string.Empty;
        SetMode(PickingPageMode.Scanning);
        await UpdateCameraAsync();
    }

    private async void OnConfirmCompletionClicked(object? sender, EventArgs e)
    {
        if (_busy
            || _mode != PickingPageMode.Completion
            || (HasPickingDeviation && !_deviationConfirmed)
            || (HasPendingCommand && _pendingCompletionRequestId is null))
        {
            return;
        }

        _pendingCompletionRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.CompleteShippingOrderPickingAsync(
                Details.Order.Id,
                _pendingCompletionRequestId.Value);
            _pendingCompletionRequestId = null;
            ConfirmCompletionButton.Text = "Завершить";
            ApplyDetails(response.Details);

            if (_isVisible)
            {
                var shippingPage = _services.GetRequiredService<ShippingOrderShippingPage>();
                shippingPage.Show(response.Details);
                await Navigation.PushAsync(shippingPage);
                Navigation.RemovePage(this);
            }
        }
        catch (MobileApiException exception)
        {
            _pendingCompletionRequestId = null;
            ConfirmCompletionButton.Text = "Завершить";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ConfirmCompletionButton.Text = "Повторить завершение";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите завершение отбора.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private void ApplyDetails(MobileShippingOrderDetailsResponse details)
    {
        _details = details;
        NumberLabel.Text = $"Ордер {details.Order.Number}";
        StatusLabel.Text = MapStatus(details.Order.Status);
        WarehouseLabel.Text = $"Склад: {details.Order.WarehouseName}";
        ReceiverLabel.Text = $"Получатель: {details.Order.ReceiverName}";
        QueueLabel.Text = $"Очередь: {details.Order.Queue}";
        PlannedDateLabel.Text = details.Order.PlannedShippingDate is DateTime plannedDate
            ? $"План отгрузки: {plannedDate:dd.MM.yyyy HH:mm}"
            : "План отгрузки не указан";
        LocationLabel.Text = details.Order.ShippingLocation is null
            ? "Позиция отгрузки не указана"
            : $"Позиция отгрузки: {details.Order.ShippingLocation.Address}";
        ProgressLabel.Text = $"Отобрано: {details.Order.Progress.FactQuantity:g} "
            + $"из {details.Order.Progress.PlanQuantity:g}";
        CommentLabel.Text = details.Order.Comment;
        CommentLabel.IsVisible = !string.IsNullOrWhiteSpace(details.Order.Comment);
        ApplyCompletionSummary(details);
        SynchronizeLines(details);
        RefreshActionAvailability();
    }

    private void ApplyCompletionSummary(MobileShippingOrderDetailsResponse details)
    {
        var progress = details.Order.Progress;
        var deviation = progress.PlanQuantity - progress.FactQuantity;
        CompletionLinesLabel.Text = $"Полностью: {progress.FullyPickedLineCount}; "
            + $"частично: {progress.PartiallyPickedLineCount}; "
            + $"нулевой факт: {progress.ZeroPickedLineCount}.";
        CompletionQuantitiesLabel.Text = $"План: {progress.PlanQuantity:g}; "
            + $"факт: {progress.FactQuantity:g}; отклонение: {deviation:g}.";
        CompletionLocationLabel.Text = details.Order.ShippingLocation is null
            ? "Позиция отгрузки не указана."
            : $"Позиция отгрузки: {details.Order.ShippingLocation.Address}.";
        CompletionWarningLabel.Text = deviation == 0
            ? "Отбор выполнен полностью."
            : "Отбор выполнен не полностью. Неотобранное количество будет передано в 1С.";
        CompletionWarningLabel.TextColor = deviation == 0 ? Colors.Green : Colors.DarkOrange;
    }

    private void SynchronizeLines(MobileShippingOrderDetailsResponse details)
    {
        var numbers = details.Lines.Select(x => x.LineNumber).ToHashSet();
        for (var index = LineStates.Count - 1; index >= 0; index--)
        {
            if (!numbers.Contains(LineStates[index].LineNumber))
            {
                LineStates.RemoveAt(index);
            }
        }

        foreach (var line in details.Lines.OrderBy(x => x.LineNumber))
        {
            var movements = details.Movements.Where(x => x.LineNumber == line.LineNumber);
            var state = LineStates.SingleOrDefault(x => x.LineNumber == line.LineNumber);
            if (state is null)
            {
                LineStates.Add(ShippingOrderPickingLineViewState.From(
                    line,
                    movements,
                    CanStartMovement,
                    _accentedLineNumber,
                    _accentedMovementId));
            }
            else
            {
                state.Update(
                    line,
                    movements,
                    CanStartMovement,
                    _accentedLineNumber,
                    _accentedMovementId);
            }
        }
    }

    private void SetMode(PickingPageMode mode)
    {
        _mode = mode;
        LocationConfirmationPanel.IsVisible = mode == PickingPageMode.LocationConfirmation;
        LineCandidatesPanel.IsVisible = mode == PickingPageMode.CandidateSelection;
        LineSearchPanel.IsVisible = mode == PickingPageMode.Searching;
        CompletionPanel.IsVisible = mode == PickingPageMode.Completion;
        (StepLabel.Text, InstructionLabel.Text) = mode switch
        {
            PickingPageMode.Ready => (
                "Начало отбора",
                "Проверьте ордер и нажмите «Начать отбор»."),
            PickingPageMode.LocationScanning => (
                "Позиция отгрузки",
                "Отсканируйте активную позицию зоны отгрузки этого склада."),
            PickingPageMode.LocationConfirmation => (
                "Подтверждение позиции",
                "Проверьте адрес и подтвердите начало отбора."),
            PickingPageMode.CandidateSelection => (
                "Выбор строки",
                "Одинаковый товар есть в нескольких строках."),
            PickingPageMode.Searching => (
                "Ручной выбор строки",
                "Выберите строку с положительным остатком к отбору."),
            PickingPageMode.Completion => (
                "Завершение отбора",
                "Проверьте итог и подтвердите завершение."),
            _ => (
                "Отбор товара",
                "Отсканируйте товар или выберите строку ниже.")
        };
        RefreshActionAvailability();
    }

    private async Task UpdateCameraAsync()
    {
        if (_isVisible
            && _scanner.ActiveSource == BarcodeScanSource.Camera
            && IsScanExpected)
        {
            await CameraScannerView.StartAsync();
        }
        else
        {
            CameraScannerView.Stop();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        RefreshActionAvailability();
    }

    private void RefreshActionAvailability()
    {
        if (_details is null)
        {
            return;
        }

        StartPickingButton.IsVisible = _mode == PickingPageMode.Ready;
        StartPickingButton.IsEnabled = !_busy && !HasPendingCommand;
        ConfirmLocationButton.IsEnabled = !_busy && _scannedLocationBarcode is not null;
        CancelLocationButton.IsEnabled = !_busy && _pendingStartRequestId is null;
        LineSearchPrompt.IsVisible = IsEditable && _mode == PickingPageMode.Scanning;
        LineSearchPrompt.IsEnabled = CanStartMovement;
        CompletePickingButton.IsVisible = IsEditable && _mode == PickingPageMode.Scanning;
        CompletePickingButton.IsEnabled = !_busy && !HasPendingCommand;
        ConfirmDeviationButton.IsVisible = HasPickingDeviation && !_deviationConfirmed;
        ConfirmDeviationButton.IsEnabled = !_busy && _pendingCompletionRequestId is null;
        DeviationConfirmedLabel.IsVisible = HasPickingDeviation && _deviationConfirmed;
        ConfirmCompletionButton.IsEnabled = !_busy
            && (!HasPickingDeviation || _deviationConfirmed);
        CancelCompletionButton.IsEnabled = !_busy && _pendingCompletionRequestId is null;
        foreach (var line in LineStates)
        {
            line.SetActionAvailability(
                CanStartMovement,
                _pendingDeleteRequestId is null ? null : _pendingDeleteMovementId);
        }
    }

    private void SetSearchBusy(bool busy) =>
        LineSearchIndicator.Opacity = busy ? 1 : 0;

    private static string MapStatus(MobileShippingOrderStatus status) => status switch
    {
        MobileShippingOrderStatus.Prepared => "Подготовлен",
        MobileShippingOrderStatus.ReadyForPicking => "В отборе",
        MobileShippingOrderStatus.ReadyForVerification => "Готов к проверке",
        MobileShippingOrderStatus.InVerification => "На проверке",
        MobileShippingOrderStatus.Verified => "Проверен",
        _ => "Отбор"
    };

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            DisableAndroidFocus(element);
        }
    }

    private static void DisableAndroidFocus(VisualElement element)
    {
#if ANDROID
        if (element.Handler?.PlatformView is Android.Views.View view)
        {
            view.Focusable = false;
            view.FocusableInTouchMode = false;
        }
#endif
    }

    private enum PickingPageMode
    {
        Ready,
        LocationScanning,
        LocationConfirmation,
        Scanning,
        CandidateSelection,
        Searching,
        Completion
    }
}
