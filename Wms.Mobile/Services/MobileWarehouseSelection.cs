using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile.Services;

public sealed class MobileWarehouseSelection
{
    private Guid? _defaultWarehouseId;
    private Guid? _selectedWarehouseId;
    private string? _unavailableWarning;

    public void Initialize(MobileCurrentUserResponse user)
    {
        _defaultWarehouseId = user.DefaultWarehouseId;
        _selectedWarehouseId = user.DefaultWarehouseId;
        _unavailableWarning = null;
    }

    public void Clear()
    {
        _defaultWarehouseId = null;
        _selectedWarehouseId = null;
        _unavailableWarning = null;
    }

    public void Select(Guid warehouseId)
    {
        _selectedWarehouseId = warehouseId;
        _unavailableWarning = null;
    }

    public (int Index, string? Warning) Resolve(IReadOnlyList<MobileWarehouseResponse> warehouses)
    {
        if (_selectedWarehouseId is Guid selectedWarehouseId)
        {
            for (var index = 0; index < warehouses.Count; index++)
            {
                if (warehouses[index].Id == selectedWarehouseId)
                {
                    _unavailableWarning = null;
                    return (index, null);
                }
            }

            _selectedWarehouseId = null;
            _unavailableWarning = selectedWarehouseId == _defaultWarehouseId
                ? "Склад по умолчанию недоступен. Выберите другой склад."
                : "Выбранный склад недоступен. Выберите другой склад.";
            return (-1, _unavailableWarning);
        }

        if (_unavailableWarning is not null)
            return (-1, _unavailableWarning);

        if (_defaultWarehouseId is not null)
            return (-1, "Склад по умолчанию недоступен. Выберите другой склад.");

        if (warehouses.Count == 1)
        {
            _selectedWarehouseId = warehouses[0].Id;
            return (0, null);
        }

        return (-1, null);
    }
}
