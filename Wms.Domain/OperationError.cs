namespace Wms.Common;

public record OperationError(OperationErrorType Type, string Message)
{
    public static OperationError Invalid(string message) => new(OperationErrorType.Invalid, message);
    public static OperationError NotFound(string message) => new(OperationErrorType.NotFound, message);
    public static OperationError Failure(string message) => new(OperationErrorType.Failure, message);
    public static OperationError Conflict(string message) => new(OperationErrorType.Conflict, message);
}
