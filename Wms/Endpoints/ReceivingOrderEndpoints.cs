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
            .ProducesValidationProblem();

        group.MapPost("/ReceivingOrder/{id:guid}/start", StartOrder)
            .WithTags("Start Receiving Order");

        group.MapPost("/ReceivingOrder/{id:guid}/complete", CompleteOrder)
           .WithTags("Complete Receiving Order");

        group.MapGet("/ReceivingOrder/{id:guid}", GetOrder)
           .WithTags("Get Receiving Order"); ;

        return routeBuilder;
    }

    static async Task<IResult> StartOrder(
        [FromServices] ReceivingOrderCommandService service,
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var success = await service.StartOrderAsync(id, ct);

        return success ? TypedResults.Ok() : Results.BadRequest();
    }

    static async Task<IResult> CompleteOrder(
        [FromServices] ReceivingOrderCommandService service,
        [FromRoute] Guid id,
        CancellationToken ct)
    {
        var success = await service.CompleteOrderAsync(id, ct);

        return success ? TypedResults.Ok() : Results.BadRequest();
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
