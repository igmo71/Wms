using Microsoft.AspNetCore.Components;
using Wms.Application;
using Wms.Application.ReceivingOrders;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ReceivingOrderPages;

public partial class Details
{
    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private ReceivingOrderQueryService OrderQueryService { get; set; } = null!;
    [Inject]
    private ReceivingOrderCommandService OrderCommandService { get; set; } = null!;
    [Inject]
    private StorageLocationService StorageLocationService { get; set; } = null!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = null!;

    private ReceivingOrder? _order;
    private List<StorageLocation> _storageLocations = [];
    private Guid? _receivingLocationId;
    private bool _isLoading = true;
    private bool _isStarting;
    private bool _startOrderFailed;
    private string? _errorMessage;

    protected override async Task OnParametersSetAsync()
    {
        _isLoading = true;

        var orderTask = OrderQueryService.GetOrderAsync(Id);
        var storageLocationsTask = StorageLocationService.ListAsync(new ListQuery
        {
            SortBy = "Name",
            Take = int.MaxValue
        });

        await Task.WhenAll(orderTask, storageLocationsTask);

        _order = await orderTask;
        _storageLocations = (await storageLocationsTask).Items;
        _receivingLocationId = _order?.ReceivingLocationId;
        _isLoading = false;
    }

    private static string FormatDateTimeOffset(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

    private Task OnReceivingLocationChanged(Guid? receivingLocationId)
    {
        _receivingLocationId = receivingLocationId;
        return Task.CompletedTask;
    }

    private async Task StartOrderAsync()
    {
        if (_receivingLocationId is not Guid receivingLocationId)
            return;

        _isStarting = true;
        _startOrderFailed = false;

        try
        {
            var setLocationResult = await OrderCommandService.SetReceivingLocationAsync(Id, receivingLocationId);
            if (!setLocationResult.IsSuccess)
            {
                _startOrderFailed = true;
                _errorMessage = setLocationResult.Error?.Message ?? "Не удалось сохранить место приёмки";
                return;
            }

            var result = await OrderCommandService.StartOrderAsync(Id);
            if (result.IsSuccess)
                NavigationManager.NavigateTo($"receiving-orders/{Id}/in-process");
            else
            {
                _startOrderFailed = true;
                _errorMessage = result.Error?.Message ?? "Не удалось взять ордер в работу";
            }
        }
        catch
        {
            _startOrderFailed = true;
        }
        finally
        {
            _isStarting = false;
        }
    }
}
