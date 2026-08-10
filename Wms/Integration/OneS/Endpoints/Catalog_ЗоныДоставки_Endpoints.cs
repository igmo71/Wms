using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS.Endpoints;

public static class Catalog_ЗоныДоставки_Endpoints
{
    public static IEndpointRouteBuilder MapCatalog_ЗоныДоставки_Endpoints(this IEndpointRouteBuilder routeBuilder)
    {

        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("Catalog_ЗоныДоставки_Endpoints")
            .ProducesValidationProblem();

        group.MapGet("/Catalog_ЗоныДоставки_Endpoints/import", Import)
            .WithName("Catalog_ЗоныДоставки.Import");
        group.MapPost("/Catalog_ЗоныДоставки_Endpoints/notify", Notify)
            .WithName("Catalog_ЗоныДоставки.Notify");

        return routeBuilder;
    }

    static async Task<IResult> Import(
        [FromServices] Catalog_ЗоныДоставки_Service service,
        CancellationToken ct)
    {
        await service.ImportListAsync(ct);

        return TypedResults.Ok();
    }

    static async Task<IResult> Notify(
        [FromServices] NotifyChannel notifyChannel,
        [FromBody] NotifyRequest request,
        CancellationToken ct)
    {
        await notifyChannel.Writer.WriteAsync(
            new NotifyRecord(request.Ref_Key, nameof(Catalog_ЗоныДоставки)),
            ct);

        return TypedResults.Ok();
    }
}
