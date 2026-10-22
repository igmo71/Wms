using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderLicensePlateNumberQuantityPage : ContentPage
{
    private readonly MobileReceivingOrderClient _orders;
    private ReceivingOrderReceivingProcess _process = null!;
    private Guid _orderId;
    private Guid _lpnId;
    private Guid _skuId;
    private bool _busy;

    public ReceivingOrderLicensePlateNumberQuantityPage(MobileReceivingOrderClient orders)
    {
        InitializeComponent();
        _orders = orders;
    }

    public void Show(Guid orderId, Guid lpnId, Guid skuId, ReceivingOrderReceivingProcess process)
    {
        _orderId = orderId;
        _lpnId = lpnId;
        _skuId = skuId;
        _process = process;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var order = await _orders.GetAsync(_orderId);
            var sku = order.Items.SingleOrDefault(x => x.SkuId == _skuId)
                ?? throw new InvalidOperationException("SKU отсутствует в ордере.");
            var lpn = order.Lpns.SingleOrDefault(x => x.Id == _lpnId)
                ?? throw new InvalidOperationException("LPN отсутствует в ордере.");
            var item = lpn.Items.SingleOrDefault(x => x.SkuId == _skuId);
            SkuNameLabel.Text = sku.SkuName ?? sku.SkuId.ToString();
            SkuCodeLabel.Text = sku.SkuCode;
            PlanLabel.Text = Format(sku.PlanQuantity);
            FactLabel.Text = ReceivingStatusSymbols.FactText(sku.PlanQuantity, sku.FactQuantity);
            SemanticProperties.SetDescription(FactLabel,
                ReceivingStatusSymbols.FactDescription(sku.PlanQuantity, sku.FactQuantity));
            CurrentLabel.Text = item is null ? "нет" : Format(item.Quantity);
            UnitLabel.Text = sku.UnitOfMeasure ?? "—";
            QuantityEntry.Text = item?.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
            ExpirationPanel.IsVisible = sku.TracksExpirationDate || item?.ExpirationDate is not null;
            ExpirationHintLabel.Text = sku.TracksExpirationDate
                ? "Для закрытия LPN с положительным количеством укажите срок годности."
                : "Сохранённый срок годности не удаляется при выключении его учёта.";
            ExpirationSwitch.IsToggled = item?.ExpirationDate is not null;
            if (item?.ExpirationDate is DateOnly date)
                ExpirationPicker.Date = date.ToDateTime(TimeOnly.MinValue);
            else
                ExpirationPicker.Date = DateTime.Today;
            ExpirationPicker.IsVisible = ExpirationSwitch.IsToggled;
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось загрузить SKU и LPN."; }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { SetBusy(false); }
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
        if (_busy || _process.HasPendingCommand) return;
        var text = (QuantityEntry.Text ?? string.Empty).Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var quantity)
            || !WarehouseQuantityInput.IsSupported(quantity) || quantity <= 0)
        {
            ErrorLabel.Text = "Введите положительное количество с точностью до трёх знаков.";
            return;
        }
        DateOnly? expiration = null;
        if (ExpirationPanel.IsVisible && ExpirationSwitch.IsToggled)
        {
            if (ExpirationPicker.Date is not DateTime date)
            {
                ErrorLabel.Text = "Выберите дату срока годности.";
                return;
            }
            expiration = DateOnly.FromDateTime(date);
        }
        await ExecuteAsync(() => _process.EditLpnAsync(
            _orderId, _lpnId, _skuId, quantity, expiration));
    }

    private async Task ExecuteAsync(Func<Task<MobileReceivingWorkOrderResponse>> command)
    {
        SetBusy(true);
        ErrorLabel.Text = string.Empty;
        try
        {
            await command();
            await Navigation.PopAsync();
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Ответ не подтверждён. Повторите то же изменение LPN."; }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async void OnRetryCommandClicked(object? sender, EventArgs e)
    {
        if (!_busy && _process.HasPendingCommand)
            await ExecuteAsync(_process.RetryAsync);
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_busy && !_process.HasPendingCommand) return base.OnBackButtonPressed();
        ErrorLabel.Text = "Сначала завершите повтор команды.";
        return true;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.IsVisible = busy;
        QuantityEntry.IsEnabled = !busy && !_process.HasPendingCommand;
        ExpirationSwitch.IsEnabled = QuantityEntry.IsEnabled;
        ExpirationPicker.IsEnabled = QuantityEntry.IsEnabled;
        ApplyButton.IsEnabled = !busy && !_process.HasPendingCommand;
        RetryCommandButton.IsVisible = _process.HasPendingCommand;
        RetryCommandButton.IsEnabled = !busy;
        NavigationPage.SetHasBackButton(this, !busy && !_process.HasPendingCommand);
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element) AndroidFocus.Suppress(element);
    }

    private static string Format(decimal? value) => value?.ToString("0.###") ?? "?";
}
