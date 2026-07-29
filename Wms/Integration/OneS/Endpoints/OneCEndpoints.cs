using Microsoft.AspNetCore.Routing;

namespace Wms.Integration.OneS.Endpoints;

public static class OneCEndpoints
{
    public static IEndpointRouteBuilder MapOneCEndpints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapCatalog_УпаковкиЕдиницыИзмерения_Endpoints();

        return routeBuilder;
    }
}
