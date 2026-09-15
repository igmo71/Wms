using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Wms.Data;

namespace Wms.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlDatabaseCollection : ICollectionFixture<SqlDatabase>
{
    public const string Name = "SQL Server integration";
}

public sealed class SqlDatabase : IAsyncLifetime, IDbContextFactory<ApplicationDbContext>
{
    private readonly ServiceProvider _services;
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public SqlDatabase()
    {
        var databaseName = $"WmsTests_{Guid.NewGuid():N}";
        ConnectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true";
        _services = new ServiceCollection()
            .Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .BuildServiceProvider();
        _options = BuildOptions();
    }

    public string ConnectionString { get; }

    public ApplicationDbContext CreateDbContext() => new(_options);

    public IDbContextFactory<ApplicationDbContext> WithInterceptor(IInterceptor interceptor) =>
        new ContextFactory(BuildOptions(interceptor));

    public async Task InitializeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.EnsureDeletedAsync();
        await _services.DisposeAsync();
    }

    private DbContextOptions<ApplicationDbContext> BuildOptions(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseApplicationServiceProvider(_services)
            .UseSqlServer(ConnectionString);
        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);
        return builder.Options;
    }

    private sealed class ContextFactory(DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
    }
}
