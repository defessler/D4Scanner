## D4Scanner v0.63.0

**More capture memory cleanup — the OCR scan path no longer leaks WinRT objects.**

Following v0.62.0's fix to the frame-grab path, a sweep of the sibling OCR
capture code found the same class of leak in two more places:

- The OCR scanner converted each captured frame into a native `SoftwareBitmap`
  for text recognition but never disposed it — leaving another full-screen-sized
  bitmap (~33 MB at 4K) to the garbage collector's finalizer on every changed
  frame it scanned (roughly every 20 seconds during play).
- The bitmap-conversion helper leaked its `DataWriter` (a small COM wrapper) per
  scan for the same reason.

Both are now disposed deterministically. The recognition result is unchanged;
only the cleanup of internal buffers becomes immediate, reducing native-memory
pressure during active OCR scanning.

No action needed — the fix applies automatically.
