# Mobile V1 putaway scan contract

The existing location-resolution request currently uses `MobileStorageLocationContext.Storage` for receiving putaway and for older picking, transfer and count screens. It resolves only the current `Storage` zone type.

Add an explicit request context `PutawayDestination` for the receiving LPN putaway screen. It accepts an active, unlocked picking or reserve cell in the expected warehouse. The server's putaway command rechecks the same route at posting. Keep the existing `Storage` request context strict so other screens do not silently gain reserve eligibility.

Add a response zone value for Reserve so the resolved cell can identify its actual purpose. Append enum values without renumbering existing Mobile V1 values. The receiving putaway screen sends the new context; older clients retain their existing requests and server-side behavior. The user-facing error for another zone remains an invalid destination, with the scanned address available for correction.

The receiving putaway suggestions include both picking and reserve destinations under the existing recommendation ordering. This contract does not define replenishment, picking or inventory-count requests.
