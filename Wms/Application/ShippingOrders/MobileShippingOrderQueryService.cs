using Microsoft.EntityFrameworkCore;
using Wms.Application.Parties;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;
using Wms.Integration.OneS;

namespace Wms.Application.ShippingOrders;

public sealed class MobileShippingOrderQueryService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    PartyQueryService partyQueryService)
{
    public async Task<MobileShippingOrderWorkQueue> GetWorkQueueAsync(
        Guid warehouseId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var orders = await QueueOrderQuery(dbContext)
            .Where(x => x.WarehouseId == warehouseId
                && !x.DeletionMark
                && (x.Status == ShippingOrderStatus.Prepared
                    || x.Status == ShippingOrderStatus.ReadyForPicking
                    || x.Status == ShippingOrderStatus.ReadyForVerification
                    || x.Status == ShippingOrderStatus.InVerification
                    || x.Status == ShippingOrderStatus.Verified
                    || x.Status == ShippingOrderStatus.ReadyForShipment))
            .AsSplitQuery()
            .ToListAsync(ct);

        var receivers = await LoadReceiversAsync(orders, ct);
        var summaries = orders
            .Select(order => MapSummary(order, receivers))
            .ToList();

        return new MobileShippingOrderWorkQueue(
            OrderQueue(summaries.Where(x => IsPickingWork(x.Status))),
            OrderQueue(summaries.Where(x => IsShippingWork(x.Status))));
    }

    public async Task<OperationResult<MobileShippingOrderDetails>> GetDetailsAsync(
        Guid orderId,
        CancellationToken ct = default) =>
        await GetDetailsAsync(orderId, requireMobileWork: true, ct);

    public async Task<OperationResult<MobileShippingOrderDetails>>
        GetCommandResultDetailsAsync(
            Guid orderId,
            CancellationToken ct = default) =>
        await GetDetailsAsync(orderId, requireMobileWork: false, ct);

    private async Task<OperationResult<MobileShippingOrderDetails>> GetDetailsAsync(
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
            return OperationError.NotFound($"Расходный ордер '{orderId}' не найден.");
        }

        if (requireMobileWork && !IsMobileWork(order))
        {
            return OperationError.Invalid(
                "Для расходного ордера нет доступного мобильного действия.");
        }

