using Wms.WebApp.Abstractions;

namespace Wms.WebApp.Common;

public class ServiceResult : IServiceResult
{
    public bool IsSuccess { get; }

    public ServiceError? Error { get; }

    protected ServiceResult()
    {
        IsSuccess = true;
        Error = null;
    }

    protected ServiceResult(ServiceError error)
    {
        IsSuccess = false;
        Error = error;
    }

    protected ServiceResult(ServiceErrorType errorType, string? message)
    {
        IsSuccess = false;
        Error = new ServiceError(errorType, message);
    }


    public static ServiceResult Success() => new();

    public static ServiceResult Fail(ServiceError error) => new(error);

    public static ServiceResult Fail(ServiceErrorType errorType, string? message) => new(errorType, message);


    public static implicit operator ServiceResult(ServiceError error) => Fail(error);
}

public class ServiceResult<TValue> : ServiceResult, IServiceResult<TValue>
{
    public TValue? Value { get; }

    public static ServiceResult<TValue> Success(TValue value) => new(value);

    private ServiceResult(TValue value) : base()
    {
        Value = value;
    }

    private ServiceResult(ServiceError error) : base(error)
    {
        Value = default;
    }

    protected ServiceResult(ServiceErrorType errorType, string? message) : base(errorType, message)
    {
        Value = default;
    }

    public static new ServiceResult<TValue> Fail(ServiceError error) => new(error);
    public static new ServiceResult<TValue> Fail(ServiceErrorType errorType, string? message) => new(errorType, message);


    public static implicit operator ServiceResult<TValue>(TValue value) => Success(value);

    public static implicit operator ServiceResult<TValue>(ServiceError error) => Fail(error);
}