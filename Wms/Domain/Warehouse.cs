namespace Wms.Domain;

public class Warehouse
{
    private readonly List<Zone> _zones = [];
    private readonly List<StorageLocation> _storageLocations = [];

    public Guid Id { get; set; }
    public string? Name { get; set; }
    public bool DeletionMark { get; set; }

    public IReadOnlyCollection<Zone> Zones => _zones;
    public IReadOnlyCollection<StorageLocation> StorageLocations => _storageLocations;
}
