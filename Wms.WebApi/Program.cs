using Serilog;
using SerilogTracing;
using Wms.Application;
using Wms.Data;
using Wms.Endpoints;
using Wms.Integration;
using Wms.Integration.OneS.Endpoints;

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

        // Add services to the container.
        builder.Services.AddAuthorization();

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();

        builder.Services.AddProblemDetails();

        builder.Services.AddApplicationDbContext(builder.Configuration);
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

        app.UseAuthorization();

        app.UseSerilogRequestLogging();

        app.MapApplicationEndpoints();
        app.MapOneCEndpoints();

        app.Run();
    }
}
