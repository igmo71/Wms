using Microsoft.EntityFrameworkCore;
using Wms.Application.Parties;
using Wms.Common;
using Wms.Data;
using Wms.Domain;
using Wms.Domain.Enums;

namespace Wms.Application.ReceivingOrders;

public sealed class MobileReceivingOrderQueryService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    PartyQueryService partyQueryService)
{
    public async Task<IReadOnlyList<MobileReceivingOrderSummary>> GetWorkQueueAsync(
        Guid warehouseId,
        string userId,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var orders = await QueueOrderQuery(dbContext)
            .Where(x => x.WarehouseId == warehouseId
                && x.Participants.Any(participant => participant.UserId == userId)
                && (x.Status == ReceivingOrderStatus.ReadyForReceiving
                    || x.Status == ReceivingOrderStatus.InReceiving))
            .AsSplitQuery()
            .ToListAsync(ct);

        var shippers = await LoadShippersAsync(orders, ct);
        var summaries = orders
            .Select(order => MapSummary(order, shippers, userId))
            .ToList();

        return OrderQueue(summaries);
    }

    public async Task<OperationResult<MobileReceivingOrderDetails>> GetDetailsAsync(
        Guid orderId,
        string userId,
        CancellationToken ct = default) =>
        await GetDetailsAsync(orderId, userId, requireMobileWork: true, ct);

    public Task<OperationResult<MobileReceivingOrderDetails>> GetDetailsAsync(
        Guid orderId,
        CancellationToken ct = default) => GetDetailsAsync(orderId, string.Empty, ct);

    public async Task<OperationResult<MobileReceivingOrderDetails>>
        GetCommandResultDetailsAsync(
            Guid orderId,
            string userId,
            CancellationToken ct = default) =>
        await GetDetailsAsync(orderId, userId, requireMobileWork: false, ct);

    public Task<OperationResult<MobileReceivingOrderDetails>> GetCommandResultDetailsAsync(
        Guid orderId,
        CancellationToken ct = default) =>
        GetCommandResultDetailsAsync(orderId, string.Empty, ct);

    private async Task<OperationResult<MobileReceivingOrderDetails>> GetDetailsAsync(
        Guid orderId,
        string userId,
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

        if (requireMobileWork && !IsReceivingWork(order))
        {
            return OperationError.Invalid(
                "Для приходного ордера нет доступного мобильного действия.");
        }

        var shippers = await LoadShippersAsync([order], ct);
        var lpnUserIds = order.LicensePlateNumbers
            .SelectMany(x => new[] { x.ResponsibleUserId, x.CreatedBy, x.ClosedBy, x.ReopenedBy }
                .Concat(x.Items.SelectMany(item => new[] { item.CreatedBy, item.UpdatedBy })))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
        var lpnUserNames = await dbContext.Users
            .Where(x => lpnUserIds.Contains(x.Id))
            .ToDictionaryAsync(
                x => x.Id,
                x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.UserName ?? x.Id : x.DisplayName,
                ct);
        var canManage = !string.IsNullOrWhiteSpace(userId)
            && await (from userRole in dbContext.UserRoles
                      join role in dbContext.Roles on userRole.RoleId equals role.Id
                      where userRole.UserId == userId
                          && (role.Name == ApplicationRoles.Manager
                              || role.Name == ApplicationRoles.Administrator)
                      select role.Id).AnyAsync(ct);
        return MapDetails(order, shippers, userId, canManage, lpnUserNames);
    }

    public async Task<OperationResult<MobileReceivingOrderDetails>> ResolveDocumentAsync(
        Guid warehouseId,
        Guid orderId,
        string userId,
        CancellationToken ct = default)
    {
        if (warehouseId == Guid.Empty)
        {
            return OperationError.Invalid("Перед сканированием документа необходимо выбрать склад.");
        }

        var detailsResult = await GetDetailsAsync(orderId, userId, ct);
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

        var matches = workResult.Value!.Items
            .Where(x => !x.StockKeepingUnit!.DeletionMark
                && x.StockKeepingUnit.Barcodes.Any(itemBarcode => itemBarcode.Value == barcode))
            .Select(x => MapCandidate(x, true))
            .OrderBy(x => x.LineNumber)
            .ToList();

        return matches.Count > 0
            ? matches
            : OperationError.NotFound(
                "Товар с таким штрихкодом не найден в приходном ордере.");
    }

    private async Task<OperationResult<ReceivingOrder>> LoadLineWorkAsync(
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

        if (order.Status != ReceivingOrderStatus.InReceiving)
        {
            return OperationError.Invalid(
                "Выбор товара доступен только во время приёмки ордера.");
        }
        return order;
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
                    .ThenInclude(x => x!.Barcodes)
            .Include(x => x.Participants)
            .Include(x => x.LicensePlateNumbers)
                .ThenInclude(x => x.Items)
                    .ThenInclude(x => x.StockKeepingUnit)
                        .ThenInclude(x => x.BaseUnitOfMeasure);


    private static IQueryable<ReceivingOrder> QueueOrderQuery(
        ApplicationDbContext dbContext) =>
        dbContext.ReceivingOrders
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.ReceivingLocation)
                .ThenInclude(x => x!.Zone)
            .Include(x => x.Items)
            .Include(x => x.Participants);

    private async Task<IReadOnlyDictionary<PartyReference, PartyInfo>> LoadShippersAsync(
        IReadOnlyCollection<ReceivingOrder> orders,
        CancellationToken ct) =>
        await partyQueryService.GetManyAsync(
            orders.Select(x => new PartyReference(x.ShipperId, x.ShipperType)),
            ct);

