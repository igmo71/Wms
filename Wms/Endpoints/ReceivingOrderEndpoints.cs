using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Application.ReceivingOrders;
using Wms.Data;

namespace Wms.Endpoints;

internal static class ReceivingOrderEndpoints
{
    public static IEndpointRouteBuilder MapReceivingOrderEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("/api")
            .WithTags("ReceivingOrder")
            .ProducesValidationProblem()
            .RequireAuthorization(policy => policy.RequireRole(ApplicationRoles.All));

        group.MapGet("/ReceivingOrder/{id:guid}", GetOrder);
        group.MapPost("/ReceivingOrder/{id:guid}/set-in-receiving", SetInReceiving);
        group.MapPost("/ReceivingOrder/{id:guid}/set-received", SetReceived);

        return routeBuilder;
    }

    static async Task<IResult> SetInReceiving(
        [FromServices] ReceivingOrderCommandService service,
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await service.SetInReceivingAsync(id, userId, ct);

        return result.IsSuccess ? TypedResults.Ok() : Results.BadRequest();
    }

    static async Task<IResult> SetReceived(
        [FromServices] ReceivingOrderCommandService service,
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        CancellationToken ct)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await service.SetReceivedAsync(id, userId, ct);

        return result.IsSuccess ? TypedResults.Ok() : Results.BadRequest();
    }

    static async Task<IResult> GetOrder(
        [FromServices] ReceivingOrderQueryService service,
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var orderDetails = await service.GetOrderAsync(id, ct);

        if (orderDetails == null)
        {
            return Results.NotFound();
        }
        return Results.Ok(orderDetails);
    }
}
