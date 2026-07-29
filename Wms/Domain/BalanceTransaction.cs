namespace Wms.Domain;

public class BalanceTransaction
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string? Description { get; set; }

    public List<BalanceAndTurnovers>? Turnovers { get; set; }
}
