namespace Wms.Common;

public record OperationError(OperationErrorType Type, string? Message)
{
    public static OperationError Invalid(string? message = null) => new(OperationErrorType.Invalid, message ?? "Is invalid");
    public static OperationError NotFound(string? message = null) => new(OperationErrorType.NotFound, message ?? "Not found.");
    public static OperationError Failure(string? message = null) => new(OperationErrorType.Failure, message ?? "Failed.");
    public static OperationError Conflict(string? message = null) => new(OperationErrorType.Conflict, message ?? "Conflict.");
    public static OperationError Invalid<TEntity>(string? message = null) => new(OperationErrorType.Invalid, message ?? $"{typeof(TEntity).Name} is invalid.");
    public static OperationError NotFound<TEntity>(string? message = null) => new(OperationErrorType.NotFound, message ?? $"{typeof(TEntity).Name} not found.");
    public static OperationError Failure<TEntity>(string? message = null) => new(OperationErrorType.Failure, message ?? $"{typeof(TEntity).Name} failed.");
    public static OperationError Conflict<TEntity>(string? message = null) => new(OperationErrorType.Conflict, message ?? $"{typeof(TEntity).Name} conflict.");
}
