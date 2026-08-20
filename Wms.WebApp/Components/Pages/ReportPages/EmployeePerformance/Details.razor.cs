using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.Reports.EmployeePerformance;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.ReportPages.EmployeePerformance;

public partial class Details
{
    [Parameter] public string UserId { get; set; } = string.Empty;
    [SupplyParameterFromQuery] public Guid? WarehouseId { get; set; }
    [SupplyParameterFromQuery] public DateTime? DateFrom { get; set; }
    [SupplyParameterFromQuery] public DateTime? DateTo { get; set; }

    [Inject] private EmployeePerformanceReportService ReportService { get; set; } = null!;
    [Inject] private WarehouseService WarehouseService { get; set; } = null!;

    private MudDataGrid<EmployeePerformanceDocumentItem> _dataGrid = null!;
    private string _userName = string.Empty;
    private string? _searchString;
    private Warehouse? _warehouse;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private EmployeePerformanceDocumentType? _documentType;
    private bool _parametersInitialized;

    protected override async Task OnParametersSetAsync()
    {
        if (_parametersInitialized)
            return;

        _parametersInitialized = true;
        _userName = await ReportService.GetUserNameAsync(UserId);
        _warehouse = WarehouseId is Guid warehouseId
            ? await WarehouseService.GetAsync(warehouseId)
            : null;
        _dateFrom = DateFrom ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _dateTo = DateTo ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
            .AddMonths(1)
            .AddDays(-1);
    }

    private async Task<GridData<EmployeePerformanceDocumentItem>> LoadServerDataAsync(
        GridState<EmployeePerformanceDocumentItem> state,
        CancellationToken ct)
    {
        var sortDefinition = state.SortDefinitions.FirstOrDefault();
        var result = await ReportService.GetDetailsAsync(new EmployeePerformanceDetailsQuery
        {
            UserId = UserId,
            SearchString = _searchString,
            WarehouseId = _warehouse?.Id,
            DateFrom = _dateFrom,
            DateTo = _dateTo,
            DocumentType = _documentType,
            SortBy = sortDefinition?.SortBy,
            SortDescending = sortDefinition?.Descending ?? true,
            Skip = state.Page * state.PageSize,
            Take = state.PageSize
        }, ct);

        return new GridData<EmployeePerformanceDocumentItem>
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

    private Task OnSearchChangedAsync(string value)
    {
        _searchString = value;
        return _dataGrid.ReloadServerData();
    }

    private Task OnWarehouseChangedAsync(Warehouse? value)
    {
        _warehouse = value;
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

    private Task OnDocumentTypeChangedAsync(EmployeePerformanceDocumentType? value)
    {
        _documentType = value;
        return _dataGrid.ReloadServerData();
    }

    private string GetSummaryHref()
    {
        var parameters = new List<string>();

        if (_warehouse is not null)
            parameters.Add($"warehouseId={_warehouse.Id:D}");

        if (_dateFrom is DateTime dateFrom)
            parameters.Add($"dateFrom={dateFrom:yyyy-MM-dd}");

        if (_dateTo is DateTime dateTo)
            parameters.Add($"dateTo={dateTo:yyyy-MM-dd}");

        return parameters.Count == 0
            ? "reports/employee-performance"
            : $"reports/employee-performance?{string.Join('&', parameters)}";
    }

    private static string GetDocumentHref(EmployeePerformanceDocumentItem item) =>
        item.DocumentType == EmployeePerformanceDocumentType.Receiving
            ? $"receiving-orders/{item.DocumentId}"
            : $"shipping-orders/{item.DocumentId}";

    private static string GetDocumentTypeName(EmployeePerformanceDocumentType documentType) =>
        documentType == EmployeePerformanceDocumentType.Receiving
            ? "Приемка"
            : "Комплектация";

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
