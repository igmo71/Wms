using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Reports.EmployeePerformance;
using Wms.Application.Users;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ReportPages.EmployeePerformance;

public partial class Index
{
    [SupplyParameterFromQuery] public Guid? WarehouseId { get; set; }
    [SupplyParameterFromQuery] public DateTime? DateFrom { get; set; }
    [SupplyParameterFromQuery] public DateTime? DateTo { get; set; }

    [Inject] private EmployeePerformanceReportService ReportService { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;

    private MudDataGrid<EmployeePerformanceSummaryItem> _dataGrid = null!;
    private Warehouse? _warehouse;
    private EmployeePerformanceUserOption? _user;
    private DateTime? _dateFrom = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _dateTo = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
        .AddMonths(1)
        .AddDays(-1);
    private EmployeePerformanceTotals _totals = new();
    private bool _parametersInitialized;

    protected override async Task OnParametersSetAsync()
    {
        if (_parametersInitialized)
            return;

        _parametersInitialized = true;
        _warehouse = WarehouseId is Guid warehouseId
            ? await WarehouseService.GetAsync(warehouseId)
            : null;

        if (DateFrom is not null)
            _dateFrom = DateFrom;

        if (DateTo is not null)
            _dateTo = DateTo;
    }

    private async Task<GridData<EmployeePerformanceSummaryItem>> LoadServerDataAsync(
        GridState<EmployeePerformanceSummaryItem> state,
        CancellationToken ct)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var result = await ReportService.GetSummaryAsync(new EmployeePerformanceSummaryQuery
        {
            WarehouseId = _warehouse?.Id,
            UserId = _user?.Id,
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, ct);

        _totals = result.Totals;

        return new GridData<EmployeePerformanceSummaryItem>
        {
            Items = result.Items,
            TotalItems = result.TotalItems
        };
    }

    private async Task<IEnumerable<Warehouse>> SearchWarehousesAsync(
        string? searchText,
        CancellationToken ct)
    {
        var result = await WarehouseService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            ExcludeDeleted = false,
            Take = 10
        }, ct);

        return result.Items;
    }

    private async Task<IEnumerable<EmployeePerformanceUserOption>> SearchUsersAsync(
        string? searchText,
        CancellationToken ct) =>
        await ReportService.SearchUsersAsync(new EmployeePerformanceUserSearchQuery
        {
            SearchString = searchText,
            WarehouseId = _warehouse?.Id,
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            Take = 10
        }, ct);

    private Task OnWarehouseChangedAsync(Warehouse? value)
    {
        _warehouse = value;
        return _dataGrid.ReloadServerData();
    }

    private Task OnUserChangedAsync(EmployeePerformanceUserOption? value)
    {
        _user = value;
        return _dataGrid.ReloadServerData();
    }

    private Task OnDateFromChangedAsync(DateTime? value)
    {
        _dateFrom = value;
        return _dataGrid.ReloadServerData();
    }

    private Task OnDateToChangedAsync(DateTime? value)
    {
        _dateTo = value;
        return _dataGrid.ReloadServerData();
    }

    private string GetDetailsHref(EmployeePerformanceSummaryItem item)
    {
        var href = $"reports/employee-performance/{Uri.EscapeDataString(item.UserId)}";
        var parameters = new List<string>
        {
            $"warehouseId={item.WarehouseId:D}"
        };

        if (_dateFrom is DateTime dateFrom)
            parameters.Add($"dateFrom={dateFrom:yyyy-MM-dd}");

        if (_dateTo is DateTime dateTo)
            parameters.Add($"dateTo={dateTo:yyyy-MM-dd}");

        return $"{href}?{string.Join('&', parameters)}";
    }
}
