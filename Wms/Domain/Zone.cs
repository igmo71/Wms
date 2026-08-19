using Wms.Domain.Enums;

namespace Wms.Domain;

public class Zone
{
    private readonly List<StorageLocation> _storageLocations = [];

    private Zone()
    {
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool DeletionMark { get; private set; }
    public ZoneType Type { get; private set; }

    public Guid WarehouseId { get; private set; }
    public Warehouse? Warehouse { get; private set; }

    public IReadOnlyCollection<StorageLocation> StorageLocations => _storageLocations;

    public static Zone Create(
        Guid id,
        Guid warehouseId,
        string code,
        string name,
        ZoneType type)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Zone identifier is required.", nameof(id));
        }

        var zone = new Zone
        {
            Id = id
        };

        zone.MoveToWarehouse(warehouseId);
        zone.UpdateDetails(code, name, type);
        return zone;
    }

    public void UpdateDetails(string code, string name, ZoneType type)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Zone code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Zone name is required.", nameof(name));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "Zone type is invalid.");
        }

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Type = type;
    }

    public void MoveToWarehouse(Guid warehouseId)
    {
        if (warehouseId == Guid.Empty)
        {
            throw new ArgumentException("Warehouse identifier is required.", nameof(warehouseId));
        }

        WarehouseId = warehouseId;
    }

    public void Deactivate() => DeletionMark = true;

    public void Activate() => DeletionMark = false;
}
