using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS.Endpoints;

internal static class Catalog_ФизическиеЛица_Endpoints
{
    public static IEndpointRouteBuilder MapCatalog_ФизическиеЛица_Endpoints(
        this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("Catalog_ФизическиеЛица")
            .ProducesValidationProblem();

        group.MapGet("/Catalog_ФизическиеЛица/import", Import)
            .WithName("Catalog_ФизическиеЛица.Import");
        group.MapPost("/Catalog_ФизическиеЛица/notify", Notify)
            .WithName("Catalog_ФизическиеЛица.Notify");

        return routeBuilder;
    }

    private static async Task<IResult> Import(
        [FromServices] Catalog_ФизическиеЛица_Service service,
        CancellationToken ct)
    {
        var result = await service.ImportListAsync(ct);

        return result.IsSuccess
            ? TypedResults.Ok()
            : Results.Problem(result.Error?.Message, statusCode: StatusCodes.Status502BadGateway);
    }

    private static async Task<IResult> Notify(
        [FromServices] NotifyChannel notifyChannel,
        [FromBody] NotifyRequest request,
        CancellationToken ct)
    {
        await notifyChannel.Writer.WriteAsync(
            new NotifyRecord(request.Ref_Key, nameof(Catalog_ФизическиеЛица)),
            ct);

        return TypedResults.Ok();
    }
}
