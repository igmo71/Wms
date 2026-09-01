using Wms.Common;
using Wms.Contracts.Mobile.V1;

namespace Wms.WebApi.Mobile;

internal static class MobileEndpointResults
{
    public static IResult CommandProblem(OperationError error)
    {
        var (statusCode, code) = error.Type switch
        {
            OperationErrorType.NotFound => (
                StatusCodes.Status404NotFound,
                MobileProblemCodes.ResourceNotFound),
            OperationErrorType.Conflict => (
                StatusCodes.Status409Conflict,
                MobileProblemCodes.RequestConflict),
            OperationErrorType.Invalid => (
                StatusCodes.Status422UnprocessableEntity,
                MobileProblemCodes.InvalidCommand),
            _ => (StatusCodes.Status400BadRequest, MobileProblemCodes.CommandFailed)
        };

        return Results.Json(new MobileProblemResponse(code, error.Message), statusCode: statusCode);
    }

    public static RouteHandlerBuilder WithMobileResponses<TResponse>(
        this RouteHandlerBuilder builder) =>
        builder
            .Produces<TResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces<MobileProblemResponse>(StatusCodes.Status400BadRequest)
            .Produces<MobileProblemResponse>(StatusCodes.Status404NotFound)
            .Produces<MobileProblemResponse>(StatusCodes.Status409Conflict)
            .Produces<MobileProblemResponse>(StatusCodes.Status422UnprocessableEntity);
}
