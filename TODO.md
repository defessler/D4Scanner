# TODO — captured 2026-06-12, to process later
# Plan: 6 phased releases (v0.39.0–v0.44.0) — see .claude plan / release notes per tag.

## UI / display

- [x] The green up-arrow (upgrade) badge is clipped on the item icons. *(v0.39.0)*
- [x] When an upgrade exists in the inventory, find a good way to show WHICH inventory item
      the upgrade is when hovering (link the badge to the concrete item). *(v0.41.0)*
- [x] Better item-identity tracking: duplicates of the same item should appear as separate
      items if ANY of their tooltip text differs — excluding stateful text (EQUIPPED,
      favorited, junk marks, durability, etc.). *(v0.41.0)*
- [x] Improve the layout/formatting of the ASPECT / UNIQUE POWER boxes (compare cards & detail). *(v0.39.0)*
- [x] BUILD WANTS tooltip should show the VALUES it wants (thresholds), not just affix names. *(v0.42.0)*
- [x] EQUIPPED tooltip (and a general rule for similar spots): special progress-bar/text
      treatment when a roll EXCEEDS the wanted amount — e.g. reddish→yellow while below
      target, orange→purple when exceeding, and a glow treatment for "All …" umbrella affixes. *(v0.42.0)*
- [x] Move the "Build spec" button inline, aligned to the left of the "My Gear" tab. *(v0.40.0)*
- [x] The "Builds" button should just switch between builds we've already been working on. *(v0.40.0)*
- [x] "Search builds" should scope the shown categories to the selected class. *(v0.40.0)*

## Settings

- [x] Clearing the cache should reprocess the TTS file afterwards. *(v0.43.0)*
- [ ] Don't allow "changing" the log file path — instead allow specifying a NEW location and
      MOVE the existing log there.
- [ ] Break the TTS log into multiple files (rotate by total size, date, and/or game session).
- [x] "Clear all data" should restore/rebuild as much as possible afterwards (TTS log, icons,
      Maxroll build info). *(v0.43.0)*
- [x] Add an "open the log folder" option in Settings. *(v0.39.0)*
- [ ] Add configuration for log retention: max file age, how many files are kept, max single
      file size — with reasonable defaults.
- [ ] Add a button + combo box to load your build from a previous date/session.
- [ ] Add a "clear logs" button.
- [x] Fix: "Diagnose capture" locks up the app. *(v0.39.0)*
- [x] Remove the affix roll-quality threshold setting. *(v0.42.0)*
- [x] Make "Clear selected" for the cache feel more a part of the cache section. *(v0.43.0)*
- [x] Remove the Cancel button from the cache section. *(v0.43.0)*
- [x] Add "Save" and "Revert" buttons to Settings. *(v0.43.0)*
- [x] Clicking the ✕ (top right) of Settings should close WITHOUT applying changes. *(v0.43.0)*
