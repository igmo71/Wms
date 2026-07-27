namespace Wms.WebApp.Abstractions;

public interface IQuery
{
}

public interface IQuery<TResult> : IQuery
    where TResult : IServiceResult
{
}
