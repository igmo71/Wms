using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Picking;
using Wms.Common;
using Wms.Contracts.Mobile.V1;
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

    private static MobilePickingAssemblyListResponse MapList(
        PickingAssemblyListItem item) => new(
        item.Id, item.Number, item.WarehouseId,
        (MobilePickingAssemblyStatus)item.Status, item.Revision,
        item.CreatedBy, item.ResponsibleUserId, item.ResponsibleDisplayName,
        item.CreatedAtUtc, item.StartedAtUtc, item.HasWarning,
        item.Sources.Select(x => new MobilePickingListSourceResponse(
            x.SourceRefKey, x.Number, x.StartConfirmed,
            x.StartError, x.SourceCheckError)).ToArray());

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
            x.PlanSourceSnapshotJson, x.CurrentSourceJson)).ToArray(),
        view.PlanLines.Select(x => new MobilePickingPlanLineResponse(
            x.SourceRefKey, x.SkuId, x.SkuCode, x.SkuName,
            x.Quantity)).ToArray());
}
