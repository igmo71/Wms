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
            .WithTags("1С")
            .ProducesValidationProblem();

        group.MapGet("/Catalog_Номенклатура/import", ImportListCatalog_Номенклатура)
            .WithTags("Import Catalog_Номенклатура");

        group.MapPost("/Catalog_Номенклатура/notify", NotifyCatalog_Номенклатура)
           .WithTags("Notify Catalog_Номенклатура");

        return routeBuilder;
    }

    static async Task<IResult> ImportListCatalog_Номенклатура(
        [FromServices] Catalog_Номенклатура_Service service,
        CancellationToken cancellationToken)
    {
        await service.ImportList(cancellationToken);

        return TypedResults.Ok();
    }

    static async Task<IResult> NotifyCatalog_Номенклатура(
        [FromServices] NotifyChannel notifyChannel,
        [FromBody] NotifyRequest request,
        CancellationToken cancellationToken)
    {
        await notifyChannel.Writer.WriteAsync(
            new NotifyRecord(request.Ref_Key, nameof(Catalog_Номенклатура)),
            cancellationToken);

        return TypedResults.Ok();
    }

}
