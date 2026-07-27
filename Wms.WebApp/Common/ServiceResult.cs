using Wms.WebApp.Abstractions;

namespace Wms.WebApp.Common;

public class ServiceResult : IServiceResult
{
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

    public bool IsSuccess { get; }

    public ServiceError? Error { get; }

    public static ServiceResult Success() => new();

    public static ServiceResult Fail(ServiceError error) => new(error);

    public static implicit operator ServiceResult(ServiceError error) => Fail(error);
}

public class ServiceResult<TValue> : ServiceResult, IServiceResult<TValue>
{
    private ServiceResult(TValue value) : base()
    {
        ArgumentNullException.ThrowIfNull(value);

        Value = value;
    }

    private ServiceResult(ServiceError error) : base(error)
    {
        Value = default;
    }

    public TValue? Value { get; }

    public static ServiceResult<TValue> Success(TValue value) => new(value);

    public static new ServiceResult<TValue> Fail(ServiceError error) => new(error);
}