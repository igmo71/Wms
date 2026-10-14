using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Wms.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationDbContext(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Строка подключения 'DefaultConnection' не найдена.");

        services.AddDbContextFactory<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString);
            options.EnableSensitiveDataLogging();
            options.EnableDetailedErrors();
        });

        services.AddDatabaseDeveloperPageExceptionFilter();

        return services;
    }
}
