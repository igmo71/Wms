using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using Wms.Common;
using Wms.Data;

namespace Wms.Application.Users;

public sealed class ApplicationUserManagementService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    IServiceScopeFactory scopeFactory)
{
    public async Task<ListResult<ApplicationUserListItem>> ListAsync(
        ApplicationUserListQuery listQuery,
        CancellationToken ct = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        var query = dbContext.Users
            .AsNoTracking()
            .Select(user => new ApplicationUserListItem
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? user.Id,
                DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                    ? user.UserName ?? user.Id
                    : user.DisplayName,
                DefaultWarehouseId = user.DefaultWarehouseId,
                DefaultWarehouseName = user.DefaultWarehouse == null
                    ? null
                    : user.DefaultWarehouse.Name,
                DefaultWarehouseIsDeleted = user.DefaultWarehouse != null
                    && user.DefaultWarehouse.DeletionMark,
                Roles = (from userRole in dbContext.UserRoles
                        join role in dbContext.Roles on userRole.RoleId equals role.Id
                        where userRole.UserId == user.Id
                            && (role.Name == ApplicationRoles.Administrator
                                || role.Name == ApplicationRoles.Manager
                                || role.Name == ApplicationRoles.Operator)
                        orderby role.Name
                        select role.Name!).ToArray(),
                IsBlocked = user.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow
            });

        if (!string.IsNullOrWhiteSpace(listQuery.SearchString))
        {
            var searchString = listQuery.SearchString.Trim();
            query = query.Where(x => x.DisplayName.Contains(searchString)
                || x.Email.Contains(searchString));
        }

