using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Serilog;
using SerilogTracing;
using Wms.Application;
using Wms.Contracts.Mobile.V1;
using Wms.Data;
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

        // The shared EF context lives in Wms.Infrastructure; new use cases use this registration.
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
            .AddBearerToken(IdentityConstants.BearerScheme, options =>
            {
                var configuredExpiration = builder.Configuration.GetValue<TimeSpan?>(
                    "MobileAuthentication:AccessTokenExpiration");
                if (configuredExpiration is { } expiration)
                {
                    options.BearerTokenExpiration = expiration;
                }
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(
                MobileAuthorization.WarehouseOperatorPolicy,
                policy => policy.RequireRole(ApplicationRoles.All))
            .AddPolicy(
                MobileAuthorization.ReceivingWorkerPolicy,
                policy => policy.RequireRole(ApplicationRoles.Operator))
            .AddPolicy(
                MobileAuthorization.PickingWorkerPolicy,
                policy => policy.RequireRole(ApplicationRoles.Operator));

        // Existing service registrations remain in the legacy Wms assembly until their replacement.
        builder.Services.AddIntegrationServices(builder.Configuration);
        builder.Services.AddReceivingSourceReader(builder.Configuration);
        builder.Services.AddShippingSourceReader(builder.Configuration);
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

        app.Use((context, next) =>
        {
            if (context.Request.Headers[MobileDiagnosticHeaders.RequestServerDuration] == "1"
                && context.Request.Path.StartsWithSegments(MobileApiRoutes.Base))
            {
                var started = Stopwatch.GetTimestamp();
                context.Response.OnStarting(() =>
                {
                    // Headers must be set before the first response bytes are sent.
                    context.Response.Headers[MobileDiagnosticHeaders.ServerDurationMilliseconds] =
                        Stopwatch.GetElapsedTime(started).TotalMilliseconds
                            .ToString("0.000", CultureInfo.InvariantCulture);
                    return Task.CompletedTask;
                });
            }
            return next(context);
        });

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseSerilogRequestLogging();

        app.MapMobileIdentityEndpoints();
        app.MapMobileBarcodeEndpoints();
        app.MapMobileReceivingOrderEndpoints();
        app.MapMobileLicensePlateNumberPutawayEndpoints();
        app.MapMobileInventoryTransferEndpoints();
        app.MapMobileInventoryCountEndpoints();
        app.MapMobilePickingEndpoints();
        app.MapMobileReplenishmentEndpoints();
        app.MapMobileStockAvailabilityEndpoints();
        app.MapMobileStockCorrectionEndpoints();
        app.MapOneCEndpoints();

        app.Run();
    }
}
