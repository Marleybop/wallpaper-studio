# Making your own wallpaper

Rust wallpapers are Steam Workshop items, the same as skins. Make one, publish it, then add its link in
Wallpaper Studio: **My Wallpapers** (just for you), or **Admin → add** (for everyone).

## What you need
| File | Size | Notes |
|---|---|---|
| Main image (colour) | **2048 × 2048 px**, PNG | Square. It covers one wall, floor or ceiling panel. 1024 × 1024 also works but looks softer. |
| Normal map (optional) | 2048 × 2048 PNG | Adds surface bumps. OpenGL style (green = up). |
| Specular/gloss map (optional) | 2048 × 2048 PNG | Shininess, with gloss in the alpha channel. Leave it out for a matte look. |
| Occlusion map (optional) | 2048 × 2048 PNG | Soft shadows in creases. |

Tips:
- **Crop to a square first.** Wide images get squashed or cropped.
- **Avoid fine text and tiny detail.** A wall panel is about 3 m wide, so it's seen from a distance.
- **Seamless patterns** (bricks, tiles, wood) look best on floors and ceilings, where panels sit side by side.
- **Use your own art,** or images you're allowed to share. Uploading other people's work publicly can
  get it taken down.
- **Avoid nudity and sexual content.** Steam flags it as mature, most players' games won't show it, and
  many servers don't allow it.

## Upload it
1. Start Rust and open the **Workshop** skin editor from the main menu.
2. Pick the item type: **Wallpaper Wall**, **Wallpaper Flooring** or **Wallpaper Ceiling**.
   - Pick the right one. A wall wallpaper only works on walls.
   - The old plain "Wallpaper" type no longer loads in game.
3. Load your main image into the **Diffuse / Main** slot, and any optional maps into their slots.
   Leave the colour tint white.
4. Check the preview, give it a title, and **Publish**.
5. On the item's Steam Workshop page, copy the link. Set it to **Hidden** or **Unlisted** if you don't
   want it public; it still works in game.

## Use it in game
- **For yourself:** Wallpaper Studio → **My Wallpapers**, paste the link and press Enter, or type
  `/wp add <link> wall` in chat (use `floor` or `ceiling` for those).
- **For everyone (admins):** **Admin** tab → paste the link, pick the surface, **ADD**. Or run
  `wpstudio.add <link> wall` in the console.

New uploads can take a few minutes before Steam serves them. If it shows as plain, wait and try again.