    private static MobileReceivingOrderDetails MapDetails(
        ReceivingOrder order,
        IReadOnlyDictionary<PartyReference, PartyInfo> shippers,
        string userId,
        bool canManage,
        IReadOnlyDictionary<string, string> userNames)
    {
        var lines = order.Items
            .OrderBy(x => x.LineNumber)
            .Select(MapLine)
            .ToList();
        var lpns = order.LicensePlateNumbers
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Code)
            .Select(x => new MobileReceivingOrderLicensePlateNumber(
                x.Id,
                x.Code,
                x.Status,
                x.ResponsibleUserId ?? string.Empty,
                UserName(x.ResponsibleUserId, userNames),
                UserName(x.CreatedBy, userNames),
                x.CreatedAtUtc ?? DateTimeOffset.MinValue,
                x.ClosedBy is null ? null : UserName(x.ClosedBy, userNames),
                x.ClosedAtUtc,
                x.ReopenedBy is null ? null : UserName(x.ReopenedBy, userNames),
                x.ReopenedAtUtc,
                x.CancelledAtUtc,
                x.ResponsibleUserId == userId,
                x.Status == LicensePlateNumberStatus.Open
                    && (x.ResponsibleUserId == userId || canManage)
                    && order.Status == ReceivingOrderStatus.InReceiving,
                x.Status == LicensePlateNumberStatus.Open
                    && x.ResponsibleUserId == userId
                    && x.Items.All(item => item.IsDeleted)
                    && order.Status == ReceivingOrderStatus.InReceiving,
                x.Status == LicensePlateNumberStatus.Open
                    && (x.ResponsibleUserId == userId || canManage)
                    && order.Status == ReceivingOrderStatus.InReceiving,
                x.Status == LicensePlateNumberStatus.Closed
                    && canManage
                    && order.Status == ReceivingOrderStatus.InReceiving,
                x.Items
                    .Where(item => !item.IsDeleted)
                    .OrderBy(item => item.StockKeepingUnit.Name)
                    .Select(item => new MobileReceivingOrderLicensePlateNumberItem(
                        item.StockKeepingUnitId,
                        item.StockKeepingUnit.Code ?? string.Empty,
                        item.StockKeepingUnit.Name ?? string.Empty,
                        GetUnitOfMeasure(item.StockKeepingUnit),
                        item.Quantity,
                        UserName(item.CreatedBy, userNames),
                        item.CreatedAtUtc,
                        item.UpdatedBy is null ? null : UserName(item.UpdatedBy, userNames),
                        item.UpdatedAtUtc))
                    .ToList()))
            .ToList();

        return new MobileReceivingOrderDetails(
            MapSummary(order, shippers, userId),
            lines,
            lpns);
    }

    private static string UserName(
        string? userId,
        IReadOnlyDictionary<string, string> userNames) =>
        !string.IsNullOrWhiteSpace(userId) && userNames.TryGetValue(userId, out var name)
            ? name
            : "Пользователь не найден";

    private static MobileReceivingOrderSummary MapSummary(
        ReceivingOrder order,
        IReadOnlyDictionary<PartyReference, PartyInfo> shippers,
        string userId)
    {
        var shipperReference = new PartyReference(order.ShipperId, order.ShipperType);
        shippers.TryGetValue(shipperReference, out var shipper);
        OperationResult eligibility = order.ValidateReceivingParticipationEligibility();
        bool isParticipant = order.Participants.Any(x => x.UserId == userId);

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
            order.SynchronizationLevel,
            order.Comment,
            order.ReceivingLocation is null ? null : MapLocation(order.ReceivingLocation),
            order.Items.Count,
            order.Items.Count(x => x.IsFactConfirmed),
            order.Items.Count(x => x.FactQuantity > 0),
            order.StartedAtUtc,
            order.CompletedAtUtc,
            isParticipant,
            !isParticipant
                && order.Status is ReceivingOrderStatus.ReadyForReceiving or ReceivingOrderStatus.InReceiving
                && eligibility.IsSuccess
                && order.SynchronizationLevel == OrderSynchronizationLevel.Synchronized,
            eligibility.IsSuccess && order.SynchronizationLevel == OrderSynchronizationLevel.Synchronized
                ? null
                : eligibility.Error?.Message ?? "Ордер заблокирован расхождениями с 1С.");
    }

    private static MobileReceivingOrderLine MapLine(ReceivingOrderItem item) => new(
            item.LineNumber,
            item.StockKeepingUnitId,
            item.StockKeepingUnit?.Code ?? string.Empty,
            item.StockKeepingUnit?.Name ?? string.Empty,
            GetUnitOfMeasure(item.StockKeepingUnit),
            item.PlanQuantity,
            item.FactQuantity,
            item.Comment);

    private static MobileReceivingOrderLineCandidate MapCandidate(
        ReceivingOrderItem item,
        bool isExactMatch)
        => new MobileReceivingOrderLineCandidate(
            item.LineNumber,
            item.StockKeepingUnitId,
            item.StockKeepingUnit?.Code ?? string.Empty,
            item.StockKeepingUnit?.Name ?? string.Empty,
            GetUnitOfMeasure(item.StockKeepingUnit),
            item.PlanQuantity,
            item.FactQuantity,
            isExactMatch);

    private static MobileReceivingOrderLocation MapLocation(StorageLocation location) => new(
        location.Id,
        location.Name,
        $"{location.Zone?.Code}-{location.Code}",
        location.ZoneId,
        location.Zone?.Name ?? string.Empty);

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

    private static bool IsReceivingWork(ReceivingOrder order) =>
        order.Status is ReceivingOrderStatus.ReadyForReceiving or ReceivingOrderStatus.InReceiving;

}
