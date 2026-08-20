using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS.Endpoints;

internal static class Catalog_СтруктураПредприятия_Endpoints
{
    public static IEndpointRouteBuilder MapCatalog_СтруктураПредприятия_Endpoints(
        this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("Catalog_СтруктураПредприятия")
            .ProducesValidationProblem();

        group.MapGet("/Catalog_СтруктураПредприятия/import", Import)
            .WithName("Catalog_СтруктураПредприятия.Import");
        group.MapPost("/Catalog_СтруктураПредприятия/notify", Notify)
            .WithName("Catalog_СтруктураПредприятия.Notify");

        return routeBuilder;
    }

    private static async Task<IResult> Import(
        [FromServices] Catalog_СтруктураПредприятия_Service service,
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
            new NotifyRecord(request.Ref_Key, nameof(Catalog_СтруктураПредприятия)),
            ct);

        return TypedResults.Ok();
    }
}
