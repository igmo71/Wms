using Wms.Common;
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

    public static OperationResult<Zone> Create(
        Guid id,
        Guid warehouseId,
        string code,
        string name,
        ZoneType type)
    {
        if (id == Guid.Empty)
        {
            return OperationError.Invalid<Zone>("Zone identifier is required.");
        }

        var warehouseResult = ValidateWarehouse(warehouseId);
        if (!warehouseResult.IsSuccess)
        {
            return warehouseResult.Error!;
        }

        var detailsResult = ValidateDetails(code, name, type);
        if (!detailsResult.IsSuccess)
        {
            return detailsResult.Error!;
        }

        var zone = new Zone
        {
            Id = id,
            WarehouseId = warehouseId
        };

        zone.ApplyDetails(code, name, type);
        return zone;
    }

    public OperationResult UpdateDetails(string code, string name, ZoneType type)
    {
        var validation = ValidateDetails(code, name, type);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        ApplyDetails(code, name, type);
        return OperationResult.Success();
    }

    public OperationResult MoveToWarehouse(Guid warehouseId)
    {
        var validation = ValidateWarehouse(warehouseId);
        if (!validation.IsSuccess)
        {
            return validation;
        }

        WarehouseId = warehouseId;
        return OperationResult.Success();
    }

    public void Deactivate() => DeletionMark = true;

    public void Activate() => DeletionMark = false;

    private static OperationResult ValidateDetails(string code, string name, ZoneType type)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return OperationError.Invalid<Zone>("Zone code is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationError.Invalid<Zone>("Zone name is required.");
        }

        if (!Enum.IsDefined(type))
        {
            return OperationError.Invalid<Zone>("Zone type is invalid.");
        }

        return OperationResult.Success();
    }

    private void ApplyDetails(string code, string name, ZoneType type)
    {
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Type = type;
    }

    private static OperationResult ValidateWarehouse(Guid warehouseId)
    {
        if (warehouseId == Guid.Empty)
        {
            return OperationError.Invalid<Warehouse>("Warehouse identifier is required.");
        }

        return OperationResult.Success();
    }
}
