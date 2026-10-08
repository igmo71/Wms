namespace Wms.Contracts.Mobile.V1;

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
    string Email,
    Guid? DefaultWarehouseId);

public sealed record MobileProblemResponse(string Code, string Message);

