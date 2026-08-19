namespace Wms.Common;

public class OperationResult
{
    public bool IsSuccess { get; }

    public OperationError? Error { get; }

    protected OperationResult()
    {
        IsSuccess = true;
        Error = null;
    }

    protected OperationResult(OperationError error)
    {
        IsSuccess = false;
        Error = error;
    }

    public static OperationResult Success() => new();

    public static OperationResult Failure(OperationError error) => new(error);

    public static OperationResult Failure(OperationErrorType errorType, string? message) =>
        new(new OperationError(errorType, message));

    public static implicit operator OperationResult(OperationError error) => Failure(error);
}

public class OperationResult<TValue> : OperationResult
{
    public TValue? Value { get; }

    public static OperationResult<TValue> Success(TValue value) => new(value);

    private OperationResult(TValue value) : base()
    {
        Value = value;
    }

    private OperationResult(OperationError error) : base(error)
    {
        Value = default;
    }

    public static new OperationResult<TValue> Failure(OperationError error) => new(error);

    public static new OperationResult<TValue> Failure(OperationErrorType errorType, string? message) =>
        new(new OperationError(errorType, message));

    public static implicit operator OperationResult<TValue>(TValue value) => Success(value);

    public static implicit operator OperationResult<TValue>(OperationError error) => Failure(error);
}
