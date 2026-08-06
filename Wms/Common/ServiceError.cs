namespace Wms.Common;

public record ServiceError(ServiceErrorType Type, string? Message)
{
    public static ServiceError Invalid(string? message = null) => new(ServiceErrorType.Failure, message ?? "Is invalid");
    public static ServiceError NotFound(string? message = null) => new(ServiceErrorType.NotFound, message ?? "Not found.");
    public static ServiceError Failure(string? message = null) => new(ServiceErrorType.Failure, message ?? "Failed.");
    public static ServiceError Invalid<TEntity>(string? message = null) => new(ServiceErrorType.Invalid, message ?? $"{typeof(TEntity).Name} is invalid.");
    public static ServiceError NotFound<TEntity>(string? message = null) => new(ServiceErrorType.NotFound, message ?? $"{typeof(TEntity).Name} not found.");
    public static ServiceError Failure<TEntity>(string? message = null) => new(ServiceErrorType.Failure, message ?? $"{typeof(TEntity).Name} failed.");
}
