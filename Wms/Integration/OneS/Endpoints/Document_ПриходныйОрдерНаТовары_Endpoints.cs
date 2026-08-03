using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS.Endpoints;

public static class Document_ПриходныйОрдерНаТовары_Endpoints
{
    public static IEndpointRouteBuilder MapDocument_ПриходныйОрдерНаТовары_Endpoints(this IEndpointRouteBuilder routeBuilder)
    {

        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("1С")
            .ProducesValidationProblem();

        group.MapGet("/Document_ПриходныйОрдерНаТовары/import", ImportOrder)
            .WithTags("Import Document_ПриходныйОрдерНаТовары");

        group.MapPost("/Document_ПриходныйОрдерНаТовары/notify", NotifyOrder)
           .WithTags("Notify Document_ПриходныйОрдерНаТовары");

        return routeBuilder;
    }

    static async Task<IResult> ImportOrder(
        [FromServices] Document_ПриходныйОрдерНаТовары_InboundService service,
        CancellationToken ct)
    {
        await service.ImportListAsync(ct);

        return TypedResults.Ok();
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
