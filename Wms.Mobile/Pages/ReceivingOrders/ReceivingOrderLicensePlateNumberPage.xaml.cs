using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Scanning;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderLicensePlateNumberPage : ContentPage
{
    private readonly MobileReceivingOrderClient _orders;
    private readonly IOperationalBarcodeScanner _scanner;
    private readonly IServiceProvider _services;
    private ReceivingOrderReceivingProcess _process = null!;
    private MobileReceivingWorkOrderResponse? _order;
    private Guid _orderId;
    private Guid _lpnId;
    private Guid? _pendingSkuId;
    private Guid? _selectedSkuId;
    private bool _catalogSearch;
    private bool _visible;
    private bool _busy;
    private bool _closeAfterSave;
    private bool _confirmedExpiredDate;
    private long _scanGeneration;

    public ReceivingOrderLicensePlateNumberPage(MobileReceivingOrderClient orders,
        IOperationalBarcodeScanner scanner, IServiceProvider services)
    {
        InitializeComponent();
        _orders = orders;
        _scanner = scanner;
        _services = services;
        BindingContext = this;
        CameraScannerView.Configure(scanner);
    }

    public IReadOnlyList<ReceivingSkuChoiceRow> CandidateRows { get; private set; } = [];
    private MobileReceivingWorkLpnResponse? Lpn =>
        _order?.Lpns.SingleOrDefault(x => x.Id == _lpnId);

    public void Show(Guid orderId, Guid lpnId, ReceivingOrderReceivingProcess process,
        Guid? selectedSkuId = null)
    {
        _orderId = orderId;
        _lpnId = lpnId;
        _process = process;
        _selectedSkuId = selectedSkuId;
        _catalogSearch = false;
        ScopeButton.Text = "Поиск: состав ордера";
        SkuSearch.Text = string.Empty;
        _closeAfterSave = false;
        _confirmedExpiredDate = false;
        SearchPanel.IsVisible = false;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        _scanner.ScanReceived += OnScanReceived;
        await RefreshAsync();
        if (SearchPanel.IsVisible) await SearchAsync();
        await UpdateCameraAsync();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        Interlocked.Increment(ref _scanGeneration);
        _scanner.ScanReceived -= OnScanReceived;
        CameraScannerView.Stop();
        base.OnDisappearing();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        LpnRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy || _orderId == Guid.Empty) return;
        SetBusy(true);
        try { Apply(await _orders.GetAsync(_orderId)); }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить LPN."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private void Apply(MobileReceivingWorkOrderResponse order)
    {
        _order = order;
        var lpn = Lpn;
        if (lpn is null)
        {
            ErrorLabel.Text = "LPN больше не входит в этот ордер.";
            return;
        }
        CodeLabel.Text = lpn.Code;
        var status = ReceivingStatusSymbols.ForLpn(lpn.Status);
        StatusIcon.Text = status.Glyph;
        StatusIcon.TextColor = status.TextColor;
        StatusIcon.FontAttributes = status.GlyphFontAttributes;
        SemanticProperties.SetDescription(StatusIcon, $"Статус LPN: {status.Description}");
        if (lpn.Items.SingleOrDefault() is { } item)
            _selectedSkuId = item.SkuId;
        else if (_selectedSkuId is Guid selected && order.Items.All(x => x.SkuId != selected))
            _selectedSkuId = null;
        var editable = lpn.Status == MobileLicensePlateNumberStatus.Open
            && order.IsParticipant;
        ScanHintLabel.IsVisible = editable && _selectedSkuId is null;
        ActionsGrid.IsVisible = editable;
        SearchPanel.IsVisible = editable && _selectedSkuId is null;
        ShowSelectedSku();
        UpdateRecovery();
    }

    private void ShowSelectedSku()
    {
        var sku = _order?.Items.FirstOrDefault(x => x.SkuId == _selectedSkuId);
        EditorPanel.IsVisible = sku is not null;
        ClearSkuButton.IsVisible = sku is not null
            && Lpn?.Status == MobileLicensePlateNumberStatus.Open
            && _order?.IsParticipant == true;
        ApplyButton.IsVisible = sku is not null && Lpn?.Status == MobileLicensePlateNumberStatus.Open
            && _order?.IsParticipant == true;
        CloseButton.IsVisible = ApplyButton.IsVisible;
        Grid.SetColumn(CancelButton, ApplyButton.IsVisible ? 2 : 0);
        Grid.SetColumnSpan(CancelButton, ApplyButton.IsVisible ? 1 : 3);
        if (sku is null) return;

        var item = Lpn?.Items.SingleOrDefault(x => x.SkuId == sku.SkuId);
        SkuNameLabel.Text = sku.SkuName ?? sku.SkuCode ?? sku.SkuId.ToString();
        SkuCodeLabel.Text = sku.SkuCode;
        PlanLabel.Text = Format(sku.PlanQuantity);
        FactLabel.Text = ReceivingStatusSymbols.FactText(sku.PlanQuantity, sku.FactQuantity);
        FactLabel.TextColor = ReceivingStatusSymbols.FactColor(sku.PlanQuantity, sku.FactQuantity);
        SemanticProperties.SetDescription(FactLabel,
            ReceivingStatusSymbols.FactDescription(sku.PlanQuantity, sku.FactQuantity));
        CurrentLabel.Text = item is null ? "нет" : Format(item.Quantity);
        UnitLabel.Text = sku.UnitOfMeasure ?? "—";
        QuantityEntry.Text = item?.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
        ExpirationPanel.IsVisible = sku.TracksExpirationDate || item?.ExpirationDate is not null;
        ExpirationHintLabel.Text = sku.TracksExpirationDate
            ? "Для закрытия LPN укажите срок годности."
            : "Сохранённый срок годности не удаляется при выключении его учёта.";
        ExpirationSwitch.IsToggled = item?.ExpirationDate is not null;
        ExpirationPicker.Date = item?.ExpirationDate is DateOnly date
            ? date.ToDateTime(TimeOnly.MinValue) : DateTime.Today;
        ExpirationPicker.IsVisible = ExpirationSwitch.IsToggled;
    }

    private void OnScanReceived(object? sender, BarcodeScanEvent scan)
    {
        var generation = Volatile.Read(ref _scanGeneration);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_visible && generation == Volatile.Read(ref _scanGeneration))
                await HandleScanAsync(scan.Value);
        });
    }

    private async Task HandleScanAsync(string barcode)
    {
        if (!_visible || _busy || _process.HasPendingCommand || Lpn is null) return;
        if (_order?.IsParticipant != true)
        {
            ErrorLabel.Text = "Сначала присоединитесь к приёмке.";
            return;
        }
        if (barcode.Trim().StartsWith("LPN", StringComparison.OrdinalIgnoreCase))
        {
            ErrorLabel.Text = "LPN сканируется в списке LPN ордера.";
            return;
        }
        if (Lpn.Status != MobileLicensePlateNumberStatus.Open)
        {
            ErrorLabel.Text = "Закрытый LPN нельзя изменить.";
            return;
        }
        if (_selectedSkuId is not null)
        {
            ErrorLabel.Text = "Сначала уберите выбранный SKU.";
            return;
        }
        SetBusy(true);
        try
        {
            var sku = await _orders.ResolveSkuAsync(_orderId, barcode);
            SetBusy(false);
            await SelectSkuAsync(sku.SkuId, sku.IsInOrder,
                !string.IsNullOrWhiteSpace(sku.Name) ? sku.Name
                : !string.IsNullOrWhiteSpace(sku.Code) ? sku.Code : sku.SkuId.ToString());
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось определить SKU. Повторите сканирование."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnClearSkuClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand || _selectedSkuId is null
            || Lpn is not { Status: MobileLicensePlateNumberStatus.Open } lpn
            || _order?.IsParticipant != true) return;

        if (lpn.Items.SingleOrDefault() is null)
        {
            if (HasUnsavedChanges(lpn) && !await DisplayAlertAsync("Убрать SKU",
                    "Несохранённое количество и срок будут сброшены. Продолжить?",
                    "Убрать", "Назад")) return;
            ResetSkuSelection();
            ShowSelectedSku();
            UpdateRecovery();
            await SearchAsync();
            return;
        }

        var confirmation = $"Убрать SKU и сохранённое количество из {lpn.Code}?";
        if (HasUnsavedChanges(lpn))
            confirmation += " Несохранённые изменения тоже будут сброшены.";
        if (!await DisplayAlertAsync("Убрать SKU", confirmation,
                "Убрать", "Назад")) return;
        if (await ExecuteAsync(async () => ApplyClearedSku(
                await _process.ClearLpnItemAsync(_orderId, _lpnId, lpn.Revision))))
            await SearchAsync();
    }

    private void ResetSkuSelection()
    {
        _selectedSkuId = null;
        _catalogSearch = false;
        ScopeButton.Text = "Поиск: состав ордера";
        SkuSearch.Text = string.Empty;
        SearchPanel.IsVisible = true;
        ScanHintLabel.IsVisible = true;
    }

    private void ApplyClearedSku(MobileReceivingWorkOrderResponse order)
    {
        ResetSkuSelection();
        Apply(order);
    }

    private async void OnScopeClicked(object? sender, EventArgs e)
    {
        _catalogSearch = !_catalogSearch;
        ScopeButton.Text = _catalogSearch ? "Поиск: весь справочник" : "Поиск: состав ордера";
        await SearchAsync();
    }

    private async void OnSearchClicked(object? sender, EventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        if (_busy || _process.HasPendingCommand || _order?.IsParticipant != true
            || Lpn?.Status != MobileLicensePlateNumberStatus.Open) return;
        SetBusy(true);
        try
        {
            var results = await _orders.SearchSkuAsync(_orderId, SkuSearch.Text, _catalogSearch);
            CandidateRows = results.Select(x =>
            {
                var orderItem = _order?.Items.FirstOrDefault(item => item.SkuId == x.SkuId);
                var name = !string.IsNullOrWhiteSpace(x.Name) ? x.Name
                    : !string.IsNullOrWhiteSpace(x.Code) ? x.Code : x.SkuId.ToString();
                var details = string.Join(" · ", new[]
                {
                    x.Code == name ? null : x.Code,
                    x.IsInOrder ? null : "Вне плана"
                }.Where(part => !string.IsNullOrWhiteSpace(part)));
                var unit = string.IsNullOrWhiteSpace(orderItem?.UnitOfMeasure)
                    ? string.Empty : $" {orderItem.UnitOfMeasure}";
                var plan = x.IsInOrder ? x.CurrentPlanQuantity : null;
                var fact = x.FactQuantity;
                return new ReceivingSkuChoiceRow(x.SkuId, name, details,
                    $"{Format(plan)}{unit}", ReceivingStatusSymbols.FactText(plan, fact),
                    ReceivingStatusSymbols.FactDescription(plan, fact),
                    ReceivingStatusSymbols.FactColor(plan, fact), x.IsInOrder,
                    fact is null,
                    x.IsInOrder && plan is decimal expected && fact == expected,
                    x.IsInOrder && plan is decimal planned && fact is decimal actual && planned != actual);
            })
                .OrderBy(x => x.Name, StringComparer.Create(
                    System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true))
                .ThenBy(x => x.Details, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.SkuId)
                .ToArray();
            OnPropertyChanged(nameof(CandidateRows));
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось найти SKU."; }
        finally { SetBusy(false); await UpdateCameraAsync(); }
    }

    private async void OnCandidateTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Guid id || _busy || _process.HasPendingCommand) return;
        var candidate = CandidateRows.SingleOrDefault(x => x.SkuId == id);
        if (candidate is not null) await SelectSkuAsync(id, candidate.IsInOrder, candidate.Name);
    }

    private async Task SelectSkuAsync(Guid skuId, bool inOrder, string? skuName = null)
    {
        if (_busy || _process.HasPendingCommand || _order?.IsParticipant != true
            || Lpn?.Status != MobileLicensePlateNumberStatus.Open) return;
        if (_selectedSkuId is Guid currentSku && currentSku != skuId
            && Lpn is { } currentLpn && HasUnsavedChanges(currentLpn)
            && !await DisplayAlertAsync("Несохранённое количество",
                "Выбор другого SKU сбросит несохранённое количество и срок. Продолжить?",
                "Продолжить", "Назад")) return;
        if (!inOrder)
        {
            if (!await DisplayAlertAsync("Нет в ордере", skuName ?? skuId.ToString(),
                    "Добавить с планом 0", "Назад")) return;
            _pendingSkuId = skuId;
            await ExecuteAsync(async () => Apply(await _process.AddSkuAsync(_orderId, skuId)));
            if (_process.HasPendingCommand || _pendingSkuId is null) return;
            _pendingSkuId = null;
        }
        _selectedSkuId = skuId;
        SearchPanel.IsVisible = false;
        ScanHintLabel.IsVisible = false;
        ShowSelectedSku();
        UpdateRecovery();
        await UpdateCameraAsync();
    }

    private void OnExpirationToggled(object? sender, ToggledEventArgs e) =>
        ExpirationPicker.IsVisible = e.Value;

    private async void OnQuantityCompleted(object? sender, EventArgs e)
    {
        QuantityEntry.Unfocus();
        await QuantityEntry.HideSoftInputAsync(CancellationToken.None);
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand
            || Lpn is not { Status: MobileLicensePlateNumberStatus.Open } lpn
            || _order?.IsParticipant != true) return;
        if (!TryReadEditedValues(out var skuId, out var quantity, out var expiration)) return;
        if (!HasUnsavedChanges(lpn))
        {
            await ReturnToLpnListAsync();
            return;
        }
        if (await ExecuteAsync(async () => Apply(await _process.EditLpnAsync(
            _orderId, _lpnId, skuId, quantity, expiration))))
            await ReturnToLpnListAsync();
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        if (Lpn is not { Status: MobileLicensePlateNumberStatus.Open } lpn
            || _busy || _process.HasPendingCommand || _order?.IsParticipant != true) return;
        if (!TryReadEditedValues(out var skuId, out var quantity, out var expiration)) return;
        if (_order.Items.FirstOrDefault(x => x.SkuId == skuId)?.TracksExpirationDate == true
            && expiration is null)
        {
            ErrorLabel.Text = "Укажите срок годности перед закрытием LPN.";
            return;
        }
        var expired = expiration is DateOnly date
            && date < DateOnly.FromDateTime(DateTime.UtcNow);
        if (expired && !await DisplayAlertAsync("Прошедший срок",
                "Срок годности прошёл. Подтвердить закрытие LPN?", "Подтвердить", "Назад")) return;
        if (!await DisplayAlertAsync("Провести приход",
                $"Закрыть {lpn.Code} и провести {quantity:0.###}?", "Да", "Нет")) return;
        if (_busy || _process.HasPendingCommand || Lpn?.Status != MobileLicensePlateNumberStatus.Open)
            return;

        _confirmedExpiredDate = expired;
        if (HasUnsavedChanges(lpn))
        {
            _closeAfterSave = true;
            var saved = await ExecuteAsync(async () => Apply(await _process.EditLpnAsync(
                _orderId, _lpnId, skuId, quantity, expiration)));
            if (!saved)
            {
                if (!_process.HasPendingCommand) _closeAfterSave = false;
                return;
            }
            _closeAfterSave = false;
        }
        await CloseSavedLpnAsync();
    }

    private async Task CloseSavedLpnAsync()
    {
        if (!await ExecuteAsync(async () => Apply(await _process.CloseLpnAsync(
                _orderId, _lpnId, _confirmedExpiredDate, null)), refreshOnConflict: true))
            return;
        await FinishCloseAsync();
    }

    private async Task FinishCloseAsync()
    {
        if (_process.LastCloseResult is null) return;
        var result = _process.LastCloseResult;
        if (!result.SourceCheckSucceeded)
            await DisplayAlertAsync("Сверка 1С", result.SourceCheckError ?? "План не подтверждён.", "Понятно");
        await Navigation.PopAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (Lpn is not { Status: MobileLicensePlateNumberStatus.Open } lpn
            || _busy || _process.HasPendingCommand) return;
        if (!await DisplayAlertAsync("Отменить LPN", $"Отменить {lpn.Code} без прихода?", "Отменить", "Назад")) return;
        if (await ExecuteAsync(async () => Apply(await _process.CancelLpnAsync(_orderId, _lpnId)))
            && Lpn?.Status == MobileLicensePlateNumberStatus.Cancelled)
            await Navigation.PopAsync();
    }

    private async Task<bool> ExecuteAsync(Func<Task> action, bool refreshOnConflict = false)
    {
        if (_busy || _process.HasPendingCommand) return false;
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try { await action(); return true; }
        catch (MobileApiException ex)
        {
            _pendingSkuId = null;
            if (refreshOnConflict && ex.Code == "request_conflict")
            {
                try { Apply(await _orders.GetAsync(_orderId)); }
                catch (MobileApiException) { }
                catch (HttpRequestException) { }
                ErrorLabel.Text = ex.Message;
            }
            else ErrorLabel.Text = ex.Message;
        }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { SetBusy(false); UpdateRecovery(); await UpdateCameraAsync(); }
        return false;
    }

    private async void OnRetryClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand) return;
        var action = _process.PendingLabel;
        var succeeded = false;
        SetBusy(true);
        try
        {
            var order = await _process.RetryAsync();
            if (action == "Убрать SKU") ApplyClearedSku(order);
            else Apply(order);
            ErrorLabel.Text = string.Empty;
            succeeded = true;
        }
        catch (MobileApiException ex)
        {
            ErrorLabel.Text = ex.Message;
            _pendingSkuId = null;
            _closeAfterSave = false;
        }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите ту же команду."; }
        finally { SetBusy(false); UpdateRecovery(); await UpdateCameraAsync(); }
        if (!succeeded) return;
        if (action == "Добавить SKU" && _pendingSkuId is Guid skuId)
        {
            _pendingSkuId = null;
            await SelectSkuAsync(skuId, true);
        }
        else if (action == "Сохранить LPN")
        {
            if (_closeAfterSave)
            {
                _closeAfterSave = false;
                await CloseSavedLpnAsync();
            }
            else await ReturnToLpnListAsync();
        }
        else if (action == "Закрыть LPN")
            await FinishCloseAsync();
        else if (action == "Убрать SKU")
            await SearchAsync();
        else if (action == "Отменить LPN")
            await Navigation.PopAsync();
    }

    private async Task ReturnToLpnListAsync()
    {
        var stack = Navigation.NavigationStack.ToArray();
        if (stack.Length > 1 && stack[^2] is ReceivingOrderLpnListPage)
        {
            await Navigation.PopAsync();
            return;
        }

        if (_order is null) return;
        var section = Array.FindLastIndex(stack, page =>
            page is ReceivingOrderSkuPage or ReceivingOrderLpnListPage);
        if (section < 0) return;

        var list = _services.GetRequiredService<ReceivingOrderLpnListPage>();
        list.Show(_order, _process);
        await Navigation.PushAsync(list);
        foreach (var page in stack.Skip(section))
            Navigation.RemovePage(page);
    }

    private void UpdateRecovery()
    {
        RetryButton.IsVisible = _process.HasPendingCommand;
        RetryButton.Text = $"Повторить: {_process.PendingLabel}";
        var canEdit = !_busy && !_process.HasPendingCommand
            && Lpn?.Status == MobileLicensePlateNumberStatus.Open
            && _order?.IsParticipant == true;
        ClearSkuButton.IsEnabled = canEdit;
        CloseButton.IsEnabled = canEdit;
        CancelButton.IsEnabled = canEdit;
        ScopeButton.IsEnabled = canEdit;
        SkuSearch.IsEnabled = canEdit;
        QuantityEntry.IsEnabled = canEdit;
        ExpirationSwitch.IsEnabled = canEdit;
        ExpirationPicker.IsEnabled = canEdit;
        ApplyButton.IsEnabled = canEdit && _selectedSkuId is not null;
        NavigationPage.SetHasBackButton(this, !_busy && !_process.HasPendingCommand);
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор команды.";
        return true;
    }

    private void SetBusy(bool busy)
    {
        Interlocked.Increment(ref _scanGeneration);
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        UpdateRecovery();
    }

    private async Task UpdateCameraAsync()
    {
        if (_visible && !_busy && !_process.HasPendingCommand
            && Lpn?.Status == MobileLicensePlateNumberStatus.Open
            && _order?.IsParticipant == true
            && _selectedSkuId is null
            && _scanner.ActiveSource == BarcodeScanSource.Camera)
            await CameraScannerView.StartAsync();
        else CameraScannerView.Stop();
    }

    private static string Format(decimal? value) => value?.ToString("0.###") ?? "?";

    private bool TryReadEditedValues(out Guid skuId, out decimal quantity,
        out DateOnly? expiration)
    {
        skuId = _selectedSkuId ?? Guid.Empty;
        expiration = SelectedExpiration();
        if (skuId == Guid.Empty)
        {
            quantity = default;
            ErrorLabel.Text = "Выберите SKU.";
            return false;
        }
        if (!TryReadQuantity(out quantity))
        {
            ErrorLabel.Text = "Введите положительное количество с точностью до трёх знаков.";
            return false;
        }
        if (ExpirationPanel.IsVisible && ExpirationSwitch.IsToggled && expiration is null)
        {
            ErrorLabel.Text = "Выберите дату срока годности.";
            return false;
        }
        return true;
    }

    private bool TryReadQuantity(out decimal quantity) =>
        decimal.TryParse((QuantityEntry.Text ?? string.Empty).Trim().Replace(',', '.'),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out quantity)
        && WarehouseQuantityInput.IsSupported(quantity) && quantity > 0;

    private DateOnly? SelectedExpiration() =>
        ExpirationPanel.IsVisible && ExpirationSwitch.IsToggled
            && ExpirationPicker.Date is DateTime date
            ? DateOnly.FromDateTime(date) : null;

    private bool HasUnsavedChanges(MobileReceivingWorkLpnResponse lpn)
    {
        var item = lpn.Items.SingleOrDefault();
        if (item is null)
            return _selectedSkuId is not null
                && (!string.IsNullOrWhiteSpace(QuantityEntry.Text)
                    || SelectedExpiration() is not null);
        return _selectedSkuId != item.SkuId
            || !TryReadQuantity(out var quantity) || quantity != item.Quantity
            || SelectedExpiration() != item.ExpirationDate;
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element) AndroidFocus.Suppress(element);
    }
}

public sealed record ReceivingSkuChoiceRow(Guid SkuId, string Name, string Details,
    string PlanText, string FactText, string FactDescription, Color FactColor, bool IsInOrder,
    bool IsUnchecked, bool IsMatched, bool IsDifferent)
{
    public bool HasDetails => !string.IsNullOrEmpty(Details);
}
