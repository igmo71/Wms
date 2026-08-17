using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS.Endpoints;

public static class Catalog_Номенклатура_Endpoints
{
    public static IEndpointRouteBuilder MapCatalog_Номенклатура_Endpoints(this IEndpointRouteBuilder routeBuilder)
    {

        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("Catalog_Номенклатура")
            .ProducesValidationProblem();

        group.MapGet("/Catalog_Номенклатура/import", Import)
            .WithName("Catalog_Номенклатура.Import");
        group.MapPost("/Catalog_Номенклатура/notify", Notify)
            .WithName("Catalog_Номенклатура.Notify");

        return routeBuilder;
    }

    static async Task<IResult> Import(
        [FromServices] Catalog_Номенклатура_Service service,
        CancellationToken ct)
    {
        var result = await service.ImportListAsync(ct);

        return result.IsSuccess
            ? TypedResults.Ok()
            : Results.Problem(result.Error?.Message, statusCode: StatusCodes.Status502BadGateway);
    }

    static async Task<IResult> Notify(
        [FromServices] NotifyChannel notifyChannel,
        [FromBody] NotifyRequest request,
        CancellationToken ct)
    {
        await notifyChannel.Writer.WriteAsync(
            new NotifyRecord(request.Ref_Key, nameof(Catalog_Номенклатура)),
            ct);

        return TypedResults.Ok();
    }

}
