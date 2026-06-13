## D4Scanner v0.62.0

**Capture memory fix — periodic OCR scanning no longer leaks native bitmaps.**

The Windows.Graphics.Capture frame-grab path created an intermediate native
`SoftwareBitmap` (a copy of the captured surface) but never disposed it,
leaving a full-screen-sized bitmap — about 33 MB at 4K — to the garbage
collector's finalizer on every successful grab. Because OCR capture grabs run
on a periodic timer, these accumulated as native-memory pressure between GC
passes during active OCR scanning.

The intermediate is now disposed deterministically the moment it has been
converted to the bitmap the app actually uses. On-screen behavior is
unchanged; only the cleanup of an internal buffer becomes immediate.

No action needed — the fix applies automatically.
