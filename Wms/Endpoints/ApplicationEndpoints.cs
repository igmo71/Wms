using Microsoft.AspNetCore.Routing;

namespace Wms.Endpoints;

public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        routeBuilder.MapReceivingOrderEndpoints();

        return routeBuilder;
    }
}
