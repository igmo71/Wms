using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
using Wms.Domain.Enums;
using Wms.Integration.OneS;

namespace Wms.WebApi.Mobile;

internal static class MobilePickingEndpoints
{
    public static IEndpointRouteBuilder MapMobilePickingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(MobileApiRoutes.PickingAssemblies)
            .WithTags("Mobile Picking Assemblies")
            .RequireAuthorization(MobileAuthorization.PickingWorkerPolicy);

        group.MapGet("", ListAsync)
            .WithMobileResponses<IReadOnlyList<MobilePickingAssemblyListResponse>>();
        group.MapPost("/sources/resolve", ResolveBarcodeAsync)
            .WithMobileResponses<MobilePickingSourceResolutionResponse>();
        group.MapGet("/by-source/{sourceRefKey:guid}", ResolveSourceAsync)
            .WithMobileResponses<MobilePickingSourceResolutionResponse>();
        group.MapPost("", CreateAsync)
            .WithMobileResponses<MobilePickingCommandIdResponse>();
        group.MapGet("/{assemblyId:guid}", GetAsync)
            .WithMobileResponses<MobilePickingAssemblyResponse>();
        group.MapPost("/{assemblyId:guid}/sources", AttachAsync)
            .WithMobileResponses<MobilePickingCommandIdResponse>();
        group.MapDelete("/{assemblyId:guid}/sources/{sourceRefKey:guid}", DetachAsync)
            .WithMobileResponses<MobilePickingCommandIdResponse>();
        group.MapPost("/{assemblyId:guid}/start", StartAsync)
            .WithMobileResponses<MobilePickingStartResponse>();
        group.MapPost("/{assemblyId:guid}/take-over", TakeOverAsync)
            .WithMobileResponses<MobilePickingCommandIdResponse>();
        group.MapGet("/{assemblyId:guid}/work", GetWorkAsync)
            .WithMobileResponses<MobilePickingWorkResponse>();
        group.MapGet("/{assemblyId:guid}/route", GetRouteAsync)
            .WithMobileResponses<MobilePickingRouteResponse>();
        group.MapGet("/{assemblyId:guid}/skus/{skuId:guid}", GetSkuAsync)
            .WithMobileResponses<MobilePickingSkuResponse>();
        group.MapPost("/{assemblyId:guid}/source/scan", ScanSourceAsync)
            .WithMobileResponses<MobilePickingScannedSourceResponse>();
        group.MapPost("/{assemblyId:guid}/pallets/hold", HoldPalletAsync)
            .WithMobileResponses<MobilePickingResourceIdResponse>();
        group.MapPost("/{assemblyId:guid}/pallets/return", ReturnPalletAsync)
            .WithMobileResponses<MobilePickingResourceIdResponse>();
        group.MapPost("/{assemblyId:guid}/picks", ConfirmPickAsync)
            .WithMobileResponses<MobilePickingResourceIdResponse>();
        group.MapPost("/{assemblyId:guid}/picks/{entryId:guid}/return", ReducePickAsync)
            .WithMobileResponses<MobilePickingResourceIdResponse>();
        group.MapPost("/{assemblyId:guid}/shortages", ReportShortageAsync)
            .WithMobileResponses<MobilePickingResourceIdResponse>();
        group.MapGet("/{assemblyId:guid}/closure-check", CheckClosureAsync)
            .WithMobileResponses<MobilePickingClosureCheckResponse>();
        group.MapPost("/{assemblyId:guid}/close", CloseAsync)
            .WithMobileResponses<MobilePickingCloseResponse>();
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid warehouseId, ClaimsPrincipal principal, GetPickingAssemblies get,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var result = await get.ExecuteAsync(warehouseId, userId, ct);
        return result.IsSuccess
            ? TypedResults.Ok<IReadOnlyList<MobilePickingAssemblyListResponse>>(
                result.Value!.Select(MapList).ToArray())
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> ResolveBarcodeAsync(
        Guid warehouseId, MobilePickingResolveSourceRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        GetPickingAssembly get, IShippingSourceReader reader, CancellationToken ct)
    {
        var decoded = ShippingDocumentBarcodeCodec.Decode(request.Barcode);
        if (!decoded.IsSuccess)
            return MobileEndpointResults.CommandProblem(decoded.Error!);
        return await ResolveAsync(decoded.Value, warehouseId, principal,
            assemblies, get, reader, ct);
    }

