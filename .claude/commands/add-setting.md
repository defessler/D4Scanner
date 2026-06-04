Add a new persisted setting to D4Scanner following the established pattern.

Given: a setting name, backing field type (bool / string / double / int), default value, and a description of what it controls.

Steps:
1. Add a private field to `MainWindow.xaml.cs` near the other setting fields (~line 160):
   ```csharp
   bool _myField = <default>;
   ```

2. Add a `TryGetValue` read in `LoadSettings()` (~line 370):
   ```csharp
   if (s.TryGetValue("myKey", out var v)) _myField = v == "1";  // bool
   // or for double:
   if (s.TryGetValue("myKey", out var mv) && double.TryParse(mv, CultureInfo.InvariantCulture, out var d)) _myField = d;
   ```

3. Add the key to the `Dictionary<string, string?>` in `SaveSettings()` (~line 410):
   ```csharp
   ["myKey"] = _myField ? "1" : "0",   // bool
   // or: ["myKey"] = _myField.ToString(CultureInfo.InvariantCulture),
   ```

4. Add a toggle row in `ShowSettings()` if the user should be able to change it:
   Copy the debug-mode block (~line 1410) — it's a `DockPanel` with a `CheckBox` docked left
   and a `StackPanel` of `TBs(title, Ink, 13.5, true)` + `TB(description, Soft, 11.5, false)`.
   The `Checked`/`Unchecked` handlers set the field → `SaveSettings()` → `Render()` (or `StartWatching()`).

5. Run `/build` and `/test` to confirm no regressions.
