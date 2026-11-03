# Wallpaper Studio: design

## Goals
- **Fast:**
  - Clicks resend only the part of the menu that changed.
  - No per-frame work for players who aren't holding the Wallpaper Tool.
  - Saves are batched.
- **Seamless:**
  - Hotkeys and automatic surface switching while holding the tool.
  - A hotbar panel showing what will be placed.
  - Bulk jobs and full undo.
- **Safe:**
  - Every UI command is checked against permissions and state on the server.
  - Bulk jobs are time-sliced.
  - Data is never lost to a bad config.
- **Vanilla-compatible:** placement, cost and rotation stay with the game; the plugin only chooses the skin.

## How the game handles wallpaper
- **The tool:** the tool's held entity (`WallpaperPlanner`, Rust's own class) has three skin slots:
  `wallSkinID`, `flooringSkinID` and `ceilingSkinID`. `currentMode` is 1 = wall, 2 = floor, 3 = ceiling.
  Setting a slot and sending the `CLIENT_ChangeSkin` / `CLIENT_SwitchMode` RPCs is all it takes. The
  game's own `DoBuild` places the wallpaper and charges `WallpaperSettings.PlacementPrice`.
- **Surfaces:** each `BuildingBlock` has two sides (0/1).
  - `WallpaperPlanner.Settings.GetCategory(block, side)` says whether a side is a wall, floor or ceiling.
    The top of a floor block is a floor; its underside is a ceiling.
  - `SetWallpaper(id, side, rotation)` and `RemoveWallpaper(side)` change a side.
  - `CheckWallpaper()` removes wallpaper from faces that must be inside but aren't.
- **Skins:** wallpapers are Steam Workshop items. A skin ID is a Workshop ID, and each client downloads
  the texture from Steam. A server can't send textures.

## Structure (one file)
- **Config:**
  - keys and hotkeys
  - auto-switch, HUD and close-on-pick
  - bulk limits and undo size/time
  - submission rules
  - optional collection permissions
  - free-for-all
- **Catalogue** (`catalogue.json`): wallpapers `{Id, Name, Kind, Collection, Tags}` and server themes.
  It's indexed in memory by kind and ID.
- **Players** (`players.json`): favourites, recents per surface, submissions, personal themes, and view
  preferences (layout, icon size, auto-switch, HUD).
- **Input:**
  - A small component is attached only while a player holds the tool. It polls the configured hotkey
    combos, runs auto-switch and refreshes the HUD 5 times a second.
  - The client sends no input while a cursor menu is open, so closing by key relies on client binds to
    the `wpstudio.ui` command.
- **UI (CUI):**
  - A root overlay and frame, then separately replaceable panels: header, rail, grid, side and modal.
  - The HUD panel lives on the `Hud` layer with no cursor.
- **Undo:** a per-player stack of actions. Each records `{block, side, old skin, old rotation, was empty,
  new skin, strip}` plus the cloth to settle.
  - Single placements are captured through the `CanBuild` hook, with the result checked on the next tick.
  - Undo skips any surface that has changed since.
- **Bulk:** builds a target list from the building's blocks, filtered by surface type and level (walls
  sit on the room's floor height, ceilings are one storey up).
  - It then applies 40 surfaces per frame, and the game's `CheckWallpaper` rejects outside faces.
- **Submissions:** players add Workshop IDs.
  - A Steam `GetPublishedFileDetails` lookup fills in names and flags non-Rust items. Many wallpaper
    items are hidden from the public API but still work.
  - Usable straight away by that player (and their team if sharing is on). Items that aren't Rust
    Workshop items are flagged.

## Testing
Use a local dedicated server (SteamCMD app 258550 with Oxide). Start it, then run `tools/deploy-test.sh`
to compile the plugin there.

In game, check:
- the menu on every tab
- grid and list views at each icon size
- the hotkeys
- auto-switch while aiming at walls, floors and ceilings
- bulk paint and strip at each scope
- undo 1/5/all
- adding personal wallpapers by link and from the browser
