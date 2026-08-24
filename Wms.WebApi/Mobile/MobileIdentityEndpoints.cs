using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Wms.Contracts.Mobile.V1;
using Wms.Data;

namespace Wms.WebApi.Mobile;

internal static class MobileIdentityEndpoints
{
    public static IEndpointRouteBuilder MapMobileIdentityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(MobileApiRoutes.Base)
            .WithTags("Mobile Identity");

        group.MapPost("/auth/login", LoginAsync)
            .AllowAnonymous()
            .Produces<MobileSessionResponse>()
            .Produces<MobileProblemResponse>(StatusCodes.Status400BadRequest)
            .Produces<MobileProblemResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/auth/refresh", RefreshAsync)
            .AllowAnonymous()
            .Produces<MobileSessionResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization(MobileAuthorization.WarehouseOperatorPolicy)
            .Produces<MobileCurrentUserResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        MobileLoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "invalid_login_request",
                "Укажите email и пароль.");
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return AuthenticationFailed();
        }

        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        var result = await signInManager.PasswordSignInAsync(
            user,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return TypedResults.Empty;
        }

        if (result.RequiresTwoFactor)
        {
            return Problem(
                StatusCodes.Status401Unauthorized,
                "two_factor_required",
                "Для этой учетной записи требуется второй фактор, который пока не поддерживается мобильным клиентом.");
        }

        return AuthenticationFailed();
    }

    private static async Task<IResult> RefreshAsync(
        MobileRefreshRequest request,
        SignInManager<ApplicationUser> signInManager,
        IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return TypedResults.Unauthorized();
        }

        var protector = bearerTokenOptions
            .Get(IdentityConstants.BearerScheme)
            .RefreshTokenProtector;
        var ticket = protector.Unprotect(request.RefreshToken);

        if (ticket?.Properties.ExpiresUtc is not { } expiresUtc
            || timeProvider.GetUtcNow() >= expiresUtc
            || await signInManager.ValidateSecurityStampAsync(ticket.Principal)
                is not ApplicationUser user)
        {
            return TypedResults.Unauthorized();
        }

        var principal = await signInManager.CreateUserPrincipalAsync(user);

        return TypedResults.SignIn(
            principal,
            authenticationScheme: IdentityConstants.BearerScheme);
    }

    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new MobileCurrentUserResponse(
            user.Id,
            user.GetDisplayName(),
            user.Email ?? user.UserName ?? string.Empty));
    }

    private static IResult AuthenticationFailed() => Problem(
        StatusCodes.Status401Unauthorized,
        "authentication_failed",
        "Неверный email или пароль либо учетная запись недоступна.");

    private static IResult Problem(int statusCode, string code, string message) =>
        Results.Json(new MobileProblemResponse(code, message), statusCode: statusCode);
}
