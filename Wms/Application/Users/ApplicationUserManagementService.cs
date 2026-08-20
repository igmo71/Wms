using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Wms.Common;
using Wms.Data;

namespace Wms.Application.Users;

public sealed class ApplicationUserManagementService(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    UserManager<ApplicationUser> userManager)
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
                Role = (from userRole in dbContext.UserRoles
                        join role in dbContext.Roles on userRole.RoleId equals role.Id
                        where userRole.UserId == user.Id
                            && (role.Name == ApplicationRoles.Administrator
                                || role.Name == ApplicationRoles.Operator)
                        select role.Name).FirstOrDefault() ?? string.Empty,
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
            "Role" => listQuery.SortDescending
                ? query.OrderByDescending(x => x.Role).ThenBy(x => x.DisplayName)
                : query.OrderBy(x => x.Role).ThenBy(x => x.DisplayName),
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
        var validationError = Validate(command.DisplayName, command.Role);
        if (validationError is not null)
            return validationError;

        if (string.IsNullOrWhiteSpace(command.Email))
            return OperationError.Invalid("Укажите email пользователя.");

        if (string.IsNullOrWhiteSpace(command.Password))
            return OperationError.Invalid("Укажите начальный пароль пользователя.");

        var email = command.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            DisplayName = NormalizeDisplayName(command.DisplayName)
        };

        var createResult = await userManager.CreateAsync(user, command.Password);
        if (!createResult.Succeeded)
            return ToOperationError(createResult, "Не удалось создать пользователя");

        var roleResult = await userManager.AddToRoleAsync(user, command.Role);
        if (roleResult.Succeeded)
            return OperationResult.Success();

        await userManager.DeleteAsync(user);
        return ToOperationError(roleResult, "Не удалось назначить роль пользователю");
    }

    public async Task<OperationResult> UpdateAsync(UpdateApplicationUserCommand command)
    {
        var validationError = Validate(command.DisplayName, command.Role);
        if (validationError is not null)
            return validationError;

        var user = await userManager.FindByIdAsync(command.UserId);
        if (user is null)
            return OperationError.NotFound($"Пользователь {command.UserId} не найден.");

        var currentRoles = await userManager.GetRolesAsync(user);
        var currentRole = currentRoles.FirstOrDefault(ApplicationRoles.All.Contains);
        var securityChanged = currentRole != command.Role || IsBlocked(user) != command.IsBlocked;
        var removesAdministrator = currentRole == ApplicationRoles.Administrator
            && command.Role != ApplicationRoles.Administrator;
        var blocksActiveAdministrator = currentRole == ApplicationRoles.Administrator
            && command.IsBlocked
            && !IsBlocked(user);

        if (user.Id == command.CurrentUserId && (removesAdministrator || command.IsBlocked))
        {
            return OperationError.Invalid(
                "Нельзя заблокировать собственную учетную запись или снять у себя роль администратора.");
        }

        if ((removesAdministrator || blocksActiveAdministrator)
            && !await HasAnotherActiveAdministratorAsync(user.Id))
        {
            return OperationError.Invalid(
                "Нельзя заблокировать или понизить последнего активного администратора.");
        }

        user.DisplayName = NormalizeDisplayName(command.DisplayName);
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return ToOperationError(updateResult, "Не удалось обновить пользователя");

        if (currentRole != command.Role)
        {
            var addRoleResult = await userManager.AddToRoleAsync(user, command.Role);
            if (!addRoleResult.Succeeded)
                return ToOperationError(addRoleResult, "Не удалось назначить роль пользователю");

            var obsoleteRoles = currentRoles.Where(ApplicationRoles.All.Contains).ToArray();
            if (obsoleteRoles.Length > 0)
            {
                var removeRoleResult = await userManager.RemoveFromRolesAsync(user, obsoleteRoles);
                if (!removeRoleResult.Succeeded)
                    return ToOperationError(removeRoleResult, "Не удалось снять прежнюю роль пользователя");
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

        return OperationResult.Success();
    }

    private async Task<bool> HasAnotherActiveAdministratorAsync(string excludedUserId)
    {
        var administrators = await userManager.GetUsersInRoleAsync(ApplicationRoles.Administrator);
        return administrators.Any(x => x.Id != excludedUserId && !IsBlocked(x));
    }

    private static bool IsBlocked(ApplicationUser user) =>
        user.LockoutEnd is DateTimeOffset lockoutEnd && lockoutEnd > DateTimeOffset.UtcNow;

    private static OperationError? Validate(string displayName, string role)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return OperationError.Invalid("Укажите отображаемое имя пользователя.");

        if (displayName.Trim().Length > ApplicationUser.DisplayNameMaxLength)
            return OperationError.Invalid(
                $"Отображаемое имя не должно превышать {ApplicationUser.DisplayNameMaxLength} символов.");

        if (!ApplicationRoles.All.Contains(role))
            return OperationError.Invalid("Выбрана неизвестная роль пользователя.");

        return null;
    }

    private static string NormalizeDisplayName(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static OperationError ToOperationError(IdentityResult result, string prefix) =>
        OperationError.Invalid($"{prefix}: {string.Join("; ", result.Errors.Select(x => x.Description))}");
}
