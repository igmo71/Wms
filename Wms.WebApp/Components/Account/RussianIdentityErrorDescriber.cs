using Microsoft.AspNetCore.Identity;

namespace Wms.WebApp.Components.Account;

public sealed class RussianIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Error(nameof(DefaultError), "Произошла непредвиденная ошибка.");
    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "Данные были изменены. Повторите операцию.");
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "Неверный пароль.");
    public override IdentityError InvalidToken() => Error(nameof(InvalidToken), "Некорректный токен.");
    public override IdentityError RecoveryCodeRedemptionFailed() => Error(nameof(RecoveryCodeRedemptionFailed), "Код восстановления недействителен.");
    public override IdentityError LoginAlreadyAssociated() => Error(nameof(LoginAlreadyAssociated), "Этот внешний способ входа уже связан с другой учетной записью.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), $"Имя пользователя '{userName}' недопустимо.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), $"Адрес электронной почты '{email}' недопустим.");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), $"Имя пользователя '{userName}' уже используется.");
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), $"Адрес электронной почты '{email}' уже используется.");
    public override IdentityError InvalidRoleName(string? role) => Error(nameof(InvalidRoleName), $"Имя роли '{role}' недопустимо.");
    public override IdentityError DuplicateRoleName(string role) => Error(nameof(DuplicateRoleName), $"Имя роли '{role}' уже используется.");
    public override IdentityError UserAlreadyHasPassword() => Error(nameof(UserAlreadyHasPassword), "Для пользователя уже установлен пароль.");
    public override IdentityError UserLockoutNotEnabled() => Error(nameof(UserLockoutNotEnabled), "Блокировка для этого пользователя не включена.");
    public override IdentityError UserAlreadyInRole(string role) => Error(nameof(UserAlreadyInRole), $"Пользователь уже входит в роль '{role}'.");
    public override IdentityError UserNotInRole(string role) => Error(nameof(UserNotInRole), $"Пользователь не входит в роль '{role}'.");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"Пароль должен содержать не менее {length} символов.");
    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => Error(nameof(PasswordRequiresUniqueChars), $"Пароль должен содержать не менее {uniqueChars} различных символов.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "Пароль должен содержать хотя бы один специальный символ.");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Пароль должен содержать хотя бы одну цифру.");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Пароль должен содержать хотя бы одну строчную букву.");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Пароль должен содержать хотя бы одну прописную букву.");

    private static IdentityError Error(string code, string description) => new()
    {
        Code = code,
        Description = description
    };
}
