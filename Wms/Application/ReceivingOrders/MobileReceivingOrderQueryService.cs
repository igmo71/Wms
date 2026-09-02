using Microsoft.EntityFrameworkCore;
using Wms.Application.Parties;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS;

namespace Wms.Application.ReceivingOrders;

public sealed class MobileReceivingOrderQueryService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    PartyQueryService partyQueryService)
{
    public async Task<MobileReceivingOrderWorkQueue> GetWorkQueueAsync(
        Guid warehouseId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var orders = await QueueOrderQuery(dbContext)
            .Where(x => x.WarehouseId == warehouseId
                && !x.DeletionMark
                && (x.Status == ReceivingOrderStatus.ReadyForReceiving
                    || x.Status == ReceivingOrderStatus.InReceiving
                    || x.Status == ReceivingOrderStatus.ProcessingRequired
                    || (x.Status == ReceivingOrderStatus.Received
                        && (x.PutawayStatus == PutawayStatus.Pending
                            || x.PutawayStatus == PutawayStatus.InProgress))))
            .AsSplitQuery()
            .ToListAsync(ct);

        var movements = await LoadDraftMovementsAsync(
            dbContext,
            orders.Select(x => x.Id),
            ct);
        var shippers = await LoadShippersAsync(orders, ct);
        var summaries = orders
            .Select(order => MapSummary(order, movements, shippers))
            .ToList();

        return new MobileReceivingOrderWorkQueue(
            OrderQueue(summaries.Where(x => IsReceivingWork(x.Status))),
            OrderQueue(summaries.Where(x => IsPutawayWork(x.Status, x.PutawayStatus))));
    }

    public async Task<OperationResult<MobileReceivingOrderDetails>> GetDetailsAsync(
        Guid orderId,
        CancellationToken ct = default) =>
        await GetDetailsAsync(orderId, requireMobileWork: true, ct);

    public async Task<OperationResult<MobileReceivingOrderDetails>>
        GetCommandResultDetailsAsync(
            Guid orderId,
            CancellationToken ct = default) =>
        await GetDetailsAsync(orderId, requireMobileWork: false, ct);

    private async Task<OperationResult<MobileReceivingOrderDetails>> GetDetailsAsync(
        Guid orderId,
        bool requireMobileWork,
        CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var order = await BaseOrderQuery(dbContext)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);

        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        if (requireMobileWork && !IsMobileWork(order))
        {
            return OperationError.Invalid(
                "Для приходного ордера нет доступного мобильного действия.");
        }

