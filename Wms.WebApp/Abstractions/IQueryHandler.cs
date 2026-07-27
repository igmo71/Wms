namespace Wms.WebApp.Abstractions;

public interface IQueryHandler<TQuery, TResult> : IHandler
    where TQuery : IQuery<TResult>
    where TResult : IServiceResult
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
