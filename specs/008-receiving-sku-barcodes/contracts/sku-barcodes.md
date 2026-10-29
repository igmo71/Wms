# Contracts: Receiving SKU barcodes

## Mobile order view

Each `MobileReceivingWorkItemResponse` gains `HasBarcode: bool`. It means at least one current barcode association exists for that SKU, regardless of source. The flag drives only the SKU card appearance; it does not grant permission.

## Mobile add command

Authenticated receiving-worker route under the existing Mobile V1 receiving order group:

`POST /{orderId}/skus/{skuId}/barcodes`

Request: `ClientRequestId` and scanned `Barcode`. Success returns the selected SKU ID in the standard receiving command response. The server verifies active order/current SKU/current-cycle participation and Operator role. A blank/too-long value is invalid; a value associated with another SKU is a conflict identifying that SKU; an existing pair for the same SKU does not create a second row. Replaying the same request ID and input returns the original outcome. The client retains an uncertain request for explicit retry and reloads the order after success.

## Web list and removal

The Web barcode list reads server-paged rows with `Value`, SKU display name/code, `Source` and `IsDuplicate`. `OnlyDuplicates` filters values assigned to more than one distinct SKU before paging and combines with existing search and inactive-SKU controls. A Manager may request removal for a `Wms` row and confirm the displayed pair/source. The server checks Manager membership and current source again; if the pair changed or was promoted to `OneC`, the removal is rejected and the list refreshes. Successful removal records audit. The `OneC` source has no Web removal action.

## 1C adapter

The existing single-SKU notification and full manual/import endpoint keep their transport routes and source DTO. After a complete successful read, they call one application reconciliation operation with the scope and incoming pairs. Same-value pairs for different SKUs are accepted. The operation never writes to 1C.
