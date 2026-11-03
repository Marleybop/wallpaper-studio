# Wallpaper Studio

An Oxide plugin for Rust that turns the Wallpaper Tool into a proper decorating tool:
- a fast picker with search, collections, favourites, recents, room sets, and grid or list views
- hotkeys, automatic wall/floor/ceiling switching, and copying from a wall
- bulk paint or strip for a level or a whole building
- full undo
- players' own Steam Workshop wallpapers (added straight to their personal list)

Everything is in one file: [`plugin/WallpaperStudio.cs`](plugin/WallpaperStudio.cs).

## Install
1. Copy `plugin/WallpaperStudio.cs` to your server's `oxide/plugins/` folder. It compiles and loads
   automatically.
2. Grant permissions, for example:
   ```
   oxide.grant group default wallpaperstudio.use
   oxide.grant group default wallpaperstudio.submit
   oxide.grant group vip wallpaperstudio.bulk
   oxide.grant user <steamid> wallpaperstudio.admin
   ```
3. Add wallpapers through **Admin** in the menu, with `wpstudio.add <link>` in the console, or by importing
   a list (see below).

Wallpapers are Steam Workshop items, referenced by their Workshop ID. The plugin stores the IDs, and
each player's game downloads the textures from Steam.

## Permissions
| Permission | What it allows |
|---|---|
| `wallpaperstudio.use` | Open the menu and pick wallpapers |
| `wallpaperstudio.bulk` | Bulk paint / strip a level or a building |
| `wallpaperstudio.free` | No cloth cost: bulk jobs and, by default, single placements |
| `wallpaperstudio.submit` | My Wallpapers: add your own Workshop wallpapers |
| `wallpaperstudio.admin` | Admin tab and Browse → Add to server, remove server wallpapers, server room sets, see every collection |

Optional: gate a collection behind a permission of your choosing in the config
(`"Collection permissions": { "Collection name": "wallpaperstudio.something" }`). Leave it empty and every
collection is public.

## Using it
The menu has a **Help** tab that explains everything using the server's actual settings and keys. In short:

- **Menu:**
  - Hold the Wallpaper Tool and press middle mouse, or type `/wp`.
  - Close it with ✕ or by clicking outside.
  - Click a wallpaper to load it into the tool, then place it as normal.
  - **Plain** sets the tool back to Rust's standard wallpaper.
- **Menu layout:**
  - **Wallpapers:** All, Favourites (★), Recent, Collections (server-wide, set by admins).
  - **Yours:** My Wallpapers (your own Workshop finds) and Room Sets (a matching wall + floor + ceiling
    you can load in one click).
  - **Find more:** Steam Workshop (search the Workshop in game).
  - **At the bottom:** Settings (view, icon size, auto surface, hotbar panel; saved per player), Help,
    and Admin (admins only).
  - The version number is shown next to the title.
- **Hotkeys while holding the tool** (configurable):
  - **R:** next favourite (or recent).
  - **Shift+R:** copy the wallpaper you're looking at.
  - **Shift+E:** strip the surface you're looking at (cloth refunded).
- **Auto surface:** the tool switches to wall, floor or ceiling to match what you aim at. It's a
  per-player toggle (`/wp auto`).
- **Hotbar panel:** shows a picture of what the tool will place (`/wp hud` to hide or show it).
- **Room Sets:** a matching wall + floor + ceiling, saved per player (`/wp set <name>`). Admins can also
  save server room sets. Click one to load all three into the tool.
- **Bulk** (`/wp bulk`):
  - Choose PAINT or STRIP, WALLS / FLOORS / CEILINGS / ALL 3, and THIS LEVEL or WHOLE BUILDING.
  - It shows the count and the cost or refund first, and runs in small batches so the server doesn't
    stutter. Outside faces are skipped.
- **Undo:** covers every placement, strip and bulk job (last 30 actions, 15 minutes, both configurable).
  Use UNDO 1 / 5 / ALL or `/wp undo [n|all]`. Cloth is settled both ways.
- **Browse Workshop:** search the Steam Workshop from inside the menu, with preview images and sorting
  (trending, top rated, most used, newest). Click a result to preview it, then add it to My Wallpapers or,
  for admins, to the server. This needs a free Steam Web API key in the config
  (`"Workshop browser"` → `"Steam Web API key"`, from https://steamcommunity.com/dev/apikey). Only items
  Steam lists publicly appear; any link can still be pasted directly.
- **My Wallpapers:** paste a Steam Workshop link or ID into the box in the My Wallpapers tab and press
  Enter, or use chat: `/wp add <link> [wall|floor|ceiling]`.
- **Cost:** every placement costs the game's single wallpaper price. To make it free:
  - for everyone, set "Everything is free for everyone" in the config
  - for a group, `oxide.grant group <group> wallpaperstudio.free`

## Keys and binds
Rust only passes its built-in actions to the server (movement, jump, crouch, sprint, E, R, mouse
buttons), and passes nothing while a cursor menu is open. Any other key works through a one-off
client bind in the F1 console:

| Bind (any key you like) | Does |
|---|---|
| `bind q wpstudio.ui toggle` | open / close the menu |
| `bind c wpstudio.ui copy` | copy the wallpaper you're looking at |
| `bind x wpstudio.ui strip` | strip the surface you're looking at |
| `bind z "wpstudio.ui undo 1"` | undo the last action |
| `bind v wpstudio.ui next` / `bind b wpstudio.ui prev` | next / previous favourite |
| `bind n wpstudio.ui bulk` | open the bulk window |

## Console commands (admin)
- `wpstudio.add <id or link> [wall|floor|ceiling] [collection...]`: add one wallpaper.
- `wpstudio.importlist [file]`: import `oxide/data/WallpaperStudio/<file>.json` (default `import`). It's a
  JSON list of `{ "Id": 123, "Name": "Brick", "Kind": "Wall", "Collection": "Starter", "Tags": [] }`.
- `wpstudio.export`: write the catalogue and server room sets to `oxide/data/WallpaperStudio/export.json`.

## Data
- `oxide/data/WallpaperStudio/catalogue.json`: wallpapers and server room sets. The previous version is
  kept in `catalogue_backup.json`.
- `oxide/data/WallpaperStudio/players.json`: favourites, recents, personal wallpapers, room sets and view
  settings.
- Data is saved every 30 s, on server save and on unload. A broken config file is never overwritten;
  the plugin uses defaults in memory and logs a warning.

## Versions
See [CHANGELOG.md](CHANGELOG.md). Releases are tagged `vX.Y.Z` in git, and the version number also
shows next to the title in the menu.

## Development
See [docs/DESIGN.md](docs/DESIGN.md) for how it works.
[`tools/deploy-test.sh`](tools/deploy-test.sh) copies the plugin to a local test server and prints the
compile result. Set `RUST_TEST_SERVER` to your server folder.
