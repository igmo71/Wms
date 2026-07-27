namespace Wms.WebApp.Abstractions;

public interface ICommand : IRequest
{
}

public interface ICommand<TResult> : ICommand
    where TResult : IServiceResult
{
}
