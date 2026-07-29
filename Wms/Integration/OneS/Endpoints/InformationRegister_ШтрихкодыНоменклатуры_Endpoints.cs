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
            .WithTags("1С")
            .ProducesValidationProblem();

        group.MapGet("/InformationRegister_ШтрихкодыНоменклатуры/import", ImportListInformationRegister_ШтрихкодыНоменклатуры)
            .WithTags("Import InformationRegister_ШтрихкодыНоменклатуры");

        group.MapPost("/InformationRegister_ШтрихкодыНоменклатуры/notify", NotifyInformationRegister_ШтрихкодыНоменклатуры)
           .WithTags("Notify InformationRegister_ШтрихкодыНоменклатуры");

        return routeBuilder;
    }

    static async Task<IResult> ImportListInformationRegister_ШтрихкодыНоменклатуры(
        [FromServices] InformationRegister_ШтрихкодыНоменклатуры_Service service,
        CancellationToken ct)
    {
        await service.ImportListAsync(ct);

        return TypedResults.Ok();
    }

    static async Task<IResult> NotifyInformationRegister_ШтрихкодыНоменклатуры(
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
