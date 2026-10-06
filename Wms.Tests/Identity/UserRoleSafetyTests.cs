using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Wms.Application.Users;
using Wms.Data;
using Wms.Tests.Infrastructure;

namespace Wms.Tests.Identity;

[Collection(SqlDatabaseCollection.Name)]
public sealed class UserRoleSafetyTests : IAsyncLifetime
{
    private readonly SqlDatabase _database = new();
    private readonly StampFailureInterceptor _failure = new();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDbContextFactory<ApplicationDbContext>>(_database);
        services.AddScoped(_ => _database.WithInterceptor(_failure).CreateDbContext());
        services.AddIdentityCore<ApplicationUser>(options =>
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddScoped<ApplicationUserManagementService>();
        _services = services.BuildServiceProvider();
        await using var scope = _services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in ApplicationRoles.All)
            Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_demotion_and_block_preserve_one_active_multi_role_administrator()
    {
        var first = await CreateAsync("first", ApplicationRoles.Administrator, ApplicationRoles.Manager);
        var second = await CreateAsync("second", ApplicationRoles.Administrator, ApplicationRoles.Manager);
        var service = _services.GetRequiredService<ApplicationUserManagementService>();

        // Both requests start while two administrators are active. Each targets
        // the other account, so the self-protection rule is not the deciding guard.
        var results = await Task.WhenAll(
            service.UpdateAsync(Update(first, second, [ApplicationRoles.Manager])),
            service.UpdateAsync(Update(second, first,
                [ApplicationRoles.Administrator, ApplicationRoles.Manager], blocked: true)));

        Assert.Single(results.Where(x => x.IsSuccess));
        Assert.Contains("последнего активного администратора", results.Single(x => !x.IsSuccess).Error!.Message);
        await using var scope = _services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admins = await manager.GetUsersInRoleAsync(ApplicationRoles.Administrator);
        var remaining = Assert.Single(admins.Where(x => x.LockoutEnd is null || x.LockoutEnd <= DateTimeOffset.UtcNow));
        Assert.True(await manager.IsInRoleAsync(remaining, ApplicationRoles.Manager));
    }

    [Fact]
    public async Task Role_changes_and_session_stamp_commit_together_or_roll_back()
    {
        var userId = await CreateAsync("account", ApplicationRoles.Administrator);
        var service = _services.GetRequiredService<ApplicationUserManagementService>();
        var originalStamp = await StampAsync(userId);
        var addManager = Update(userId, userId, [ApplicationRoles.Administrator, ApplicationRoles.Manager]);

        _failure.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(addManager));
        _failure.Armed = false;
        Assert.Equal(originalStamp, await StampAsync(userId));
        Assert.Equal(new[] { ApplicationRoles.Administrator }, await RolesAsync(userId));
        await using (var read = _database.CreateDbContext())
            Assert.Equal("account", (await read.Users.SingleAsync(x => x.Id == userId)).DisplayName);

        Assert.True((await service.UpdateAsync(addManager)).IsSuccess);
        var combinedStamp = await StampAsync(userId);
        Assert.NotEqual(originalStamp, combinedStamp);
        Assert.Equal(new[] { ApplicationRoles.Administrator, ApplicationRoles.Manager }, await RolesAsync(userId));
        var listed = await service.ListAsync(new ApplicationUserListQuery { Take = 10 });
        Assert.Equal(new[] { ApplicationRoles.Administrator, ApplicationRoles.Manager }, Assert.Single(listed.Items).Roles);

        Assert.True((await service.UpdateAsync(Update(userId, userId,
            [ApplicationRoles.Manager, ApplicationRoles.Administrator, ApplicationRoles.Manager]))).IsSuccess);
        Assert.Equal(combinedStamp, await StampAsync(userId));
        Assert.True((await service.UpdateAsync(Update(userId, userId, [ApplicationRoles.Administrator]))).IsSuccess);
        Assert.NotEqual(combinedStamp, await StampAsync(userId));
        Assert.Equal(new[] { ApplicationRoles.Administrator }, await RolesAsync(userId));
    }

    private async Task<string> CreateAsync(string name, params string[] roles)
    {
        var service = _services.GetRequiredService<ApplicationUserManagementService>();
        Assert.True((await service.CreateAsync(new CreateApplicationUserCommand
        {
            Email = $"{name}@example.test",
            DisplayName = name,
            Password = "Test-password-123!",
            Roles = roles
        })).IsSuccess);
        await using var read = _database.CreateDbContext();
        return await read.Users.Where(x => x.Email == $"{name}@example.test").Select(x => x.Id).SingleAsync();
    }

    private static UpdateApplicationUserCommand Update(string target, string actor, string[] roles, bool blocked = false) => new()
    {
        UserId = target,
        CurrentUserId = actor,
        DisplayName = "Updated account",
        Roles = roles,
        IsBlocked = blocked
    };

    private async Task<string?> StampAsync(string userId)
    {
        await using var read = _database.CreateDbContext();
        return await read.Users.Where(x => x.Id == userId).Select(x => x.SecurityStamp).SingleAsync();
    }

    private async Task<string[]> RolesAsync(string userId)
    {
        await using var scope = _services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await manager.FindByIdAsync(userId))!;
        return (await manager.GetRolesAsync(user)).Order().ToArray();
    }

    private sealed class StampFailureInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<ApplicationUser>().Any(entry =>
                entry.Property(x => x.SecurityStamp).OriginalValue != entry.Property(x => x.SecurityStamp).CurrentValue))
            {
                throw new InvalidOperationException("Injected security stamp persistence failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
