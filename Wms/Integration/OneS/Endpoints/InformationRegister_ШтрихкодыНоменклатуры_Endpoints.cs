using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Wms.Integration.OneS.Models;
using Wms.Integration.OneS.Services;

namespace Wms.Integration.OneS.Endpoints;

internal static class InformationRegister_ШтрихкодыНоменклатуры_Endpoints
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
        var result = await service.ImportListAsync(ct);

        return result.IsSuccess
            ? TypedResults.Ok()
            : Results.Problem(result.Error?.Message, statusCode: StatusCodes.Status502BadGateway);
    }

    static async Task<IResult> Notify(
        [FromServices] NotifyChannel notifyChannel,
        [FromBody] NotifyRequest[] request,
        CancellationToken ct)
    {
        foreach (var item in request)
        {
            await notifyChannel.Writer.WriteAsync(
                new NotifyRecord(item.Ref_Key, nameof(InformationRegister_ШтрихкодыНоменклатуры)),
                ct);
        }

        return TypedResults.Ok();
    }
}
