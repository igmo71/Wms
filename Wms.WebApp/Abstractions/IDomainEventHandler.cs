namespace Wms.WebApp.Abstractions;

public interface IDomainEventHandler<in TEvent> : IEventHandler
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
