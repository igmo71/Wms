using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Endpoints;

internal static class Document_РасходныйОрдерНаТовары_Endpoints
{
    public static IEndpointRouteBuilder MapDocument_РасходныйОрдерНаТовары_Endpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("Document_РасходныйОрдерНаТовары")
            .ProducesValidationProblem();

        group.MapPost("/Document_РасходныйОрдерНаТовары/notify", NotifyOrder)
            .WithName("Document_РасходныйОрдерНаТовары.Notify");

        return routeBuilder;
    }

    static async Task<IResult> NotifyOrder(
        [FromServices] NotifyChannel notifyChannel,
        [FromBody] NotifyRequest request,
        CancellationToken ct)
    {
        await notifyChannel.Writer.WriteAsync(
            new NotifyRecord(request.Ref_Key, nameof(Document_РасходныйОрдерНаТовары)),
            ct);

        return TypedResults.Ok();
    }
}
