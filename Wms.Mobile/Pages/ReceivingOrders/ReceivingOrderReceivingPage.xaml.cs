using System.Collections.ObjectModel;
using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderReceivingPage : ContentPage
{
    private readonly MobileApiClient _apiClient;
    private readonly IOperationalBarcodeScanner _scanner;
    private MobileReceivingOrderDetailsResponse? _details;
    private MobileOrderSynchronizationResponse? _synchronization;
    private ReceivingPageMode _mode = ReceivingPageMode.Ready;
    private ReceivingOrderLineViewState? _editingLine;
    private MobileStorageLocationResponse? _scannedLocation;
    private string? _scannedLocationBarcode;
    private string? _selectedScanBarcode;
    private int? _pendingScanLineNumber;
    private int? _pendingQuantityLineNumber;
    private int _searchVersion;
    private bool _isVisible;
    private bool _scannerSubscribed;
    private bool _busy;
    private Guid? _pendingStartRequestId;
    private Guid? _pendingScanRequestId;
    private Guid? _pendingQuantityRequestId;
    private Guid? _pendingCompletionRequestId;

    public ReceivingOrderReceivingPage(
        MobileApiClient apiClient,
        IOperationalBarcodeScanner scanner)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _scanner = scanner;
        CameraScannerView.Configure(scanner);
    }

    public ObservableCollection<ReceivingOrderLineViewState> LineStates { get; } = [];
    public IReadOnlyList<MobileReceivingOrderLineCandidateResponse> ScanCandidates { get; private set; } = [];
    public IReadOnlyList<MobileReceivingOrderLineCandidateResponse> SearchCandidates { get; private set; } = [];

    private MobileReceivingOrderDetailsResponse Details =>
        _details ?? throw new InvalidOperationException("Приходный ордер не загружен.");

    private bool IsActiveReceiving => Details.Order.Status is
        MobileReceivingOrderStatus.InReceiving or
        MobileReceivingOrderStatus.ProcessingRequired;

    private bool IsSynchronizationResolved =>
        _synchronization is not null
        && OrderSynchronizationPresentation.CanPerformCriticalTransition(_synchronization);

    private bool HasPendingCommand => _pendingStartRequestId is not null
        || _pendingScanRequestId is not null
        || _pendingQuantityRequestId is not null
        || _pendingCompletionRequestId is not null;

    private bool CanStartNewAction => IsActiveReceiving
        && !_busy
        && _mode == ReceivingPageMode.Scanning
        && !HasPendingCommand;

    private bool IsScanExpected => !_busy
        && (_mode == ReceivingPageMode.LocationScanning
            || (_mode == ReceivingPageMode.Scanning
                && (!HasPendingCommand || _pendingScanRequestId is not null)));

    public void Show(MobileReceivingOrderDetailsResponse details)
    {
        _synchronization = null;
        ApplyDetails(details);
        SetMode(details.Order.Status == MobileReceivingOrderStatus.ReadyForReceiving
            ? ReceivingPageMode.Ready
            : ReceivingPageMode.Scanning);
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

    private async void OnStartReceivingClicked(object? sender, EventArgs e)
    {
        if (_busy || HasPendingCommand || Details.Order.Status != MobileReceivingOrderStatus.ReadyForReceiving)
        {
            return;
        }

        ErrorLabel.Text = string.Empty;
        SetMode(ReceivingPageMode.LocationScanning);
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

        if (_mode == ReceivingPageMode.LocationScanning)
        {
            await ResolveReceivingLocationAsync(barcode);
        }
        else
        {
            await ResolveOrIncrementSkuAsync(barcode);
        }
    }

    private async Task ResolveReceivingLocationAsync(string barcode)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var location = await _apiClient.ResolveStorageLocationAsync(
                barcode,
                Details.Order.WarehouseId,
                MobileStorageLocationContext.Receiving);
            _scannedLocation = location;
            _scannedLocationBarcode = barcode;
            ScannedLocationLabel.Text = $"{location.Address} · {location.Name}";
            SetMode(ReceivingPageMode.LocationConfirmation);
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
            var response = await _apiClient.StartReceivingOrderAsync(
                Details.Order.Id,
                _scannedLocationBarcode,
                _pendingStartRequestId.Value);
            _pendingStartRequestId = null;
            _scannedLocation = null;
            _scannedLocationBarcode = null;
            ApplyDetails(response.Details);
            SetMode(ReceivingPageMode.Scanning);
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
            ErrorLabel.Text = "Ответ сервера не получен. Повторите начало с тем же адресом.";
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
            ErrorLabel.Text = "Сначала повторите начало приёмки с тем же адресом.";
            return;
        }

        _scannedLocation = null;
        _scannedLocationBarcode = null;
        ConfirmLocationButton.Text = "Начать";
        SetMode(ReceivingPageMode.LocationScanning);
        await UpdateCameraAsync();
    }

    private async Task ResolveOrIncrementSkuAsync(string barcode)
    {
        if (_pendingScanRequestId is not null)
        {
            if (_selectedScanBarcode != barcode || _pendingScanLineNumber is not int retryLine)
            {
                ErrorLabel.Text = "Повторите предыдущий штрихкод: ответ сервера не был получен.";
                return;
            }

            await IncrementLineAsync(retryLine);
            return;
        }

        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var candidates = await _apiClient.ResolveReceivingOrderSkuAsync(
                Details.Order.Id,
                barcode);
            _selectedScanBarcode = barcode;
            if (candidates.Count == 1)
            {
                _pendingScanLineNumber = candidates[0].LineNumber;
                _pendingScanRequestId = Guid.NewGuid();
                await IncrementLineCoreAsync(candidates[0].LineNumber);
                return;
            }

            ScanCandidates = candidates;
            OnPropertyChanged(nameof(ScanCandidates));
            SetMode(ReceivingPageMode.CandidateSelection);
        }
        catch (MobileApiException exception)
        {
            _selectedScanBarcode = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            _selectedScanBarcode = null;
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
        if (_busy || e.Parameter is not MobileReceivingOrderLineCandidateResponse candidate)
        {
            return;
        }

        if (_pendingScanLineNumber is int pendingLine && pendingLine != candidate.LineNumber)
        {
            ErrorLabel.Text = "Повторите предыдущую строку: ответ сервера не был получен.";
            return;
        }

        _pendingScanLineNumber = candidate.LineNumber;
        _pendingScanRequestId ??= Guid.NewGuid();
        await IncrementLineAsync(candidate.LineNumber);
    }

    private async Task IncrementLineAsync(int lineNumber)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            await IncrementLineCoreAsync(lineNumber);
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async Task IncrementLineCoreAsync(int lineNumber)
    {
        try
        {
            var response = await _apiClient.IncrementReceivingOrderLineAsync(
                Details.Order.Id,
                lineNumber,
                _pendingScanRequestId!.Value);
            _pendingScanRequestId = null;
            _pendingScanLineNumber = null;
            _selectedScanBarcode = null;
            ScanCandidates = [];
            OnPropertyChanged(nameof(ScanCandidates));
            ApplyDetails(response.Details);
            AccentLine(lineNumber, "+1");
            SetMode(ReceivingPageMode.Scanning);
            InstructionLabel.Text = "Принято +1. Сканируйте следующий товар.";
        }
        catch (MobileApiException exception)
        {
            _pendingScanRequestId = null;
            _pendingScanLineNumber = null;
            if (_mode == ReceivingPageMode.Scanning)
            {
                _selectedScanBarcode = null;
            }
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = _mode == ReceivingPageMode.CandidateSelection
                ? "Ответ сервера не получен. Повторно выберите эту же строку."
                : "Ответ сервера не получен. Повторно отсканируйте этот же товар.";
        }
    }

    private async void OnCancelCandidateClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (_pendingScanRequestId is not null)
        {
            ErrorLabel.Text = "Сначала повторно выберите предыдущую строку.";
            return;
        }

        _selectedScanBarcode = null;
        ScanCandidates = [];
        OnPropertyChanged(nameof(ScanCandidates));
        ReturnToScanning();
        await UpdateCameraAsync();
    }

    private void OnOpenLineSearchTapped(object? sender, TappedEventArgs e)
    {
        if (!CanStartNewAction)
        {
            return;
        }

        SetMode(ReceivingPageMode.Searching);
        CameraScannerView.Stop();
        Dispatcher.Dispatch(() => LineSearchEntry.Focus());
    }

    private async void OnCancelLineSearchTapped(object? sender, TappedEventArgs e)
    {
        await ClearSearchAsync();
        ReturnToScanning();
        await UpdateCameraAsync();
    }

    private async void OnLineSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var version = ++_searchVersion;
        SetSearchBusy(false);
        SearchCandidates = [];
        OnPropertyChanged(nameof(SearchCandidates));
        if (_mode != ReceivingPageMode.Searching)
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
            var result = await _apiClient.SearchReceivingOrderLinesAsync(
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
        version == _searchVersion && _mode == ReceivingPageMode.Searching;

    private async void OnSearchCandidateTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not MobileReceivingOrderLineCandidateResponse candidate)
        {
            return;
        }

        var line = LineStates.Single(x => x.LineNumber == candidate.LineNumber);
        await ClearSearchAsync();
        BeginQuantityEdit(line);
    }

    private void OnEditQuantityTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is ReceivingOrderLineViewState line)
        {
            BeginQuantityEdit(line);
        }
    }

    private void BeginQuantityEdit(ReceivingOrderLineViewState line)
    {
        if (!CanStartNewAction && _mode != ReceivingPageMode.Searching)
        {
            return;
        }

        CameraScannerView.Stop();
        _editingLine = line;
        SetMode(ReceivingPageMode.Editing);
        AccentLine(line.LineNumber, null);
        line.BeginEditing();
        InstructionLabel.Text = "Введите итоговое фактическое количество, включая 0.";
    }

    private async void OnSaveQuantityClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: ReceivingOrderLineViewState line })
        {
            await SaveQuantityAsync(line);
        }
    }

    private async Task SaveQuantityAsync(ReceivingOrderLineViewState line)
    {
        if (_busy || !ReferenceEquals(_editingLine, line))
        {
            return;
        }

        if (_pendingQuantityLineNumber is int pendingLine && pendingLine != line.LineNumber)
        {
            ErrorLabel.Text = "Сначала повторите сохранение предыдущей строки.";
            return;
        }

        if (!TryReadQuantity(line, out var quantity))
        {
            return;
        }

        _pendingQuantityLineNumber = line.LineNumber;
        _pendingQuantityRequestId ??= Guid.NewGuid();
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _apiClient.SetReceivingOrderLineQuantityAsync(
                Details.Order.Id,
                line.LineNumber,
                quantity,
                _pendingQuantityRequestId.Value);
            _pendingQuantityRequestId = null;
            _pendingQuantityLineNumber = null;
            line.EndEditing();
            _editingLine = null;
            ApplyDetails(response.Details);
            AccentLine(line.LineNumber, "Итог");
            ReturnToScanning();
        }
        catch (MobileApiException exception)
        {
            _pendingQuantityRequestId = null;
            _pendingQuantityLineNumber = null;
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Ответ сервера не получен. Повторите сохранение этого количества.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private async void OnCancelQuantityClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: ReceivingOrderLineViewState line }
            || _busy
            || !ReferenceEquals(_editingLine, line))
        {
            return;
        }

        if (_pendingQuantityRequestId is not null)
        {
            ErrorLabel.Text = "Сначала повторите сохранение количества.";
            return;
        }

        line.EndEditing();
        _editingLine = null;
        ReturnToScanning();
        await UpdateCameraAsync();
    }

    private async void OnCompleteReceivingClicked(object? sender, EventArgs e)
    {
        if (_busy
            || _mode != ReceivingPageMode.Scanning
            || (HasPendingCommand && _pendingCompletionRequestId is null))
        {
            return;
        }

        if (Details.Lines.Any(x => x.FactQuantity is null))
        {
            ErrorLabel.Text = "Сначала проверьте фактическое количество каждой строки.";
            return;
        }

        if (_pendingCompletionRequestId is null)
        {
            SetBusy(true);
            CameraScannerView.Stop();
            var confirmed = await DisplayAlertAsync(
                "Завершить приёмку",
                BuildCompletionSummary(),
                "Завершить",
                "Отмена");
            if (!confirmed)
            {
                SetBusy(false);
                await UpdateCameraAsync();
                return;
            }
        }
        else
        {
            SetBusy(true);
        }

        _pendingCompletionRequestId ??= Guid.NewGuid();
        try
        {
            var response = await _apiClient.CompleteReceivingOrderAsync(
                Details.Order.Id,
                _pendingCompletionRequestId.Value);
            _pendingCompletionRequestId = null;
            ApplyDetails(response.Details);
            if (_isVisible)
            {
                await DisplayAlertAsync("Готово", "Приёмка завершена.", "ОК");
                if (_isVisible)
                {
                    await Navigation.PopAsync();
                }
            }
        }
        catch (MobileApiException exception)
        {
            _pendingCompletionRequestId = null;
            CompleteReceivingButton.Text = "Завершить приёмку";
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            CompleteReceivingButton.Text = "Повторить завершение";
            ErrorLabel.Text = "Ответ сервера не получен. Повторите завершение приёмки.";
        }
        finally
        {
            SetBusy(false);
            await UpdateCameraAsync();
        }
    }

    private string BuildCompletionSummary()
    {
        var lines = Details.Lines;
        var exact = lines.Count(x => x.FactQuantity == x.PlanQuantity);
        var zero = lines.Count(x => x.FactQuantity == 0);
        var shortage = lines.Count(x => x.FactQuantity > 0 && x.FactQuantity < x.PlanQuantity);
        var overage = lines.Count(x => x.FactQuantity > x.PlanQuantity);
        var location = Details.Order.ReceivingLocation?.Address ?? "не указана";
        return $"План: {Details.Order.Progress.PlanQuantity:g}\n"
            + $"Факт: {Details.Order.Progress.FactQuantity:g}\n"
            + $"Совпало: {exact}; недоприёмка: {shortage}; переприёмка: {overage}; ноль: {zero}\n"
            + $"Позиция приёмки: {location}";
    }

    private void ApplyDetails(MobileReceivingOrderDetailsResponse details)
    {
        _details = details;
        _synchronization = OrderSynchronizationPresentation.MergeOpeningAssessment(
            _synchronization,
            details.Order.Synchronization);
        NumberLabel.Text = $"Приёмка ордера {details.Order.Number}";
        StatusLabel.Text = details.Order.Status switch
        {
            MobileReceivingOrderStatus.ReadyForReceiving => "Готов к приёмке",
            MobileReceivingOrderStatus.InReceiving => "В приёмке",
            MobileReceivingOrderStatus.ProcessingRequired => "Требуется обработка",
            _ => "Приёмка завершена"
        };
        WarehouseLabel.Text = $"Склад: {details.Order.WarehouseName}";
        ShipperLabel.Text = $"Отправитель: {details.Order.ShipperName}";
        LocationLabel.Text = details.Order.ReceivingLocation is null
            ? "Позиция приёмки не выбрана"
            : $"Позиция приёмки: {details.Order.ReceivingLocation.Address}";
        ProgressLabel.Text = $"Факт: {details.Order.Progress.FactQuantity:g} из "
            + $"{details.Order.Progress.PlanQuantity:g} · Проверено строк: "
            + $"{details.Order.Progress.ConfirmedLineCount} из "
            + $"{details.Order.Progress.TotalLineCount}";
        SynchronizationPanel.IsVisible = OrderSynchronizationPresentation.HasIssue(
            _synchronization);
        SynchronizationTitleLabel.Text = OrderSynchronizationPresentation.BuildTitle(
            _synchronization);
        SynchronizationDetailsLabel.Text = OrderSynchronizationPresentation.BuildDetails(
            _synchronization);
        SynchronizeLineStates(details.Lines);
        RefreshActionAvailability();
    }

    private void SynchronizeLineStates(IReadOnlyList<MobileReceivingOrderLineResponse> lines)
    {
        var lineNumbers = lines.Select(x => x.LineNumber).ToHashSet();
        for (var index = LineStates.Count - 1; index >= 0; index--)
        {
            if (!lineNumbers.Contains(LineStates[index].LineNumber))
            {
                LineStates.RemoveAt(index);
            }
        }

        foreach (var line in lines)
        {
            var state = LineStates.SingleOrDefault(x => x.LineNumber == line.LineNumber);
            if (state is null)
            {
                LineStates.Add(ReceivingOrderLineViewState.From(line, IsActiveReceiving));
            }
            else
            {
                state.Update(line, IsActiveReceiving);
            }
        }
    }

    private static bool TryReadQuantity(
        ReceivingOrderLineViewState line,
        out decimal quantity)
    {
        var value = line.QuantityText.Trim().Replace(',', '.');
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out quantity)
            && WarehouseQuantityInput.IsSupported(quantity)
            && quantity >= 0)
        {
            return true;
        }

        line.MarkQuantityInvalid();
        return false;
    }

    private void AccentLine(int lineNumber, string? text)
    {
        foreach (var line in LineStates)
        {
            line.SetAccent(line.LineNumber == lineNumber, text);
        }
    }

    private async Task ClearSearchAsync()
    {
        _searchVersion++;
        SetSearchBusy(false);
        LineSearchEntry.Unfocus();
        await LineSearchEntry.HideSoftInputAsync(CancellationToken.None);
        LineSearchEntry.Text = string.Empty;
        SearchCandidates = [];
        OnPropertyChanged(nameof(SearchCandidates));
    }

    private void ReturnToScanning()
    {
        SetMode(ReceivingPageMode.Scanning);
        ErrorLabel.Text = string.Empty;
    }

    private void SetMode(ReceivingPageMode mode)
    {
        _mode = mode;
        StartReceivingButton.IsVisible = mode == ReceivingPageMode.Ready;
        LocationConfirmationPanel.IsVisible = mode == ReceivingPageMode.LocationConfirmation;
        LineSearchPrompt.IsVisible = mode == ReceivingPageMode.Scanning && IsActiveReceiving;
        LineSearchPanel.IsVisible = mode == ReceivingPageMode.Searching;
        LineCandidatesPanel.IsVisible = mode == ReceivingPageMode.CandidateSelection;

        (StepLabel.Text, InstructionLabel.Text) = mode switch
        {
            ReceivingPageMode.Ready => (
                "Начало приёмки",
                "Проверьте ордер и начните приёмку."),
            ReceivingPageMode.LocationScanning => (
                "Позиция приёмки",
                "Отсканируйте позицию зоны приёмки этого склада."),
            ReceivingPageMode.LocationConfirmation => (
                "Подтверждение позиции",
                "Проверьте адрес и подтвердите начало приёмки."),
            ReceivingPageMode.Searching => (
                "Ручной выбор строки",
                "Выбор строки открывает итоговое количество и не добавляет единицу."),
            ReceivingPageMode.CandidateSelection => (
                "Выбор строки",
                "Одинаковый товар есть в нескольких строках."),
            ReceivingPageMode.Editing => (
                "Итоговое количество",
                "Введите абсолютное фактическое количество."),
            _ => (
                "Приёмка товара",
                "Отсканируйте товар. Каждый принятый скан добавляет одну единицу.")
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
        CommandProgressIndicator.Opacity = busy ? 1 : 0;
        RefreshActionAvailability();
    }

    private void RefreshActionAvailability()
    {
        if (_details is null)
        {
            return;
        }

        StartReceivingButton.IsEnabled = !_busy
            && !HasPendingCommand
            && IsSynchronizationResolved;
        ConfirmLocationButton.IsEnabled = !_busy
            && (!HasPendingCommand || _pendingStartRequestId is not null);
        CancelLocationButton.IsEnabled = !_busy && _pendingStartRequestId is null;
        CancelCandidateButton.IsEnabled = !_busy && _pendingScanRequestId is null;
        LineSearchPrompt.IsEnabled = CanStartNewAction;
        CompleteReceivingButton.IsVisible = IsActiveReceiving;
        CompleteReceivingButton.IsEnabled = !_busy
            && _mode == ReceivingPageMode.Scanning
            && IsSynchronizationResolved
            && (!HasPendingCommand || _pendingCompletionRequestId is not null)
            && Details.Lines.All(x => x.FactQuantity.HasValue);
        foreach (var line in LineStates)
        {
            line.SetActionsEnabled(CanStartNewAction);
        }
    }

    private void SetSearchBusy(bool busy) =>
        LineSearchIndicator.Opacity = busy ? 1 : 0;

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

    private enum ReceivingPageMode
    {
        Ready,
        LocationScanning,
        LocationConfirmation,
        Scanning,
        CandidateSelection,
        Searching,
        Editing
    }
}