    private static Task<IResult> ResolveSourceAsync(
        Guid sourceRefKey, Guid warehouseId, ClaimsPrincipal principal,
        IPickingAssemblyStore assemblies, GetPickingAssembly get,
        IShippingSourceReader reader, CancellationToken ct) =>
        ResolveAsync(sourceRefKey, warehouseId, principal, assemblies, get, reader, ct);

    private static async Task<IResult> ResolveAsync(
        Guid sourceRefKey, Guid warehouseId, ClaimsPrincipal principal,
        IPickingAssemblyStore assemblies, GetPickingAssembly get,
        IShippingSourceReader reader, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        if (warehouseId == Guid.Empty || sourceRefKey == Guid.Empty)
            return MobileEndpointResults.CommandProblem(
                OperationError.Invalid("Выберите склад и РО 1С."));

        var linkedId = await assemblies.FindLinkedAssemblyIdAsync(sourceRefKey, ct);
        if (linkedId is Guid assemblyId)
        {
            var found = await get.ExecuteAsync(assemblyId, userId, ct);
            if (!found.IsSuccess)
                return MobileEndpointResults.CommandProblem(found.Error!);
            if (found.Value!.WarehouseId != warehouseId)
                return MobileEndpointResults.CommandProblem(
                    OperationError.Conflict("Сборка относится к другому складу."));
            return TypedResults.Ok(new MobilePickingSourceResolutionResponse(
                MapAssembly(found.Value), null));
        }

        var read = await reader.ReadAsync(sourceRefKey, ct);
        if (!read.IsSuccess)
            return MobileEndpointResults.CommandProblem(read.Error!);
        var source = read.Value!;
        if (source.WarehouseId != warehouseId)
            return MobileEndpointResults.CommandProblem(
                OperationError.Conflict("Документ относится к другому складу."));
        if (source.Posted != true || source.DeletionMark != false
            || source.Status != Wms.Domain.Enums.ShippingOrderStatus.Prepared)
            return MobileEndpointResults.CommandProblem(
                OperationError.Conflict("Нужен проведённый РО 1С в статусе Подготовлен."));
        return TypedResults.Ok(new MobilePickingSourceResolutionResponse(
            null, new MobilePickingFreeSourceResponse(
                source.RefKey, source.Number, warehouseId, source.Status.ToString())));
    }

    private static async Task<IResult> CreateAsync(
        MobilePickingCreateRequest request, ClaimsPrincipal principal,
        CreatePickingAssembly create, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var decoded = ShippingDocumentBarcodeCodec.Decode(request.Barcode);
        if (!decoded.IsSuccess)
            return MobileEndpointResults.CommandProblem(decoded.Error!);
        return CommandId(await create.ExecuteAsync(new CreatePickingAssemblyRequest(
            request.ClientRequestId, decoded.Value, request.WarehouseId, userId), ct));
    }

