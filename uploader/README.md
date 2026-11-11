# Wallpaper Studio Uploader

A Windows app that turns any image into a Rust **wallpaper** (wall, floor or ceiling) or **spray can
decal** and uploads it to the Steam Workshop for you. You don't need to open Rust's Workshop editor.

![Wallpaper Studio Uploader](../docs/uploader.png)

## What you need
- Windows 10 or 11 (64-bit).
- **Steam running** and logged in with the account that **owns Rust**. Items are uploaded to that account.
- Nothing else. The download includes everything (no .NET install needed).

## Install
1. Open the [Releases page](https://github.com/Marleybop/wallpaper-studio/releases) and download
   `WallpaperStudioUploader-<version>-win-x64.zip` from the newest **Uploader** release.
2. Unzip it anywhere, for example `Documents\Wallpaper Studio Uploader app`. Keep
   `WallpaperStudioUploader.exe` and `steam_api64.dll` in the same folder.
3. Start Steam, then double-click `WallpaperStudioUploader.exe`.
   - Windows may say *"Windows protected your PC"*, because the app isn't code-signed. Click
     **More info → Run anyway**.
4. The badge in the top-right corner should say **Steam · \<your name\>** with a green dot. If it's amber
   and says *not connected*, start Steam, log in, then click the badge to retry.

## Upload a new wallpaper or spray
The left side has four numbered steps. The right side is a live preview.

1. **Image**: any of these work:
   - drag an image file onto the big preview box, or click **Browse…**;
   - paste a link into **…or paste an image link** and click **Get** (or press Enter). It can be a
     direct image link, or a web page with the image on it (Imgur, ArtStation, Reddit and so on);
   - drag an image straight from your browser onto the preview;
   - press **Ctrl+V** anywhere in the window to paste a copied link or a copied image
     (right-click an image in your browser → **Copy image**).

   PNG, JPG, BMP, GIF and TIFF work. For WebP images, save them as PNG or JPG first. The card warns
   you if the image is small (under 512 px), because it will look blurry in game.
2. **Type**: click **Wall**, **Floor**, **Ceiling** or **Spray**. A wall wallpaper only works on walls, so
   pick the right one.
3. **Details**: the **Title** is filled in from the file name, so change it if you like. **Description** is
   optional. Then pick the **Visibility on Steam**:

   | Choice | Who can find it on Steam | Works in game? |
   |---|---|---|
   | Public | Everyone | Yes |
   | Unlisted | Only people with the link | Yes |
   | Friends | Your Steam friends | Yes |
   | Hidden | Only you (default) | Yes, by its ID |

4. **Look** (wallpapers) or **Size** (sprays):

   | Setting | What it does |
   |---|---|
   | Texture size | **2048 px** is best. **1024 px** is a smaller download but softer. |
   | Repeat | Wallpapers only. Repeats the image 2×2 up to 8×8, for small patterns like tiles. |
   | Shine | Wallpapers only. Left = matte, right = glossy. |
   | Surface depth | Wallpapers only. How much bumpy surface is made from the image. Far left = flat. |

   The preview shows the picture exactly as it will be cropped and repeated. Shine and surface depth
   only show in game.
5. Click **Upload to Workshop** (bottom right), check the summary and click **Upload**.
6. When it finishes, a green box shows the new **Workshop ID** with buttons to **Copy ID**,
   **Copy add command** (wallpapers) and **Open Workshop page**. The add command
   (`wpstudio.add <id> wall`) is also already on your clipboard.

New uploads can take a few minutes before Steam serves them in game.

**First upload ever?** Steam may ask you to accept the Workshop legal agreement. The app tells you if
it does. Open the Workshop page, accept it, and the item becomes usable.

### How images are fitted
- **Wallpapers** use the **centre square** of your image (the sides or top/bottom are cropped), so crop
  it yourself first if a part matters.
- **Sprays** keep the **whole image** and its **transparency**. The preview shows a checkerboard where
  it's see-through. Use a PNG with a transparent background for a clean decal.

## Add a wallpaper to your server
The easy way: in game, open Wallpaper Studio → **Admin** tab, paste (Ctrl+V) the ID, pick the surface
and press **ADD**. Or paste the copied `wpstudio.add …` command into the server console.

Optionally the app can do it for you over RCON:
1. Click **Send to a Wallpaper Studio server** (bottom left) to open it.
2. Fill in **Address**, the **RCON port** (the server's `rcon.port`, often 28016) and the
   **RCON password** (`rcon.password`). The server needs `rcon.web 1`, which is the default.
3. Optional: a **Collection** name groups it in the in-game menu.
4. Turn on **Add wallpapers to the server after upload**. From then on every wallpaper you upload is
   added straight away. **Add the current ID now** adds the last upload (or the ID under *Update an item*).

The RCON password controls the whole server, so only use this on a PC you trust. It's saved encrypted
for your Windows account. Sprays aren't managed by Wallpaper Studio, so they're never sent to the server.

## Update an item you already uploaded
1. Click **Update an item you already uploaded** (bottom left) to open it.
2. Paste the Workshop ID or link and click **Load**. The title, description, type and visibility are
   filled in. You can only update items your Steam account owns.
3. To replace the picture, choose a new image. To change only the text or visibility, don't.
4. Click **Upload to Workshop**, then **Update**.

To go back to making a new item, click **New item instead**. Players who already downloaded the item
see the change after Steam's next update check.

## Where things are kept
| What | Where |
|---|---|
| Files of each upload (textures, icons, `manifest.txt`) | `Documents\Wallpaper Studio Uploader\<Workshop ID>\` |
| Images downloaded from links or pasted | `%LOCALAPPDATA%\WallpaperStudioUploader\downloads\` (safe to empty) |
| Settings (server address, last choices) | `%APPDATA%\WallpaperStudioUploader\settings.json` |
| RCON password | In the settings file, encrypted for your Windows account (not plain text) |

## Building from source (developers)
1. Install the .NET 10 SDK.
2. In this folder run `dotnet publish -c Release -o publish`. The `publish` folder is the release:
   `WallpaperStudioUploader.exe` (self-contained) and `steam_api64.dll`.
3. Command-line use (output goes to the console you start it from):
   - Build the textures without Steam:
     `WallpaperStudioUploader.exe --build <image> <wall|floor|ceiling|spray> <out folder> [--size 1024] [--tile N] [--gloss 0.25] [--bump 1]`
   - Upload (Steam must be running):
     `WallpaperStudioUploader.exe --upload <image or link> <wall|floor|ceiling|spray> --title "Name" [--visibility hidden|unlisted|friends|public] [--id <existing ID>] [--size 1024] [--tile N]`
     It prints the new ID and, for wallpapers, the `wpstudio.add` command.

Uses [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks) (MIT) to talk to Steam as Rust (app 252490).
