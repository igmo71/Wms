using Wms.Common;

namespace Wms.Application.Services;

internal static class DomainOperation
{
    public static ServiceResult<TValue> Execute<TValue>(Func<TValue> operation)
    {
        try
        {
            return operation();
        }
        catch (ArgumentException exception)
        {
            return ServiceError.Invalid<TValue>(exception.Message);
        }
    }

    public static ServiceResult Execute(Action operation)
    {
        try
        {
            operation();
            return ServiceResult.Success();
        }
        catch (ArgumentException exception)
        {
            return ServiceError.Invalid(exception.Message);
        }
    }
}