    private static async Task<IResult> GetAsync(
        Guid assemblyId, Guid warehouseId, ClaimsPrincipal principal,
        GetPickingAssembly get, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        if (warehouseId == Guid.Empty)
            return MobileEndpointResults.CommandProblem(
                OperationError.Invalid("Выберите склад."));
        var result = await get.ExecuteAsync(assemblyId, userId, ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        return result.Value!.WarehouseId == warehouseId
            ? TypedResults.Ok(MapAssembly(result.Value))
            : MobileEndpointResults.CommandProblem(
                OperationError.Conflict("Сборка относится к другому складу."));
    }

    private static async Task<IResult> AttachAsync(
        Guid assemblyId, MobilePickingAttachSourceRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        AttachPickingSource attach, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        var decoded = ShippingDocumentBarcodeCodec.Decode(request.Barcode);
        if (!decoded.IsSuccess)
            return MobileEndpointResults.CommandProblem(decoded.Error!);
        return CommandId(await attach.ExecuteAsync(new AttachPickingSourceRequest(
            request.ClientRequestId, assemblyId, decoded.Value, userId), ct));
    }

    private static async Task<IResult> DetachAsync(
        Guid assemblyId, Guid sourceRefKey,
        [FromBody] MobilePickingCommandRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        DetachPickingSource detach, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return CommandId(await detach.ExecuteAsync(new DetachPickingSourceRequest(
            request.ClientRequestId, assemblyId, sourceRefKey, userId), ct));
    }

    private static async Task<IResult> StartAsync(
        Guid assemblyId, MobilePickingCommandRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        StartPickingAssembly start, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        var result = await start.ExecuteAsync(new StartPickingAssemblyRequest(
            request.ClientRequestId, assemblyId, userId), ct);
        return result.IsSuccess
            ? TypedResults.Ok(new MobilePickingStartResponse(
                result.Value!.PickingAssemblyId,
                (MobilePickingAssemblyStatus)result.Value.Status,
                result.Value.Sources.Select(x => new MobilePickingSourceStartResponse(
                    x.ShippingOrderId, x.Confirmed, x.Error)).ToArray()))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<IResult> TakeOverAsync(
        Guid assemblyId, MobilePickingTakeOverRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        TakeOverPickingAssembly takeOver, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
            return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return CommandId(await takeOver.ExecuteAsync(
            new TakeOverPickingAssemblyRequest(request.ClientRequestId,
                assemblyId, request.ExpectedRevision, userId), ct));
    }

    private static async Task<IResult> GetSkuAsync(
        Guid assemblyId, Guid skuId, Guid warehouseId,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        GetPickingSku get, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies, assemblyId, warehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        var result = await get.ExecuteAsync(assemblyId, skuId, userId, ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        var sku = result.Value!;
        return TypedResults.Ok(new MobilePickingSkuResponse(sku.StockKeepingUnitId,
            sku.PlannedQuantity, sku.ConfirmedQuantity, sku.RemainingQuantity,
            sku.Availability.Select(x => new MobilePickingAvailabilityResponse(
                x.Group.StorageLocationId, x.LocationCode,
                x.Group.ExpirationDate, x.AccountingQuantity,
                x.AvailableForNewPick(assemblyId), x.ReservedQuantity,
                x.BlockedQuantity, x.Hold is not null)).ToArray()));
    }

    private static async Task<IResult> GetWorkAsync(
        Guid assemblyId, Guid warehouseId, ClaimsPrincipal principal,
        GetPickingWork get, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        if (warehouseId == Guid.Empty)
            return MobileEndpointResults.CommandProblem(
                OperationError.Invalid("Выберите склад."));
        var result = await get.ExecuteAsync(assemblyId, userId, ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        var work = result.Value!;
        if (work.WarehouseId != warehouseId)
            return MobileEndpointResults.CommandProblem(
                OperationError.Conflict("Сборка относится к другому складу."));
        return TypedResults.Ok(new MobilePickingWorkResponse(work.Id,
            (MobilePickingAssemblyStatus)work.Status, work.Revision,
            work.ResponsibleUserId,
            work.Skus.Select(x => new MobilePickingWorkSkuResponse(x.SkuId,
                x.Code, x.Name, x.Unit, x.TracksExpirationDate,
                x.PlannedQuantity, x.ConfirmedQuantity)).ToArray(),
            work.Entries.Select(x => new MobilePickingWorkEntryResponse(x.Id,
                x.StorageLocationId, x.LocationCode, x.SkuId,
                x.ExpirationDate, x.Quantity, x.ConfirmedBy,
                x.ConfirmedAtUtc, x.FromReserve)).ToArray()));
    }

    private static async Task<IResult> GetRouteAsync(
        Guid assemblyId, Guid warehouseId, Guid? previousLocationId,
        Guid? nextLocationId, ClaimsPrincipal principal,
        IPickingAssemblyStore assemblies, GetPickingRoute get,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies, assemblyId, warehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        var result = await get.ExecuteAsync(assemblyId, userId,
            previousLocationId, nextLocationId, ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        var route = result.Value!;
        return TypedResults.Ok(new MobilePickingRouteResponse(route.Revision,
            route.Steps.Select(x => new MobilePickingRouteStepResponse(
                x.StorageLocationId, x.LocationCode, x.StockKeepingUnitId,
                x.ExpirationDate, x.Quantity, x.DirectPalletPick)).ToArray(),
            route.Gaps.Select(x => new MobilePickingRouteGapResponse(
                x.StockKeepingUnitId, x.ExpirationDate, x.Quantity)).ToArray()));
    }

    private static async Task<IResult> ScanSourceAsync(
        Guid assemblyId, MobilePickingScanSourceRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        GetPickingSource get, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        var result = await get.ExecuteAsync(assemblyId, request.SkuId,
            request.LocationCode, userId, request.ScannedSkuId,
            request.ExpirationDate, request.DateGroupSelected, ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        var source = result.Value!;
        return TypedResults.Ok(new MobilePickingScannedSourceResponse(
            source.StorageLocationId, source.LocationCode,
            source.StockKeepingUnitId, source.ExpirationDate,
            source.AccountingQuantity, source.AvailableQuantity,
            source.SuggestedQuantity, source.ReservedQuantity,
            source.BlockedQuantity, source.RequiresVisualExpiryCheck));
    }

    private static async Task<IResult> HoldPalletAsync(
        Guid assemblyId, MobilePickingHoldPalletRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        HoldPickingPallet hold, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return ResourceId(await hold.ExecuteAsync(new HoldPickingPalletRequest(
            request.ClientRequestId, assemblyId, request.ExpectedRevision,
            request.LocationCode, request.SkuId, userId), ct));
    }

    private static async Task<IResult> ReturnPalletAsync(
        Guid assemblyId, MobilePickingReturnPalletRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        ReturnPickingPallet returnPallet, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return ResourceId(await returnPallet.ExecuteAsync(new ReturnPickingPalletRequest(
            request.ClientRequestId, assemblyId, request.ExpectedRevision,
            request.ScannedLocationCode, userId), ct));
    }

    private static async Task<IResult> ConfirmPickAsync(
        Guid assemblyId, MobilePickingConfirmRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        ConfirmPick confirm, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return ResourceId(await confirm.ExecuteAsync(new ConfirmPickRequest(
            request.ClientRequestId, assemblyId, request.ExpectedRevision,
            request.ScannedLocationCode, request.SkuId, request.ExpirationDate,
            request.Quantity, userId, request.ScannedSkuId), ct));
    }

    private static async Task<IResult> ReducePickAsync(
        Guid assemblyId, Guid entryId, MobilePickingReduceRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        ReducePick reduce, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return ResourceId(await reduce.ExecuteAsync(new ReducePickRequest(
            request.ClientRequestId, assemblyId, entryId, request.ExpectedRevision,
            request.ScannedLocationCode, request.Quantity,
            request.PhysicalReturnConfirmed, userId, request.Explanation), ct));
    }

    private static async Task<IResult> ReportShortageAsync(
        Guid assemblyId, MobilePickingShortageRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        ReportPickingShortage report, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        return ResourceId(await report.ExecuteAsync(new ReportPickingShortageRequest(
            request.ClientRequestId, assemblyId, request.ExpectedRevision,
            request.ScannedLocationCode, request.SkuId, request.ExpirationDate,
            request.ExpectedQuantity, request.FoundQuantity, userId,
            request.ExistingPickingEntryId, request.Comment), ct));
    }

    private static async Task<IResult> CheckClosureAsync(
        Guid assemblyId, Guid warehouseId, ClaimsPrincipal principal,
        IPickingAssemblyStore assemblies, CheckPickingClosure check,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies, assemblyId, warehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        var result = await check.ExecuteAsync(assemblyId, userId, ct: ct);
        if (!result.IsSuccess)
            return MobileEndpointResults.CommandProblem(result.Error!);
        var review = result.Value!;
        return TypedResults.Ok(new MobilePickingClosureCheckResponse(
            review.Revision, review.FactMatchesPlan, review.CanOperatorClose,
            review.HardBlocks, review.ManagerIssues));
    }

    private static async Task<IResult> CloseAsync(
        Guid assemblyId, MobilePickingCloseRequest request,
        ClaimsPrincipal principal, IPickingAssemblyStore assemblies,
        CloseAndShipPickingAssembly close, CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return TypedResults.Unauthorized();
        var warehouse = await CheckWarehouseAsync(assemblies,
            assemblyId, request.WarehouseId, ct);
        if (!warehouse.IsSuccess)
            return MobileEndpointResults.CommandProblem(warehouse.Error!);
        var result = await close.ExecuteAsync(new ClosePickingAssemblyRequest(
            request.ClientRequestId, assemblyId, request.ExpectedRevision,
            userId), ct);
        return result.IsSuccess
            ? TypedResults.Ok(new MobilePickingCloseResponse(result.Value!.PickingAssemblyId,
                result.Value.AllConfirmed, result.Value.Documents.Select(x =>
                    new MobilePickingExchangeDocumentResponse(x.ShippingOrderId,
                        (MobileShippingExchangeStage)x.Stage, x.Error)).ToArray()))
            : MobileEndpointResults.CommandProblem(result.Error!);
    }

    private static async Task<OperationResult> CheckWarehouseAsync(
        IPickingAssemblyStore store, Guid assemblyId, Guid warehouseId,
        CancellationToken ct)
    {
        if (warehouseId == Guid.Empty || assemblyId == Guid.Empty)
            return OperationError.Invalid("Выберите склад и сборку.");
        var assembly = await store.ReadAssemblyAsync(assemblyId, ct);
        if (assembly is null)
            return OperationError.NotFound("Сборка WMS не найдена.");
        return assembly.WarehouseId == warehouseId
            ? OperationResult.Success()
            : OperationError.Conflict("Сборка относится к другому складу.");
    }

    private static IResult CommandId(OperationResult<Guid> result) =>
        result.IsSuccess
            ? TypedResults.Ok(new MobilePickingCommandIdResponse(result.Value))
            : MobileEndpointResults.CommandProblem(result.Error!);

    private static IResult ResourceId(OperationResult<Guid> result) =>
        result.IsSuccess
            ? TypedResults.Ok(new MobilePickingResourceIdResponse(result.Value))
            : MobileEndpointResults.CommandProblem(result.Error!);

    private static MobilePickingAssemblyListResponse MapList(
        PickingAssemblyListItem item) => new(
        item.Id, item.Number, item.WarehouseId,
        (MobilePickingAssemblyStatus)item.Status, item.Revision,
        item.CreatedBy, item.ResponsibleUserId, item.ResponsibleDisplayName,
        item.CreatedAtUtc, item.StartedAtUtc, item.HasWarning,
        item.Sources.Select(x => new MobilePickingListSourceResponse(
            x.SourceRefKey, x.Number, x.StartConfirmed,
            x.StartError, x.SourceCheckError)
            {
                ExchangeStage = x.ExchangeStage is { } stage
                    ? (MobileShippingExchangeStage)stage : null,
                ExchangeError = x.ExchangeError
            }).ToArray());

    private static MobilePickingAssemblyResponse MapAssembly(PickingAssemblyView view) => new(
        view.Id, view.Number, view.WarehouseId,
        (MobilePickingAssemblyStatus)view.Status, view.Revision,
        view.CreatedBy, view.ResponsibleUserId, view.ResponsibleDisplayName,
        view.CreatedAtUtc, view.FirstStartAttemptAtUtc, view.StartedAtUtc,
        view.ClosedAtUtc,
        view.Sources.Select(x => new MobilePickingSourceResponse(
            x.SourceRefKey, x.Number, x.Status.ToString(),
            x.Posted, x.DeletionMark, x.LastCheckedAtUtc, x.LastCheckError,
            x.StartConfirmedAtUtc, x.LastStartAttemptAtUtc, x.LastStartError,
            x.PlanSourceSnapshotJson, x.CurrentSourceJson)
            {
                ExchangeStage = x.ExchangeStage is { } stage
                    ? (MobileShippingExchangeStage)stage : null,
                ExchangeError = x.ExchangeError
            }).ToArray(),
        view.PlanLines.Select(x => new MobilePickingPlanLineResponse(
            x.SourceRefKey, x.SkuId, x.SkuCode, x.SkuName,
            x.Quantity)).ToArray());
}
