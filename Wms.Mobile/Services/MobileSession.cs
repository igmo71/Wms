namespace Wms.Mobile.Services;

internal sealed record MobileSession(
    string TokenType,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAtUtc);
