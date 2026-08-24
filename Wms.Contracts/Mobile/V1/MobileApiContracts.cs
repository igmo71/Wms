namespace Wms.Contracts.Mobile.V1;

public static class MobileApiRoutes
{
    public const string Base = "/api/mobile/v1";
    public const string Login = Base + "/auth/login";
    public const string Refresh = Base + "/auth/refresh";
    public const string Me = Base + "/me";
}

public sealed record MobileLoginRequest(string Email, string Password);

public sealed record MobileRefreshRequest(string RefreshToken);

public sealed record MobileSessionResponse(
    string TokenType,
    string AccessToken,
    long ExpiresIn,
    string RefreshToken);

public sealed record MobileCurrentUserResponse(
    string Id,
    string DisplayName,
    string Email);

public sealed record MobileProblemResponse(string Code, string Message);
