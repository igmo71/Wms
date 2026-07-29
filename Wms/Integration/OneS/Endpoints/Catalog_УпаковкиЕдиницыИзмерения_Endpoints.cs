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
            .WithTags("1С")
            .ProducesValidationProblem();

        group.MapGet("/Catalog_УпаковкиЕдиницыИзмерения", GetListCatalog_УпаковкиЕдиницыИзмерения)
            .WithTags("Get List Catalog_УпаковкиЕдиницыИзмерения");

        group.MapGet("/Catalog_УпаковкиЕдиницыИзмерения/import", ImportListCatalog_УпаковкиЕдиницыИзмерения)
            .WithTags("Import Catalog_УпаковкиЕдиницыИзмерения");

        group.MapPost("/Catalog_УпаковкиЕдиницыИзмерения/notify", NotifyCatalog_УпаковкиЕдиницыИзмерения)
           .WithTags("Notify Catalog_УпаковкиЕдиницыИзмерения");

        return routeBuilder;
    }

    static async Task<IResult> GetListCatalog_УпаковкиЕдиницыИзмерения(
        [FromServices] Catalog_УпаковкиЕдиницыИзмерения_Service service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetList(cancellationToken);

        return TypedResults.Ok(result);
    }

    static async Task<IResult> ImportListCatalog_УпаковкиЕдиницыИзмерения(
        [FromServices] Catalog_УпаковкиЕдиницыИзмерения_Service service,
        CancellationToken cancellationToken)
    {
        await service.ImportList(cancellationToken);

        return TypedResults.Ok();
    }

    static async Task<IResult> NotifyCatalog_УпаковкиЕдиницыИзмерения(
        [FromServices] NotifyChannel notifyChannel,
        [FromBody] NotifyRequest request,
        CancellationToken cancellationToken)
    {
        await notifyChannel.Writer.WriteAsync(
            new NotifyRecord(request.Ref_Key, nameof(Catalog_УпаковкиЕдиницыИзмерения)),
            cancellationToken);

        return TypedResults.Ok();
    }
}
