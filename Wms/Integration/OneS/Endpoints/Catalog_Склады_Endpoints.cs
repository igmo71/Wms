using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS.Endpoints;

public static class Catalog_Склады_Endpoints
{
    public static IEndpointRouteBuilder MapCatalog_Склады_Endpoints(this IEndpointRouteBuilder routeBuilder)
    {

        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("1С")
            .ProducesValidationProblem();

        group.MapGet("/Catalog_Склады/import", ImportListCatalog_Склады)
            .WithTags("Import Catalog_Склады");

        group.MapPost("/Catalog_Склады/notify", NotifyCatalog_Склады)
           .WithTags("Notify Catalog_Склады");

        return routeBuilder;
    }

    static async Task<IResult> ImportListCatalog_Склады(
        [FromServices] Catalog_Склады_Service service,
        CancellationToken cancellationToken)
    {
        await service.ImportListAsync(cancellationToken);

        return TypedResults.Ok();
    }

    static async Task<IResult> NotifyCatalog_Склады(
        [FromServices] NotifyChannel notifyChannel,
        [FromBody] NotifyRequest request,
        CancellationToken cancellationToken)
    {
        await notifyChannel.Writer.WriteAsync(
            new NotifyRecord(request.Ref_Key, nameof(Catalog_Склады)),
            cancellationToken);

        return TypedResults.Ok();
    }
}
