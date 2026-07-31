using Microsoft.AspNetCore.Routing;

namespace Wms.Endpoints;

public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapReceivingOrderEndpoints();

        return routeBuilder;
    }
}
