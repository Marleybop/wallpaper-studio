# Changelog

## 1.14.0
- Hotkeys moved off R, which the game's wallpaper tool uses to rotate (e.g. turning a carpet).
  Next is now **E**, copy is **Shift+middle mouse**. Configs still on the old R / Shift+R defaults are
  moved over automatically; keys you changed yourself are kept.
- New **previous** hotkey (**Shift+E**) to step back through favourites / recents.
- Strip no longer has a default hotkey (Shift+E is now previous): use `/wp strip` or an F1 bind
  (`bind x wpstudio.ui strip`). Hotkeys never use Ctrl, since it crouches.
- `/wp next` and `/wp prev` chat commands.
- Hint, hotbar panel, tips and Help only list the hotkeys that are set.
- A surface with no wallpapers on the server (e.g. no ceilings added yet) now says so and points to
  the Steam Workshop tab instead of showing just the Plain tile.

## 1.13.2
- Paid wallpapers from the Rust item store are blocked unless the player owns them, just like the
  game's own wallpaper tool. They're refused when added (Admin tab, `wpstudio.add`, `wpstudio.importlist`,
  Workshop browser, My Wallpapers), hidden from menus and the browser, and can't be copied off another
  player's wall. Owners can still add them to My Wallpapers, but never to the server list for everyone.
- The Steam API key is cleaned on load, so a pasted space or line break no longer makes Steam reject it.

## 1.13.1
- Workshop browser no longer lists items of the old "Wallpaper" type, which no longer load in game
  (existing configs are cleaned automatically).

## 1.13.0
- Admin tab: one "Auto update" switch; update messages in game no longer name the source.
- Names that come from Steam have formatting tags stripped, so they can't distort the menu.
- A short cooldown between wallpaper adds, to limit Steam lookups.

## 1.12.2
- Fix: text box placeholders are created before their input field (removes the "Unable to find
  placeholder panel" client message).

## 1.12.1
- Clearer tile controls: a bigger star on a dark backing, red REMOVE / DELETE buttons instead of a small ✕,
  and solid ADDED / MATURE badges in the Workshop browser.

## 1.12.0
- Outside faces (when allowed): click them with the tool to place, like inside faces; it costs cloth
  and goes into undo history as normal.
- BULK window: INCLUDE OUTSIDE FACES checkbox (unticked by default).

## 1.11.1
- SERVER SETTINGS: switch for wallpaper on outside faces.

## 1.11.0
- Admin tab: SERVER SETTINGS with ON/OFF switches for mature wallpapers, free for everyone, hotkeys,
  update checks and automatic installs, plus CHECK FOR UPDATE NOW. Changes save to the config at once.

## 1.10.1
- With mature content disallowed, a pasted wallpaper is only added after Steam confirms it isn't
  mature (before, it was added and then removed a moment later). If it can't be checked, it isn't added.

## 1.10.0
- Text boxes use a real placeholder that disappears as you type.
- Mature content: "Allow mature (adult) wallpapers" (off by default). When it's off, mature-flagged Workshop
  items are hidden in the browser and refused when pasted; when it's on, they're marked MATURE.
  Pasted links are checked with the Steam key when one is set.
- Self-updating: checks the latest GitHub release tag every 6 hours and installs it (verified, with a
  backup). Both are on by default and can be switched off. New `wpstudio.update [install]` command.

## 1.9.1
- Fix: 1.9.0 did not compile (a line break inside the Room Sets help text). Use 1.9.1.

## 1.9.0
- Room Sets rebuilt as a builder: three slots (wall, floor, ceiling) with CHANGE, plus COPY THIS ROOM,
  FROM MY TOOL and CLEAR. Name it and SAVE.
- Each saved set has USE (load into the tool) and PAINT ROOM (load and open the bulk window on ALL 3).

## 1.8.1
- Workshop browser: clearer buttons, **ADD FOR EVERYONE** (admins) and **ADD JUST FOR ME**. The surface
  (wall/floor/ceiling) now comes from the item's own Workshop tag and is shown above the buttons.

## 1.8.0
- Tidier menu: tabs grouped under **Wallpapers**, **Yours** and **Find more**, with Settings, Help and
  Admin pinned at the bottom.
- New **Settings** tab for view (grid/list), icon size, auto surface and hotbar panel, each with a short
  explanation.
- "Themes" renamed to **Room Sets** (`/wp set <name>`; `/wp theme` still works).
- The version number is shown next to the title in the menu.

## 1.7.0
- Players' own wallpapers are usable straight away; no approval step.
- Removed the admin edit mode. Admins remove a server wallpaper from the side panel.

## 1.6.0
- In-menu **Steam Workshop** browser: search, sort, preview images, add to your list or to the server.
  It defaults to the Workshop's wallpaper tags for each surface.

## 1.5.1
- Links can be pasted into the menu's text boxes; help text updated.

## 1.5.0
- First public version: picker with search, collections, favourites, recents, room sets, grid/list
  views, configurable hotkeys, auto surface switching, hotbar preview, bulk paint/strip, undo,
  personal Workshop wallpapers, and a Help tab built from the server's config.
