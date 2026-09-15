using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using MudBlazor.Services;
using Serilog;
using Wms.Application;
using Wms.Application.Users;
using Wms.Data;
using Wms.Integration;
using Wms.WebApp.Components;
using Wms.WebApp.Components.Account;

namespace Wms.WebApp;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));

        // Add MudBlazor services
        builder.Services.AddMudServices();

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.SignIn.RequireConfirmedAccount = true;
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddErrorDescriber<RussianIdentityErrorDescriber>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
        builder.Services.AddScoped<ApplicationUserManagementService>();
        builder.Services.Configure<IdentityBootstrapOptions>(
            builder.Configuration.GetSection(IdentityBootstrapOptions.SectionName));

        builder.Services.AddApplicationDbContext(builder.Configuration);
        builder.Services.AddIntegrationServices(builder.Configuration);
        builder.Services.AddApplicationServices(builder.Configuration);

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapGet("/lpn-labels/print/{id:guid}", async (Guid id, bool? batch,
            Wms.Application.LicensePlateNumbers.LicensePlateNumberService labels, HttpContext context, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var items = await labels.GetForPrintAsync(id, batch == true, ct);
            return items.Count == 0 ? Results.NotFound("Этикетки не найдены.")
                : Results.Content(Printing.LicensePlateNumberPrintDocument.Render(items), "text/html; charset=utf-8");
        }).RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.Operator, ApplicationRoles.Administrator));
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // Add additional endpoints required by the Identity /Account Razor components.
        app.MapAdditionalIdentityEndpoints();

        await IdentityDataInitializer.InitializeAsync(app.Services);

        await app.RunAsync();
    }
}
