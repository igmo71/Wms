namespace Wms.Common;

public record OperationError(OperationErrorType Type, string? Message)
{
    public static OperationError Invalid(string? message = null) => new(OperationErrorType.Invalid, message ?? "Некорректные данные.");
    public static OperationError NotFound(string? message = null) => new(OperationErrorType.NotFound, message ?? "Объект не найден.");
    public static OperationError Failure(string? message = null) => new(OperationErrorType.Failure, message ?? "Не удалось выполнить операцию.");
    public static OperationError Conflict(string? message = null) => new(OperationErrorType.Conflict, message ?? "Обнаружен конфликт данных.");
}
