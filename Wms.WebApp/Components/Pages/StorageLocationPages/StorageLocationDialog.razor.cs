using Microsoft.AspNetCore.Components;
using MudBlazor;
using Wms.Application.StorageLocations;
using Wms.Common;
using Wms.Domain;

namespace Wms.WebApp.Components.Pages.StorageLocationPages;

public partial class StorageLocationDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;
    [Parameter, EditorRequired] public Warehouse Warehouse { get; set; } = null!;
    [Parameter, EditorRequired] public Zone Zone { get; set; } = null!;
    [Parameter] public StorageLocation? Parent { get; set; }
    [Parameter] public StorageLocation? StorageLocation { get; set; }
    [Inject] private StorageLocationCommandService StorageLocationCommandService { get; set; } = null!;

    private string _name = string.Empty;
    private bool _isFolder;
    private int _number = 1;
    private int _segmentWidth = 2;
    private long? _pickSequence;
    private double? _length;
    private double? _width;
    private double? _height;
    private double? _volume;
    private double? _volumeFactor;
    private double? _maxWeight;
    private double? _x;
    private double? _y;
    private double? _z;
    private bool _isSaving;
    private string? _errorMessage;

    private string FullAddress => $"{Zone.Code}-{StorageLocation?.Code}";

    protected override void OnInitialized()
    {
        if (StorageLocation is null)
        {
            return;
        }

        _name = StorageLocation.Name;
        _isFolder = StorageLocation.IsFolder;
        _pickSequence = StorageLocation.PickSequence;
        _length = StorageLocation.Dimensions.Length;
        _width = StorageLocation.Dimensions.Width;
        _height = StorageLocation.Dimensions.Height;
        _volume = StorageLocation.Dimensions.Volume;
        _volumeFactor = StorageLocation.Dimensions.VolumeFactor;
        _maxWeight = StorageLocation.Dimensions.MaxWeight;
        _x = StorageLocation.Coordinates.X;
        _y = StorageLocation.Coordinates.Y;
        _z = StorageLocation.Coordinates.Z;
    }

    private async Task SaveAsync()
    {
        _isSaving = true;
        _errorMessage = null;
        try
        {
            var detailsResult = CreateDetails();
            if (!detailsResult.IsSuccess)
            {
                _errorMessage = detailsResult.Error?.Message;
                return;
            }

            var details = detailsResult.Value!;
            OperationResult result;
            if (StorageLocation is null)
            {
                result = await StorageLocationCommandService.CreateAsync(new CreateStorageLocationCommand
                {
                    WarehouseId = Warehouse.Id,
                    ZoneId = Zone.Id,
                    ParentId = Parent?.Id,
                    Number = _number,
                    SegmentWidth = _segmentWidth,
                    Details = details
                });
            }
            else
            {
                result = await StorageLocationCommandService.UpdateAsync(StorageLocation.Id, details);
            }

            if (!result.IsSuccess)
            {
                _errorMessage = result.Error?.Message ?? "Не удалось сохранить складскую позицию.";
                return;
            }

            MudDialog.Close(DialogResult.Ok(true));
        }
        catch
        {
            _errorMessage = "Не удалось сохранить складскую позицию.";
        }
        finally
        {
            _isSaving = false;
        }
    }

    private OperationResult<StorageLocationDetails> CreateDetails()
    {
        var dimensionsResult = LocationDimensions.Create(
            _length,
            _width,
            _height,
            _volume,
            _volumeFactor,
            _maxWeight);
        if (!dimensionsResult.IsSuccess)
        {
            return dimensionsResult.Error!;
        }

        var coordinatesResult = LocationCoordinates.Create(_x, _y, _z);
        if (!coordinatesResult.IsSuccess)
        {
            return coordinatesResult.Error!;
        }

        return StorageLocationDetails.Create(
            _name,
            _isFolder,
            dimensionsResult.Value,
            coordinatesResult.Value,
            _pickSequence);
    }

    private void Cancel() => MudDialog.Cancel();
}
