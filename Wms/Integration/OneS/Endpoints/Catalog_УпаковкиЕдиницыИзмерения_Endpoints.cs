using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS.Endpoints;

public static class Catalog_УпаковкиЕдиницыИзмерения_Endpoints
{
    public static IEndpointRouteBuilder MapCatalog_УпаковкиЕдиницыИзмерения_Endpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("Catalog_УпаковкиЕдиницыИзмерения")
            .ProducesValidationProblem();

        group.MapGet("/Catalog_УпаковкиЕдиницыИзмерения/import", Import)
            .WithName("Catalog_УпаковкиЕдиницыИзмерения.Import");
        group.MapPost("/Catalog_УпаковкиЕдиницыИзмерения/notify", Notify)
            .WithName("Catalog_УпаковкиЕдиницыИзмерения.Notify");

        return routeBuilder;
    }

    static async Task<IResult> Import(
        [FromServices] Catalog_УпаковкиЕдиницыИзмерения_Service service,
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
            new NotifyRecord(request.Ref_Key, nameof(Catalog_УпаковкиЕдиницыИзмерения)),
            ct);

        return TypedResults.Ok();
    }
}
