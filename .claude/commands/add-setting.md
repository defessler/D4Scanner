Add a new persisted setting to D4Scanner following the established pattern.

Given: a setting name, backing field type (bool / string / double / int), default
value, and a description of what it controls.

Everything lives in `csharp/D4Scanner.App/MainWindow.xaml.cs`. Settings persist
to `app.json` under `%LOCALAPPDATA%\d4scanner` as a flat
`Dictionary<string, string?>`. Bools go in as `"1"` / `"0"`. Doubles and ints go
through `CultureInfo.InvariantCulture` on both write and parse.

The UI is defer-everything (since v0.43.0): every control in the settings modal
edits a `SettingsDraft`, nothing applies until Save, Revert re-seeds the draft
from live state, and X / backdrop / Esc discard it. So a user-editable setting
touches the draft plumbing as well as persistence. `_captureDiag` (persisted key
`captureDiag`) is a complete exemplar of every site below. `_invSort` is the
exemplar for a persisted value with no settings-modal control.

Persistence (every setting):

1. Add a private field near the other setting fields, next to `_debugMode` and
   `_captureDiag`:
   ```csharp
   bool _myField = <default>;   // one-line comment saying what it controls
   ```

2. Add a `TryGetValue` read in `LoadSettings()`:
   ```csharp
   if (s.TryGetValue("myKey", out var mk)) _myField = mk == "1";   // bool
   // int:    if (s.TryGetValue("myKey", out var mk) && int.TryParse(mk, out var mkv)) _myField = mkv;
   // double: if (s.TryGetValue("myKey", out var mk) && double.TryParse(mk, System.Globalization.CultureInfo.InvariantCulture, out var mkv)) _myField = mkv;
   ```
   The file has no `using System.Globalization`. Spell the culture out as the
   `zoom` read does, or put the read after the local `inv` that `LoadSettings`
   declares just before the `winW` / `winH` block. `SaveSettings` declares its
   own `inv` at the top.

   Clamp on read if the value has a valid range (see `ClampLogMaxMB` and its
   siblings, shared by load, draft, and apply).

3. Add the key to the one-line dictionary literal in `SaveSettings()`:
   ```csharp
   ["myKey"] = _myField ? "1" : "0",   // bool
   // or: ["myKey"] = _myField.ToString(inv),
   ```
   That line is deliberately one very long line. Append to it, don't reflow it.
   Leave the `.tmp` + `File.Move(..., overwrite: true)` write alone. It's the
   atomic-save contract.

User-editable settings only (the draft model):

4. Add a matching field to the `SettingsDraft` class (`sealed class
   SettingsDraft`, near `_settingsDraft`).

5. Seed it in `NewDraft()`: `MyField = _myField`. This is the single seed for
   both open and Revert. Its summary records that a missed entry once left
   Revert stale.

6. Add the control in `RenderSettings()`. For a bool, call the local helper
   under the right `Section("...")`, exactly as the debug toggle does:
   ```csharp
   ToggleRow("Label", "One-sentence description.",
       d.MyField, on => { d.MyField = on; RefreshPending(); });
   ```
   The handler edits the draft and calls `RefreshPending()`. It must NOT call
   `SaveSettings()`, `Render()`, or `StartWatching()`.

7. Add a line to the local `Pending()` list in `RenderSettings()` so the footer
   shows the staged change:
   ```csharp
   if (d.MyField != _myField) list.Add(d.MyField ? "Enable ..." : "Disable ...");
   ```

8. Copy the draft back to the live field in `ApplySettings()`, on the line that
   already does `_useTts = d.UseTts; _useCapture = d.UseCapture; ...`. If the
   change needs the watcher restarted, fold it into `watchChanged` there rather
   than calling `StartWatching()` yourself. `ApplySettings` already ends with
   one `SaveSettings()`, one optional `StartWatching()`, and one `Render()`.

9. Run `/build` and `/test`. Then open Settings, toggle the new control, and
   check that Revert clears it, that Esc discards it, and that Save persists it
   across a restart.
