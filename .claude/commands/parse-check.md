Verify that GearParser correctly parses a TTS tooltip block.

Paste one or more raw lines from `d4_tts.log` (or a sample tooltip) as the argument to this command.
Feed them through GearParser and report:
- Whether an Item was emitted
- The parsed Name, Slot, Rarity, ItemPower, Affixes (text + value + range), Temper/Masterwork counts, Quality, Aspect

Example usage: `/parse-check HARLEQUIN CREST\n925 Item Power\nLegendary Helm\n...`

Implementation: write a short inline C# script that constructs a `GearParser`, feeds each line, and prints the result. Use `dotnet-script` or inline `dotnet run` with a temp `.csx` file, or add a one-off test assertion to `D4Scanner.Tests/Program.cs` and run it.

If the item fails to parse, check:
1. Does the name line look ALL-CAPS? (`NameCandidate` requires all-uppercase letters, length 2–64)
2. Is there an `Item Power` line? (`LooksLikeItem` requires it for non-seal/charm/rune items)
3. Does the block end with a line containing `"mouse button"` or `"action button"`? (those are the `EndMarkers`)
4. Is the item type in `GearParser.TypeSlot`? (see the dict at the top of `GearParser.cs`)
