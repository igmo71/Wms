using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS.Endpoints;

public static class InformationRegister_ШтрихкодыНоменклатуры_Endpoints
{
    public static IEndpointRouteBuilder MapInformationRegister_ШтрихкодыНоменклатуры_Endpoints(this IEndpointRouteBuilder routeBuilder)
    {

        var group = routeBuilder.MapGroup("/api/1c")
            .WithTags("InformationRegister_ШтрихкодыНоменклатуры")
            .ProducesValidationProblem();

        group.MapGet("/InformationRegister_ШтрихкодыНоменклатуры/import", Import);
        group.MapPost("/InformationRegister_ШтрихкодыНоменклатуры/notify", Notify);

        return routeBuilder;
    }

    static async Task<IResult> Import(
        [FromServices] InformationRegister_ШтрихкодыНоменклатуры_Service service,
        CancellationToken ct)
    {
        await service.ImportListAsync(ct);

        return TypedResults.Ok();
    }

    static async Task<IResult> Notify(
        [FromServices] NotifyChannel notifyChannel,
        [FromBody] NotifyRequest request,
        CancellationToken ct)
    {
        await notifyChannel.Writer.WriteAsync(
            new NotifyRecord(request.Ref_Key, nameof(InformationRegister_ШтрихкодыНоменклатуры)),
            ct);

        return TypedResults.Ok();
    }
}
