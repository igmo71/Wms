using Microsoft.Extensions.DependencyInjection;
using Wms.Contracts.Mobile.V1;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class ReceivingOrderSkuDistributionPage : ContentPage
{
    private readonly MobileReceivingOrderClient _orders;
    private readonly IServiceProvider _services;
    private ReceivingOrderReceivingProcess _process = null!;
    private Guid _orderId;
    private Guid _skuId;
    private bool _busy;

    public ReceivingOrderSkuDistributionPage(MobileReceivingOrderClient orders,
        IServiceProvider services)
    {
        InitializeComponent();
        _orders = orders;
        _services = services;
    }

    public IReadOnlyList<ReceivingOrderSkuLpnRow> LpnRows { get; private set; } = [];

    public void Show(Guid orderId, Guid skuId, ReceivingOrderReceivingProcess process)
    {
        _orderId = orderId;
        _skuId = skuId;
        _process = process;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        SkuRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var order = await _orders.GetAsync(_orderId);
            var item = order.Items.SingleOrDefault(x => x.SkuId == _skuId)
                ?? throw new InvalidOperationException("SKU отсутствует в ордере.");
            SkuNameLabel.Text = item.SkuName ?? item.SkuId.ToString();
            SkuCodeLabel.Text = item.SkuCode;
            PlanLabel.Text = item.PlanQuantity?.ToString("0.###") ?? "?";
            FactLabel.Text = ReceivingStatusSymbols.FactText(item.PlanQuantity, item.FactQuantity);
            SemanticProperties.SetDescription(FactLabel,
                ReceivingStatusSymbols.FactDescription(item.PlanQuantity, item.FactQuantity));
            UnitLabel.Text = item.UnitOfMeasure ?? "—";
            LpnRows = order.Lpns.Where(x => x.Status is not
                (MobileLicensePlateNumberStatus.Cancelled or MobileLicensePlateNumberStatus.Deleted))
                .Select(x => new { Lpn = x, Item = x.Items.SingleOrDefault(y => y.SkuId == _skuId) })
                .Where(x => x.Item is not null)
                .Select(x =>
                {
                    var status = ReceivingStatusSymbols.ForLpn(x.Lpn.Status);
                    return new ReceivingOrderSkuLpnRow(x.Lpn.Id, x.Lpn.Code,
                        status.Glyph, $"Статус LPN: {status.Description}",
                        x.Lpn.ResponsibleUserId ?? "—",
                        $"{x.Item!.Quantity:0.###} {item.UnitOfMeasure ?? "—"}");
                })
                .ToArray();
            OnPropertyChanged(nameof(LpnRows));
        }
        catch (MobileApiException ex) { ErrorLabel.Text = ex.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось обновить SKU."; }
        catch (InvalidOperationException ex) { ErrorLabel.Text = ex.Message; }
        finally { _busy = false; }
    }

    private async void OnLpnTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _process.HasPendingCommand || e.Parameter is not Guid id) return;
        var page = _services.GetRequiredService<ReceivingOrderLicensePlateNumberPage>();
        page.Show(_orderId, id, _process);
        await Navigation.PushAsync(page);
    }
}

public sealed record ReceivingOrderSkuLpnRow(Guid LicensePlateNumberId,
    string Code, string StatusGlyph, string StatusDescription,
    string ResponsibleUserName, string QuantityText);
