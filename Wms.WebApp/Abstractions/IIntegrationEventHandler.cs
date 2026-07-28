namespace Wms.WebApp.Abstractions;

public interface IIntegrationEventHandler<TEvent> : IEventHandler
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken = default);
}
