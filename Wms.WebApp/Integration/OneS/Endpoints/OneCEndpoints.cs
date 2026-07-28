using Microsoft.AspNetCore.Mvc;
using Wms.WebApp.Integration.OneS.Services;

namespace Wms.WebApp.Integration.OneS.Endpoints;

public static class OneCEndpoints
{
    public static IEndpointRouteBuilder MapOneCEndpints(this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("1С")
            .ProducesValidationProblem();

        group.MapGet("/", GetListCatalog_УпаковкиЕдиницыИзмерения)
            .WithTags("Tag: Get List Catalog_УпаковкиЕдиницыИзмерения")
            .WithName("Name: Get List Catalog_УпаковкиЕдиницыИзмерения")
            .WithSummary("Summary: Get List Catalog_УпаковкиЕдиницыИзмерения")
            .WithDescription("Description: Get List Catalog_УпаковкиЕдиницыИзмерения");

        return routeBuilder;
    }

    static async Task<IResult> GetListCatalog_УпаковкиЕдиницыИзмерения(
        [FromServices] Catalog_УпаковкиЕдиницыИзмерения_Service service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetList(cancellationToken);

        return TypedResults.Ok(result);
    }
}
