using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Wms.Application.Reports;

namespace Wms.WebApp.Components.Pages.ReportPages.StockReconciliation;

public partial class Index : IDisposable
{
    [Inject] private GetStockReconciliation Report { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private ILogger<Index> Logger { get; set; } = null!;

    private readonly CancellationTokenSource _lifetime = new();
    private StockReconciliationSnapshot? _report;
    private IReadOnlyList<StockReconciliationRow> _visibleRows = [];
    private Guid? _warehouseId;
    private string? _search;
    private bool _showAll;
    private bool _loading;
    private int _page;
    private string? _error;

    private string EmptyMessage => _showAll || !string.IsNullOrWhiteSpace(_search)
        ? "Нет строк по заданным фильтрам."
        : "Расхождений нет.";

    private async Task LoadAsync()
    {
        if (_loading)
            return;

        _loading = true;
        _error = null;
        var ct = _lifetime.Token;
        try
        {
            var user = (await Authentication.GetAuthenticationStateAsync()).User;
            var result = await Report.ExecuteAsync(user.FindFirstValue(ClaimTypes.NameIdentifier), ct);
            ct.ThrowIfCancellationRequested();
            if (!result.IsSuccess)
            {
                ShowError(result.Error!.Message);
                return;
            }

            var report = result.Value!;
            _report = report with
            {
                Warehouses = report.Warehouses.OrderBy(warehouse => warehouse.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(warehouse => warehouse.Id).ToArray(),
                Rows = report.Rows.OrderBy(row => row.WarehouseName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(row => row.WarehouseId).ThenBy(row => row.SkuId).ToArray()
            };

            if (_warehouseId is Guid warehouseId && !_report.Warehouses.Any(warehouse => warehouse.Id == warehouseId))
                _warehouseId = null;
            ApplyFilters();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Failed to generate the stock reconciliation report.");
            ShowError("Не удалось сформировать отчёт. Повторите попытку.");
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyFilters()
    {
        if (_report is not { } report)
            return;

        var search = _search?.Trim();
        _visibleRows = report.Rows.Where(row =>
            (_warehouseId is null || row.WarehouseId == _warehouseId)
            && (_showAll || row.Difference != 0m)
            && (string.IsNullOrEmpty(search)
                || row.SkuName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.SkuCode?.Contains(search, StringComparison.OrdinalIgnoreCase) == true)).ToArray();
        _page = 0;
    }

    private void ShowError(string message)
    {
        _report = null;
        _visibleRows = [];
        _error = message;
    }

    private static string RowStyle(StockReconciliationRow row, int _) => row.Difference != 0m
        ? "background-color: var(--mud-palette-error-hover);"
        : string.Empty;

    private static string ReadTime(DateTimeOffset timestamp) =>
        timestamp.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
