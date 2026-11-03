using System.Security.Claims;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using Wms.Application.OpeningImports;
using Wms.Application.Warehouses;
using Wms.Common;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS;

namespace Wms.WebApp.Components.Pages.OpeningImportPages;

public partial class Index
{
    private const int PageSize = 20;

    [Inject] private WarehouseService WarehouseService { get; set; } = null!;
    [Inject] private PreviewOpeningTopology PreviewService { get; set; } = null!;
    [Inject] private ConfirmOpeningTopology ConfirmService { get; set; } = null!;
    [Inject] private PreviewOpeningStock StockPreviewService { get; set; } = null!;
    [Inject] private ConfirmOpeningStock StockConfirmService { get; set; } = null!;
    [Inject] private GetOpeningImportResult ResultService { get; set; } = null!;
    [Inject] private IOpeningMixedStockViolationStore MixedStockViolationStore { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = null!;

    private Warehouse? _warehouse;
    private byte[]? _workbook;
    private string? _sourceName;
    private OpeningTopologyPlan? _plan;
    private OpeningStockPlan? _stockPlan;
    private OpeningImportResult? _savedResult;
    private IReadOnlyDictionary<string, int> _currentMixedByAddress =
        new Dictionary<string, int>(StringComparer.Ordinal);
    private bool _loadingSavedResult;
    private bool _currentViolationsLoaded;
    private string? _resultError;
    private IReadOnlyList<OpeningStockRowPlan> _stockRows = [];
    private IReadOnlyDictionary<Guid, string> _stockSkuNames = new Dictionary<Guid, string>();
    private string _snapshotLocalText = string.Empty;
    private DateTimeOffset _snapshotAt;
    private IReadOnlyList<OpeningImportRowDecision> _specialDecisions = [];
    private Guid _requestId;
    private string? _error;
    private bool _busy;
    private bool _acknowledged;
    private bool _stockAcknowledged;
    private int _specialPage = 1;
    private int _issuePage = 1;
    private int _cellPage = 1;
    private int _stockRowPage = 1;
    private int _stockIssuePage = 1;
    private int _stockTotalPage = 1;

    private bool ConfirmDisabled => _busy || _loadingSavedResult || _plan is not { CanConfirm: true }
        || (_plan.Counts.ExcludedRows > 0 || _plan.Counts.CorrectedRows > 0) && !_acknowledged;
    private bool StockConfirmDisabled => _busy || _loadingSavedResult || _stockPlan is not { CanConfirm: true }
        || StockAcknowledgementRequired && !_stockAcknowledged;
    private bool StockAcknowledgementRequired => _stockPlan is { } plan
        && (plan.Counts.ExcludedRows > 0 || plan.Counts.CorrectedRows > 0
            || plan.Counts.NegativeRows > 0 || plan.Counts.MissingRequiredDates > 0
            || plan.Counts.PastDates > 0 || plan.Counts.ReviewedMixedCells > 0);

    private IReadOnlyList<OpeningImportRowDecision> VisibleSpecialDecisions =>
        Slice(_specialDecisions, _specialPage);
    private IReadOnlyList<OpeningTopologyIssue> VisibleIssues =>
        Slice(_plan?.Issues ?? [], _issuePage);
    private IReadOnlyList<OpeningTopologyCellPlan> VisibleCells =>
        Slice(_plan?.Cells ?? [], _cellPage);
    private IReadOnlyList<OpeningStockRowPlan> VisibleStockRows =>
        Slice(_stockRows, _stockRowPage);
    private IReadOnlyList<OpeningStockIssue> VisibleStockIssues =>
        Slice(_stockPlan?.Issues ?? [], _stockIssuePage);
    private IReadOnlyList<OpeningStockTotal> VisibleStockTotals =>
        Slice(_stockPlan?.Totals ?? [], _stockTotalPage);

    private int SpecialPageCount => Pages(_specialDecisions.Count);
    private int IssuePageCount => Pages(_plan?.Issues.Count ?? 0);
    private int CellPageCount => Pages(_plan?.Cells.Count ?? 0);
    private int StockRowPageCount => Pages(_stockRows.Count);
    private int StockIssuePageCount => Pages(_stockPlan?.Issues.Count ?? 0);
    private int StockTotalPageCount => Pages(_stockPlan?.Totals.Count ?? 0);
    private int StockCatalogIssueCount => _stockPlan?.Issues.Count(x =>
        x.BlocksConfirmation && (x.Message.Contains("номенклатура", StringComparison.OrdinalIgnoreCase)
        || x.Message.Contains("единица", StringComparison.OrdinalIgnoreCase)
        || x.Message.Contains("GUID", StringComparison.OrdinalIgnoreCase))) ?? 0;
    private IReadOnlyList<(string Reason, int Count)> StockBlockingReasons =>
        _stockPlan?.Issues.Where(x => x.BlocksConfirmation)
            .GroupBy(x => x.Message, StringComparer.Ordinal)
            .Select(x => (Reason: x.Key, Count: x.Count()))
            .OrderByDescending(x => x.Count).ThenBy(x => x.Reason, StringComparer.Ordinal)
            .ToArray() ?? [];
    private IReadOnlyList<OpeningImportExclusion> ZeroPrefixExclusions =>
        _savedResult?.Exclusions.Where(x => (x.Address is "0A00A0" or "0A00A1")
            && x.Quantity != 0).ToArray() ?? [];

    private async Task<IEnumerable<Warehouse>> SearchWarehousesAsync(string? searchText, CancellationToken ct)
    {
        var result = await WarehouseService.ListAsync(new ListQuery
        {
            SearchString = searchText,
            SortBy = "Name",
            Take = 20
        }, ct);
        return result.Items;
    }

    private async Task OnWarehouseChangedAsync(Warehouse? warehouse)
    {
        _warehouse = warehouse;
        _workbook = null;
        _sourceName = null;
        _snapshotLocalText = string.Empty;
        _savedResult = null;
        _resultError = null;
        InvalidatePreview();
        if (warehouse is not null)
            await LoadSavedResultAsync();
    }

    private async Task LoadSavedResultAsync()
    {
        if (_warehouse is null)
            return;
        var warehouseId = _warehouse.Id;

        _loadingSavedResult = true;
        _resultError = null;
        _savedResult = null;
        _currentViolationsLoaded = false;
        _currentMixedByAddress = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var result = await ResultService.ExecuteAsync(warehouseId);
            if (_warehouse?.Id != warehouseId)
                return;
            if (!result.IsSuccess)
            {
                if (result.Error?.Type != OperationErrorType.NotFound)
                    _resultError = result.Error?.Message ?? "Не удалось прочитать протокол загрузки.";
                return;
            }
            _savedResult = result.Value!;
            if (_savedResult.Stock is not null)
            {
                var violations = await MixedStockViolationStore.LoadViolationsAsync(
                    warehouseId, CancellationToken.None);
                if (_warehouse?.Id != warehouseId)
                    return;
                _currentMixedByAddress = violations.ToDictionary(
                    x => x.Address, x => x.DistinctSkuCount, StringComparer.Ordinal);
                _currentViolationsLoaded = true;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _resultError = "Не удалось прочитать сохранённый протокол или текущее состояние ячеек.";
        }
        finally
        {
            _loadingSavedResult = false;
        }
    }

    private string CurrentMixingState(string address) => !_currentViolationsLoaded
        ? "Текущее состояние не проверено"
        : _currentMixedByAddress.TryGetValue(address, out var count)
            ? $"Нарушение сохраняется: {count} SKU"
            : "Нарушение устранено";

    private static string MoscowTime(DateTimeOffset value) =>
        value.ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM.yyyy HH:mm:ss");

    private static string MoscowTime(DateTimeOffset? value) =>
        value is DateTimeOffset actual ? MoscowTime(actual) : "—";

    private static string ImportStatusName(WarehouseOpeningImportStatus status) => status switch
    {
        WarehouseOpeningImportStatus.TopologyImported => "Топология загружена; остатки ожидают загрузки",
        WarehouseOpeningImportStatus.StockImported => "Топология и остатки загружены",
        _ => "Неизвестный статус"
    };

    private async Task UploadAsync(InputFileChangeEventArgs args)
    {
        _busy = true;
        _error = null;
        _sourceName = null;
        _workbook = null;
        _snapshotLocalText = string.Empty;
        InvalidatePreview();
        try
        {
            var file = args.File;
            if (!file.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                || file.Size is <= 0 or > OpeningExportReader.MaximumFileBytes
                || file.Name.Length > WarehouseOpeningImport.MaximumSourceNameLength)
            {
                _error = $"Выберите XLSX-файл до {OpeningExportReader.MaximumFileBytes / 1_000_000} МБ.";
                return;
            }

            await using var stream = file.OpenReadStream(OpeningExportReader.MaximumFileBytes);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            _workbook = buffer.ToArray();
            _sourceName = file.Name;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _error = "Не удалось прочитать XLSX-файл. Выберите выгрузку ещё раз.";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task PreviewAsync()
    {
        if (_warehouse is null || _workbook is null)
            return;
        _busy = true;
        _error = null;
        InvalidatePreview();
        try
        {
            _plan = await PreviewService.BuildPlanAsync(_warehouse.Id, _workbook);
            _specialDecisions = _plan.Decisions.Where(x => x.Outcome is
                    OpeningImportRowOutcome.Excluded or OpeningImportRowOutcome.Corrected
                    or OpeningImportRowOutcome.Blocking)
                .ToArray();
            _requestId = Guid.NewGuid();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _error = "Не удалось проверить выгрузку. Проверьте файл и соединение с базой.";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ConfirmAsync()
    {
        if (_warehouse is null || ConfirmDisabled || _workbook is null || _sourceName is null || _plan is null)
            return;
        _busy = true;
        _error = null;
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                _error = "Не удалось определить текущего пользователя.";
                return;
            }
            var result = await ConfirmService.ExecuteAsync(new ConfirmOpeningTopologyCommand(
                _requestId, _warehouse.Id, _sourceName, _workbook, _plan.Signature, userId));
            if (result.IsSuccess)
            {
                InvalidatePreview();
                await LoadSavedResultAsync();
                return;
            }
            _error = result.Error?.Message ?? "Загрузка топологии не выполнена.";
            if (result.Error?.Type == OperationErrorType.Conflict)
                InvalidatePreview();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The save outcome may be uncertain; retain the request id for replay.
            _error = "Не удалось получить результат. Повторите подтверждение с тем же файлом.";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task PreviewStockAsync()
    {
        if (_warehouse is null || _workbook is null)
            return;
        _busy = true;
        _error = null;
        InvalidatePreview();
        try
        {
            if (!DateTime.TryParseExact(_snapshotLocalText,
                    ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"],
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime))
            {
                _error = "Укажите дату и время выгрузки начальных остатков.";
                return;
            }

            _snapshotAt = new DateTimeOffset(localTime, TimeSpan.FromHours(3));
            _stockPlan = await StockPreviewService.BuildPlanAsync(
                _warehouse.Id, _workbook, _snapshotAt);
            _stockRows = _stockPlan.Rows.Where(x => x.StockKeepingUnitId is not null
                || x.Quantity is not null || x.Outcome != OpeningImportRowOutcome.Accepted)
                .ToArray();
            _stockSkuNames = _stockPlan.Totals
                .GroupBy(x => x.StockKeepingUnitId)
                .ToDictionary(x => x.Key, x => x.First().SkuName);
            _requestId = Guid.NewGuid();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _error = "Не удалось проверить остатки. Проверьте файл и соединение с базой.";
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnSnapshotChanged(ChangeEventArgs args)
    {
        _snapshotLocalText = args.Value?.ToString() ?? string.Empty;
        InvalidateStockPreview();
    }

    private async Task ConfirmStockAsync()
    {
        if (_warehouse is null || StockConfirmDisabled || _workbook is null || _stockPlan is null)
            return;
        _busy = true;
        _error = null;
        try
        {
            var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var userId = state.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is null)
            {
                _error = "Не удалось определить текущего пользователя.";
                return;
            }
            var result = await StockConfirmService.ExecuteAsync(new ConfirmOpeningStockCommand(
                _requestId, _warehouse.Id, _workbook, _snapshotAt,
                _stockPlan.Signature, userId));
            if (result.IsSuccess)
            {
                InvalidatePreview();
                await LoadSavedResultAsync();
                return;
            }
            _error = result.Error?.Message ?? "Загрузка начальных остатков не выполнена.";
            if (result.Error?.Type == OperationErrorType.Conflict)
                InvalidateStockPreview();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The save outcome may be uncertain; retain the request id for replay.
            _error = "Не удалось получить результат. Повторите подтверждение с тем же файлом.";
        }
        finally
        {
            _busy = false;
        }
    }

    private void InvalidatePreview()
    {
        _plan = null;
        _specialDecisions = [];
        _acknowledged = false;
        _specialPage = 1;
        _issuePage = 1;
        _cellPage = 1;
        InvalidateStockPreview();
    }

    private void InvalidateStockPreview()
    {
        _stockPlan = null;
        _stockRows = [];
        _stockSkuNames = new Dictionary<Guid, string>();
        _snapshotAt = default;
        _stockAcknowledged = false;
        _stockRowPage = 1;
        _stockIssuePage = 1;
        _stockTotalPage = 1;
        _requestId = Guid.Empty;
    }

    private string StockSkuName(Guid? skuId) => skuId is Guid id
        ? _stockSkuNames.GetValueOrDefault(id, id.ToString()) : "—";

    private void ChangeSpecialPage(int offset) =>
        _specialPage = Math.Clamp(_specialPage + offset, 1, SpecialPageCount);
    private void ChangeIssuePage(int offset) =>
        _issuePage = Math.Clamp(_issuePage + offset, 1, IssuePageCount);
    private void ChangeCellPage(int offset) =>
        _cellPage = Math.Clamp(_cellPage + offset, 1, CellPageCount);
    private void ChangeStockRowPage(int offset) =>
        _stockRowPage = Math.Clamp(_stockRowPage + offset, 1, StockRowPageCount);
    private void ChangeStockIssuePage(int offset) =>
        _stockIssuePage = Math.Clamp(_stockIssuePage + offset, 1, StockIssuePageCount);
    private void ChangeStockTotalPage(int offset) =>
        _stockTotalPage = Math.Clamp(_stockTotalPage + offset, 1, StockTotalPageCount);

    private static int Pages(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);
    private static IReadOnlyList<T> Slice<T>(IReadOnlyList<T> source, int page) =>
        source.Skip((page - 1) * PageSize).Take(PageSize).ToArray();

    private static string OutcomeName(OpeningImportRowOutcome outcome) => outcome switch
    {
        OpeningImportRowOutcome.Corrected => "Исправлено",
        OpeningImportRowOutcome.Excluded => "Исключено",
        OpeningImportRowOutcome.Blocking => "Ошибка",
        _ => "Принято"
    };

    private static string PurposeName(ZoneType type) => type switch
    {
        ZoneType.Storage => "Комплектация",
        ZoneType.Reserve => "Хранение",
        ZoneType.Receiving => "Приёмка",
        ZoneType.Shipping => "Отгрузка",
        _ => type.ToString()
    };
}