        var movements = await LoadDraftMovementsAsync(dbContext, [order.Id], ct);
        var shippers = await LoadShippersAsync([order], ct);
        return MapDetails(order, movements, shippers);
    }

    public async Task<OperationResult<MobileReceivingOrderDetails>> ResolveDocumentAsync(
        Guid warehouseId,
        string? barcodePayload,
        CancellationToken ct = default)
    {
        if (warehouseId == Guid.Empty)
        {
            return OperationError.Invalid("Перед сканированием документа необходимо выбрать склад.");
        }

        var decodeResult = OneSDocumentBarcodeCodec.Decode(barcodePayload);
        if (!decodeResult.IsSuccess)
        {
            return decodeResult.Error!;
        }

        var detailsResult = await GetDetailsAsync(decodeResult.Value, ct);
        if (!detailsResult.IsSuccess)
        {
            return detailsResult.Error!;
        }

        var details = detailsResult.Value!;
        return details.Order.WarehouseId == warehouseId
            ? details
            : OperationError.Invalid(
                "Приходный ордер относится к другому складу.");
    }

    public async Task<OperationResult<IReadOnlyList<MobileReceivingOrderLineCandidate>>>
        ResolveLineBarcodeAsync(
            Guid orderId,
            string? barcode,
            CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(barcode))
        {
            return OperationError.Invalid("Штрихкод товара не указан.");
        }

        var workResult = await LoadLineWorkAsync(orderId, ct);
        if (!workResult.IsSuccess)
        {
            return workResult.Error!;
        }

        var work = workResult.Value!;
        var context = work.Context;
        var matches = work.Order.Items
            .Where(x => !x.StockKeepingUnit!.DeletionMark
                && x.StockKeepingUnit.Barcodes.Any(itemBarcode => itemBarcode.Value == barcode))
            .Select(x => MapCandidate(x, work.AllocatedByLine, true))
            .Where(x => IsAvailableInContext(x, context))
            .OrderBy(x => x.LineNumber)
            .ToList();

        return matches.Count > 0
            ? matches
            : OperationError.NotFound(
                context == ReceivingOrderLineContext.Putaway
                    ? "В ордере нет остатка к размещению для товара с таким штрихкодом."
                    : "Товар с таким штрихкодом не найден в приходном ордере.");
    }

    public async Task<OperationResult<MobileReceivingOrderLineSearchResult>> SearchLinesAsync(
        Guid orderId,
        string? searchText,
        int take,
        CancellationToken ct = default)
    {
        var term = searchText?.Trim() ?? string.Empty;
        if (term.Length < 2)
        {
            return new MobileReceivingOrderLineSearchResult([], false);
        }

        var workResult = await LoadLineWorkAsync(orderId, ct);
        if (!workResult.IsSuccess)
        {
            return workResult.Error!;
        }

        var work = workResult.Value!;
        var context = work.Context;
        var maximumItems = Math.Clamp(take, 1, 10);
        var matches = work.Order.Items
            .Where(x => !x.StockKeepingUnit!.DeletionMark
                && ((x.StockKeepingUnit.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (x.StockKeepingUnit.Code?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                    || x.StockKeepingUnit.Barcodes.Any(barcode =>
                        barcode.Value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)))
            .Select(x => MapCandidate(
                x,
                work.AllocatedByLine,
                IsExactMatch(x.StockKeepingUnit!, term)))
            .Where(x => IsAvailableInContext(x, context))
            .OrderByDescending(x => x.IsExactMatch)
            .ThenBy(x => x.SkuName)
            .ThenBy(x => x.SkuCode)
            .ThenBy(x => x.LineNumber)
            .Take(maximumItems + 1)
            .ToList();

        return new MobileReceivingOrderLineSearchResult(
            matches.Take(maximumItems).ToList(),
            matches.Count > maximumItems);
    }

    private async Task<OperationResult<ReceivingOrderLineWork>> LoadLineWorkAsync(
        Guid orderId,
        CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var order = await BaseOrderQuery(dbContext)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Приходный ордер '{orderId}' не найден.");
        }

        var context = order.Status switch
        {
            ReceivingOrderStatus.InReceiving or ReceivingOrderStatus.ProcessingRequired =>
                ReceivingOrderLineContext.Receiving,
            ReceivingOrderStatus.Received when order.PutawayStatus == PutawayStatus.InProgress =>
                ReceivingOrderLineContext.Putaway,
            _ => (ReceivingOrderLineContext?)null
        };
        if (context is null)
        {
            return OperationError.Invalid(
                "Выбор товара доступен только во время приёмки или размещения ордера.");
        }

        var movements = await LoadDraftMovementsAsync(dbContext, [order.Id], ct);
        return new ReceivingOrderLineWork(
            order,
            context.Value,
            BuildAllocatedByLine(movements));
    }

    private static IQueryable<ReceivingOrder> BaseOrderQuery(ApplicationDbContext dbContext) =>
        dbContext.ReceivingOrders
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.ReceivingLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.Items)
                .ThenInclude(x => x.StockKeepingUnit)
                    .ThenInclude(x => x!.BaseUnitOfMeasure)
            .Include(x => x.Items)
                .ThenInclude(x => x.StockKeepingUnit)
                    .ThenInclude(x => x!.Barcodes);

    private static IQueryable<ReceivingOrder> QueueOrderQuery(
        ApplicationDbContext dbContext) =>
        dbContext.ReceivingOrders
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.ReceivingLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.Items);

    private static Task<List<InventoryMovement>> LoadDraftMovementsAsync(
        ApplicationDbContext dbContext,
        IEnumerable<Guid> orderIds,
        CancellationToken ct)
    {
        var ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Task.FromResult(new List<InventoryMovement>());
        }

        return dbContext.InventoryMovements
            .AsNoTracking()
            .Include(x => x.DestinationStorageLocation)
                .ThenInclude(x => x!.Zone)
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ReceivingOrder
                && x.RecorderId != null
                && ids.Contains(x.RecorderId.Value))
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyDictionary<PartyReference, PartyInfo>> LoadShippersAsync(
        IReadOnlyCollection<ReceivingOrder> orders,
        CancellationToken ct) =>
        await partyQueryService.GetManyAsync(
            orders.Select(x => new PartyReference(x.ShipperId, x.ShipperType)),
            ct);

    private static MobileReceivingOrderDetails MapDetails(
        ReceivingOrder order,
        IReadOnlyCollection<InventoryMovement> movements,
        IReadOnlyDictionary<PartyReference, PartyInfo> shippers)
    {
        var orderMovements = movements
            .Where(x => x.RecorderId == order.Id)
            .ToList();
        var allocatedByLine = BuildAllocatedByLine(orderMovements);
        var lines = order.Items
            .OrderBy(x => x.LineNumber)
            .Select(item => MapLine(item, allocatedByLine))
            .ToList();
        var mappedMovements = orderMovements
            .Select(MapMovement)
            .ToList();

        return new MobileReceivingOrderDetails(
            MapSummary(order, orderMovements, shippers),
            lines,
            mappedMovements);
    }

    private static MobileReceivingOrderSummary MapSummary(
        ReceivingOrder order,
        IReadOnlyCollection<InventoryMovement> movements,
        IReadOnlyDictionary<PartyReference, PartyInfo> shippers)
    {
        var orderMovements = movements
            .Where(x => x.RecorderId == order.Id)
            .ToList();
        var allocatedByLine = BuildAllocatedByLine(orderMovements);
        var shipperReference = new PartyReference(order.ShipperId, order.ShipperType);
        shippers.TryGetValue(shipperReference, out var shipper);

        return new MobileReceivingOrderSummary(
            order.Id,
            order.Number ?? string.Empty,
            order.Date,
            order.WarehouseId,
            order.Warehouse?.Name ?? string.Empty,
            order.ShipperId,
            order.ShipperType,
            shipper?.Name ?? string.Empty,
            order.Queue,
            order.WarehouseOperation,
            order.BusinessOperation,
            order.Status,
            order.PutawayStatus,
            order.ExternalSynchronizationLevel,
            order.Comment,
            order.ReceivingLocation is null ? null : MapLocation(order.ReceivingLocation),
            order.Items.Count,
            order.Items.Count(x => x.IsFactConfirmed),
            order.Items.Count(x => x.FactQuantity > 0),
            order.Items.Count(item => item.FactQuantity > 0
                && GetAllocatedQuantity(allocatedByLine, item.LineNumber) == item.FactQuantity),
            order.Items.Sum(x => x.PlanQuantity),
            order.Items.Sum(x => x.FactQuantity ?? 0),
            orderMovements.Sum(x => x.Quantity),
            order.StartedAtUtc,
            order.CompletedAtUtc,
            order.PutawayStartedAtUtc,
            order.PutawayCompletedAtUtc);
    }

    private static MobileReceivingOrderLine MapLine(
        ReceivingOrderItem item,
        IReadOnlyDictionary<int, decimal> allocatedByLine)
    {
        var allocatedQuantity = GetAllocatedQuantity(allocatedByLine, item.LineNumber);
        return new MobileReceivingOrderLine(
            item.LineNumber,
            item.StockKeepingUnitId,
            item.StockKeepingUnit?.Code ?? string.Empty,
            item.StockKeepingUnit?.Name ?? string.Empty,
            GetUnitOfMeasure(item.StockKeepingUnit),
            item.PlanQuantity,
            item.FactQuantity,
            allocatedQuantity,
            item.FactQuantity is decimal factQuantity
                ? Math.Max(0, factQuantity - allocatedQuantity)
                : null,
            item.Comment);
    }

    private static MobileReceivingOrderMovement MapMovement(InventoryMovement movement) => new(
        movement.Id,
        movement.RecorderLineNumber!.Value,
        movement.StockKeepingUnitId,
        movement.Quantity,
        MapLocation(movement.DestinationStorageLocation!),
        movement.CreatedAtUtc,
        movement.UpdatedAtUtc,
        movement.PostedAtUtc);

    private static MobileReceivingOrderLineCandidate MapCandidate(
        ReceivingOrderItem item,
        IReadOnlyDictionary<int, decimal> allocatedByLine,
        bool isExactMatch)
    {
        var allocatedQuantity = GetAllocatedQuantity(allocatedByLine, item.LineNumber);
        return new MobileReceivingOrderLineCandidate(
            item.LineNumber,
            item.StockKeepingUnitId,
            item.StockKeepingUnit?.Code ?? string.Empty,
            item.StockKeepingUnit?.Name ?? string.Empty,
            GetUnitOfMeasure(item.StockKeepingUnit),
            item.PlanQuantity,
            item.FactQuantity,
            allocatedQuantity,
            item.FactQuantity is decimal factQuantity
                ? Math.Max(0, factQuantity - allocatedQuantity)
                : null,
            isExactMatch);
    }

    private static MobileReceivingOrderLocation MapLocation(StorageLocation location) => new(
        location.Id,
        location.Name,
        $"{location.Zone?.Code}-{location.Code}",
        location.ZoneId,
        location.Zone?.Name ?? string.Empty);

    private static Dictionary<int, decimal> BuildAllocatedByLine(
        IEnumerable<InventoryMovement> movements) =>
        movements
            .Where(x => x.RecorderLineNumber.HasValue)
            .GroupBy(x => x.RecorderLineNumber!.Value)
            .ToDictionary(x => x.Key, x => x.Sum(movement => movement.Quantity));

    private static decimal GetAllocatedQuantity(
        IReadOnlyDictionary<int, decimal> allocatedByLine,
        int lineNumber) =>
        allocatedByLine.TryGetValue(lineNumber, out var quantity) ? quantity : 0;

    private static bool IsAvailableInContext(
        MobileReceivingOrderLineCandidate candidate,
        ReceivingOrderLineContext context) =>
        context == ReceivingOrderLineContext.Receiving
            || candidate.RemainingPutawayQuantity > 0;

    private static bool IsExactMatch(StockKeepingUnit sku, string term) =>
        string.Equals(sku.Code, term, StringComparison.OrdinalIgnoreCase)
        || string.Equals(sku.Name, term, StringComparison.OrdinalIgnoreCase)
        || sku.Barcodes.Any(barcode =>
            string.Equals(barcode.Value, term, StringComparison.OrdinalIgnoreCase));

    private static string? GetUnitOfMeasure(StockKeepingUnit? sku) =>
        sku?.BaseUnitOfMeasure?.Description
        ?? sku?.BaseUnitOfMeasure?.Abbreviation
        ?? sku?.BaseUnitOfMeasure?.Name;

    private static IReadOnlyList<MobileReceivingOrderSummary> OrderQueue(
        IEnumerable<MobileReceivingOrderSummary> items) =>
        items
            .OrderByDescending(x => x.Queue)
            .ThenBy(x => x.Date)
            .ThenBy(x => x.Number)
            .ThenBy(x => x.Id)
            .ToList();

    private static bool IsMobileWork(ReceivingOrder order) =>
        !order.DeletionMark
        && (IsReceivingWork(order) || IsPutawayWork(order));

    private static bool IsReceivingWork(ReceivingOrder order) =>
        IsReceivingWork(order.Status);

    private static bool IsReceivingWork(ReceivingOrderStatus status) =>
        status is ReceivingOrderStatus.ReadyForReceiving
            or ReceivingOrderStatus.InReceiving
            or ReceivingOrderStatus.ProcessingRequired;

    private static bool IsReceivingEditing(ReceivingOrderStatus status) =>
        status is ReceivingOrderStatus.InReceiving
            or ReceivingOrderStatus.ProcessingRequired;

    private static bool IsPutawayWork(ReceivingOrder order) =>
        IsPutawayWork(order.Status, order.PutawayStatus);

    private static bool IsPutawayWork(
        ReceivingOrderStatus status,
        PutawayStatus putawayStatus) =>
        status == ReceivingOrderStatus.Received
        && putawayStatus is PutawayStatus.Pending or PutawayStatus.InProgress;

    private sealed record ReceivingOrderLineWork(
        ReceivingOrder Order,
        ReceivingOrderLineContext Context,
        IReadOnlyDictionary<int, decimal> AllocatedByLine);
}
