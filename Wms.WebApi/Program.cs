using Microsoft.AspNetCore.Identity;
using Serilog;
using SerilogTracing;
using Wms.Application;
using Wms.Data;
using Wms.Endpoints;
using Wms.Integration;
using Wms.Integration.OneS.Endpoints;
using Wms.WebApi.Mobile;

namespace Wms.WebApi;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));

        using var tracing = new ActivityListenerConfiguration()
            .Instrument.AspNetCoreRequests()
            .Instrument.SqlClientCommands()
            .TraceToSharedLogger();

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        builder.Services.AddProblemDetails();

        builder.Services.AddApplicationDbContext(builder.Configuration);

        builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddApiEndpoints();

        builder.Services.AddAuthentication(IdentityConstants.BearerScheme)
            .AddBearerToken(IdentityConstants.BearerScheme);

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(
                MobileAuthorization.WarehouseOperatorPolicy,
                policy => policy.RequireRole(ApplicationRoles.All));

        builder.Services.AddIntegrationServices(builder.Configuration);
        builder.Services.AddApplicationServices(builder.Configuration);
        builder.Services.AddNotificationServices();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "v1");
            });
        }

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseSerilogRequestLogging();

        app.MapApplicationEndpoints();
        app.MapMobileIdentityEndpoints();
        app.MapMobileBarcodeEndpoints();
        app.MapOneCEndpoints();

        app.Run();
    }
}
