using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderLicensePlateNumberQuantityPage : ContentPage
{
    private ReceivingOrderReceivingProcess _process = null!;
    private readonly MobileReceivingOrderClient _orderClient;
    private Guid _orderId;
    private Guid _lpnId;
    private Guid _skuId;
    private MobileReceivingOrderDetailsResponse? _details;
    private bool _busy;
    private bool _hideManualSkuSelectionAfterSave;

    public ReceivingOrderLicensePlateNumberQuantityPage(
        MobileReceivingOrderClient orderClient)
    {
        InitializeComponent();
        _orderClient = orderClient;
    }

    public void Show(Guid orderId, Guid lpnId, Guid skuId, ReceivingOrderReceivingProcess process)
    {
        _process = process;
        _orderId = orderId;
        _lpnId = lpnId;
        _skuId = skuId;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_details is null)
            await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            _details = await _orderClient.GetAsync(_orderId);
            ApplyDetails();
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = "Сервер WMS недоступен.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyDetails()
    {
        var lines = _details!.Lines.Where(x => x.StockKeepingUnitId == _skuId).ToArray();
        if (lines.Length == 0)
            throw new InvalidOperationException("Товар отсутствует в плане приходного ордера.");
        var lpn = _details.LicensePlateNumbers.Single(x => x.Id == _lpnId);
        var item = lpn.Items.SingleOrDefault(x => x.StockKeepingUnitId == _skuId);
        var first = lines[0];
        SkuNameLabel.Text = first.SkuName;
        SkuCodeLabel.Text = first.SkuCode;
        PlanLabel.Text = lines.Sum(x => x.PlanQuantity).ToString("0.###");
        FactLabel.Text = lines.All(x => x.FactQuantity.HasValue)
            ? lines.Sum(x => x.FactQuantity!.Value).ToString("0.###")
            : "?";
        CurrentLabel.Text = item?.Quantity.ToString("0.###") ?? "—";
        UnitLabel.Text = string.IsNullOrWhiteSpace(first.UnitOfMeasure) ? "—" : first.UnitOfMeasure;
        QuantityEntry.Text = item?.Quantity.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
        RemoveButton.IsVisible = item is not null;
        ApplyButton.IsEnabled = lpn.CanEdit && !_busy;
        RemoveButton.IsEnabled = lpn.CanEdit && !_busy;
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand)
            return;
        var text = QuantityEntry.Text?.Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
            || !WarehouseQuantityInput.IsSupported(quantity)
            || quantity < 0)
        {
            ErrorLabel.Text = "Введите неотрицательное количество, не более трёх знаков после запятой.";
            return;
        }
        _hideManualSkuSelectionAfterSave = true;
        await ExecuteAsync(
            () => _process.SetLicensePlateNumberItemQuantityAsync(_orderId, _lpnId, _skuId, quantity),
            "Ответ сервера не получен. Повторите сохранение того же количества.");
    }

    private async void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (_busy || _process.HasPendingCommand)
            return;
        if (!await DisplayAlertAsync(
                "Удалить товар", "Удалить товар из состава палеты?", "Удалить", "Назад"))
            return;
        _hideManualSkuSelectionAfterSave = false;
        await ExecuteAsync(
            () => _process.RemoveLicensePlateNumberItemAsync(_orderId, _lpnId, _skuId),
            "Ответ сервера не получен. Повторите удаление этого же товара.");
    }

    private async Task ExecuteAsync(
        Func<Task<MobileReceivingOrderCommandResponse>> mutation,
        string uncertainMessage)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            await mutation();
            if (_hideManualSkuSelectionAfterSave)
                Navigation.NavigationStack.OfType<ReceivingOrderLicensePlateNumberPage>()
                    .LastOrDefault()?.HideManualSkuSelection();
            _hideManualSkuSelectionAfterSave = false;
            QuantityEntry.Unfocus();
            await QuantityEntry.HideSoftInputAsync(CancellationToken.None);
            await Navigation.PopAsync();
        }
        catch (MobileApiException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            ErrorLabel.Text = uncertainMessage;
        }
        catch (InvalidOperationException exception)
        {
            ErrorLabel.Text = exception.Message;
        }
        finally
        {
            if (!_process.HasPendingCommand)
                _hideManualSkuSelectionAfterSave = false;
            SetBusy(false);
        }
    }


    private async void OnRetryCommandClicked(object? sender, EventArgs e)
    {
        if (_busy || !_process.HasPendingCommand)
            return;
        await ExecuteAsync(_process.RetryAsync, "Ответ не получен. Повторите сохранённую команду.");
    }

    private void UpdateRecoveryControls()
    {
        RetryCommandButton.IsVisible = _process.HasPendingCommand;
        RetryCommandButton.IsEnabled = !_busy;
        RetryCommandButton.Text = $"Повторить: {_process.PendingLabel}";
        NavigationPage.SetHasBackButton(this, !_busy && !_process.HasPendingCommand);
    }

    protected override bool OnBackButtonPressed()
    {
        if (_busy || _process.HasPendingCommand)
        {
            ErrorLabel.Text = "Сначала получите результат текущей команды.";
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        bool canEdit = _details?.LicensePlateNumbers.SingleOrDefault(x => x.Id == _lpnId)?.CanEdit == true;
        ApplyButton.IsEnabled = !busy && !_process.HasPendingCommand && canEdit;
        RemoveButton.IsEnabled = ApplyButton.IsEnabled;
        QuantityEntry.IsEnabled = ApplyButton.IsEnabled;
        UpdateRecoveryControls();
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
            AndroidFocus.Suppress(element);
    }
}
