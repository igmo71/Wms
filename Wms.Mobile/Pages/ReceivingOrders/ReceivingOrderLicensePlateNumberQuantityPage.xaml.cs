using System.Globalization;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderLicensePlateNumberQuantityPage : ContentPage
{
    private readonly ReceivingOrderReceivingProcess _process;
    private readonly MobileReceivingOrderClient _orderClient;
    private Guid _orderId;
    private Guid _lpnId;
    private Guid _skuId;
    private MobileReceivingOrderDetailsResponse? _details;
    private bool _busy;

    public ReceivingOrderLicensePlateNumberQuantityPage(
        ReceivingOrderReceivingProcess process,
        MobileReceivingOrderClient orderClient)
    {
        InitializeComponent();
        _process = process;
        _orderClient = orderClient;
    }

    public void Show(Guid orderId, Guid lpnId, Guid skuId)
    {
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
        if (_busy || (_process.HasPendingCommand && !_process.IsLpnMutationPending))
            return;
        var text = QuantityEntry.Text?.Trim().Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity)
            || !WarehouseQuantityInput.IsSupported(quantity)
            || quantity < 0)
        {
            ErrorLabel.Text = "Введите неотрицательное количество, не более трёх знаков после запятой.";
            return;
        }
        await ExecuteAsync(
            () => _process.SetLicensePlateNumberItemQuantityAsync(_orderId, _lpnId, _skuId, quantity),
            "Ответ сервера не получен. Повторите сохранение того же количества.");
    }

    private async void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (_busy || (_process.HasPendingCommand && !_process.IsLpnMutationPending))
            return;
        if (!_process.IsLpnMutationPending && !await DisplayAlertAsync(
                "Удалить товар", "Удалить товар из состава палеты?", "Удалить", "Назад"))
            return;
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
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ProgressIndicator.Opacity = busy ? 1 : 0;
        ApplyButton.IsEnabled = !busy;
        RemoveButton.IsEnabled = !busy;
    }

    private void OnNonScanControlLoaded(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
            AndroidFocus.Suppress(element);
    }
}