        var movements = await LoadCurrentCycleMovementsAsync(dbContext, order, ct);
        var receivers = await LoadReceiversAsync([order], ct);
        return MapDetails(order, movements, receivers);
    }

    public async Task<OperationResult<MobileShippingOrderDetails>> ResolveDocumentAsync(
        Guid warehouseId,
        string? barcodePayload,
        CancellationToken ct = default)
    {
        if (warehouseId == Guid.Empty)
        {
            return OperationError.Invalid(
                "Перед сканированием документа необходимо выбрать склад.");
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
                "Расходный ордер относится к другому складу.");
    }

    public async Task<OperationResult<IReadOnlyList<MobileShippingOrderLineCandidate>>>
        ResolveLineBarcodeAsync(
            Guid orderId,
            string? barcode,
            CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(barcode))
        {
            return OperationError.Invalid("Штрихкод товара не указан.");
        }

        var orderResult = await LoadEditableOrderAsync(orderId, ct);
        if (!orderResult.IsSuccess)
        {
            return orderResult.Error!;
        }

        var matches = orderResult.Value!.Items
            .Where(x => x.RemainingQuantity > 0
                && !x.StockKeepingUnit!.DeletionMark
                && x.StockKeepingUnit.Barcodes.Any(itemBarcode =>
                    itemBarcode.Value == barcode))
            .Select(x => MapCandidate(x, true))
            .OrderBy(x => x.LineNumber)
            .ToList();

        return matches.Count > 0
            ? matches
            : OperationError.NotFound(
                "В расходном ордере нет остатка к отбору для товара с таким штрихкодом.");
    }

    public async Task<OperationResult<MobileShippingOrderLineSearchResult>> SearchLinesAsync(
        Guid orderId,
        string? searchText,
        int take,
        CancellationToken ct = default)
    {
        var term = searchText?.Trim() ?? string.Empty;
        if (term.Length < 2)
        {
            return new MobileShippingOrderLineSearchResult([], false);
        }

        var orderResult = await LoadEditableOrderAsync(orderId, ct);
        if (!orderResult.IsSuccess)
        {
            return orderResult.Error!;
        }

        var maximumItems = Math.Clamp(take, 1, 10);
        var matches = orderResult.Value!.Items
            .Where(x => x.RemainingQuantity > 0
                && !x.StockKeepingUnit!.DeletionMark
                && ((x.StockKeepingUnit.Name?.Contains(
                        term,
                        StringComparison.OrdinalIgnoreCase) ?? false)
                    || (x.StockKeepingUnit.Code?.Contains(
                        term,
                        StringComparison.OrdinalIgnoreCase) ?? false)
                    || x.StockKeepingUnit.Barcodes.Any(barcode =>
                        barcode.Value?.Contains(
                            term,
                            StringComparison.OrdinalIgnoreCase) == true)))
            .Select(x => MapCandidate(
                x,
                IsExactMatch(x.StockKeepingUnit!, term)))
            .OrderByDescending(x => x.IsExactMatch)
            .ThenBy(x => x.SkuName)
            .ThenBy(x => x.SkuCode)
            .ThenBy(x => x.LineNumber)
            .Take(maximumItems + 1)
            .ToList();

        return new MobileShippingOrderLineSearchResult(
            matches.Take(maximumItems).ToList(),
            matches.Count > maximumItems);
    }

    public async Task<OperationResult<IReadOnlyList<MobileShippingOrderSourceAvailability>>>
        GetAvailableSourcesAsync(
            Guid orderId,
            int lineNumber,
            CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var order = await BaseOrderQuery(dbContext)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Расходный ордер '{orderId}' не найден.");
        }

        if (!IsPickingEditing(order.Status))
        {
            return OperationError.Invalid(
                "Источники доступны только во время отбора или проверки ордера.");
        }

        var line = order.Items.FirstOrDefault(x => x.LineNumber == lineNumber);
        if (line is null)
        {
            return OperationError.NotFound(
                $"Строка {lineNumber} расходного ордера '{orderId}' не найдена.");
        }

        if (line.RemainingQuantity <= 0)
        {
            return Array.Empty<MobileShippingOrderSourceAvailability>();
        }

        var draftQuantities = await dbContext.InventoryMovements
            .AsNoTracking()
            .Where(x => x.PostedAtUtc == null
                && x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == order.Id
                && x.StockKeepingUnitId == line.StockKeepingUnitId
                && x.SourceStorageLocationId != null)
            .GroupBy(x => x.SourceStorageLocationId!.Value)
            .Select(x => new
            {
                StorageLocationId = x.Key,
                Quantity = x.Sum(movement => movement.Quantity)
            })
            .ToDictionaryAsync(x => x.StorageLocationId, x => x.Quantity, ct);

        var balances = await dbContext.InventoryBalances
            .AsNoTracking()
            .Include(x => x.StorageLocation)
                .ThenInclude(x => x!.Zone)
            .Where(x => x.WarehouseId == order.WarehouseId
                && x.StockKeepingUnitId == line.StockKeepingUnitId
                && x.StorageLocationId != order.ShippingLocationId
                && !x.StorageLocation!.IsFolder
                && !x.StorageLocation.DeletionMark
                && x.StorageLocation.ActiveLock == null
                && !x.StorageLocation.Zone!.DeletionMark
                && x.StorageLocation.Zone.Type == ZoneType.Storage
                && x.Quantity > 0)
            .OrderBy(x => x.StorageLocation!.PickSequence == null)
            .ThenBy(x => x.StorageLocation!.PickSequence)
            .ThenBy(x => x.StorageLocation!.Code)
            .ToListAsync(ct);

        return balances
            .Select(balance => new MobileShippingOrderSourceAvailability(
                MapLocation(balance.StorageLocation!),
                balance.Quantity,
                draftQuantities.GetValueOrDefault(balance.StorageLocationId)))
            .ToList();
    }

    private async Task<OperationResult<ShippingOrder>> LoadEditableOrderAsync(
        Guid orderId,
        CancellationToken ct)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        var order = await BaseOrderQuery(dbContext)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null)
        {
            return OperationError.NotFound($"Расходный ордер '{orderId}' не найден.");
        }

        return IsPickingEditing(order.Status)
            ? order
            : OperationError.Invalid(
                "Выбор товара доступен только во время отбора или проверки ордера.");
    }

    private static IQueryable<ShippingOrder> BaseOrderQuery(
        ApplicationDbContext dbContext) =>
        dbContext.ShippingOrders
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.DeliveryDirection)
            .Include(x => x.ShippingLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.Items)
                .ThenInclude(x => x.StockKeepingUnit)
                    .ThenInclude(x => x!.BaseUnitOfMeasure)
            .Include(x => x.Items)
                .ThenInclude(x => x.StockKeepingUnit)
                    .ThenInclude(x => x!.Barcodes);

    private static IQueryable<ShippingOrder> QueueOrderQuery(
        ApplicationDbContext dbContext) =>
        dbContext.ShippingOrders
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.DeliveryDirection)
            .Include(x => x.ShippingLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.Items);

    private static Task<List<InventoryMovement>> LoadCurrentCycleMovementsAsync(
        ApplicationDbContext dbContext,
        ShippingOrder order,
        CancellationToken ct)
    {
        if (order.PickingStartedAtUtc is not DateTimeOffset pickingStartedAtUtc)
        {
            return Task.FromResult(new List<InventoryMovement>());
        }

        return dbContext.InventoryMovements
            .AsNoTracking()
            .Include(x => x.SourceStorageLocation)
                .ThenInclude(x => x!.Zone)
            .Where(x => x.RecorderType == RecorderType.ShippingOrder
                && x.RecorderId == order.Id
                && x.SourceStorageLocationId != null
                && x.DestinationStorageLocationId == order.ShippingLocationId
                && x.RecorderLineNumber != null
                && x.CreatedAtUtc >= pickingStartedAtUtc)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyDictionary<PartyReference, PartyInfo>>
        LoadReceiversAsync(
            IReadOnlyCollection<ShippingOrder> orders,
            CancellationToken ct) =>
        await partyQueryService.GetManyAsync(
            orders.Select(x => new PartyReference(x.ReceiverId, x.ReceiverType)),
            ct);

    private static MobileShippingOrderDetails MapDetails(
        ShippingOrder order,
        IReadOnlyCollection<InventoryMovement> movements,
        IReadOnlyDictionary<PartyReference, PartyInfo> receivers) =>
        new(
            MapSummary(order, receivers),
            order.Items
                .OrderBy(x => x.LineNumber)
                .Select(MapLine)
                .ToList(),
            movements.Select(MapMovement).ToList());

    private static MobileShippingOrderSummary MapSummary(
        ShippingOrder order,
        IReadOnlyDictionary<PartyReference, PartyInfo> receivers)
    {
        var receiverReference = new PartyReference(order.ReceiverId, order.ReceiverType);
        receivers.TryGetValue(receiverReference, out var receiver);

        return new MobileShippingOrderSummary(
            order.Id,
            order.Number ?? string.Empty,
            order.Date,
            order.WarehouseId,
            order.Warehouse?.Name ?? string.Empty,
            order.ReceiverId,
            order.ReceiverType,
            receiver?.Name ?? string.Empty,
            order.Queue,
            order.WarehouseOperation,
            order.Status,
            order.ExternalSynchronizationLevel,
            order.Comment,
            order.PlannedShippingDate,
            order.DeliveryDirection?.Description,
            order.ShippingLocation is null ? null : MapLocation(order.ShippingLocation),
            order.Items.Count,
            order.Items.Count(x => x.FactQuantity > 0
                && x.FactQuantity == x.PlanQuantity),
            order.Items.Count(x => x.FactQuantity > 0
                && x.FactQuantity < x.PlanQuantity),
            order.Items.Count(x => x.FactQuantity == 0),
            order.Items.Sum(x => x.PlanQuantity),
            order.Items.Sum(x => x.FactQuantity),
            order.PickingStartedAtUtc,
            order.ReadyForShipmentAtUtc,
            order.ShippedAtUtc);
    }

    private static MobileShippingOrderLine MapLine(ShippingOrderItem item) => new(
        item.LineNumber,
        item.StockKeepingUnitId,
        item.StockKeepingUnit?.Code ?? string.Empty,
        item.StockKeepingUnit?.Name ?? string.Empty,
        GetUnitOfMeasure(item.StockKeepingUnit),
        item.PlanQuantity,
        item.FactQuantity,
        item.Comment);

    private static MobileShippingOrderMovement MapMovement(
        InventoryMovement movement) => new(
        movement.Id,
        movement.RecorderLineNumber!.Value,
        movement.StockKeepingUnitId,
        movement.Quantity,
        MapLocation(movement.SourceStorageLocation!),
        movement.CreatedAtUtc,
        movement.UpdatedAtUtc,
        movement.PostedAtUtc);

    private static MobileShippingOrderLineCandidate MapCandidate(
        ShippingOrderItem item,
        bool isExactMatch) => new(
        item.LineNumber,
        item.StockKeepingUnitId,
        item.StockKeepingUnit?.Code ?? string.Empty,
        item.StockKeepingUnit?.Name ?? string.Empty,
        GetUnitOfMeasure(item.StockKeepingUnit),
        item.PlanQuantity,
        item.FactQuantity,
        isExactMatch);

    private static MobileShippingOrderLocation MapLocation(
        StorageLocation location) => new(
        location.Id,
        location.Name,
        $"{location.Zone?.Code}-{location.Code}",
        location.ZoneId,
        location.Zone?.Name ?? string.Empty);

    private static bool IsExactMatch(StockKeepingUnit sku, string term) =>
        string.Equals(sku.Code, term, StringComparison.OrdinalIgnoreCase)
        || string.Equals(sku.Name, term, StringComparison.OrdinalIgnoreCase)
        || sku.Barcodes.Any(barcode =>
            string.Equals(barcode.Value, term, StringComparison.OrdinalIgnoreCase));

    private static string? GetUnitOfMeasure(StockKeepingUnit? sku) =>
        sku?.BaseUnitOfMeasure?.Description
        ?? sku?.BaseUnitOfMeasure?.Abbreviation
        ?? sku?.BaseUnitOfMeasure?.Name;

    private static IReadOnlyList<MobileShippingOrderSummary> OrderQueue(
        IEnumerable<MobileShippingOrderSummary> items) =>
        items
            .OrderByDescending(x => x.Queue)
            .ThenBy(x => x.PlannedShippingDate ?? x.Date)
            .ThenBy(x => x.Date)
            .ThenBy(x => x.Number)
            .ThenBy(x => x.Id)
            .ToList();

    private static bool IsMobileWork(ShippingOrder order) =>
        !order.DeletionMark
        && (IsPickingWork(order.Status) || IsShippingWork(order.Status));

    private static bool IsPickingWork(ShippingOrderStatus status) =>
        status is ShippingOrderStatus.Prepared
            or ShippingOrderStatus.ReadyForPicking
            or ShippingOrderStatus.ReadyForVerification
            or ShippingOrderStatus.InVerification
            or ShippingOrderStatus.Verified;

    private static bool IsPickingEditing(ShippingOrderStatus status) =>
        status is ShippingOrderStatus.ReadyForPicking
            or ShippingOrderStatus.ReadyForVerification
            or ShippingOrderStatus.InVerification
            or ShippingOrderStatus.Verified;

    private static bool IsShippingWork(ShippingOrderStatus status) =>
        status == ShippingOrderStatus.ReadyForShipment;
}