        query = listQuery.SortBy switch
        {
            "Email" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.Email).ThenBy(x => x.DisplayName)
                : query.OrderBy(x => x.Email).ThenBy(x => x.DisplayName),
            _ => listQuery.SortDescending
                ? query.OrderByDescending(x => x.DisplayName).ThenBy(x => x.Email)
                : query.OrderBy(x => x.DisplayName).ThenBy(x => x.Email)
        };

        var totalItems = await query.CountAsync(ct);
        var items = await query
            .Skip(listQuery.Skip)
            .Take(listQuery.Take)
            .ToListAsync(ct);

        return new ListResult<ApplicationUserListItem>
        {
            Items = items,
            TotalItems = totalItems
        };
    }

    public async Task<OperationResult> CreateAsync(CreateApplicationUserCommand command)
    {
        var validationError = Validate(command.DisplayName, command.Roles);
        if (validationError is not null)
            return validationError;

        if (string.IsNullOrWhiteSpace(command.Email))
            return OperationError.Invalid("Укажите email пользователя.");

        if (string.IsNullOrWhiteSpace(command.Password))
            return OperationError.Invalid("Укажите начальный пароль пользователя.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        if (command.DefaultWarehouseId is Guid defaultWarehouseId
            && !await IsActiveWarehouseAsync(dbContext, defaultWarehouseId))
            return OperationError.Invalid("Склад по умолчанию не найден или недоступен.");

        var email = command.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            DisplayName = NormalizeDisplayName(command.DisplayName),
            DefaultWarehouseId = command.DefaultWarehouseId
        };

        var createResult = await userManager.CreateAsync(user, command.Password);
        if (!createResult.Succeeded)
            return ToOperationError(createResult, "Не удалось создать пользователя");

        var roleResult = await userManager.AddToRolesAsync(user, command.Roles.Distinct());
        if (!roleResult.Succeeded)
            return ToOperationError(roleResult, "Не удалось назначить роли пользователю");

        await transaction.CommitAsync();
        return OperationResult.Success();
    }

    public async Task<OperationResult> UpdateAsync(UpdateApplicationUserCommand command)
    {
        var validationError = Validate(command.DisplayName, command.Roles);
        if (validationError is not null)
            return validationError;

        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        // Serialize user administration before reading membership: concurrent demotions
        // must not each observe the other administrator as still active.
        await dbContext.Roles.FromSqlInterpolated($"""
            SELECT * FROM [AspNetRoles] WITH (UPDLOCK, HOLDLOCK)
            WHERE [NormalizedName] = {ApplicationRoles.Administrator.ToUpperInvariant()}
            """).LoadAsync();

        var user = await userManager.FindByIdAsync(command.UserId);
        if (user is null)
            return OperationError.NotFound($"Пользователь {command.UserId} не найден.");

        if (command.DefaultWarehouseId != user.DefaultWarehouseId
            && command.DefaultWarehouseId is Guid defaultWarehouseId
            && !await IsActiveWarehouseAsync(dbContext, defaultWarehouseId))
            return OperationError.Invalid("Склад по умолчанию не найден или недоступен.");

        var currentRoles = await userManager.GetRolesAsync(user);
        var requestedRoles = command.Roles.ToHashSet(StringComparer.Ordinal);
        var currentWmsRoles = currentRoles.Where(ApplicationRoles.All.Contains).ToHashSet(StringComparer.Ordinal);
        var rolesChanged = !currentWmsRoles.SetEquals(requestedRoles);
        var securityChanged = rolesChanged || IsBlocked(user) != command.IsBlocked;
        var isAdministrator = currentWmsRoles.Contains(ApplicationRoles.Administrator);
        var removesAdministrator = isAdministrator
            && !requestedRoles.Contains(ApplicationRoles.Administrator);
        var blocksActiveAdministrator = isAdministrator
            && command.IsBlocked
            && !IsBlocked(user);

        if (user.Id == command.CurrentUserId && (removesAdministrator || command.IsBlocked))
        {
            return OperationError.Invalid(
                "Нельзя заблокировать собственную учетную запись или снять у себя роль администратора.");
        }

        if ((removesAdministrator || blocksActiveAdministrator)
            && !await HasAnotherActiveAdministratorAsync(userManager, user.Id))
        {
            return OperationError.Invalid(
                "Нельзя заблокировать или понизить последнего активного администратора.");
        }

        user.DisplayName = NormalizeDisplayName(command.DisplayName);
        user.DefaultWarehouseId = command.DefaultWarehouseId;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return ToOperationError(updateResult, "Не удалось обновить пользователя");

        if (rolesChanged)
        {
            var addedRoles = requestedRoles.Except(currentWmsRoles).ToArray();
            if (addedRoles.Length > 0)
            {
                var addRoleResult = await userManager.AddToRolesAsync(user, addedRoles);
                if (!addRoleResult.Succeeded)
                    return ToOperationError(addRoleResult, "Не удалось назначить роли пользователю");
            }

            var obsoleteRoles = currentWmsRoles.Except(requestedRoles).ToArray();
            if (obsoleteRoles.Length > 0)
            {
                var removeRoleResult = await userManager.RemoveFromRolesAsync(user, obsoleteRoles);
                if (!removeRoleResult.Succeeded)
                    return ToOperationError(removeRoleResult, "Не удалось снять прежние роли пользователя");
            }
        }

        var lockoutEnd = command.IsBlocked ? DateTimeOffset.MaxValue : (DateTimeOffset?)null;
        var lockoutResult = await userManager.SetLockoutEndDateAsync(user, lockoutEnd);
        if (!lockoutResult.Succeeded)
            return ToOperationError(lockoutResult, "Не удалось изменить блокировку пользователя");

        if (securityChanged)
        {
            var securityStampResult = await userManager.UpdateSecurityStampAsync(user);
            if (!securityStampResult.Succeeded)
                return ToOperationError(securityStampResult, "Не удалось обновить сессию пользователя");
        }

        await transaction.CommitAsync();
        return OperationResult.Success();
    }

    private static async Task<bool> HasAnotherActiveAdministratorAsync(
        UserManager<ApplicationUser> userManager, string excludedUserId)
    {
        var administrators = await userManager.GetUsersInRoleAsync(ApplicationRoles.Administrator);
        return administrators.Any(x => x.Id != excludedUserId && !IsBlocked(x));
    }

    private static bool IsBlocked(ApplicationUser user) =>
        user.LockoutEnd is DateTimeOffset lockoutEnd && lockoutEnd > DateTimeOffset.UtcNow;

    private static Task<bool> IsActiveWarehouseAsync(
        ApplicationDbContext dbContext, Guid warehouseId) =>
        dbContext.Warehouses.AnyAsync(x => x.Id == warehouseId && !x.DeletionMark);

    private static OperationError? Validate(string displayName, string[]? roles)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return OperationError.Invalid("Укажите отображаемое имя пользователя.");

        if (displayName.Trim().Length > ApplicationUser.DisplayNameMaxLength)
            return OperationError.Invalid(
                $"Отображаемое имя не должно превышать {ApplicationUser.DisplayNameMaxLength} символов.");

        if (roles is null || roles.Length == 0)
            return OperationError.Invalid("Выберите хотя бы одну роль пользователя.");

        if (roles.Any(role => !ApplicationRoles.All.Contains(role)))
            return OperationError.Invalid("Выбрана неизвестная роль пользователя.");

        return null;
    }

    private static string NormalizeDisplayName(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static OperationError ToOperationError(IdentityResult result, string prefix) =>
        OperationError.Invalid($"{prefix}: {string.Join("; ", result.Errors.Select(x => x.Description))}");
}
