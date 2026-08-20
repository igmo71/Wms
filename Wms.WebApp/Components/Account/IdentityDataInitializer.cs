using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wms.Data;

namespace Wms.WebApp.Components.Account;

internal static class IdentityDataInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptions<IdentityBootstrapOptions>>()
            .Value;
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(IdentityDataInitializer));

        foreach (var roleName in ApplicationRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            EnsureSucceeded(
                await roleManager.CreateAsync(new IdentityRole(roleName)),
                $"Не удалось создать роль {roleName}");
        }

        var administrator = await EnsureBootstrapAdministratorAsync(userManager, options);
        if (administrator is not null && !await userManager.IsInRoleAsync(administrator, ApplicationRoles.Administrator))
        {
            EnsureSucceeded(
                await userManager.AddToRoleAsync(administrator, ApplicationRoles.Administrator),
                "Не удалось назначить роль первоначальному администратору");
            EnsureSucceeded(
                await userManager.UpdateSecurityStampAsync(administrator),
                "Не удалось обновить сессию первоначального администратора");
        }

        if (administrator is not null && await userManager.IsInRoleAsync(administrator, ApplicationRoles.Operator))
        {
            EnsureSucceeded(
                await userManager.RemoveFromRoleAsync(administrator, ApplicationRoles.Operator),
                "Не удалось снять роль Operator с первоначального администратора");
            EnsureSucceeded(
                await userManager.UpdateSecurityStampAsync(administrator),
                "Не удалось обновить сессию первоначального администратора");
        }

        foreach (var user in await userManager.Users.ToListAsync())
        {
            if (await userManager.IsInRoleAsync(user, ApplicationRoles.Administrator)
                || await userManager.IsInRoleAsync(user, ApplicationRoles.Operator))
            {
                continue;
            }

            EnsureSucceeded(
                await userManager.AddToRoleAsync(user, ApplicationRoles.Operator),
                $"Не удалось назначить роль Operator пользователю {user.Id}");
            EnsureSucceeded(
                await userManager.UpdateSecurityStampAsync(user),
                $"Не удалось обновить сессию пользователя {user.Id}");
        }

        if (administrator is null)
        {
            var existingAdministrators = await userManager.GetUsersInRoleAsync(ApplicationRoles.Administrator);
            if (existingAdministrators.Count == 0)
            {
                logger.LogWarning(
                    "Первоначальный администратор не настроен. Укажите {Section}:{Email} через конфигурацию, чтобы открыть управление пользователями.",
                    IdentityBootstrapOptions.SectionName,
                    nameof(IdentityBootstrapOptions.AdministratorEmail));
            }
        }
    }

    private static async Task<ApplicationUser?> EnsureBootstrapAdministratorAsync(
        UserManager<ApplicationUser> userManager,
        IdentityBootstrapOptions options)
    {
        var email = options.AdministratorEmail?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var user = await userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            if (string.IsNullOrWhiteSpace(user.DisplayName)
                && !string.IsNullOrWhiteSpace(options.AdministratorDisplayName))
            {
                user.DisplayName = options.AdministratorDisplayName.Trim();
                EnsureSucceeded(
                    await userManager.UpdateAsync(user),
                    "Не удалось заполнить имя первоначального администратора");
            }

            return user;
        }

        if (string.IsNullOrWhiteSpace(options.AdministratorDisplayName)
            || string.IsNullOrWhiteSpace(options.AdministratorPassword))
        {
            throw new InvalidOperationException(
                $"Пользователь {email} не найден. Для создания первоначального администратора задайте " +
                $"{IdentityBootstrapOptions.SectionName}:{nameof(IdentityBootstrapOptions.AdministratorDisplayName)} и " +
                $"{IdentityBootstrapOptions.SectionName}:{nameof(IdentityBootstrapOptions.AdministratorPassword)} через секретную конфигурацию.");
        }

        if (options.AdministratorDisplayName.Trim().Length > ApplicationUser.DisplayNameMaxLength)
        {
            throw new InvalidOperationException(
                $"Имя первоначального администратора не должно превышать {ApplicationUser.DisplayNameMaxLength} символов.");
        }

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            DisplayName = options.AdministratorDisplayName.Trim()
        };

        EnsureSucceeded(
            await userManager.CreateAsync(user, options.AdministratorPassword),
            "Не удалось создать первоначального администратора");

        return user;
    }

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (result.Succeeded)
            return;

        throw new InvalidOperationException(
            $"{message}: {string.Join("; ", result.Errors.Select(x => x.Description))}");
    }
}
