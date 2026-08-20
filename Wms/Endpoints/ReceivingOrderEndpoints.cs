using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Application.ReceivingOrders;

namespace Wms.Endpoints;

public static class ReceivingOrderEndpoints
{
    public static IEndpointRouteBuilder MapReceivingOrderEndpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("/api")
            .WithTags("ReceivingOrder")
            .ProducesValidationProblem();

        group.MapGet("/ReceivingOrder/{id:guid}", GetOrder);
        group.MapPost("/ReceivingOrder/{id:guid}/set-in-receiving", SetInReceiving);
        group.MapPost("/ReceivingOrder/{id:guid}/set-received", SetReceived);

        return routeBuilder;
    }

    static async Task<IResult> SetInReceiving(
        [FromServices] ReceivingOrderCommandService service,
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var result = await service.SetInReceivingAsync(id, "895f1153-a5b2-434e-b459-a2a120b291ce", ct); // TODO: Temporary crutch

        return result.IsSuccess ? TypedResults.Ok() : Results.BadRequest();
    }

    static async Task<IResult> SetReceived(
        [FromServices] ReceivingOrderCommandService service,
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var result = await service.SetReceivedAsync(id, "895f1153-a5b2-434e-b459-a2a120b291ce", ct); // TODO: Temporary crutch

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
