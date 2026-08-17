using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS.Endpoints;

public static class Catalog_Партнеры_Endpoints
{
    public static IEndpointRouteBuilder MapCatalog_Партнеры_Endpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("Catalog_Партнеры")
            .ProducesValidationProblem();

        group.MapGet("/Catalog_Партнеры/import", Import)
            .WithName("Catalog_Партнеры.Import");
        group.MapPost("/Catalog_Партнеры/notify", Notify)
            .WithName("Catalog_Партнеры.Notify");

        return routeBuilder;
    }

    private static async Task<IResult> Import(
        [FromServices] Catalog_Партнеры_Service service,
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
            new NotifyRecord(request.Ref_Key, nameof(Catalog_Партнеры)),
            ct);

        return TypedResults.Ok();
    }
}
