using Microsoft.Extensions.DependencyInjection;

namespace Wms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<UnitOfMeasureService>();

        return services;
    }
}
