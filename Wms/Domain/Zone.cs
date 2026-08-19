namespace Wms.Domain;

public class Zone
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public bool DeletionMark { get; set; }
    public Enums.ZoneType Type { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }

    public List<StorageLocation>? StorageLocations { get; set; }

    public void UpdateDetails(string code, string name, Enums.ZoneType type)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Zone code is required.", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Zone name is required.", nameof(name));

        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type), "Zone type is invalid.");

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Type = type;
    }
}
