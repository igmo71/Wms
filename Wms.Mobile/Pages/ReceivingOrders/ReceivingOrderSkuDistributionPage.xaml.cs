using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderSkuDistributionPage : ContentPage
{
    private readonly MobileReceivingOrderClient _orderClient;
    private readonly IServiceProvider _services;
    private Guid _orderId;
    private Guid _skuId;
    private bool _busy;

    public ReceivingOrderSkuDistributionPage(
        MobileReceivingOrderClient orderClient,
        IServiceProvider services)
    {
        InitializeComponent();
        _orderClient = orderClient;
        _services = services;
    }

    public IReadOnlyList<ReceivingOrderSkuPalletRow> PalletRows { get; private set; } = [];

    public void Show(Guid orderId, Guid skuId)
    {
        _orderId = orderId;
        _skuId = skuId;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync(showErrors: true);
    }

    private async void OnRefreshing(object? sender, EventArgs e) =>
        await RefreshAsync(showErrors: true);

    private async Task RefreshAsync(bool showErrors)
    {
        if (_busy)
        {
            SkuRefreshView.IsRefreshing = false;
            return;
        }
        _busy = true;
        if (showErrors) ErrorLabel.Text = string.Empty;
        try
        {
            ApplyDetails(await _orderClient.GetAsync(_orderId));
        }
        catch (MobileApiException exception)
        {
            if (showErrors) ErrorLabel.Text = exception.Message;
        }
        catch (HttpRequestException)
        {
            if (showErrors) ErrorLabel.Text = "Сервер WMS недоступен.";
        }
        catch (InvalidOperationException exception)
        {
            if (showErrors) ErrorLabel.Text = exception.Message;
        }
        finally
        {
            _busy = false;
            SkuRefreshView.IsRefreshing = false;
        }
    }

    private void ApplyDetails(MobileReceivingOrderDetailsResponse details)
    {
        var lines = details.Lines.Where(x => x.StockKeepingUnitId == _skuId).ToArray();
        if (lines.Length == 0)
            throw new InvalidOperationException("Номенклатура отсутствует в плане приходного ордера.");

        var first = lines[0];
        var unit = first.UnitOfMeasure ?? string.Empty;
        SkuNameLabel.Text = first.SkuName;
        SkuCodeLabel.Text = first.SkuCode;
        PlanLabel.Text = lines.Sum(x => x.PlanQuantity).ToString("0.###");
        FactLabel.Text = lines.All(x => x.FactQuantity.HasValue)
            ? lines.Sum(x => x.FactQuantity!.Value).ToString("0.###")
            : "?";
        UnitLabel.Text = unit;
        PalletRows = details.LicensePlateNumbers
            .Where(lpn => lpn.Status != MobileLicensePlateNumberStatus.Cancelled)
            .Select(lpn => new
            {
                LicensePlateNumber = lpn,
                Item = lpn.Items.SingleOrDefault(x => x.StockKeepingUnitId == _skuId)
            })
            .Where(x => x.Item is not null)
            .Select(x => new ReceivingOrderSkuPalletRow(
                x.LicensePlateNumber.Id,
                x.LicensePlateNumber.Code,
                x.LicensePlateNumber.StatusName,
                x.LicensePlateNumber.ResponsibleUserName,
                string.IsNullOrWhiteSpace(unit)
                    ? $"{x.Item!.Quantity:g}"
                    : $"{x.Item!.Quantity:g} {unit}"))
            .ToArray();
        OnPropertyChanged(nameof(PalletRows));
    }

    private async void OnPalletTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || e.Parameter is not Guid lpnId)
            return;
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberPage>();
        page.Show(_orderId, lpnId);
        await Navigation.PushAsync(page);
    }
}

public sealed record ReceivingOrderSkuPalletRow(
    Guid LicensePlateNumberId,
    string Code,
    string StatusName,
    string ResponsibleUserName,
    string QuantityText);
