using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;

namespace Wms.Integration.OneS.Endpoints;

internal static class Document_ПриходныйОрдерНаТовары_Endpoints
{
    public static IEndpointRouteBuilder MapDocument_ПриходныйОрдерНаТовары_Endpoints(this IEndpointRouteBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("Document_ПриходныйОрдерНаТовары")
            .ProducesValidationProblem();

        group.MapPost("/Document_ПриходныйОрдерНаТовары/notify", NotifyOrder)
            .WithName("Document_ПриходныйОрдерНаТовары.Notify");

        return routeBuilder;
    }

    static async Task<IResult> NotifyOrder(
        [FromServices] NotifyChannel notifyChannel,
        [FromBody] NotifyRequest request,
        CancellationToken ct)
    {
        await notifyChannel.Writer.WriteAsync(
            new NotifyRecord(request.Ref_Key, nameof(Document_ПриходныйОрдерНаТовары)),
            ct);

        return TypedResults.Ok();
    }
}
