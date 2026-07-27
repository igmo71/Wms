using Wms.WebApp.Common;

namespace Wms.WebApp.Abstractions;

public interface IServiceResult
{
    bool IsSuccess { get; }

    ServiceError? Error { get; }
}

public interface IServiceResult<out TValue> : IServiceResult
{
    TValue? Value { get; }
}
