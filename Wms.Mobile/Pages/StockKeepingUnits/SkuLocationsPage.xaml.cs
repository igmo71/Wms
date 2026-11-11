using System.Globalization;
using Wms.Mobile.Services;

namespace Wms.Mobile;

public partial class SkuLocationsPage : ContentPage
{
    private readonly MobileStockAvailabilityClient _client;
    private Guid _warehouseId;
    private Guid _skuId;
    private bool _busy;

    public SkuLocationsPage(MobileStockAvailabilityClient client)
    {
        InitializeComponent();
        _client = client;
        BindingContext = this;
    }

    public IReadOnlyList<SkuLocationRow> Locations { get; private set; } = [];

    public void Show(Guid warehouseId, Guid skuId, string? skuName,
        string? unit = null)
    {
        _warehouseId = warehouseId;
        _skuId = skuId;
        SkuNameLabel.Text = skuName ?? skuId.ToString();
        UnitLabel.Text = string.IsNullOrWhiteSpace(unit)
            ? "Ед. изм. неизвестна" : $"Ед. изм.: {unit}";
        Locations = [];
        OnPropertyChanged(nameof(Locations));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await RefreshAsync();
        LocationsRefreshView.IsRefreshing = false;
    }

    private async Task RefreshAsync()
    {
        if (_busy || _warehouseId == Guid.Empty || _skuId == Guid.Empty) return;
        _busy = true;
        ProgressIndicator.IsVisible = true;
        ErrorLabel.Text = string.Empty;
        try
        {
            var response = await _client.GetAsync(_warehouseId, _skuId);
            if (response.WarehouseId != _warehouseId || response.SkuId != _skuId
                || response.Locations is null)
                throw new HttpRequestException("Сервер вернул остатки другого SKU или склада.");
            Locations = response.Locations.Select(x => new SkuLocationRow(
                x.LocationCode,
                x.ExpirationDate?.ToString("dd.MM.yyyy") ?? "Срок не указан",
                $"Доступно {x.AvailableQuantity.ToString("0.###", CultureInfo.CurrentCulture)}",
                x.AccountingQuantity == x.AvailableQuantity ? string.Empty
                    : $"Учёт {x.AccountingQuantity.ToString("0.###", CultureInfo.CurrentCulture)}",
                x.AccountingQuantity != x.AvailableQuantity)).ToArray();
            OnPropertyChanged(nameof(Locations));
            EmptyLabel.IsVisible = Locations.Count == 0;
        }
        catch (MobileApiException exception) { ErrorLabel.Text = exception.Message; }
        catch (HttpRequestException) { ErrorLabel.Text = "Не удалось загрузить остатки SKU."; }
        finally { _busy = false; ProgressIndicator.IsVisible = false; }
    }
}

public sealed record SkuLocationRow(string LocationCode, string ExpirationText,
    string AvailableText, string AccountingText, bool HasDifferentAccounting);
