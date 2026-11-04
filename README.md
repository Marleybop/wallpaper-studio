# Wallpaper Studio

An Oxide plugin for Rust that turns the Wallpaper Tool into a proper decorating tool:
- **Find wallpapers quickly:** search, favourites, recents and collections, as a grid or a list.
- **Steam Workshop browser:** find new wallpapers without leaving the game.
- **Room Sets:** save a matching wall + floor + ceiling and apply it in one go.
- **Bulk paint or strip:** a whole level or building at once, with full undo.
- **Hotkeys:** plus automatic wall/floor/ceiling switching.

---

## Quick start (server owners)

You need a Rust server running **Oxide** (uMod).

### Step 1: install the plugin
1. Download [`WallpaperStudio.cs`](https://github.com/Marleybop/wallpaper-studio/raw/main/plugin/WallpaperStudio.cs).
2. Put it in your server's **`oxide/plugins`** folder. No restart is needed.
3. Check that the server console shows `Loaded plugin Wallpaper Studio`.

### Step 2: let players use it
Type these into the **server console** (or RCON) one line at a time:
```
oxide.grant group default wallpaperstudio.use
oxide.grant group default wallpaperstudio.submit
oxide.grant group default wallpaperstudio.bulk
```
`default` means every player. That gives everyone the menu, their own Workshop wallpapers, and bulk
painting. Leave out the last line if you don't want everyone bulk painting.

### Step 3: make yourself an admin
Type this into the server console, with your Steam name or Steam ID:
```
oxide.grant user YourSteamName wallpaperstudio.admin
```

### Step 4: add some wallpapers
In game:
1. Hold the **Wallpaper Tool** and press **middle mouse**, or type `/wp` in chat.
2. Open the **Admin** tab (bottom left).
3. Paste a Steam Workshop link into the first box and press **Enter**.
4. Pick **WALL**, **FLOOR** or **CEILING**, then press **ADD**.

Everyone now sees that wallpaper in the **All** tab.

### Step 5 (optional): turn on the Workshop browser
This lets you and your players search the Steam Workshop inside the menu.
1. Get a free key at https://steamcommunity.com/dev/apikey. Log in with Steam; any domain name will do.
2. Open `oxide/config/WallpaperStudio.json`, find `"Steam Web API key"` and paste the key between the quotes.
3. Type `oxide.reload WallpaperStudio` in the server console.

### Step 6: check the server settings
In game, **Admin** tab → **SERVER SETTINGS** has simple ON/OFF switches:

| Setting | Default | What it does |
|---|---|---|
| Allow mature (adult) wallpapers | OFF | Mature-flagged Workshop items are blocked |
| Free for everyone | OFF | No cloth cost for placing wallpaper |
| Allow wallpaper on outside faces | OFF | Players can paper outside walls too |
| Hotkeys | ON | E / Shift+E / Shift+middle mouse while holding the tool (R stays rotate) |
| Auto update | ON | The plugin keeps itself up to date |

That's it. Everything else is optional.

---

## Permissions

| Permission | Who should get it | What it allows |
|---|---|---|
| `wallpaperstudio.use` | Everyone | Open the menu and pick wallpapers |
| `wallpaperstudio.submit` | Everyone | Add their own Workshop wallpapers (My Wallpapers) |
| `wallpaperstudio.bulk` | Everyone, or a trusted group | Paint or strip a whole level or building |
| `wallpaperstudio.free` | Optional, e.g. a donor group | Place wallpaper without paying cloth |
| `wallpaperstudio.admin` | Admins only | Admin tab, adding wallpapers for everyone, server settings |

Give a permission to everyone with `oxide.grant group default <permission>`, to one player with
`oxide.grant user <name> <permission>`, or to any Oxide group you've made with
`oxide.grant group <group> <permission>`.

---

## For players

The menu's **Help** tab explains everything with your server's real keys and settings. The basics:

- **Open the menu:** hold the Wallpaper Tool and press **middle mouse**, or type `/wp`.
- **Close the menu:** click ✕ or anywhere outside it.
- **Pick a wallpaper:** click it, then place it with the tool as normal.
- **Switch surface:** the WALL / FLOOR / CEILING buttons. With Auto surface on, the tool also switches
  by itself to whatever you aim at.
- **Favourites:** click the ★ on any wallpaper. **E** then steps to the next favourite and **Shift+E** back to the previous one while you
  hold the tool.
- **Copy a wallpaper:** look at it and press **Shift+middle mouse**.
- **Strip a wallpaper:** look at it and type **/wp strip** (or bind a key in F1: `bind x wpstudio.ui strip`). You get the cloth back.
- **Room Sets:** stand in a room you've decorated, then **COPY THIS ROOM** → name it → **SAVE**. Later,
  **PAINT ROOM** applies it to a whole level.
- **Bulk:** **BULK: PAINT / STRIP A ROOM** in the side panel does a whole level or building, and shows
  the cost first.
- **Undo:** **UNDO 1 / 5 / ALL** in the side panel, or `/wp undo`.
- **Your own wallpapers:** **My Wallpapers** tab → paste a Workshop link → Enter. To make one yourself,
  see [docs/MAKING-WALLPAPERS.md](docs/MAKING-WALLPAPERS.md).
- **Settings:** grid or list view, icon size, auto surface, hotbar picture. They're saved for you.

### Extra keys (optional)
While the menu is open, Rust doesn't send keys to the server. To use your own keys, press **F1**
and type a bind once:

| Type this in F1 (pick any key) | Does |
|---|---|
| `bind q wpstudio.ui toggle` | Open / close the menu |
| `bind c wpstudio.ui copy` | Copy the wallpaper you're looking at |
| `bind x wpstudio.ui strip` | Strip the surface you're looking at |
| `bind z "wpstudio.ui undo 1"` | Undo the last change |
| `bind n wpstudio.ui bulk` | Open the bulk window |

---

## Admin reference

### Console commands
| Command | Does |
|---|---|
| `wpstudio.add <link> wall` | Add a wallpaper for everyone (`floor` / `ceiling` for those; add a collection name after it if you like) |
| `wpstudio.importlist` | Add many at once from `oxide/data/WallpaperStudio/import.json` (format below) |
| `wpstudio.export` | Save every wallpaper and server room set to `oxide/data/WallpaperStudio/export.json` |
| `wpstudio.update` | Check for a newer version (`wpstudio.update install` installs it) |

`import.json` format:
```json
[
  { "Id": 3542806360, "Name": "Blue Stripes", "Kind": "Wall", "Collection": "Classic" },
  { "Id": 3535979383, "Name": "Oak Planks", "Kind": "Floor", "Collection": "Classic" }
]
```

### Updates
With **Auto update** on, the plugin checks for a new release every 6 hours, installs it and reloads.
The previous version is kept in `oxide/data/WallpaperStudio/backups/`. To go back, copy that file over
`oxide/plugins/WallpaperStudio.cs`.

### Paid store wallpapers
Wallpapers sold in the Rust item store only work for players who own them, the same as with the
game's own tool. Players who don't own one won't see it in the menu or the browser, can't add it and
can't copy it off someone's wall. A player who owns one can add it to **My Wallpapers**. Nobody can
add one to the server list for everyone, admins included.

Everything else on the Steam Workshop works for everyone.

The list of store wallpapers loads from Steam a few seconds after the server starts. Until then, the
menu stays empty and adding wallpapers is refused.

### Mature content
Steam marks some Workshop items as mature (nudity or sexual content). Most players' games won't show
them, even if your server allows them. With **Allow mature** off (the default), they're hidden from
the browser and refused when pasted.

### Where data is kept
- `oxide/config/WallpaperStudio.json`: settings. It's safe to edit; reload the plugin afterwards.
- `oxide/data/WallpaperStudio/catalogue.json`: the server's wallpapers and room sets (with a backup copy).
- `oxide/data/WallpaperStudio/players.json`: each player's favourites, own wallpapers, room sets and settings.

---

## More
- [CHANGELOG.md](CHANGELOG.md): what changed in each version. The version also shows next to the menu
  title.
- [docs/MAKING-WALLPAPERS.md](docs/MAKING-WALLPAPERS.md): making and uploading your own wallpaper.
- [docs/DESIGN.md](docs/DESIGN.md): how it works, for developers. `tools/deploy-test.sh` copies the
  plugin to a local test server (set `RUST_TEST_SERVER` to its folder).

## Licence
MIT. See [LICENSE](LICENSE). © Marleybop.
