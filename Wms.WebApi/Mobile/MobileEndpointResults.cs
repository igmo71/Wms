using Wms.Common;
using Wms.Contracts.Mobile.V1;

namespace Wms.WebApi.Mobile;

internal static class MobileEndpointResults
{
    public static IResult CommandProblem(OperationError error)
    {
        var (statusCode, code) = error.Type switch
        {
            OperationErrorType.NotFound => (StatusCodes.Status404NotFound, "resource_not_found"),
            OperationErrorType.Conflict => (StatusCodes.Status409Conflict, "request_conflict"),
            OperationErrorType.Invalid => (StatusCodes.Status422UnprocessableEntity, "invalid_command"),
            _ => (StatusCodes.Status400BadRequest, "command_failed")
        };

        return Results.Json(new MobileProblemResponse(code, error.Message), statusCode: statusCode);
    }
}
