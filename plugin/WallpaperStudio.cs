// Wallpaper Studio: a wallpaper picker and painting toolkit for Rust (Oxide).
// See docs/DESIGN.md for the design and plugin/README.md for setup.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using HarmonyLib;

namespace Oxide.Plugins
{
    [Info("Wallpaper Studio", "Marleybop", "1.6.0")]
    [Description("Fast wallpaper picker: search, collections, favourites, themes, list/grid views, eyedropper, bulk paint/strip with undo, and player-submitted Workshop wallpapers.")]
    public class WallpaperStudio : RustPlugin
    {
        #region Constants & permissions

        private const string PermUse = "wallpaperstudio.use";
        private const string PermBulk = "wallpaperstudio.bulk";
        private const string PermSubmit = "wallpaperstudio.submit";
        private const string PermTrusted = "wallpaperstudio.trusted";   // submissions skip approval
        private const string PermFree = "wallpaperstudio.free";         // bulk painting costs nothing
        private const string PermAdmin = "wallpaperstudio.admin";

        private const string ToolShortname = "wallpaper.tool";
        private const string Cmd = "wpstudio.ui";

        // UI element names (each can be replaced on its own, so a click resends only what changed)
        private const string UiRoot = "WS.Root";
        private const string UiFrame = "WS.Frame";
        private const string UiHeader = "WS.Header";
        private const string UiRail = "WS.Rail";
        private const string UiGrid = "WS.Grid";
        private const string UiSide = "WS.Side";
        private const string UiModal = "WS.Modal";
        private const string UiHud = "WS.Hud";

        private static WallpaperStudio Instance;

        #endregion

        #region Config

        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Open menu button (FIRE_THIRD = middle mouse; 'disabled' = chat command only)")]
            public string OpenButton = "FIRE_THIRD";

            [JsonProperty("Hotkeys while holding the tool. Buttons: RELOAD (R), USE (E), JUMP (Space), FIRE_THIRD (middle mouse), FIRE_SECONDARY (right mouse). Modifier: SPRINT (Shift), DUCK (Ctrl, crouches), or empty")]
            public HotkeySettings Hotkeys = new HotkeySettings();

            [JsonProperty("Everything is free for everyone (no cloth for placing or bulk painting)")]
            public bool FreeForAll = false;

            [JsonProperty("Show the selected-wallpaper panel by the hotbar while holding the tool")]
            public bool ShowHud = true;

            [JsonProperty("Auto-switch the tool to wall/floor/ceiling based on what you aim at (players can turn it off)")]
            public bool AutoSwitch = true;

            [JsonProperty("wallpaperstudio.free also refunds ordinary single placements")]
            public bool FreeSinglePlacements = true;

            [JsonProperty("Close the menu after picking a wallpaper")]
            public bool CloseOnPick = false;

            [JsonProperty("Chat commands")]
            public string[] ChatCommands = { "wp", "wallpaper" };

            [JsonProperty("Default view for new players (grid or list)")]
            public string DefaultLayout = "grid";

            [JsonProperty("Default icon size for new players (S, M, L)")]
            public string DefaultSize = "M";

            [JsonProperty("Bulk painting / stripping")]
            public BulkSettings Bulk = new BulkSettings();

            [JsonProperty("Player submissions")]
            public SubmissionSettings Submissions = new SubmissionSettings();

            [JsonProperty("Workshop browser (in-menu Steam Workshop search)")]
            public BrowserSettings Browser = new BrowserSettings();

            [JsonProperty("Max personal themes per player")]
            public int MaxThemes = 20;

            [JsonProperty("Allow wallpaper on outside faces (disables the game's inside-only check for everyone)")]
            public bool AllowOutside = false;

            [JsonProperty("Recent wallpapers remembered per surface")]
            public int RecentCount = 12;

            [JsonProperty("Collection permissions (collection name -> permission required to see it)")]
            public Dictionary<string, string> CollectionPermissions = new Dictionary<string, string>();
        }

        private class Combo
        {
            [JsonProperty("Button")] public string Button = "";
            [JsonProperty("Hold modifier")] public string Modifier = "";
            [JsonIgnore] public BUTTON B; [JsonIgnore] public BUTTON M; [JsonIgnore] public bool Valid, HasMod;
        }

        private class BrowserSettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Steam Web API key (https://steamcommunity.com/dev/apikey)")] public string ApiKey = "";
            [JsonProperty("Workshop tags per surface (all must match; empty = search text only)")]
            public Dictionary<string, List<string>> Tags = new Dictionary<string, List<string>>
            {
                { "Wall", new List<string>() }, { "Floor", new List<string>() }, { "Ceiling", new List<string>() }
            };
            [JsonProperty("Words added to every search when no tags are set")] public string DefaultSearch = "wallpaper";
            [JsonProperty("Results per page")] public int PerPage = 12;
            [JsonProperty("Cache results for (minutes)")] public int CacheMinutes = 10;
        }

        private class HotkeySettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Next favourite / recent")] public Combo Next = new Combo { Button = "RELOAD" };
            [JsonProperty("Copy wallpaper you look at")] public Combo Copy = new Combo { Button = "RELOAD", Modifier = "SPRINT" };
            [JsonProperty("Strip surface you look at")] public Combo Strip = new Combo { Button = "USE", Modifier = "SPRINT" };
        }

        private class BulkSettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Max surfaces per job")] public int MaxSurfaces = 600;
            [JsonProperty("Surfaces processed per server frame")] public int PerFrame = 40;
            [JsonProperty("Undo history kept for (seconds)")] public int UndoSeconds = 900;
            [JsonProperty("Undo history size (actions per player)")] public int HistorySize = 30;
        }

        private class SubmissionSettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Require admin approval (players with wallpaperstudio.trusted skip it)")] public bool RequireApproval = true;
            [JsonProperty("Max personal wallpapers per player")] public int MaxPerPlayer = 30;
            [JsonProperty("Team members can use each other's approved wallpapers")] public bool ShareWithTeam = true;
            [JsonProperty("Look up names on Steam")] public bool SteamLookup = true;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception("empty");
            }
            catch (Exception e)
            {
                PrintError($"Config is invalid ({e.Message}); using defaults in memory. Your file was NOT overwritten - fix it and reload.");
                config = new Configuration();
                return;
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig() => config = new Configuration();
        protected override void SaveConfig() => Config.WriteObject(config, true);

        #endregion

        #region Data

        public enum Kind { Wall = 1, Floor = 2, Ceiling = 3 }
        private static readonly Kind[] Kinds = { Kind.Wall, Kind.Floor, Kind.Ceiling };

        private class Wallpaper
        {
            public ulong Id;
            public string Name = "";
            public Kind Kind;
            public string Collection = "";
            public List<string> Tags = new List<string>();
            public ulong AddedBy;
            public DateTime AddedAt = DateTime.UtcNow;
        }

        private class Theme
        {
            public string Name = "";
            public ulong Wall, Floor, Ceiling;
            public ulong Owner;   // 0 = server theme

            public ulong Get(Kind k) { return k == Kind.Floor ? Floor : k == Kind.Ceiling ? Ceiling : Wall; }
        }

        private class Catalogue
        {
            public int Version = 2;
            public List<Wallpaper> Items = new List<Wallpaper>();
            public List<Theme> Themes = new List<Theme>();
        }

        private class Submission
        {
            public ulong Id;
            public string Name = "";
            public Kind Kind;
            public string Status = "pending"; // pending | approved | denied
            public DateTime At = DateTime.UtcNow;
            public string SteamCheck = "";
        }

        private class PlayerInfo
        {
            public HashSet<ulong> Favourites = new HashSet<ulong>();
            public Dictionary<Kind, List<ulong>> Recent = new Dictionary<Kind, List<ulong>>();
            public List<Submission> Mine = new List<Submission>();
            public List<Theme> Themes = new List<Theme>();
            public string Layout = "";   // grid | list
            public string Size = "";     // S | M | L (icon size)
            public bool HintShown;
            public bool? AutoSwitch;     // null = server default
            public bool? Hud;            // null = server default
            // v1.0/1.1 stored "Grid" (small/medium/large = tile count, which felt inverted); read once to migrate
            [JsonProperty("Grid", NullValueHandling = NullValueHandling.Ignore)] public string LegacyGrid;
        }

        private Catalogue catalogue;
        private Dictionary<ulong, PlayerInfo> players;
        private bool catalogueDirty, playersDirty;
        private readonly Dictionary<Kind, List<Wallpaper>> byKind = new Dictionary<Kind, List<Wallpaper>>();
        private readonly Dictionary<ulong, Wallpaper> byId = new Dictionary<ulong, Wallpaper>();

        private void LoadData()
        {
            catalogue = Interface.Oxide.DataFileSystem.ReadObject<Catalogue>($"{Name}/catalogue") ?? new Catalogue();
            if (catalogue.Themes == null) catalogue.Themes = new List<Theme>();
            players = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PlayerInfo>>($"{Name}/players") ?? new Dictionary<ulong, PlayerInfo>();
            foreach (var p in players.Values)
            {
                if (p.Themes == null) p.Themes = new List<Theme>();
                if (!string.IsNullOrEmpty(p.LegacyGrid) && string.IsNullOrEmpty(p.Size))
                    p.Size = p.LegacyGrid == "small" ? "L" : p.LegacyGrid == "large" ? "S" : "M";
                p.LegacyGrid = null;
            }
            RebuildIndex();
        }

        private void SaveData(bool force = false)
        {
            if (catalogueDirty || force)
            {
                var previous = Interface.Oxide.DataFileSystem.ReadObject<Catalogue>($"{Name}/catalogue");
                if (previous != null && previous.Items.Count > 0)
                    Interface.Oxide.DataFileSystem.WriteObject($"{Name}/catalogue_backup", previous);
                Interface.Oxide.DataFileSystem.WriteObject($"{Name}/catalogue", catalogue);
                catalogueDirty = false;
            }
            if (playersDirty || force)
            {
                Interface.Oxide.DataFileSystem.WriteObject($"{Name}/players", players);
                playersDirty = false;
            }
        }

        private void RebuildIndex()
        {
            byKind.Clear(); byId.Clear();
            foreach (var k in Kinds) byKind[k] = new List<Wallpaper>();
            foreach (var w in catalogue.Items)
            {
                if (byId.ContainsKey(w.Id)) continue;
                byId[w.Id] = w;
                byKind[w.Kind].Add(w);
            }
            foreach (var list in byKind.Values) list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private PlayerInfo Info(ulong id)
        {
            PlayerInfo p;
            if (!players.TryGetValue(id, out p)) { p = new PlayerInfo(); players[id] = p; }
            return p;
        }

        #endregion

        #region Lifecycle

        private void Init()
        {
            Instance = this;
            foreach (var p in new[] { PermUse, PermBulk, PermSubmit, PermTrusted, PermFree, PermAdmin })
                permission.RegisterPermission(p, this);
            foreach (var p in config.CollectionPermissions.Values.Distinct())
                if (p.StartsWith("wallpaperstudio.") && !permission.PermissionExists(p, this)) permission.RegisterPermission(p, this);
            foreach (var c in config.ChatCommands)
                cmd.AddChatCommand(c, this, nameof(ChatCommand));
            LoadData();
        }

        private void OnServerInitialized()
        {
            openEnabled = ParseButton(config.OpenButton, BUTTON.FIRE_THIRD, out openButton);
            cycleEnabled = config.Hotkeys != null && config.Hotkeys.Enabled;
            if (config.Hotkeys == null) config.Hotkeys = new HotkeySettings();
            foreach (var combo in new[] { config.Hotkeys.Next, config.Hotkeys.Copy, config.Hotkeys.Strip })
            {
                combo.Valid = ParseButton(combo.Button, BUTTON.RELOAD, out combo.B);
                combo.HasMod = ParseButton(combo.Modifier, BUTTON.SPRINT, out combo.M);
            }
            foreach (var player in BasePlayer.activePlayerList)
                if (HoldingTool(player)) ToolWatcher.Attach(player);
            timer.Every(30f, () => SaveData());
            if (catalogue.Items.Count == 0)
                Puts("Catalogue is empty. Admins: open the menu and use Admin > Add, or 'wpstudio.importlist' to import a list of IDs.");
        }

        private void Unload()
        {
            foreach (var w in UnityEngine.Object.FindObjectsOfType<ToolWatcher>()) UnityEngine.Object.Destroy(w);
            foreach (var player in BasePlayer.activePlayerList) { CloseUi(player); CuiHelper.DestroyUi(player, UiHud); }
            foreach (var job in jobs.Values) job.Cancelled = true;
            SaveData();
            Instance = null;
        }

        private void OnServerSave() => SaveData();

        private void OnPlayerDisconnected(BasePlayer player)
        {
            ui.Remove(player.userID);
            openUi.Remove(player.userID);
            pendingPlan.Remove(player.userID);
        }

        #endregion

        #region Tool input

        private BUTTON openButton = BUTTON.FIRE_THIRD, cycleButton = BUTTON.RELOAD;
        private bool openEnabled = true, cycleEnabled = true;
        private readonly HashSet<ulong> openUi = new HashSet<ulong>();

        private bool ParseButton(string text, BUTTON fallback, out BUTTON button)
        {
            button = fallback;
            if (string.IsNullOrEmpty(text) || text.Equals("disabled", StringComparison.OrdinalIgnoreCase)) return false;
            if (Enum.TryParse(text, true, out button)) return true;
            PrintWarning($"Unknown button '{text}', using {fallback}");
            button = fallback;
            return true;
        }

        private static string KeyName(string button)
        {
            switch ((button ?? "").ToUpper())
            {
                case "USE": return "E";
                case "RELOAD": return "R";
                case "JUMP": return "Space";
                case "FIRE_THIRD": return "Middle mouse";
                case "DUCK": return "Ctrl";
                case "SPRINT": return "Shift";
                default: return button;
            }
        }

        private string ComboName(Combo c)
        {
            if (c == null || !c.Valid || !cycleEnabled) return "-";
            return (c.HasMod ? KeyName(c.Modifier) + "+" : "") + KeyName(c.Button);
        }

        // Pressed now? Combos with a modifier need it held; plain ones need no other modifier held.
        private bool ComboPressed(InputState input, Combo c)
        {
            if (!c.Valid || !input.WasJustPressed(c.B)) return false;
            if (c.HasMod) return input.IsDown(c.M);
            return !input.IsDown(BUTTON.SPRINT) && !input.IsDown(BUTTON.DUCK);
        }

        private static bool HoldingTool(BasePlayer player)
        {
            var item = player?.GetActiveItem();
            return item != null && item.info.shortname == ToolShortname;
        }

        private static global::WallpaperPlanner HeldTool(BasePlayer player)
        {
            return player?.GetActiveItem()?.GetHeldEntity() as global::WallpaperPlanner;
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (player == null) return;
            if (newItem != null && newItem.info.shortname == ToolShortname)
            {
                if (!permission.UserHasPermission(player.UserIDString, PermUse)) return;
                ToolWatcher.Attach(player);
                var info = Info(player.userID);
                if (!info.HintShown && openEnabled)
                {
                    Toast(player, Msg("Hint", player, openEnabled ? KeyName(config.OpenButton) : "/" + ChatCmd, ComboName(config.Hotkeys.Next), ComboName(config.Hotkeys.Copy), ComboName(config.Hotkeys.Strip), ChatCmd));
                    info.HintShown = true; playersDirty = true;
                }
            }
            else if (oldItem != null && oldItem.info.shortname == ToolShortname)
            {
                ToolWatcher.Detach(player);
                CuiHelper.DestroyUi(player, UiHud);
                CloseUi(player);
            }
        }

        // One tiny component per player, only while they hold the tool: zero cost for everyone else.
        private class ToolWatcher : FacepunchBehaviour
        {
            private BasePlayer player;
            private float nextAllowed, nextAim;
            private int shownMode = -2; private ulong shownSkin = ulong.MaxValue;

            public static void Attach(BasePlayer p)
            {
                var w = p.GetComponent<ToolWatcher>() ?? p.gameObject.AddComponent<ToolWatcher>();
                w.player = p;
            }

            public static void Detach(BasePlayer p)
            {
                var w = p.GetComponent<ToolWatcher>();
                if (w != null) Destroy(w);
            }

            private void Update()
            {
                if (player == null || Instance == null || !player.IsConnected) { Destroy(this); return; }
                if (UnityEngine.Time.realtimeSinceStartup < nextAllowed) return;
                var input = player.serverInput;
                if (Instance.openUi.Contains(player.userID)) return;   // the client sends no input while a cursor menu is open
                if (Instance.openEnabled && input.WasJustPressed(Instance.openButton))
                {
                    nextAllowed = UnityEngine.Time.realtimeSinceStartup + 0.25f;
                    if (!HoldingTool(player)) { Destroy(this); return; }
                    Instance.OpenUi(player);
                }
                else if (Instance.cycleEnabled && (Instance.ComboPressed(input, Instance.config.Hotkeys.Copy) || Instance.ComboPressed(input, Instance.config.Hotkeys.Strip) || Instance.ComboPressed(input, Instance.config.Hotkeys.Next)))
                {
                    nextAllowed = UnityEngine.Time.realtimeSinceStartup + 0.18f;
                    if (!HoldingTool(player)) { Destroy(this); return; }
                    var hk = Instance.config.Hotkeys;
                    if (Instance.ComboPressed(input, hk.Copy)) Instance.Eyedropper(player);
                    else if (Instance.ComboPressed(input, hk.Strip)) Instance.StripOne(player);
                    else Instance.Cycle(player, false);
                }
                else if (UnityEngine.Time.realtimeSinceStartup >= nextAim)
                {
                    nextAim = UnityEngine.Time.realtimeSinceStartup + 0.2f;
                    Instance.AutoSwitch(player);
                    // refresh the hotbar panel whenever the tool's mode or skin changes (from anywhere, incl. the game's own UI)
                    var tool = HeldTool(player);
                    if (tool != null)
                    {
                        var kind = ToolKind(tool);
                        ulong skin = ToolSkin(tool, kind);
                        if ((int)kind != shownMode || skin != shownSkin)
                        {
                            shownMode = (int)kind; shownSkin = skin;
                            Instance.ShowHud(player, tool);
                        }
                    }
                }
            }

            private void OnDestroy()
            {
                if (player != null && player.IsConnected) CuiHelper.DestroyUi(player, UiHud);
            }
        }

        private static Kind ToolKind(global::WallpaperPlanner tool)
        {
            int m = tool != null ? tool.currentMode : 1;
            return m >= 1 && m <= 3 ? (Kind)m : Kind.Wall;
        }

        private static ulong ToolSkin(global::WallpaperPlanner tool, Kind kind)
        {
            switch (kind)
            {
                case Kind.Floor: return tool.flooringSkinID;
                case Kind.Ceiling: return tool.ceilingSkinID;
                default: return tool.wallSkinID;
            }
        }

        private static void SetToolSkin(global::WallpaperPlanner tool, Kind kind, ulong skin)
        {
            switch (kind)
            {
                case Kind.Floor: tool.flooringSkinID = skin; break;
                case Kind.Ceiling: tool.ceilingSkinID = skin; break;
                default: tool.wallSkinID = skin; break;
            }
            if (ToolKind(tool) == kind)
            {
                tool.skinID = skin;
                tool.ClientRPC(RpcTarget.NetworkGroup("CLIENT_ChangeSkin"), skin, tool.currentMode);
            }
            tool.SendNetworkUpdate();
        }

        private static void SetToolMode(global::WallpaperPlanner tool, Kind kind)
        {
            if (tool.currentMode == (int)kind) return;
            tool.currentMode = (int)kind;
            tool.skinID = ToolSkin(tool, kind);
            tool.ClientRPC(RpcTarget.NetworkGroup("CLIENT_SwitchMode"), tool.skinID, tool.currentMode);
            tool.SendNetworkUpdate();
        }

        private static int ItemIdFor(Kind kind)
        {
            var def = WallpaperSettings.GetItemDefForCategory((WallpaperSettings.Category)(int)kind);
            return def != null ? def.itemid : 0;
        }

        private bool HudOn(BasePlayer p) => Info(p.userID).Hud ?? config.ShowHud;

        // Small panel to the right of the hotbar: picture, surface and name of what the tool will place.
        private void ShowHud(BasePlayer p, global::WallpaperPlanner tool)
        {
            if (!HudOn(p)) { CuiHelper.DestroyUi(p, UiHud); return; }
            var kind = ToolKind(tool);
            ulong skin = ToolSkin(tool, kind);
            var c = new CuiElementContainer();
            Panel(c, "Hud", UiHud, "0.08 0.09 0.1 0.78", "0.5 0", "0.5 0", "206 18", "430 84");
            Panel(c, UiHud, CuiHelper.GetGuid(), C.Accent, "0 0", "0 1", "0 0", "3 0");
            if (skin == 0) Label(c, UiHud, Msg("Plain", p).ToUpper(), 11, C.TextDim, TextAnchor.MiddleCenter, "0 0", "0 1", "8 4", "66 -4", bold: true);
            else Icon(c, UiHud, ItemIdFor(kind), skin, "0 0", "0 1", "8 4", "66 -4");
            Label(c, UiHud, Msg(kind.ToString(), p).ToUpper(), 10, C.Accent, TextAnchor.UpperLeft, "0 0", "1 1", "74 0", "-6 -6", bold: true);
            Label(c, UiHud, Trim(NameOf(p, skin, kind), 24), 13, C.Text, TextAnchor.MiddleLeft, "0 0", "1 1", "74 0", "-6 0", bold: true);
            Label(c, UiHud, Msg("HudKeys", p, ComboName(config.Hotkeys.Next), ComboName(config.Hotkeys.Copy), ComboName(config.Hotkeys.Strip)), 9, C.TextDim, TextAnchor.LowerLeft, "0 0", "1 1", "74 6", "-6 0");
            CuiHelper.AddUi(p, c);
        }

        private bool AutoSwitchOn(BasePlayer p)
        {
            var v = Info(p.userID).AutoSwitch;
            return v ?? config.AutoSwitch;
        }

        // Switch the tool to the surface type the player is aiming at (a few times a second, holders only).
        private void AutoSwitch(BasePlayer p)
        {
            if (!AutoSwitchOn(p)) return;
            var tool = HeldTool(p);
            if (tool == null) return;
            RaycastHit hit;
            if (!Physics.Raycast(p.eyes.HeadRay(), out hit, 4f, Rust.Layers.Mask.Construction)) return;
            var b = hit.GetEntity() as BuildingBlock;
            if (b == null) return;
            int side = FacingSide(b, p, false);
            var kind = side >= 0 ? KindOf(b, side) : null;
            if (kind == null || ToolKind(tool) == kind.Value) return;
            SetToolMode(tool, kind.Value);
            State(p).Kind = kind.Value;
        }

        // Step through favourites (or recents) without opening the menu.
        private void Cycle(BasePlayer p, bool backwards)
        {
            var tool = HeldTool(p);
            if (tool == null) return;
            var kind = ToolKind(tool);
            var info = Info(p.userID);
            var list = byKind[kind].Where(w => info.Favourites.Contains(w.Id) && CanSeeCollection(p, w.Collection)).Select(w => w.Id)
                .Concat(Personal(p, kind, false).Where(w => info.Favourites.Contains(w.Id)).Select(w => w.Id)).Distinct().ToList();
            string source = Msg("TabFavs", p);
            if (list.Count == 0)
            {
                List<ulong> recent;
                if (info.Recent.TryGetValue(kind, out recent)) list = recent.Where(id => MayUse(p, id, kind)).ToList();
                source = Msg("TabRecent", p);
            }
            if (list.Count == 0) { Toast(p, Msg("CycleEmpty", p, Msg(kind.ToString(), p), ComboName(config.Hotkeys.Next))); return; }
            int i = list.IndexOf(ToolSkin(tool, kind));
            i = i < 0 ? (backwards ? list.Count - 1 : 0) : (i + (backwards ? list.Count - 1 : 1)) % list.Count;
            SetToolSkin(tool, kind, list[i]);
            Toast(p, Msg("Cycled", p, NameOf(p, list[i], kind), i + 1, list.Count, source));
        }

        #endregion

        #region Catalogue queries

        private class UiState
        {
            public string Tab = "all";       // all | favs | recent | collections | themes | mine | admin
            public string Search = "";
            public string Collection = "";
            public int Page;
            public Kind Kind = Kind.Wall;
            public AdminDraft Draft;
            public ulong EditId;
            public bool EditMode;            // admins: show edit gears on tiles
            public bool ThemeToServer;       // admins: "save theme" goes to server themes
            public string BrowseSort = "trend";   // trend | top | new | subs
            public int BrowsePage = 1;
            public ulong BrowseSel;
            public string BrowseCollection = "";
        }

        private class AdminDraft
        {
            public string Id = "", Name = "", Collection = "", Tags = "";
            public Kind Kind = Kind.Wall;
        }

        private readonly Dictionary<ulong, UiState> ui = new Dictionary<ulong, UiState>();

        private UiState State(BasePlayer p)
        {
            UiState s;
            if (!ui.TryGetValue(p.userID, out s)) { s = new UiState(); ui[p.userID] = s; }
            return s;
        }

        private string ChatCmd => config.ChatCommands != null && config.ChatCommands.Length > 0 ? config.ChatCommands[0] : "wp";

        private bool IsAdmin(BasePlayer p) => permission.UserHasPermission(p.UserIDString, PermAdmin);
        private bool IsFree(BasePlayer p) => config.FreeForAll || permission.UserHasPermission(p.UserIDString, PermFree);

        private bool CanSeeCollection(BasePlayer p, string collection)
        {
            if (string.IsNullOrEmpty(collection)) return true;
            string perm;
            if (!config.CollectionPermissions.TryGetValue(collection, out perm)) return true;
            return permission.UserHasPermission(p.UserIDString, perm) || IsAdmin(p);
        }

        private static bool Matches(Wallpaper w, string search)
        {
            if (string.IsNullOrEmpty(search)) return true;
            if (w.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (w.Collection.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var t in w.Tags) if (t.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return w.Id.ToString() == search;
        }

        // Personal wallpapers a player may use: their own approved ones, plus their team's if sharing is on.
        private IEnumerable<Wallpaper> Personal(BasePlayer p, Kind kind, bool includePending)
        {
            var owners = new List<ulong> { p.userID };
            if (config.Submissions.ShareWithTeam && p.Team != null)
                owners.AddRange(p.Team.members.Where(m => m != p.userID));
            foreach (var owner in owners)
            {
                PlayerInfo info;
                if (!players.TryGetValue(owner, out info)) continue;
                foreach (var s in info.Mine)
                {
                    if (s.Kind != kind) continue;
                    bool usable = s.Status == "approved";
                    if (!usable && !(includePending && owner == p.userID)) continue;
                    yield return new Wallpaper { Id = s.Id, Name = s.Name, Kind = s.Kind, Collection = owner == p.userID ? "Mine" : "Team", AddedBy = owner };
                }
            }
        }

        private List<Wallpaper> Query(BasePlayer p, UiState s)
        {
            var info = Info(p.userID);
            IEnumerable<Wallpaper> src;
            switch (s.Tab)
            {
                case "favs":
                    src = byKind[s.Kind].Concat(Personal(p, s.Kind, false)).Where(w => info.Favourites.Contains(w.Id));
                    break;
                case "recent":
                    List<ulong> recent;
                    if (!info.Recent.TryGetValue(s.Kind, out recent)) recent = new List<ulong>();
                    var personal = Personal(p, s.Kind, false).GroupBy(w => w.Id).ToDictionary(g => g.Key, g => g.First());
                    src = recent.Select(id => byId.ContainsKey(id) ? byId[id] : (personal.ContainsKey(id) ? personal[id] : null)).Where(w => w != null);
                    break;
                case "mine":
                    src = Personal(p, s.Kind, true);
                    break;
                default:
                    src = byKind[s.Kind];
                    if (!string.IsNullOrEmpty(s.Collection)) src = src.Where(w => w.Collection.Equals(s.Collection, StringComparison.OrdinalIgnoreCase));
                    break;
            }
            return src.Where(w => CanSeeCollection(p, w.Collection) && Matches(w, s.Search)).GroupBy(w => w.Id).Select(g => g.First()).ToList();
        }

        private string NameOf(BasePlayer p, ulong id, Kind kind)
        {
            if (id == 0) return Msg("Plain", p);
            Wallpaper w;
            if (byId.TryGetValue(id, out w)) return w.Name;
            var mine = Personal(p, kind, true).FirstOrDefault(x => x.Id == id);
            return mine != null ? mine.Name : id.ToString();
        }

        private bool MayUse(BasePlayer p, ulong id, Kind kind)
        {
            if (id == 0) return true;
            Wallpaper w;
            if (byId.TryGetValue(id, out w)) return w.Kind == kind && CanSeeCollection(p, w.Collection);
            return Personal(p, kind, false).Any(x => x.Id == id);
        }

        private IEnumerable<Theme> ThemesFor(BasePlayer p)
        {
            return catalogue.Themes.Concat(Info(p.userID).Themes);
        }

        #endregion

        #region UI: building blocks

        private static class C
        {
            public const string Shade = "0 0 0 0.55";
            public const string Frame = "0.09 0.10 0.11 0.97";
            public const string Panel = "0.14 0.15 0.16 1";
            public const string PanelHi = "0.20 0.22 0.23 1";
            public const string Tile = "0.18 0.19 0.20 1";
            public const string Accent = "0.45 0.72 0.25 1";      // lime
            public const string AccentDim = "0.26 0.40 0.16 1";
            public const string OnAccent = "0.05 0.08 0.03 1";
            public const string Text = "0.92 0.92 0.90 1";
            public const string TextDim = "0.62 0.64 0.64 1";
            public const string TextFaint = "0.45 0.47 0.47 1";
            public const string Danger = "0.72 0.24 0.20 1";
            public const string Gold = "1 0.82 0.2 1";
            public const string Blur = "assets/content/ui/uibackgroundblur.mat";
        }

        private static void Panel(CuiElementContainer c, string parent, string name, string color, string min, string max, string offMin = "0 0", string offMax = "0 0", bool cursor = false, string material = null)
        {
            var img = new CuiImageComponent { Color = color };
            if (material != null) img.Material = material;
            var e = new CuiElement { Name = name, Parent = parent, DestroyUi = name, Components = { img, new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max, OffsetMin = offMin, OffsetMax = offMax } } };
            // cursor only (no keyboard capture), so the close key still reaches the server
            if (cursor) e.Components.Add(new CuiNeedsCursorComponent());
            c.Add(e);
        }

        private static void Label(CuiElementContainer c, string parent, string text, int size, string color, TextAnchor align, string min, string max, string offMin = "0 0", string offMax = "0 0", bool bold = false)
        {
            c.Add(new CuiElement
            {
                Name = CuiHelper.GetGuid(), Parent = parent,
                Components =
                {
                    new CuiTextComponent { Text = text, FontSize = size, Color = color, Align = align, Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf" },
                    new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max, OffsetMin = offMin, OffsetMax = offMax }
                }
            });
        }

        private static string Button(CuiElementContainer c, string parent, string command, string bg, string min, string max, string text = "", int size = 13, string textColor = C.Text, string offMin = "0 0", string offMax = "0 0")
        {
            var name = CuiHelper.GetGuid();
            c.Add(new CuiElement
            {
                Name = name, Parent = parent,
                Components =
                {
                    new CuiButtonComponent { Command = command, Color = bg },
                    new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max, OffsetMin = offMin, OffsetMax = offMax }
                }
            });
            if (!string.IsNullOrEmpty(text)) Label(c, name, text, size, textColor, TextAnchor.MiddleCenter, "0 0", "1 1", bold: true);
            return name;
        }

        // A row of mutually exclusive choice buttons. Returns the x after the last chip.
        private static int Chips(CuiElementContainer c, string parent, string anchor, int x, int y0, int y1, int width, IEnumerable<KeyValuePair<string, string>> options, string current, string commandPrefix, int size = 12)
        {
            foreach (var o in options)
            {
                bool on = o.Key == current;
                Button(c, parent, $"{commandPrefix} {o.Key}", on ? C.Accent : C.PanelHi, anchor, anchor, o.Value, size, on ? C.OnAccent : C.Text, $"{x} {y0}", $"{x + width} {y1}");
                x += width + 6;
            }
            return x;
        }

        private static void Icon(CuiElementContainer c, string parent, int itemId, ulong skin, string min, string max, string offMin = "0 0", string offMax = "0 0")
        {
            c.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { ItemId = itemId, SkinId = skin, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max, OffsetMin = offMin, OffsetMax = offMax }
                }
            });
        }

        private static void Sprite(CuiElementContainer c, string parent, string sprite, string color, string min, string max)
        {
            c.Add(new CuiElement
            {
                Parent = parent,
                Components = { new CuiImageComponent { Sprite = sprite, Color = color }, new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max } }
            });
        }

        private static void Input(CuiElementContainer c, string parent, string command, string text, string hint, string min, string max, string offMin = "0 0", string offMax = "0 0", int size = 13)
        {
            var bg = CuiHelper.GetGuid();
            Panel(c, parent, bg, "0.07 0.07 0.08 1", min, max, offMin, offMax);
            if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(hint))
                Label(c, bg, hint, size, C.TextFaint, TextAnchor.MiddleLeft, "0 0", "1 1", "10 0", "-8 0");
            c.Add(new CuiElement
            {
                Parent = bg,
                Components =
                {
                    new CuiInputFieldComponent { Command = command, Text = text, FontSize = size, Color = C.Text, Align = TextAnchor.MiddleLeft, CharsLimit = 300, NeedsKeyboard = true },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-8 0" }
                }
            });
        }

        private static string Trim(string s, int max) => s.Length > max ? s.Substring(0, max - 1) + "…" : s;

        #endregion

        #region UI: screens

        // icon size -> columns x rows (S = small icons = more of them)
        private static readonly Dictionary<string, int[]> GridSizes = new Dictionary<string, int[]>
        {
            { "S", new[] { 8, 4 } }, { "M", new[] { 6, 3 } }, { "L", new[] { 4, 2 } }
        };
        private const int ListRows = 9;

        private string LayoutOf(BasePlayer p)
        {
            var l = Info(p.userID).Layout;
            return l == "list" || l == "grid" ? l : (config.DefaultLayout == "list" ? "list" : "grid");
        }

        private string SizeOf(BasePlayer p)
        {
            var s = Info(p.userID).Size;
            return GridSizes.ContainsKey(s ?? "") ? s : (GridSizes.ContainsKey(config.DefaultSize) ? config.DefaultSize : "M");
        }

        private void OpenUi(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) { Toast(player, Msg("NoPerm", player)); return; }
            var tool = HeldTool(player);
            if (tool == null) { Toast(player, Msg("HoldTool", player)); return; }
            ToolWatcher.Attach(player);
            var s = State(player);
            s.Kind = ToolKind(tool);
            s.Page = 0;
            var c = new CuiElementContainer();
            Panel(c, "Overlay", UiRoot, C.Shade, "0 0", "1 1", cursor: true, material: C.Blur);
            Button(c, UiRoot, $"{Cmd} close", "0 0 0 0", "0 0", "1 1");                       // click outside closes
            Panel(c, UiRoot, UiFrame, C.Frame, "0.5 0.5", "0.5 0.5", "-640 -340", "640 340");
            Button(c, UiFrame, $"{Cmd} noop", "0 0 0 0", "0 0", "1 1");                         // swallow clicks inside
            CuiHelper.AddUi(player, c);
            openUi.Add(player.userID);
            Redraw(player, header: true, rail: true, grid: true, side: true);
        }

        private void CloseUi(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UiRoot);
            openUi.Remove(player.userID);
            pendingPlan.Remove(player.userID);
            UiState s;
            if (ui.TryGetValue(player.userID, out s)) s.Draft = null;
        }

        private void Redraw(BasePlayer p, bool header = false, bool rail = false, bool grid = false, bool side = false)
        {
            if (!openUi.Contains(p.userID)) return;
            var c = new CuiElementContainer();
            var s = State(p);
            var tool = HeldTool(p);
            if (header) BuildHeader(c, p, s);
            if (rail) BuildRail(c, p, s);
            if (grid) BuildGrid(c, p, s, tool);
            if (side) BuildSide(c, p, s, tool);
            CuiHelper.AddUi(p, c);
        }

        private void BuildHeader(CuiElementContainer c, BasePlayer p, UiState s)
        {
            Panel(c, UiFrame, UiHeader, C.Panel, "0 1", "1 1", "0 -60", "0 0");
            Label(c, UiHeader, "WALLPAPER STUDIO", 20, C.Accent, TextAnchor.LowerLeft, "0 0", "0 1", "18 26", "300 -6", bold: true);
            Label(c, UiHeader, "by Marleybop", 11, C.TextDim, TextAnchor.UpperLeft, "0 0", "0 1", "19 6", "300 -36");
            if (s.Tab != "themes")
                Chips(c, UiHeader, "0 0.5", 260, -17, 17, 100, Kinds.Select(k => new KeyValuePair<string, string>(((int)k).ToString(), Msg(k.ToString(), p).ToUpper())), ((int)s.Kind).ToString(), $"{Cmd} kind", 13);
            Input(c, UiHeader, $"{Cmd} search", s.Search, Msg("SearchHint", p), "1 0.5", "1 0.5", "-560 -17", "-180 17", 14);
            if (!string.IsNullOrEmpty(s.Search))
                Button(c, UiHeader, $"{Cmd} search", C.PanelHi, "1 0.5", "1 0.5", "✕", 12, C.TextDim, "-174 -17", "-146 17");
            Button(c, UiHeader, $"{Cmd} close", C.Danger, "1 0.5", "1 0.5", Msg("Close", p), 14, C.Text, "-136 -19", "-12 19");
        }

        private void BuildRail(CuiElementContainer c, BasePlayer p, UiState s)
        {
            Panel(c, UiFrame, UiRail, C.Panel, "0 0", "0 1", "0 0", "180 -68");
            var tabs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("all", Msg("TabAll", p)),
                new KeyValuePair<string, string>("favs", Msg("TabFavs", p)),
                new KeyValuePair<string, string>("recent", Msg("TabRecent", p)),
                new KeyValuePair<string, string>("collections", Msg("TabCollections", p)),
                new KeyValuePair<string, string>("themes", Msg("TabThemes", p)),
                new KeyValuePair<string, string>("browse", Msg("TabBrowse", p)),
                new KeyValuePair<string, string>("help", Msg("TabHelp", p)),
            };
            if (config.Submissions.Enabled && permission.UserHasPermission(p.UserIDString, PermSubmit))
                tabs.Add(new KeyValuePair<string, string>("mine", Msg("TabMine", p)));
            if (IsAdmin(p))
            {
                int pending = players.Values.Sum(i => i.Mine.Count(m => m.Status == "pending"));
                tabs.Add(new KeyValuePair<string, string>("admin", Msg("TabAdmin", p) + (pending > 0 ? $"  <color=#f0c040>({pending})</color>" : "")));
            }
            int y = -12;
            foreach (var t in tabs)
            {
                bool on = s.Tab == t.Key;
                var b = Button(c, UiRail, $"{Cmd} tab {t.Key}", on ? C.PanelHi : "0 0 0 0", "0 1", "1 1", "", 13, C.Text, $"8 {y - 36}", $"-8 {y}");
                if (on) Panel(c, b, CuiHelper.GetGuid(), C.Accent, "0 0", "0 1", "0 4", "4 -4");
                Label(c, b, t.Value, 14, on ? C.Text : C.TextDim, TextAnchor.MiddleLeft, "0 0", "1 1", "16 0", "0 0", bold: on);
                y -= 40;
            }
            if (!string.IsNullOrEmpty(s.Collection) && s.Tab == "all")
            {
                Label(c, UiRail, Msg("Filtered", p, s.Collection), 12, C.TextDim, TextAnchor.LowerLeft, "0 0", "1 0", "14 160", "-8 196");
                Button(c, UiRail, $"{Cmd} collection", C.PanelHi, "0 0", "1 0", Msg("ClearFilter", p), 12, C.Text, "8 128", "-8 154");
            }
            // view settings (saved per player)
            bool auto = AutoSwitchOn(p);
            Button(c, UiRail, $"{Cmd} auto", auto ? C.AccentDim : C.PanelHi, "0 0", "1 0", auto ? Msg("AutoOn", p) : Msg("AutoOff", p), 11, auto ? C.Text : C.TextDim, "8 92", "-8 118");
            Label(c, UiRail, Msg("View", p), 11, C.TextDim, TextAnchor.MiddleLeft, "0 0", "1 0", "14 62", "0 80");
            Chips(c, UiRail, "0 0", 14, 38, 62, 74, new[] { new KeyValuePair<string, string>("grid", Msg("Grid", p)), new KeyValuePair<string, string>("list", Msg("List", p)) }, LayoutOf(p), $"{Cmd} layout", 11);
            if (LayoutOf(p) == "grid")
            {
                Label(c, UiRail, Msg("IconSize", p), 11, C.TextDim, TextAnchor.MiddleLeft, "0 0", "1 0", "14 10", "80 32");
                Chips(c, UiRail, "0 0", 82, 10, 32, 26, new[] { "S", "M", "L" }.Select(x => new KeyValuePair<string, string>(x, x)), SizeOf(p), $"{Cmd} size", 11);
            }
        }

        private void BuildGrid(CuiElementContainer c, BasePlayer p, UiState s, global::WallpaperPlanner tool)
        {
            Panel(c, UiFrame, UiGrid, "0 0 0 0", "0 0", "1 1", "190 10", "-330 -70");
            if (s.Tab == "collections") { BuildCollections(c, p, s); return; }
            if (s.Tab == "themes") { BuildThemes(c, p, s, tool); return; }
            if (s.Tab == "admin") { BuildAdmin(c, p, s); return; }
            if (s.Tab == "help") { BuildHelp(c, p); return; }
            if (s.Tab == "browse") { BuildBrowse(c, p, s); return; }

            var items = Query(p, s);
            // "Plain" first on the All tab so players can go back to the game's standard wallpaper
            bool withPlain = s.Tab == "all" && string.IsNullOrEmpty(s.Search) && string.IsNullOrEmpty(s.Collection);
            var entries = new List<Wallpaper>();
            if (withPlain) entries.Add(null);
            entries.AddRange(items);

            bool list = LayoutOf(p) == "list";
            var size = GridSizes[SizeOf(p)];
            int per = list ? ListRows : size[0] * size[1];
            int pages = Math.Max(1, (entries.Count + per - 1) / per);
            s.Page = Mathf.Clamp(s.Page, 0, pages - 1);

            if (items.Count == 0 && !withPlain)
            {
                string why = s.Tab == "favs" ? Msg("EmptyFavs", p) : s.Tab == "recent" ? Msg("EmptyRecent", p) : s.Tab == "mine" ? Msg("EmptyMine", p) : !string.IsNullOrEmpty(s.Search) ? Msg("EmptySearch", p, s.Search) : Msg("EmptyAll", p);
                Label(c, UiGrid, why, 15, C.TextDim, TextAnchor.MiddleCenter, "0 0.1", "1 0.9");
            }

            ulong current = tool != null ? ToolSkin(tool, s.Kind) : 0;
            var page = entries.Skip(s.Page * per).Take(per).ToList();
            if (list && page.Count > 0)
            {
                string head = CuiHelper.GetGuid();
                Panel(c, UiGrid, head, "0 0 0 0", "0 0.945", "1 1");
                Label(c, head, Msg("ColName", p), 11, C.TextDim, TextAnchor.MiddleLeft, "0 0", "0.6 1", "64 0", "0 0", bold: true);
                Label(c, head, Msg("ColCollection", p), 11, C.TextDim, TextAnchor.MiddleLeft, "0.6 0", "1 1", "0 0", "-80 0", bold: true);
                Label(c, head, Msg("ColFav", p), 11, C.TextDim, TextAnchor.MiddleCenter, "1 0", "1 1", "-40 0", "-4 0", bold: true);
                Panel(c, head, CuiHelper.GetGuid(), C.PanelHi, "0 0", "1 0", "0 0", "0 1");
            }
            for (int i = 0; i < page.Count; i++)
            {
                if (list) ListRow(c, p, s, page[i], i, current);
                else GridTile(c, p, s, page[i], i, size[0], size[1], current);
            }
            // pager + count
            if (pages > 1)
            {
                Button(c, UiGrid, $"{Cmd} page 0", C.PanelHi, "0.5 0", "0.5 0", "«", 16, C.Text, "-150 0", "-112 28");
                Button(c, UiGrid, $"{Cmd} page {s.Page - 1}", C.PanelHi, "0.5 0", "0.5 0", "‹", 16, C.Text, "-106 0", "-68 28");
                Label(c, UiGrid, Msg("PageOf", p, s.Page + 1, pages), 13, C.Text, TextAnchor.MiddleCenter, "0.5 0", "0.5 0", "-62 0", "62 28");
                Button(c, UiGrid, $"{Cmd} page {s.Page + 1}", C.PanelHi, "0.5 0", "0.5 0", "›", 16, C.Text, "68 0", "106 28");
                Button(c, UiGrid, $"{Cmd} page {pages - 1}", C.PanelHi, "0.5 0", "0.5 0", "»", 16, C.Text, "112 0", "150 28");
            }
            Label(c, UiGrid, Msg("Count", p, items.Count), 11, C.TextDim, TextAnchor.MiddleLeft, "0 0", "0.3 0", "2 0", "0 28");
        }

        private void TileExtras(CuiElementContainer c, BasePlayer p, UiState s, Wallpaper w, string parent, string favAnchor, string favOff0, string favOff1, string gearAnchor, string gearOff0, string gearOff1)
        {
            if (w == null) return;
            var info = Info(p.userID);
            bool fav = info.Favourites.Contains(w.Id);
            var star = Button(c, parent, $"{Cmd} fav {w.Id}", "0 0 0 0", favAnchor, favAnchor, "", 0, C.Text, favOff0, favOff1);
            Sprite(c, star, fav ? "assets/icons/favourite_active.png" : "assets/icons/favourite_inactive.png", fav ? C.Gold : "1 1 1 0.5", "0 0", "1 1");
            if (s.EditMode && IsAdmin(p) && byId.ContainsKey(w.Id))
            {
                var gear = Button(c, parent, $"{Cmd} edit {w.Id}", "0 0 0 0", gearAnchor, gearAnchor, "", 0, C.Text, gearOff0, gearOff1);
                Sprite(c, gear, "assets/icons/gear.png", "1 1 1 0.8", "0 0", "1 1");
            }
            if (s.Tab == "mine" && w.Collection == "Mine")
            {
                var sub = info.Mine.FirstOrDefault(m => m.Id == w.Id);
                if (sub != null && sub.Status != "approved")
                    Label(c, parent, sub.Status == "pending" ? Msg("Pending", p) : Msg("Denied", p), 11, sub.Status == "pending" ? C.Gold : C.Danger, TextAnchor.MiddleCenter, "0 0.5", "1 0.62", bold: true);
                Button(c, parent, $"{Cmd} unsubmit {w.Id}", "0 0 0 0.5", gearAnchor, gearAnchor, "✕", 11, C.Danger, gearOff0, gearOff1);
            }
        }

        private void GridTile(CuiElementContainer c, BasePlayer p, UiState s, Wallpaper w, int slot, int cols, int rows, ulong current)
        {
            float gap = 0.012f;
            float cw = (1f - gap * (cols - 1)) / cols, ch = (0.93f - gap * (rows - 1)) / rows;
            int col = slot % cols, row = slot / cols;
            float x0 = col * (cw + gap), y1 = 1f - row * (ch + gap);
            ulong id = w == null ? 0 : w.Id;
            bool selected = id == current;
            string tile = CuiHelper.GetGuid();
            Panel(c, UiGrid, tile, selected ? C.Accent : C.Tile, $"{x0} {y1 - ch}", $"{x0 + cw} {y1}");
            string inner = CuiHelper.GetGuid();
            Panel(c, tile, inner, C.Tile, "0 0", "1 1", selected ? "3 3" : "0 0", selected ? "-3 -3" : "0 0");
            if (w == null) Label(c, inner, Msg("PlainTile", p), 12, C.TextDim, TextAnchor.MiddleCenter, "0.05 0.22", "0.95 0.95", bold: true);
            else Icon(c, inner, ItemIdFor(s.Kind), id, "0.1 0.22", "0.9 0.95");
            Panel(c, inner, CuiHelper.GetGuid(), "0 0 0 0.6", "0 0", "1 0.2");
            int font = cols >= 8 ? 10 : 12;
            Label(c, inner, Trim(w == null ? Msg("Plain", p) : w.Name, cols >= 8 ? 18 : 26), font, C.Text, TextAnchor.MiddleCenter, "0 0", "1 0.2", "3 0", "-3 0");
            Button(c, inner, $"{Cmd} apply {id}", "0 0 0 0", "0 0", "1 1");
            int ic = cols >= 8 ? 18 : 24;
            TileExtras(c, p, s, w, inner, "1 1", $"{-ic - 3} {-ic - 3}", "-3 -3", "0 1", $"3 {-ic - 3}", $"{ic + 3} -3");
        }

        private void ListRow(CuiElementContainer c, BasePlayer p, UiState s, Wallpaper w, int index, ulong current)
        {
            float h = 0.89f / ListRows;
            float y1 = 0.94f - index * h, y0 = y1 - h + 0.006f;
            ulong id = w == null ? 0 : w.Id;
            bool selected = id == current;
            string row = CuiHelper.GetGuid();
            Panel(c, UiGrid, row, selected ? C.AccentDim : (index % 2 == 0 ? C.Tile : C.Panel), $"0 {y0}", $"1 {y1}");
            if (w != null) Icon(c, row, ItemIdFor(s.Kind), id, "0 0", "0 1", "6 3", "54 -3");
            Label(c, row, w == null ? Msg("Plain", p) : w.Name, 14, C.Text, TextAnchor.MiddleLeft, "0 0", "0.6 1", "64 0", "0 0", bold: selected);
            string meta = w == null ? Msg("PlainHelp", p) : (w.Collection + (w.Tags.Count > 0 ? "  ·  " + string.Join(", ", w.Tags) : ""));
            Label(c, row, meta, 12, C.TextDim, TextAnchor.MiddleLeft, "0.6 0", "1 1", "0 0", "-80 0");
            Button(c, row, $"{Cmd} apply {id}", "0 0 0 0", "0 0", "1 1");
            TileExtras(c, p, s, w, row, "1 0.5", "-34 -12", "-10 12", "1 0.5", "-66 -12", "-42 12");
        }

        // Everything a player needs to know, filled in from this server's config and their permissions.
        private void BuildHelp(CuiElementContainer c, BasePlayer p)
        {
            var hk = config.Hotkeys;
            int priceItem; string priceName;
            int price = Price(out priceItem, out priceName);
            bool bulk = config.Bulk.Enabled && permission.UserHasPermission(p.UserIDString, PermBulk);
            bool submit = config.Submissions.Enabled && permission.UserHasPermission(p.UserIDString, PermSubmit);
            string open = openEnabled ? KeyName(config.OpenButton) : Msg("HelpNoKey", p);

            var left = new List<string>
            {
                Msg("HelpOpen", p, open, ChatCmd),
                Msg("HelpPick", p),
                cycleEnabled ? Msg("HelpHotkeys", p, ComboName(hk.Next), ComboName(hk.Copy), ComboName(hk.Strip)) : Msg("HelpHotkeysOff", p),
                Msg(AutoSwitchOn(p) ? "HelpAutoOn" : "HelpAutoOff", p, ChatCmd),
                Msg("HelpHud", p, ChatCmd),
            };
            var right = new List<string>
            {
                IsFree(p) ? Msg("HelpCostFree", p) : Msg("HelpCost", p, price, priceName),
                Msg("HelpUndo", p, config.Bulk.HistorySize, Mathf.RoundToInt(config.Bulk.UndoSeconds / 60f), ChatCmd),
            };
            if (bulk) right.Add(Msg("HelpBulk", p, config.Bulk.MaxSurfaces));
            right.Add(Msg("HelpThemes", p, config.MaxThemes, ChatCmd));
            if (BrowserReady) right.Add(Msg("HelpBrowse", p));
            if (submit) right.Add(Msg(config.Submissions.RequireApproval && !permission.UserHasPermission(p.UserIDString, PermTrusted) && !IsAdmin(p) ? "HelpSubmitApproval" : "HelpSubmit", p, ChatCmd, config.Submissions.MaxPerPlayer) + (config.Submissions.ShareWithTeam ? " " + Msg("HelpTeam", p) : ""));
            right.Add(Msg("HelpBinds", p));

            Label(c, UiGrid, string.Join("\n\n", left), 12, C.Text, TextAnchor.UpperLeft, "0 0", "0.49 1", "4 0", "0 -4");
            Label(c, UiGrid, string.Join("\n\n", right), 12, C.Text, TextAnchor.UpperLeft, "0.51 0", "1 1", "0 0", "-4 -4");
        }

        private void BuildCollections(CuiElementContainer c, BasePlayer p, UiState s)
        {
            var groups = byKind[s.Kind].Where(w => !string.IsNullOrEmpty(w.Collection) && CanSeeCollection(p, w.Collection) && Matches(w, s.Search))
                .GroupBy(w => w.Collection, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key).ToList();
            Label(c, UiGrid, Msg("CollectionsHelp", p), 12, C.TextDim, TextAnchor.UpperLeft, "0 1", "1 1", "0 -22", "0 0");
            if (groups.Count == 0) { Label(c, UiGrid, Msg("NoCollections", p), 15, C.TextDim, TextAnchor.MiddleCenter, "0 0", "1 1"); return; }
            int cols = 4, i = 0, itemId = ItemIdFor(s.Kind);
            float gap = 0.015f, cw = (1f - gap * (cols - 1)) / cols, ch = 0.28f;
            foreach (var g in groups.Take(12))
            {
                int col = i % cols, row = i / cols;
                float x0 = col * (cw + gap), y1 = 0.95f - row * (ch + gap);
                string tile = CuiHelper.GetGuid();
                Panel(c, UiGrid, tile, C.Tile, $"{x0} {y1 - ch}", $"{x0 + cw} {y1}");
                var sample = g.Take(3).ToList();
                for (int k = 0; k < sample.Count; k++)
                    Icon(c, tile, itemId, sample[k].Id, $"{0.04 + k * 0.31} 0.3", $"{0.04 + k * 0.31 + 0.29} 0.95");
                Label(c, tile, $"{g.Key}  <color=#9aa0a0>({g.Count()})</color>", 14, C.Text, TextAnchor.MiddleCenter, "0 0", "1 0.28", bold: true);
                Button(c, tile, $"{Cmd} collection {Uri.EscapeDataString(g.Key)}", "0 0 0 0", "0 0", "1 1");
                i++;
            }
        }

        private void BuildThemes(CuiElementContainer c, BasePlayer p, UiState s, global::WallpaperPlanner tool)
        {
            bool admin = IsAdmin(p);
            Label(c, UiGrid, Msg("ThemesHelp", p), 12, C.TextDim, TextAnchor.UpperLeft, "0 1", "1 1", "0 -36", "0 0");
            // save current selection
            int x = 0;
            if (admin)
            {
                x = Chips(c, UiGrid, "0 1", 0, -74, -44, 92, new[] { new KeyValuePair<string, string>("mine", Msg("ThemeMine", p)), new KeyValuePair<string, string>("server", Msg("ThemeServer", p)) }, s.ThemeToServer ? "server" : "mine", $"{Cmd} themeto", 11);
            }
            Input(c, UiGrid, $"{Cmd} savetheme", "", Msg("ThemeSaveHint", p), "0 1", "1 1", $"{x} -74", "0 -44");

            var themes = ThemesFor(p).Where(t => string.IsNullOrEmpty(s.Search) || t.Name.IndexOf(s.Search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (themes.Count == 0) { Label(c, UiGrid, Msg("NoThemes", p), 15, C.TextDim, TextAnchor.MiddleCenter, "0 0", "1 0.85"); return; }
            int cols = 3, rows = 3, per = cols * rows;
            int pages = Math.Max(1, (themes.Count + per - 1) / per);
            s.Page = Mathf.Clamp(s.Page, 0, pages - 1);
            float gap = 0.015f, cw = (1f - gap * (cols - 1)) / cols, top = 0.86f, ch = (top - 0.07f - gap * (rows - 1)) / rows;
            var shown = themes.Skip(s.Page * per).Take(per).ToList();
            for (int i = 0; i < shown.Count; i++)
            {
                var t = shown[i];
                int col = i % cols, row = i / cols;
                float x0 = col * (cw + gap), y1 = top - row * (ch + gap);
                bool loaded = tool != null && tool.wallSkinID == t.Wall && tool.flooringSkinID == t.Floor && tool.ceilingSkinID == t.Ceiling;
                string tile = CuiHelper.GetGuid();
                Panel(c, UiGrid, tile, loaded ? C.Accent : C.Tile, $"{x0} {y1 - ch}", $"{x0 + cw} {y1}");
                string inner = CuiHelper.GetGuid();
                Panel(c, tile, inner, C.Tile, "0 0", "1 1", loaded ? "3 3" : "0 0", loaded ? "-3 -3" : "0 0");
                for (int k = 0; k < 3; k++)
                {
                    var kind = Kinds[k];
                    ulong id = t.Get(kind);
                    string min = $"{0.03 + k * 0.32} 0.3", max = $"{0.03 + k * 0.32 + 0.30} 0.96";
                    if (id == 0) Label(c, inner, Msg(kind.ToString(), p) + "\n" + Msg("Plain", p), 10, C.TextFaint, TextAnchor.MiddleCenter, min, max);
                    else Icon(c, inner, ItemIdFor(kind), id, min, max);
                }
                Label(c, inner, Trim(t.Name, 28) + (t.Owner == 0 ? "  <color=#9aa0a0>· " + Msg("ThemeServer", p) + "</color>" : ""), 13, C.Text, TextAnchor.MiddleCenter, "0 0", "1 0.28", bold: true);
                Button(c, inner, $"{Cmd} loadtheme {Uri.EscapeDataString(t.Name)} {t.Owner}", "0 0 0 0", "0 0", "1 1");
                if (t.Owner == p.userID || (t.Owner == 0 && admin))
                    Button(c, inner, $"{Cmd} deltheme {Uri.EscapeDataString(t.Name)} {t.Owner}", "0 0 0 0.5", "1 1", "1 1", "✕", 11, C.Danger, "-24 -24", "-3 -3");
            }
            if (pages > 1)
            {
                Button(c, UiGrid, $"{Cmd} page {s.Page - 1}", C.PanelHi, "0.5 0", "0.5 0", "‹", 16, C.Text, "-106 0", "-68 28");
                Label(c, UiGrid, Msg("PageOf", p, s.Page + 1, pages), 13, C.Text, TextAnchor.MiddleCenter, "0.5 0", "0.5 0", "-62 0", "62 28");
                Button(c, UiGrid, $"{Cmd} page {s.Page + 1}", C.PanelHi, "0.5 0", "0.5 0", "›", 16, C.Text, "68 0", "106 28");
            }
        }

        // Right panel: fixed slots from the top (preview, name, meta) and from the bottom (actions), so nothing overlaps.
        private void BuildSide(CuiElementContainer c, BasePlayer p, UiState s, global::WallpaperPlanner tool)
        {
            Panel(c, UiFrame, UiSide, C.Panel, "1 0", "1 1", "-322 0", "0 -68");
            if (s.Tab == "browse" && s.BrowseSel != 0) { BuildBrowseSide(c, p, s); return; }
            ulong current = tool != null ? ToolSkin(tool, s.Kind) : 0;
            Label(c, UiSide, Msg("Selected", p, Msg(s.Kind.ToString(), p)).ToUpper(), 12, C.TextDim, TextAnchor.MiddleLeft, "0 1", "1 1", "16 -32", "-16 -8", bold: true);
            string box = CuiHelper.GetGuid();
            Panel(c, UiSide, box, C.Tile, "0 1", "1 1", "16 -252", "-16 -38");
            if (current == 0) Label(c, box, Msg("PlainTile", p), 16, C.TextDim, TextAnchor.MiddleCenter, "0 0", "1 1", bold: true);
            else Icon(c, box, ItemIdFor(s.Kind), current, "0.08 0.05", "0.92 0.95");
            Label(c, UiSide, Trim(NameOf(p, current, s.Kind), 34), 16, C.Text, TextAnchor.MiddleLeft, "0 1", "1 1", "16 -282", "-16 -258", bold: true);
            Wallpaper w;
            string meta = current == 0 ? Msg("PlainHelp", p) : byId.TryGetValue(current, out w) ? (w.Collection + (w.Tags.Count > 0 ? "  ·  " + string.Join(", ", w.Tags) : "")) : "";
            Label(c, UiSide, Trim(meta, 52), 12, C.TextDim, TextAnchor.MiddleLeft, "0 1", "1 1", "16 -302", "-16 -284");
            int priceItem; string priceName;
            int price = Price(out priceItem, out priceName);
            bool free = IsFree(p);
            Label(c, UiSide, free ? Msg("CostFree", p) : Msg("Cost", p, price, priceName), 11, C.TextFaint, TextAnchor.MiddleLeft, "0 1", "1 1", "16 -322", "-16 -304");

            // bottom-up action stack
            int y = 14;
            var hist = History(p);
            if (hist.Count > 0)
            {
                Button(c, UiSide, $"{Cmd} undo 1", C.PanelHi, "0 0", "0 0", Msg("Undo1", p), 12, C.Text, $"16 {y}", $"108 {y + 32}");
                Button(c, UiSide, $"{Cmd} undo 5", C.PanelHi, "0 0", "0 0", Msg("Undo5", p), 12, C.Text, $"114 {y}", $"206 {y + 32}");
                Button(c, UiSide, $"{Cmd} undo all", C.PanelHi, "0 0", "0 0", Msg("UndoAll", p), 12, C.Text, $"212 {y}", $"306 {y + 32}");
                Label(c, UiSide, Msg("UndoLabel", p, hist.Count), 11, C.TextDim, TextAnchor.MiddleLeft, "0 0", "1 0", $"16 {y + 34}", $"-16 {y + 50}");
                y += 58;
            }
            if (config.Bulk.Enabled && permission.UserHasPermission(p.UserIDString, PermBulk))
            {
                Button(c, UiSide, $"{Cmd} bulk", C.AccentDim, "0 0", "1 0", Msg("BtnBulk", p), 13, C.Text, $"16 {y}", $"-16 {y + 36}");
                y += 44;
            }
            Button(c, UiSide, $"{Cmd} copy", C.PanelHi, "0 0", "1 0", Msg("BtnCopy", p), 13, C.Text, $"16 {y}", $"-16 {y + 36}");
            y += 46;

            if (s.Tab == "mine" && config.Submissions.Enabled && permission.UserHasPermission(p.UserIDString, PermSubmit))
            {
                Input(c, UiSide, $"{Cmd} submit", "", Msg("SubmitBox", p), "0 0", "1 0", $"16 {y}", $"-16 {y + 32}", 13);
                Label(c, UiSide, Msg("SubmitHint", p, config.ChatCommands.FirstOrDefault() ?? "wp", Msg(s.Kind.ToString(), p)), 11, C.TextDim, TextAnchor.LowerLeft, "0 0", "1 0", $"16 {y + 36}", $"-16 {y + 86}");
            }
            else
            {
                Label(c, UiSide, Msg("Tips", p, ComboName(config.Hotkeys.Next), ComboName(config.Hotkeys.Copy), ComboName(config.Hotkeys.Strip), ChatCmd), 11, C.TextDim, TextAnchor.LowerLeft, "0 0", "1 0", $"16 {y}", $"-16 {y + 76}");
            }
        }

        private void BuildAdmin(CuiElementContainer c, BasePlayer p, UiState s)
        {
            var d = s.Draft ?? (s.Draft = new AdminDraft { Kind = s.Kind });
            bool edit = s.EditId != 0;
            Label(c, UiGrid, edit ? Msg("AdminEdit", p) : Msg("AdminAdd", p), 16, C.Accent, TextAnchor.MiddleLeft, "0 1", "0.5 1", "0 -30", "0 0", bold: true);
            Button(c, UiGrid, $"{Cmd} editmode", s.EditMode ? C.Accent : C.PanelHi, "1 1", "1 1", s.EditMode ? Msg("EditOn", p) : Msg("EditOff", p), 11, s.EditMode ? C.OnAccent : C.Text, "-190 -30", "0 -2");
            string[] labels = { Msg("FieldId", p), Msg("FieldName", p), Msg("FieldCollection", p), Msg("FieldTags", p) };
            string[] fields = { "id", "name", "collection", "tags" };
            string[] values = { d.Id, d.Name, d.Collection, d.Tags };
            for (int i = 0; i < 4; i++)
            {
                int top = -44 - i * 42;
                Label(c, UiGrid, labels[i], 12, C.TextDim, TextAnchor.MiddleLeft, "0 1", "0.24 1", $"0 {top - 30}", $"0 {top}");
                if (edit && i == 0) { Label(c, UiGrid, s.EditId.ToString(), 14, C.Text, TextAnchor.MiddleLeft, "0.24 1", "0.64 1", $"10 {top - 30}", $"0 {top}"); continue; }
                Input(c, UiGrid, $"{Cmd} draft {fields[i]}", values[i], "", "0.24 1", "0.64 1", $"0 {top - 30}", $"0 {top}");
            }
            Chips(c, UiGrid, "0.24 1", 0, -240, -212, 92, Kinds.Select(k => new KeyValuePair<string, string>(((int)k).ToString(), Msg(k.ToString(), p).ToUpper())), ((int)d.Kind).ToString(), $"{Cmd} draft kind", 12);
            Button(c, UiGrid, $"{Cmd} save", C.Accent, "0.24 1", "0.24 1", edit ? Msg("Save", p) : Msg("Add", p), 13, C.OnAccent, "0 -288", "140 -254");
            if (edit)
            {
                Button(c, UiGrid, $"{Cmd} delete {s.EditId}", C.Danger, "0.24 1", "0.24 1", Msg("Delete", p), 13, C.Text, "150 -288", "260 -254");
                Button(c, UiGrid, $"{Cmd} cancel", C.PanelHi, "0.24 1", "0.24 1", Msg("Cancel", p), 13, C.Text, "270 -288", "370 -254");
            }
            Label(c, UiGrid, Msg("AdminTips", p), 11, C.TextDim, TextAnchor.UpperLeft, "0.66 1", "1 1", "0 -290", "0 -40");

            var queue = players.SelectMany(kv => kv.Value.Mine.Where(m => m.Status == "pending").Select(m => new { Owner = kv.Key, Sub = m })).Take(5).ToList();
            Label(c, UiGrid, Msg("Queue", p, queue.Count), 14, C.Accent, TextAnchor.MiddleLeft, "0 0", "1 0", "0 196", "0 222", bold: true);
            for (int i = 0; i < queue.Count; i++)
            {
                var q = queue[i];
                int y = 156 - i * 38;
                string row = CuiHelper.GetGuid();
                Panel(c, UiGrid, row, C.Tile, "0 0", "1 0", $"0 {y}", $"0 {y + 34}");
                Icon(c, row, ItemIdFor(q.Sub.Kind), q.Sub.Id, "0 0", "0 1", "2 2", "34 -2");
                var ownerName = covalence.Players.FindPlayerById(q.Owner.ToString())?.Name ?? q.Owner.ToString();
                Label(c, row, $"{Trim(q.Sub.Name, 30)}  <color=#9aa0a0>{Msg(q.Sub.Kind.ToString(), p)} · {q.Sub.Id} · {ownerName} · Steam: {q.Sub.SteamCheck}</color>", 12, C.Text, TextAnchor.MiddleLeft, "0 0", "1 1", "42 0", "-330 0");
                Button(c, row, $"{Cmd} approve {q.Owner} {q.Sub.Id} 0", C.AccentDim, "1 0", "1 1", Msg("Approve", p), 11, C.Text, "-324 4", "-224 -4");
                Button(c, row, $"{Cmd} approve {q.Owner} {q.Sub.Id} 1", C.Accent, "1 0", "1 1", Msg("ApproveAdd", p), 11, C.OnAccent, "-218 4", "-88 -4");
                Button(c, row, $"{Cmd} deny {q.Owner} {q.Sub.Id}", C.Danger, "1 0", "1 1", Msg("Deny", p), 11, C.Text, "-82 4", "-4 -4");
            }
        }

        private void BuildBulkModal(CuiElementContainer c, BasePlayer p)
        {
            BulkPlan plan;
            if (!pendingPlan.TryGetValue(p.userID, out plan)) return;
            Panel(c, UiFrame, UiModal, "0 0 0 0.78", "0 0", "1 1");
            Button(c, UiModal, $"{Cmd} noop", "0 0 0 0", "0 0", "1 1");
            string box = CuiHelper.GetGuid();
            Panel(c, UiModal, box, C.Panel, "0.5 0.5", "0.5 0.5", "-300 -170", "300 170");
            Label(c, box, Msg("BulkTitle", p), 18, C.Accent, TextAnchor.MiddleCenter, "0 1", "1 1", "0 -44", "0 -10", bold: true);

            Label(c, box, Msg("BulkAction", p), 12, C.TextDim, TextAnchor.MiddleLeft, "0 1", "0 1", "24 -82", "130 -56");
            Chips(c, box, "0 1", 130, -82, -54, 140, new[] { new KeyValuePair<string, string>("paint", Msg("ActPaint", p)), new KeyValuePair<string, string>("strip", Msg("ActStrip", p)) }, plan.Action, $"{Cmd} bulkset action");
            Label(c, box, Msg("BulkSurface", p), 12, C.TextDim, TextAnchor.MiddleLeft, "0 1", "0 1", "24 -118", "130 -92");
            Chips(c, box, "0 1", 130, -118, -90, 104, new[] { "wall", "floor", "ceiling", "all" }.Select(k => new KeyValuePair<string, string>(k, Msg("Surf_" + k, p))), plan.SurfaceKey, $"{Cmd} bulkset surface");
            Label(c, box, Msg("BulkScope", p), 12, C.TextDim, TextAnchor.MiddleLeft, "0 1", "0 1", "24 -154", "130 -128");
            Chips(c, box, "0 1", 130, -154, -126, 140, new[] { new KeyValuePair<string, string>("level", Msg("ScopeLevel", p)), new KeyValuePair<string, string>("building", Msg("ScopeBuilding", p)) }, plan.Scope, $"{Cmd} bulkset scope");

            string summary;
            if (plan.Action == "strip")
                summary = Msg("StripBody", p, plan.Targets.Count, plan.Free ? Msg("Free", p) : $"{plan.Targets.Count * plan.CostAmount} {plan.CostName}");
            else
            {
                var what = plan.SurfaceKey == "all" ? Msg("ThemeLoaded", p) : $"<b>{NameOf(p, plan.Skins[plan.Kinds[0]], plan.Kinds[0])}</b>";
                string cost = plan.Free ? Msg("Free", p) : $"{plan.NewSurfaces * plan.CostAmount} {plan.CostName}";
                summary = Msg("PaintBody", p, what, plan.Targets.Count, plan.NewSurfaces, cost);
            }
            Label(c, box, summary, 13, C.Text, TextAnchor.MiddleCenter, "0 0", "1 1", "20 70", "-20 -164");
            bool any = plan.Targets.Count > 0;
            Button(c, box, any ? $"{Cmd} bulkgo" : $"{Cmd} noop", any ? C.Accent : C.PanelHi, "0.5 0", "0.5 0", plan.Action == "strip" ? Msg("GoStrip", p) : Msg("GoPaint", p), 14, any ? C.OnAccent : C.TextFaint, "-180 18", "-10 56");
            Button(c, box, $"{Cmd} bulkcancel", C.PanelHi, "0.5 0", "0.5 0", Msg("Cancel", p), 14, C.Text, "10 18", "180 56");
        }

        #endregion

        #region UI commands

        [ConsoleCommand(Cmd)]
        private void UiCommand(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null || !arg.HasArgs()) return;
            if (!permission.UserHasPermission(p.UserIDString, PermUse)) return;
            var s = State(p);
            var tool = HeldTool(p);
            bool admin = IsAdmin(p);
            string action = arg.GetString(0);
            string rest = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : "";

            switch (action)
            {
                case "noop": return;
                case "close": CloseUi(p); return;
                case "open": OpenUi(p); return;
                case "toggle": if (openUi.Contains(p.userID)) CloseUi(p); else OpenUi(p); return;
                case "auto":
                    Info(p.userID).AutoSwitch = !AutoSwitchOn(p); playersDirty = true;
                    Toast(p, AutoSwitchOn(p) ? Msg("AutoOn", p) : Msg("AutoOff", p));
                    Redraw(p, rail: true); return;

                case "tab":
                    s.Tab = arg.GetString(1, "all"); s.Page = 0; s.EditId = 0; s.Draft = null;
                    if (s.Tab != "all") s.Collection = "";
                    if (s.Tab == "admin" && !admin) s.Tab = "all";
                    Redraw(p, header: true, rail: true, grid: true, side: true); return;

                case "kind":
                    {
                        int k = arg.GetInt(1, 1);
                        if (k < 1 || k > 3) return;
                        s.Kind = (Kind)k; s.Page = 0;
                        if (tool != null) SetToolMode(tool, s.Kind);
                        Redraw(p, header: true, grid: true, side: true); return;
                    }

                case "search":
                    s.Search = rest.Trim(); s.Page = 0; s.BrowsePage = 1; s.BrowseSel = 0;
                    if (s.Tab == "admin") s.Tab = "all";
                    if (s.Tab == "browse") { Redraw(p, header: true, grid: true, side: true); return; }
                    Redraw(p, header: true, rail: true, grid: true); return;

                case "collection":
                    s.Collection = Uri.UnescapeDataString(rest.Trim()); s.Tab = "all"; s.Page = 0;
                    Redraw(p, rail: true, grid: true); return;

                case "page":
                    s.Page = Math.Max(0, arg.GetInt(1)); Redraw(p, grid: true); return;

                case "layout":
                    {
                        string l = arg.GetString(1);
                        if (l != "grid" && l != "list") return;
                        Info(p.userID).Layout = l; playersDirty = true; s.Page = 0;
                        Redraw(p, rail: true, grid: true); return;
                    }
                case "size":
                    {
                        string z = arg.GetString(1).ToUpper();
                        if (!GridSizes.ContainsKey(z)) return;
                        Info(p.userID).Size = z; playersDirty = true; s.Page = 0;
                        Redraw(p, rail: true, grid: true); return;
                    }

                case "apply":
                    {
                        if (tool == null) { Toast(p, Msg("HoldTool", p)); CloseUi(p); return; }
                        ulong id = arg.GetULong(1);
                        if (!MayUse(p, id, s.Kind)) { Toast(p, Msg("NotAllowed", p)); return; }
                        SetToolSkin(tool, s.Kind, id);
                        if (id != 0) PushRecent(p, s.Kind, id);
                        Toast(p, Msg("Selected1", p, NameOf(p, id, s.Kind)));
                        if (config.CloseOnPick) { CloseUi(p); return; }
                        Redraw(p, grid: true, side: true); return;
                    }

                case "fav":
                    {
                        ulong id = arg.GetULong(1);
                        var favs = Info(p.userID).Favourites;
                        if (!favs.Remove(id)) favs.Add(id);
                        playersDirty = true;
                        Redraw(p, grid: true); return;
                    }

                case "copy": Eyedropper(p); return;
                case "strip": StripOne(p); return;
                case "next": Cycle(p, false); return;
                case "prev": Cycle(p, true); return;
                case "hud":
                    Info(p.userID).Hud = !HudOn(p); playersDirty = true;
                    if (tool != null) ShowHud(p, tool);
                    return;
                case "bulk": OpenBulk(p); return;
                case "bulkset": BulkSet(p, arg.GetString(1), arg.GetString(2)); return;
                case "bulkgo": RunBulk(p); return;
                case "bulkcancel": pendingPlan.Remove(p.userID); CuiHelper.DestroyUi(p, UiModal); return;
                case "undo": Undo(p, ParseUndoCount(arg.GetString(1, "1"))); return;

                case "submit": Submit(p, rest.Trim(), s.Kind); return;
                case "unsubmit":
                    {
                        ulong id = arg.GetULong(1);
                        Info(p.userID).Mine.RemoveAll(m => m.Id == id); playersDirty = true;
                        Redraw(p, grid: true); return;
                    }

                case "bsort": s.BrowseSort = arg.GetString(1, "trend"); s.BrowsePage = 1; s.BrowseSel = 0; Redraw(p, grid: true, side: true); return;
                case "bpage": s.BrowsePage = Math.Max(1, arg.GetInt(1, 1)); s.BrowseSel = 0; Redraw(p, grid: true, side: true); return;
                case "bsel": s.BrowseSel = arg.GetULong(1); Redraw(p, grid: true, side: true); return;
                case "bcoll": s.BrowseCollection = rest.Trim(); return;
                case "badd":
                    {
                        ulong id = arg.GetULong(1);
                        var r = BrowseItem(id);
                        if (arg.GetString(2) == "server" && admin)
                        {
                            if (byId.ContainsKey(id)) { Toast(p, Msg("AlreadyListed", p)); return; }
                            var w = new Wallpaper { Id = id, Name = r != null ? r.Title : id.ToString(), Kind = s.Kind, Collection = s.BrowseCollection, AddedBy = p.userID };
                            catalogue.Items.Add(w); catalogueDirty = true; RebuildIndex();
                            Toast(p, Msg("AddedServer", p, w.Name, Msg(s.Kind.ToString(), p)));
                        }
                        else
                        {
                            Submit(p, id.ToString(), s.Kind, r != null ? r.Title : null, stayOnTab: true);
                        }
                        Redraw(p, rail: true, side: true); return;
                    }
                case "themeto":
                    s.ThemeToServer = admin && arg.GetString(1) == "server";
                    Redraw(p, grid: true); return;
                case "savetheme": SaveTheme(p, rest.Trim(), s.ThemeToServer && admin); return;
                case "loadtheme": LoadTheme(p, Uri.UnescapeDataString(arg.GetString(1)), arg.GetULong(2)); return;
                case "deltheme":
                    {
                        string name = Uri.UnescapeDataString(arg.GetString(1));
                        ulong owner = arg.GetULong(2);
                        if (owner == 0 && admin) { catalogue.Themes.RemoveAll(t => t.Name == name); catalogueDirty = true; }
                        else if (owner == p.userID) { Info(p.userID).Themes.RemoveAll(t => t.Name == name); playersDirty = true; }
                        Redraw(p, grid: true); return;
                    }
            }

            if (!admin) return;
            switch (action)
            {
                case "editmode": s.EditMode = !s.EditMode; Redraw(p, grid: true); return;
                case "edit":
                    {
                        Wallpaper w;
                        if (!byId.TryGetValue(arg.GetULong(1), out w)) return;
                        s.Tab = "admin"; s.EditId = w.Id;
                        s.Draft = new AdminDraft { Id = w.Id.ToString(), Name = w.Name, Collection = w.Collection, Tags = string.Join(", ", w.Tags), Kind = w.Kind };
                        Redraw(p, rail: true, grid: true); return;
                    }
                case "cancel": s.EditId = 0; s.Draft = null; Redraw(p, grid: true); return;
                case "draft":
                    {
                        var d = s.Draft ?? (s.Draft = new AdminDraft { Kind = s.Kind });
                        string field = arg.GetString(1);
                        string value = arg.Args.Length > 2 ? string.Join(" ", arg.Args.Skip(2)).Trim() : "";
                        switch (field)
                        {
                            case "id": d.Id = value; break;
                            case "name": d.Name = value; break;
                            case "collection": d.Collection = value; break;
                            case "tags": d.Tags = value; break;
                            case "kind": int k; if (int.TryParse(value, out k) && k >= 1 && k <= 3) { d.Kind = (Kind)k; Redraw(p, grid: true); } break;
                        }
                        return;
                    }
                case "save": AdminSave(p, s); return;
                case "delete":
                    {
                        ulong id = arg.GetULong(1);
                        if (catalogue.Items.RemoveAll(w => w.Id == id) > 0) { catalogueDirty = true; RebuildIndex(); Toast(p, Msg("Deleted", p, id)); }
                        s.EditId = 0; s.Draft = null; s.Tab = "all";
                        Redraw(p, rail: true, grid: true, side: true); return;
                    }
                case "approve":
                case "deny":
                    {
                        ulong owner = arg.GetULong(1), id = arg.GetULong(2);
                        PlayerInfo info;
                        if (!players.TryGetValue(owner, out info)) return;
                        var sub = info.Mine.FirstOrDefault(m => m.Id == id);
                        if (sub == null) return;
                        sub.Status = action == "approve" ? "approved" : "denied";
                        if (action == "approve" && arg.GetInt(3) == 1 && !byId.ContainsKey(id))
                        {
                            catalogue.Items.Add(new Wallpaper { Id = id, Name = sub.Name, Kind = sub.Kind, Collection = "Community", AddedBy = owner });
                            catalogueDirty = true; RebuildIndex();
                        }
                        playersDirty = true;
                        var ownerPlayer = BasePlayer.FindByID(owner);
                        if (ownerPlayer != null) Toast(ownerPlayer, Msg(action == "approve" ? "YourApproved" : "YourDenied", ownerPlayer, sub.Name));
                        Redraw(p, rail: true, grid: true); return;
                    }
            }
        }

        private void PushRecent(BasePlayer p, Kind kind, ulong id)
        {
            var info = Info(p.userID);
            List<ulong> list;
            if (!info.Recent.TryGetValue(kind, out list)) info.Recent[kind] = list = new List<ulong>();
            list.Remove(id); list.Insert(0, id);
            if (list.Count > config.RecentCount) list.RemoveRange(config.RecentCount, list.Count - config.RecentCount);
            playersDirty = true;
        }

        private void ChatCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse)) { Toast(player, Msg("NoPerm", player)); return; }
            string sub = args.Length > 0 ? args[0].ToLower() : "";
            switch (sub)
            {
                case "copy": Eyedropper(player); return;
                case "strip": StripOne(player); return;
                case "auto":
                    Info(player.userID).AutoSwitch = !AutoSwitchOn(player); playersDirty = true;
                    Toast(player, AutoSwitchOn(player) ? Msg("AutoOn", player) : Msg("AutoOff", player)); return;
                case "hud":
                    Info(player.userID).Hud = !HudOn(player); playersDirty = true;
                    if (HeldTool(player) != null) ShowHud(player, HeldTool(player));
                    return;
                case "paint": case "bulk": OpenBulk(player); return;
                case "undo": Undo(player, ParseUndoCount(args.Length > 1 ? args[1] : "1")); return;
                case "theme":
                    if (args.Length < 2) { player.ChatMessage(Msg("Usage", player, command)); return; }
                    SaveTheme(player, string.Join(" ", args.Skip(1)), false); return;
                case "add":
                    if (args.Length < 2) { player.ChatMessage(Msg("Usage", player, command)); return; }
                    Kind k = HeldTool(player) != null ? ToolKind(HeldTool(player)) : Kind.Wall;
                    if (args.Length > 2) Enum.TryParse(args[2], true, out k);
                    Submit(player, args[1], k);
                    return;
                case "help": player.ChatMessage(Msg("Usage", player, command)); player.ChatMessage(Msg("Binds", player)); return;
                default: OpenUi(player); return;
            }
        }

        #endregion

        #region Themes

        private void SaveTheme(BasePlayer p, string name, bool toServer)
        {
            var tool = HeldTool(p);
            if (tool == null) { Toast(p, Msg("HoldTool", p)); return; }
            name = (name ?? "").Trim();
            if (name.Length == 0) { Toast(p, Msg("ThemeNeedsName", p)); return; }
            if (name.Length > 40) name = name.Substring(0, 40);
            var list = toServer ? catalogue.Themes : Info(p.userID).Themes;
            if (!toServer && list.Count >= config.MaxThemes && !list.Any(t => t.Name == name)) { Toast(p, Msg("TooManyThemes", p, config.MaxThemes)); return; }
            list.RemoveAll(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            list.Add(new Theme { Name = name, Wall = tool.wallSkinID, Floor = tool.flooringSkinID, Ceiling = tool.ceilingSkinID, Owner = toServer ? 0 : p.userID });
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (toServer) catalogueDirty = true; else playersDirty = true;
            Toast(p, Msg("ThemeSaved", p, name));
            var s = State(p); s.Tab = "themes";
            Redraw(p, header: true, rail: true, grid: true);
        }

        private void LoadTheme(BasePlayer p, string name, ulong owner)
        {
            var tool = HeldTool(p);
            if (tool == null) { Toast(p, Msg("HoldTool", p)); return; }
            var t = (owner == 0 ? catalogue.Themes : Info(p.userID).Themes).FirstOrDefault(x => x.Name == name);
            if (t == null) return;
            foreach (var k in Kinds)
            {
                ulong id = t.Get(k);
                if (MayUse(p, id, k)) SetToolSkin(tool, k, id);
            }
            Toast(p, Msg("ThemeLoadedToast", p, t.Name));
            Redraw(p, grid: true, side: true);
        }

        #endregion

        #region Admin save / import

        private static readonly Regex WorkshopId = new Regex(@"(?:id=)?(\d{6,20})", RegexOptions.Compiled);

        private static bool ParseId(string text, out ulong id)
        {
            id = 0;
            if (string.IsNullOrEmpty(text)) return false;
            var m = WorkshopId.Match(text);
            return m.Success && ulong.TryParse(m.Groups[1].Value, out id) && id > 0;
        }

        private void AdminSave(BasePlayer p, UiState s)
        {
            var d = s.Draft;
            if (d == null) return;
            var tags = d.Tags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
            if (s.EditId != 0)
            {
                Wallpaper w;
                if (!byId.TryGetValue(s.EditId, out w)) return;
                if (!string.IsNullOrEmpty(d.Name)) w.Name = d.Name;
                w.Collection = d.Collection; w.Tags = tags; w.Kind = d.Kind;
                catalogueDirty = true; RebuildIndex();
                Toast(p, Msg("Saved", p, w.Name));
                s.EditId = 0; s.Draft = null;
                Redraw(p, grid: true, side: true);
                return;
            }
            // bulk paste: several IDs separated by spaces/commas all get the same collection and kind
            var ids = WorkshopId.Matches(d.Id).Cast<Match>().Select(m => ulong.Parse(m.Groups[1].Value)).Distinct().ToList();
            if (ids.Count == 0) { Toast(p, Msg("BadId", p)); return; }
            int added = 0;
            foreach (var id in ids)
            {
                if (byId.ContainsKey(id)) continue;
                var w = new Wallpaper { Id = id, Name = ids.Count == 1 && !string.IsNullOrEmpty(d.Name) ? d.Name : id.ToString(), Kind = d.Kind, Collection = d.Collection, Tags = tags, AddedBy = p.userID };
                catalogue.Items.Add(w);
                added++;
                if (string.IsNullOrEmpty(d.Name) || ids.Count > 1) LookupSteam(id, (title, check) => { if (!string.IsNullOrEmpty(title)) { w.Name = title; catalogueDirty = true; RebuildIndex(); } });
            }
            catalogueDirty = true; RebuildIndex();
            Toast(p, Msg("AddedN", p, added, ids.Count - added));
            s.Draft = new AdminDraft { Kind = d.Kind, Collection = d.Collection };
            Redraw(p, grid: true);
        }

        // Admins can also add from the server console or F1: wpstudio.add <link> [wall|floor|ceiling] [collection]
        [ConsoleCommand("wpstudio.add")]
        private void AddCommand(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (arg.Connection != null && (p == null || !IsAdmin(p))) return;
            if (!arg.HasArgs()) { arg.ReplyWith("wpstudio.add <id or link> [wall|floor|ceiling] [collection...]"); return; }
            ulong id;
            if (!ParseId(arg.GetString(0), out id)) { arg.ReplyWith("Not a Workshop ID or link"); return; }
            Kind k = Kind.Wall;
            if (arg.Args.Length > 1) Enum.TryParse(arg.GetString(1), true, out k);
            string collection = arg.Args.Length > 2 ? string.Join(" ", arg.Args.Skip(2)) : "";
            if (byId.ContainsKey(id)) { arg.ReplyWith("Already listed"); return; }
            var w = new Wallpaper { Id = id, Name = id.ToString(), Kind = k, Collection = collection, AddedBy = p != null ? p.userID : 0 };
            catalogue.Items.Add(w); catalogueDirty = true; RebuildIndex();
            LookupSteam(id, (title, check) => { if (!string.IsNullOrEmpty(title)) { w.Name = title; catalogueDirty = true; RebuildIndex(); } });
            arg.ReplyWith($"Added {id} as {k}");
        }

        // Bulk import from a plain list: oxide/data/WallpaperStudio/<file>.json
        // [ { "Id": 123, "Name": "Brick", "Kind": "Wall", "Collection": "Starter", "Tags": ["red"] }, ... ]
        // Kind is Wall, Floor or Ceiling (or 1/2/3). Existing IDs are skipped.
        [ConsoleCommand("wpstudio.importlist")]
        private void ImportListCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Player()?.UserIDString ?? "", PermAdmin)) return;
            string file = arg.GetString(0, "import");
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile($"{Name}/{file}")) { arg.ReplyWith($"Not found: oxide/data/{Name}/{file}.json"); return; }
            List<Wallpaper> list;
            try { list = Interface.Oxide.DataFileSystem.ReadObject<List<Wallpaper>>($"{Name}/{file}"); }
            catch (Exception e) { arg.ReplyWith($"Could not read {file}.json: {e.Message}"); return; }
            int added = 0, skipped = 0;
            foreach (var w in list ?? new List<Wallpaper>())
            {
                if (w == null || w.Id == 0 || byId.ContainsKey(w.Id) || !Enum.IsDefined(typeof(Kind), w.Kind)) { skipped++; continue; }
                if (string.IsNullOrEmpty(w.Name)) w.Name = w.Id.ToString();
                if (w.Tags == null) w.Tags = new List<string>();
                if (w.Collection == null) w.Collection = "";
                catalogue.Items.Add(w); byId[w.Id] = w;
                added++;
            }
            catalogueDirty = true; RebuildIndex(); SaveData();
            arg.ReplyWith($"Imported {added} wallpapers ({skipped} skipped: duplicates or invalid).");
        }

        [ConsoleCommand("wpstudio.export")]
        private void ExportCommand(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Player()?.UserIDString ?? "", PermAdmin)) return;
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}/export", catalogue);
            arg.ReplyWith($"Wrote {catalogue.Items.Count} wallpapers and {catalogue.Themes.Count} server themes to oxide/data/{Name}/export.json");
        }

        #endregion

        #region Steam lookup & submissions

        #region Workshop browser

        private class BrowseResult { public ulong Id; public string Title = "", Preview = "", Author = ""; public int Subs; public List<string> Tags = new List<string>(); }
        private class WorkshopPage { public float At; public int Total; public List<BrowseResult> Items = new List<BrowseResult>(); public bool Loading; public string Error; }

        private readonly Dictionary<string, WorkshopPage> browseCache = new Dictionary<string, WorkshopPage>();
        private readonly Dictionary<ulong, BrowseResult> browseItems = new Dictionary<ulong, BrowseResult>();

        private BrowseResult BrowseItem(ulong id) { BrowseResult r; return browseItems.TryGetValue(id, out r) ? r : null; }

        private bool BrowserReady => config.Browser.Enabled && !string.IsNullOrEmpty(config.Browser.ApiKey);

        private static int QueryType(string sort)
        {
            switch (sort)
            {
                case "top": return 0;    // RankedByVote
                case "new": return 1;    // RankedByPublicationDate
                case "subs": return 9;   // RankedByTotalUniqueSubscriptions
                default: return 3;       // RankedByTrend
            }
        }

        // Fetch (or reuse) one page of Workshop results for a surface/search/sort; redraws waiting players when it lands.
        private WorkshopPage GetBrowsePage(Kind kind, string search, string sort, int page)
        {
            string key = $"{kind}|{search.ToLower()}|{sort}|{page}";
            WorkshopPage cached;
            if (browseCache.TryGetValue(key, out cached) && (cached.Loading || UnityEngine.Time.realtimeSinceStartup - cached.At < config.Browser.CacheMinutes * 60f)) return cached;
            var bp = new WorkshopPage { Loading = true, At = UnityEngine.Time.realtimeSinceStartup };
            browseCache[key] = bp;
            var args = new List<string>
            {
                "key=" + Uri.EscapeDataString(config.Browser.ApiKey), "appid=252490", "creator_appid=252490",
                "numperpage=" + Mathf.Clamp(config.Browser.PerPage, 4, 50), "page=" + page, "return_tags=true", "return_previews=false"
            };
            List<string> tags;
            if (!config.Browser.Tags.TryGetValue(kind.ToString(), out tags)) tags = new List<string>();
            for (int i = 0; i < tags.Count; i++) args.Add($"requiredtags[{i}]=" + Uri.EscapeDataString(tags[i]));
            string text = (search ?? "").Trim();
            if (tags.Count == 0 && !string.IsNullOrEmpty(config.Browser.DefaultSearch)) text = (config.Browser.DefaultSearch + " " + text).Trim();
            if (text.Length > 0) { args.Add("search_text=" + Uri.EscapeDataString(text)); args.Add("query_type=" + (sort == "trend" ? 12 : QueryType(sort))); }
            else args.Add("query_type=" + QueryType(sort));
            string url = "https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/?" + string.Join("&", args);
            webrequest.Enqueue(url, null, (code, body) =>
            {
                bp.Loading = false; bp.At = UnityEngine.Time.realtimeSinceStartup;
                try
                {
                    if (code != 200) { bp.Error = code == 403 ? "Steam rejected the API key" : $"Steam returned {code}"; }
                    else
                    {
                        var r = JObject.Parse(body)["response"];
                        bp.Total = r?.Value<int?>("total") ?? 0;
                        var files = r?["publishedfiledetails"] as JArray;
                        if (files != null)
                            foreach (var f in files)
                            {
                                ulong id;
                                if (!ulong.TryParse(f.Value<string>("publishedfileid"), out id)) continue;
                                var item = new BrowseResult
                                {
                                    Id = id, Title = f.Value<string>("title") ?? id.ToString(), Preview = f.Value<string>("preview_url") ?? "",
                                    Subs = f.Value<int?>("subscriptions") ?? 0,
                                    Tags = (f["tags"] as JArray)?.Select(t => t.Value<string>("tag")).Where(t => t != null).ToList() ?? new List<string>()
                                };
                                bp.Items.Add(item); browseItems[id] = item;
                            }
                    }
                }
                catch { bp.Error = "Couldn't read Steam's reply"; }
                // never log the URL: it contains the API key
                foreach (var pl in BasePlayer.activePlayerList)
                {
                    UiState st;
                    if (openUi.Contains(pl.userID) && ui.TryGetValue(pl.userID, out st) && st.Tab == "browse") Redraw(pl, grid: true);
                }
            }, this, RequestMethod.GET, null, 15f);
            return bp;
        }

        private static void UrlImage(CuiElementContainer c, string parent, string url, string min, string max, string offMin = "0 0", string offMax = "0 0")
        {
            c.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiRawImageComponent { Url = url, Color = "1 1 1 1" },
                    new CuiRectTransformComponent { AnchorMin = min, AnchorMax = max, OffsetMin = offMin, OffsetMax = offMax }
                }
            });
        }

        private void BuildBrowse(CuiElementContainer c, BasePlayer p, UiState s)
        {
            if (!BrowserReady)
            {
                Label(c, UiGrid, IsAdmin(p) ? Msg("BrowseNoKeyAdmin", p) : Msg("BrowseNoKey", p), 14, C.TextDim, TextAnchor.MiddleCenter, "0.1 0.2", "0.9 0.8");
                return;
            }
            Chips(c, UiGrid, "0 1", 0, -28, 0, 110, new[] { "trend", "top", "subs", "new" }.Select(k => new KeyValuePair<string, string>(k, Msg("Sort_" + k, p))), s.BrowseSort, $"{Cmd} bsort", 11);
            Label(c, UiGrid, Msg("BrowseFor", p, Msg(s.Kind.ToString(), p), string.IsNullOrEmpty(s.Search) ? "" : $" · \"{s.Search}\""), 11, C.TextDim, TextAnchor.MiddleRight, "0.5 1", "1 1", "0 -28", "0 0");
            var page = GetBrowsePage(s.Kind, s.Search, s.BrowseSort, s.BrowsePage);
            if (page.Loading) { Label(c, UiGrid, Msg("BrowseLoading", p), 15, C.TextDim, TextAnchor.MiddleCenter, "0 0", "1 1"); return; }
            if (!string.IsNullOrEmpty(page.Error)) { Label(c, UiGrid, page.Error, 14, C.Danger, TextAnchor.MiddleCenter, "0 0", "1 1"); return; }
            if (page.Items.Count == 0) { Label(c, UiGrid, Msg("BrowseNone", p), 15, C.TextDim, TextAnchor.MiddleCenter, "0 0", "1 1"); return; }

            int cols = 4, rows = (Mathf.Clamp(config.Browser.PerPage, 4, 50) + 3) / 4;
            float gap = 0.012f, top = 0.94f, bottom = 0.06f;
            float cw = (1f - gap * (cols - 1)) / cols, ch = (top - bottom - gap * (rows - 1)) / rows;
            for (int i = 0; i < page.Items.Count && i < cols * rows; i++)
            {
                var r = page.Items[i];
                int col = i % cols, row = i / cols;
                float x0 = col * (cw + gap), y1 = top - row * (ch + gap);
                bool sel = r.Id == s.BrowseSel;
                bool listed = byId.ContainsKey(r.Id) || Info(p.userID).Mine.Any(m => m.Id == r.Id);
                string tile = CuiHelper.GetGuid();
                Panel(c, UiGrid, tile, sel ? C.Accent : C.Tile, $"{x0} {y1 - ch}", $"{x0 + cw} {y1}");
                string inner = CuiHelper.GetGuid();
                Panel(c, tile, inner, C.Tile, "0 0", "1 1", sel ? "3 3" : "0 0", sel ? "-3 -3" : "0 0");
                if (!string.IsNullOrEmpty(r.Preview)) UrlImage(c, inner, r.Preview, "0.04 0.24", "0.96 0.97");
                Panel(c, inner, CuiHelper.GetGuid(), "0 0 0 0.65", "0 0", "1 0.22");
                Label(c, inner, Trim(r.Title, 26), 11, C.Text, TextAnchor.MiddleCenter, "0 0.08", "1 0.22", "3 0", "-3 0", bold: true);
                Label(c, inner, Msg("Subs", p, r.Subs.ToString("N0")), 9, C.TextDim, TextAnchor.MiddleCenter, "0 0", "1 0.09");
                if (listed) Label(c, inner, Msg("Listed", p), 10, C.Gold, TextAnchor.UpperRight, "0 0.8", "1 0.97", "0 0", "-5 0", bold: true);
                Button(c, inner, $"{Cmd} bsel {r.Id}", "0 0 0 0", "0 0", "1 1");
            }
            int pages = Math.Max(1, (page.Total + config.Browser.PerPage - 1) / Math.Max(1, config.Browser.PerPage));
            if (s.BrowsePage > 1) Button(c, UiGrid, $"{Cmd} bpage {s.BrowsePage - 1}", C.PanelHi, "0.5 0", "0.5 0", "‹", 16, C.Text, "-106 0", "-68 26");
            Label(c, UiGrid, Msg("PageOf", p, s.BrowsePage, pages), 13, C.Text, TextAnchor.MiddleCenter, "0.5 0", "0.5 0", "-62 0", "62 26");
            if (s.BrowsePage < pages) Button(c, UiGrid, $"{Cmd} bpage {s.BrowsePage + 1}", C.PanelHi, "0.5 0", "0.5 0", "›", 16, C.Text, "68 0", "106 26");
            Label(c, UiGrid, Msg("BrowseTotal", p, page.Total.ToString("N0")), 11, C.TextDim, TextAnchor.MiddleLeft, "0 0", "0.3 0", "2 0", "0 26");
        }

        private void BuildBrowseSide(CuiElementContainer c, BasePlayer p, UiState s)
        {
            var r = BrowseItem(s.BrowseSel);
            if (r == null) return;
            Label(c, UiSide, Msg("BrowseSelected", p).ToUpper(), 12, C.TextDim, TextAnchor.MiddleLeft, "0 1", "1 1", "16 -32", "-16 -8", bold: true);
            string box = CuiHelper.GetGuid();
            Panel(c, UiSide, box, C.Tile, "0 1", "1 1", "16 -300", "-16 -38");
            if (!string.IsNullOrEmpty(r.Preview)) UrlImage(c, box, r.Preview, "0 0", "1 1", "4 4", "-4 -4");
            Label(c, UiSide, Trim(r.Title, 34), 15, C.Text, TextAnchor.MiddleLeft, "0 1", "1 1", "16 -328", "-16 -304", bold: true);
            Label(c, UiSide, Msg("Subs", p, r.Subs.ToString("N0")) + (r.Tags.Count > 0 ? "  ·  " + Trim(string.Join(", ", r.Tags), 40) : ""), 11, C.TextDim, TextAnchor.MiddleLeft, "0 1", "1 1", "16 -348", "-16 -330");
            Label(c, UiSide, Msg("BrowseWorkshopId", p, r.Id), 11, C.TextFaint, TextAnchor.MiddleLeft, "0 1", "1 1", "16 -366", "-16 -350");

            int y = 14;
            bool admin = IsAdmin(p);
            bool onServer = byId.ContainsKey(r.Id);
            bool mine = Info(p.userID).Mine.Any(m => m.Id == r.Id);
            if (admin)
            {
                Button(c, UiSide, onServer ? $"{Cmd} noop" : $"{Cmd} badd {r.Id} server", onServer ? C.PanelHi : C.Accent, "0 0", "1 0", onServer ? Msg("AlreadyListed", p) : Msg("BrowseAddServer", p, Msg(s.Kind.ToString(), p)), 13, onServer ? C.TextFaint : C.OnAccent, $"16 {y}", $"-16 {y + 36}");
                y += 42;
                Input(c, UiSide, $"{Cmd} bcoll", s.BrowseCollection, Msg("BrowseCollectionHint", p), "0 0", "1 0", $"16 {y}", $"-16 {y + 30}", 12);
                y += 40;
            }
            if (config.Submissions.Enabled && permission.UserHasPermission(p.UserIDString, PermSubmit))
            {
                Button(c, UiSide, mine ? $"{Cmd} noop" : $"{Cmd} badd {r.Id} mine", mine ? C.PanelHi : C.AccentDim, "0 0", "1 0", mine ? Msg("AlreadyMine", p) : Msg("BrowseAddMine", p, Msg(s.Kind.ToString(), p)), 13, mine ? C.TextFaint : C.Text, $"16 {y}", $"-16 {y + 36}");
                y += 44;
            }
            Label(c, UiSide, Msg("BrowseNote", p), 11, C.TextDim, TextAnchor.LowerLeft, "0 0", "1 0", $"16 {y}", $"-16 {y + 48}");
        }

        #endregion

        private void LookupSteam(ulong id, Action<string, string> done)
        {
            if (!config.Submissions.SteamLookup) { done(null, ""); return; }
            webrequest.Enqueue("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                $"itemcount=1&publishedfileids[0]={id}", (code, body) =>
                {
                    string title = null, check = "unknown";
                    try
                    {
                        if (code == 200)
                        {
                            var d = JObject.Parse(body)["response"]?["publishedfiledetails"]?[0];
                            int result = d?.Value<int?>("result") ?? 0;
                            if (result == 1)
                            {
                                check = d.Value<uint?>("consumer_app_id") == 252490 ? "ok" : "not a Rust item";
                                title = d.Value<string>("title");
                            }
                            else check = "hidden or removed";   // common for wallpaper items; they can still work
                        }
                    }
                    catch { check = "lookup failed"; }
                    done(title, check);
                }, this, RequestMethod.POST);
        }

        private void Submit(BasePlayer p, string text, Kind kind, string knownTitle = null, bool stayOnTab = false)
        {
            if (!config.Submissions.Enabled || !permission.UserHasPermission(p.UserIDString, PermSubmit)) { Toast(p, Msg("NoPerm", p)); return; }
            ulong id;
            if (!ParseId(text, out id)) { Toast(p, Msg("BadId", p)); return; }
            var info = Info(p.userID);
            if (info.Mine.Any(m => m.Id == id)) { Toast(p, Msg("AlreadyMine", p)); return; }
            if (info.Mine.Count >= config.Submissions.MaxPerPlayer) { Toast(p, Msg("TooMany", p, config.Submissions.MaxPerPlayer)); return; }
            bool trusted = !config.Submissions.RequireApproval || permission.UserHasPermission(p.UserIDString, PermTrusted) || IsAdmin(p);
            var sub = new Submission { Id = id, Name = string.IsNullOrEmpty(knownTitle) ? id.ToString() : knownTitle, Kind = kind, Status = trusted ? "approved" : "pending" };
            info.Mine.Add(sub); playersDirty = true;
            Toast(p, Msg(trusted ? "SubmittedOk" : "SubmittedPending", p, Msg(kind.ToString(), p)));
            LookupSteam(id, (title, check) =>
            {
                sub.SteamCheck = check;
                if (!string.IsNullOrEmpty(title)) sub.Name = title;
                if (check == "not a Rust item") { sub.Status = "denied"; Toast(p, Msg("NotRust", p)); }
                playersDirty = true;
                if (p != null && p.IsConnected) Redraw(p, grid: true);
                if (sub.Status == "pending")
                    foreach (var a in BasePlayer.activePlayerList.Where(IsAdmin))
                        Toast(a, Msg("NewSubmission", a, p.displayName));
            });
            if (openUi.Contains(p.userID) && !stayOnTab)
            {
                var s = State(p); s.Tab = "mine"; s.Kind = kind;
                Redraw(p, header: true, rail: true, grid: true, side: true);
            }
        }

        #endregion

        #region Eyedropper, strip, bulk, undo

        private BuildingBlock LookedAtBlock(BasePlayer p)
        {
            RaycastHit hit;
            if (!Physics.Raycast(p.eyes.HeadRay(), out hit, 8f, Rust.Layers.Mask.Construction)) return null;
            return hit.GetEntity() as BuildingBlock;
        }

        // Which side of a block faces the player: the one whose wallpaper socket they can see.
        private static int FacingSide(BuildingBlock b, BasePlayer p, bool preferWallpapered = true)
        {
            bool s0 = b.CanSeeWallpaperSocket(p, 0), s1 = b.CanSeeWallpaperSocket(p, 1);
            if (s0 && !s1) return 0;
            if (s1 && !s0) return 1;
            if (s0 && s1) return preferWallpapered && !b.HasWallpaper(0) ? 1 : 0;
            return -1;
        }

        private static Kind? KindOf(BuildingBlock b, int side)
        {
            int cat = (int)global::WallpaperPlanner.Settings.GetCategory(b, side);
            return cat >= 1 && cat <= 3 ? (Kind?)cat : null;
        }

        private void Eyedropper(BasePlayer p)
        {
            var tool = HeldTool(p);
            if (tool == null) { Toast(p, Msg("HoldTool", p)); return; }
            var b = LookedAtBlock(p);
            int side = b != null ? FacingSide(b, p) : -1;
            var kind = side >= 0 ? KindOf(b, side) : null;
            if (b == null || side < 0 || !b.HasWallpaper(side) || kind == null) { Toast(p, Msg("CopyNothing", p)); return; }
            ulong id = b.GetWallpaperSkin(side);
            SetToolMode(tool, kind.Value);
            SetToolSkin(tool, kind.Value, id);
            if (id != 0) PushRecent(p, kind.Value, id);
            State(p).Kind = kind.Value;
            Toast(p, Msg("Copied", p, NameOf(p, id, kind.Value), Msg(kind.Value.ToString(), p)));
            Redraw(p, header: true, grid: true, side: true);
        }

        private bool CanEdit(BasePlayer p, BuildingBlock b)
        {
            var priv = b.GetBuildingPrivilege();
            return priv != null ? priv.IsAuthed(p) : p.CanBuild();
        }

        private int Price(out int itemId, out string name)
        {
            var price = global::WallpaperPlanner.Settings.PlacementPrice;
            itemId = price.itemid; name = price.itemDef.displayName.english;
            return (int)price.amount;
        }

        // Strip the single surface the player is looking at (Crouch + R, or /wp strip). Refunds cloth like a pickup.
        private void StripOne(BasePlayer p)
        {
            var b = LookedAtBlock(p);
            int side = b != null ? FacingSide(b, p) : -1;
            if (b == null || side < 0 || !b.HasWallpaper(side)) { Toast(p, Msg("StripNothing", p)); return; }
            if (!CanEdit(p, b)) { Toast(p, Msg("NeedAuth", p)); return; }
            int item; string costName;
            int amount = Price(out item, out costName);
            var change = new Change { Block = b.net.ID, Side = side, Old = b.GetWallpaperSkin(side), OldRot = b.GetWallpaperRotation(side), Strip = true };
            b.RemoveWallpaper(side);
            bool free = IsFree(p);
            if (!free) GiveBack(p, item, amount);
            var r = new UndoRecord { At = UnityEngine.Time.realtimeSinceStartup, CostItem = item, Charge = free ? 0 : amount };
            r.Changes.Add(change);
            PushHistory(p, r);
            Toast(p, Msg("Stripped", p, 1));
            Redraw(p, side: true);
        }

        private static void GiveBack(BasePlayer p, int itemId, int amount)
        {
            if (amount <= 0) return;
            var item = ItemManager.CreateByItemID(itemId, amount);
            if (item != null) p.GiveItem(item);
        }

        private class Target { public BuildingBlock Block; public int Side; public Kind Kind; }

        private class BulkPlan
        {
            public string Action = "paint";       // paint | strip
            public string SurfaceKey = "wall";    // wall | floor | ceiling | all
            public string Scope = "level";        // level | building
            public BuildingBlock Ref; public float Base;
            public List<Kind> Kinds = new List<Kind>();
            public Dictionary<Kind, ulong> Skins = new Dictionary<Kind, ulong>();
            public List<Target> Targets = new List<Target>();
            public int NewSurfaces, CostItem, CostAmount; public string CostName; public bool Free;
        }

        private class Change { public NetworkableId Block; public int Side; public ulong Old; public float OldRot; public bool WasEmpty; public ulong New; public bool Strip; }
        // Refund = cloth to give back on undo (paid for new placements); Charge = cloth to take back on undo (refunded by a strip)
        private class UndoRecord { public float At; public List<Change> Changes = new List<Change>(); public int Refund; public int Charge; public int CostItem; }
        private class Job { public bool Cancelled; }

        private readonly Dictionary<ulong, BulkPlan> pendingPlan = new Dictionary<ulong, BulkPlan>();
        private readonly Dictionary<ulong, List<UndoRecord>> history = new Dictionary<ulong, List<UndoRecord>>();
        private readonly Dictionary<ulong, Job> jobs = new Dictionary<ulong, Job>();

        private List<UndoRecord> History(BasePlayer p)
        {
            List<UndoRecord> list;
            if (!history.TryGetValue(p.userID, out list)) history[p.userID] = list = new List<UndoRecord>();
            float now = UnityEngine.Time.realtimeSinceStartup;
            list.RemoveAll(r => now - r.At > config.Bulk.UndoSeconds);
            return list;
        }

        private void PushHistory(BasePlayer p, UndoRecord r)
        {
            var list = History(p);
            list.Add(r);
            if (list.Count > config.Bulk.HistorySize) list.RemoveRange(0, list.Count - config.Bulk.HistorySize);
        }

        private static int ParseUndoCount(string text)
        {
            if (string.Equals(text, "all", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
            int n;
            return int.TryParse(text, out n) && n > 0 ? n : 1;
        }

        // Record ordinary single placements made with the tool, so they can be undone too.
        private void CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var tool = planner as global::WallpaperPlanner;
            var b = target.entity as BuildingBlock;
            if (tool == null || b == null || target.socket == null) return;
            var p = tool.GetOwnerPlayer();
            if (p == null) return;
            int side = target.socket.socketName.EndsWith("1") ? 0 : 1;
            var change = new Change { Block = b.net.ID, Side = side, Old = b.GetWallpaperSkin(side), OldRot = b.GetWallpaperRotation(side), WasEmpty = !b.HasWallpaper(side) };
            int item; string costName;
            int amount = Price(out item, out costName);
            bool charged = change.WasEmpty && !(p.IsInCreativeMode && ConVar.Creative.freeBuild);
            NextTick(() =>
            {
                if (b == null || b.IsDestroyed || p == null || !b.HasWallpaper(side)) return;
                change.New = b.GetWallpaperSkin(side);
                if (!change.WasEmpty && change.New == change.Old && Mathf.Approximately(b.GetWallpaperRotation(side), change.OldRot)) return;
                if (charged && config.FreeSinglePlacements && IsFree(p))
                {
                    GiveBack(p, item, amount);   // free placement: hand the cloth straight back
                    charged = false;
                }
                var r = new UndoRecord { At = UnityEngine.Time.realtimeSinceStartup, CostItem = item, Refund = charged ? amount : 0 };
                r.Changes.Add(change);
                PushHistory(p, r);
                Redraw(p, side: true);
            });
        }

        // Floor height of the room the player is looking into: walls sit on it, floors are at it,
        // ceilings are the floor blocks one storey (3 m) above it.
        private float RoomBase(BuildingBlock b, BasePlayer p)
        {
            float y = b.transform.position.y;
            if (b.PrefabName.Contains("floor"))
            {
                int side = FacingSide(b, p, false);
                var k = side >= 0 ? KindOf(b, side) : null;
                if (k == Kind.Ceiling || p.eyes.position.y < y) return y - 3f;
            }
            return y;
        }

        private void OpenBulk(BasePlayer p)
        {
            if (!config.Bulk.Enabled || !permission.UserHasPermission(p.UserIDString, PermBulk)) { Toast(p, Msg("NoPerm", p)); return; }
            var tool = HeldTool(p);
            if (tool == null) { Toast(p, Msg("HoldTool", p)); return; }
            if (jobs.ContainsKey(p.userID)) { Toast(p, Msg("Busy", p)); return; }
            var b = LookedAtBlock(p);
            if (b == null || b.GetBuilding() == null) { Toast(p, Msg("LookAtBuilding", p)); return; }
            if (!CanEdit(p, b)) { Toast(p, Msg("NeedAuth", p)); return; }
            var plan = new BulkPlan { Ref = b, Base = RoomBase(b, p), SurfaceKey = State(p).Kind.ToString().ToLower() };
            pendingPlan[p.userID] = plan;
            if (!openUi.Contains(p.userID)) OpenUi(p);
            Recompute(p, plan);
        }

        private void BulkSet(BasePlayer p, string field, string value)
        {
            BulkPlan plan;
            if (!pendingPlan.TryGetValue(p.userID, out plan)) return;
            switch (field)
            {
                case "action": if (value == "paint" || value == "strip") plan.Action = value; break;
                case "surface": if (new[] { "wall", "floor", "ceiling", "all" }.Contains(value)) plan.SurfaceKey = value; break;
                case "scope": if (value == "level" || value == "building") plan.Scope = value; break;
            }
            Recompute(p, plan);
        }

        private void Recompute(BasePlayer p, BulkPlan plan)
        {
            var tool = HeldTool(p);
            if (tool == null || plan.Ref == null || plan.Ref.IsDestroyed) { pendingPlan.Remove(p.userID); CuiHelper.DestroyUi(p, UiModal); return; }
            var building = plan.Ref.GetBuilding();
            plan.Kinds = plan.SurfaceKey == "all" ? Kinds.ToList() : new List<Kind> { (Kind)Enum.Parse(typeof(Kind), plan.SurfaceKey, true) };
            plan.Skins = Kinds.ToDictionary(k => k, k => ToolSkin(tool, k));
            plan.CostAmount = Price(out plan.CostItem, out plan.CostName);
            plan.Free = IsFree(p);
            plan.Targets.Clear(); plan.NewSurfaces = 0;
            if (building != null)
                foreach (var block in building.buildingBlocks)
                {
                    if (block == null || block.IsDestroyed) continue;
                    for (int side = 0; side < 2; side++)
                    {
                        var k = KindOf(block, side);
                        if (k == null || !plan.Kinds.Contains(k.Value)) continue;
                        if (plan.Scope == "level")
                        {
                            float want = k.Value == Kind.Ceiling ? plan.Base + 3f : plan.Base;
                            if (Mathf.Abs(block.transform.position.y - want) > 1.0f) continue;
                        }
                        if (global::WallpaperPlanner.Settings.GetConstruction(block, side) == null) continue;
                        bool has = block.HasWallpaper(side);
                        if (plan.Action == "strip") { if (!has) continue; }
                        else if (has && block.GetWallpaperSkin(side) == plan.Skins[k.Value]) continue;
                        plan.Targets.Add(new Target { Block = block, Side = side, Kind = k.Value });
                        if (!has) plan.NewSurfaces++;
                        if (plan.Targets.Count >= config.Bulk.MaxSurfaces) break;
                    }
                    if (plan.Targets.Count >= config.Bulk.MaxSurfaces) break;
                }
            var c = new CuiElementContainer();
            BuildBulkModal(c, p);
            CuiHelper.AddUi(p, c);
        }

        private void RunBulk(BasePlayer p)
        {
            BulkPlan plan;
            if (!pendingPlan.TryGetValue(p.userID, out plan)) return;
            pendingPlan.Remove(p.userID);
            CuiHelper.DestroyUi(p, UiModal);
            if (plan.Targets.Count == 0) return;
            bool strip = plan.Action == "strip";
            if (!strip && !plan.Free && plan.NewSurfaces > 0 && p.inventory.GetAmount(plan.CostItem) < plan.CostAmount) { Toast(p, Msg("Broke", p, plan.CostAmount, plan.CostName)); return; }
            var record = new UndoRecord { At = UnityEngine.Time.realtimeSinceStartup, CostItem = plan.CostItem };
            var job = new Job(); jobs[p.userID] = job;
            int index = 0, done = 0, outside = 0, taken = 0, given = 0;
            bool broke = false;
            Action step = null;
            step = () =>
            {
                if (job.Cancelled || p == null || !p.IsConnected) { jobs.Remove(p.userID); return; }
                int n = 0;
                while (index < plan.Targets.Count && n < config.Bulk.PerFrame)
                {
                    var t = plan.Targets[index++]; n++;
                    var b = t.Block;
                    if (b == null || b.IsDestroyed) continue;
                    var change = new Change { Block = b.net.ID, Side = t.Side, Old = b.GetWallpaperSkin(t.Side), OldRot = b.GetWallpaperRotation(t.Side), WasEmpty = !b.HasWallpaper(t.Side) };
                    if (strip)
                    {
                        if (change.WasEmpty) continue;
                        change.Strip = true;
                        b.RemoveWallpaper(t.Side);
                        if (!plan.Free) given += plan.CostAmount;
                    }
                    else
                    {
                        if (change.WasEmpty && !plan.Free && p.inventory.GetAmount(plan.CostItem) < plan.CostAmount) { broke = true; index = plan.Targets.Count; break; }
                        change.New = plan.Skins[t.Kind];
                        b.SetWallpaper(change.New, t.Side, change.WasEmpty ? 0f : change.OldRot);
                        if (!config.AllowOutside) b.CheckWallpaper();      // the game removes it again if this face is outside
                        if (!b.HasWallpaper(t.Side))
                        {
                            outside++;
                            if (!change.WasEmpty) b.SetWallpaper(change.Old, t.Side, change.OldRot);
                            continue;
                        }
                        if (change.WasEmpty && !plan.Free) { p.inventory.Take(null, plan.CostItem, plan.CostAmount); taken += plan.CostAmount; }
                    }
                    record.Changes.Add(change);
                    done++;
                }
                if (index < plan.Targets.Count) { NextFrame(step); return; }
                jobs.Remove(p.userID);
                if (taken > 0) p.Command("note.inv", plan.CostItem, -taken);
                if (given > 0) GiveBack(p, plan.CostItem, given);
                record.Refund = taken; record.Charge = given;
                if (record.Changes.Count > 0) PushHistory(p, record);
                string msg = strip ? Msg("Stripped", p, done) : Msg(broke ? "PaintedBroke" : "Painted", p, done);
                Toast(p, msg + (outside > 0 ? " " + Msg("SkippedOutside", p, outside) : ""));
                Redraw(p, side: true);
            };
            step();
        }

        private void Undo(BasePlayer p, int count)
        {
            var list = History(p);
            if (list.Count == 0) { Toast(p, Msg("NothingToUndo", p)); return; }
            int actions = 0, restored = 0, skipped = 0, refund = 0, charge = 0, itemId = 0;
            while (list.Count > 0 && actions < count)
            {
                var u = list[list.Count - 1];
                list.RemoveAt(list.Count - 1);
                actions++;
                itemId = u.CostItem;
                int paidSurfaces = u.Changes.Count(c => c.WasEmpty && !c.Strip);
                int perPaid = paidSurfaces > 0 ? u.Refund / paidSurfaces : 0;
                int stripped = u.Changes.Count(c => c.Strip);
                int perStrip = stripped > 0 ? u.Charge / stripped : 0;
                for (int i = u.Changes.Count - 1; i >= 0; i--)
                {
                    var ch = u.Changes[i];
                    var b = BaseNetworkable.serverEntities.Find(ch.Block) as BuildingBlock;
                    if (b == null) { skipped++; continue; }
                    if (ch.Strip)
                    {
                        // put the stripped wallpaper back, unless someone has papered that face since
                        if (b.HasWallpaper(ch.Side)) { skipped++; continue; }
                        b.SetWallpaper(ch.Old, ch.Side, ch.OldRot);
                        charge += perStrip;
                    }
                    else
                    {
                        // leave it alone if someone has changed it since
                        if (!b.HasWallpaper(ch.Side) || b.GetWallpaperSkin(ch.Side) != ch.New) { skipped++; continue; }
                        if (ch.WasEmpty) { b.RemoveWallpaper(ch.Side); refund += perPaid; }
                        else b.SetWallpaper(ch.Old, ch.Side, ch.OldRot);
                    }
                    restored++;
                }
            }
            int net = refund - charge;
            if (net > 0) GiveBack(p, itemId, net);
            else if (net < 0) p.inventory.Take(null, itemId, Math.Min(-net, p.inventory.GetAmount(itemId)));
            Toast(p, Msg("Undone", p, actions, restored) + (skipped > 0 ? " " + Msg("UndoSkipped", p, skipped) : ""));
            Redraw(p, side: true);
        }

        #endregion

        #region Outside wallpaper (optional)

        // When AllowOutside is on, skip the game's check that strips wallpaper from outside faces.
        [AutoPatch]
        [HarmonyPatch(typeof(BuildingBlock), nameof(BuildingBlock.CheckWallpaper))]
        internal static class CheckWallpaperPatch
        {
            [HarmonyPrefix]
            internal static bool Prefix() => Instance == null || !Instance.config.AllowOutside;
        }

        #endregion

        #region Lang & toasts

        private static void Toast(BasePlayer p, string text)
        {
            if (p != null && p.IsConnected) p.ShowToast(GameTip.Styles.Blue_Normal, text, false);
        }

        private string Msg(string key, BasePlayer p, params object[] args)
        {
            var s = lang.GetMessage(key, this, p?.UserIDString);
            return args.Length > 0 ? string.Format(s, args) : s;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Hint"] = "Wallpaper Studio: {0} = menu · {1} = next · {2} = copy · {3} = strip · Help tab in the menu",
                ["NoPerm"] = "You don't have permission to do that",
                ["HoldTool"] = "Hold the Wallpaper Tool first",
                ["NotAllowed"] = "That wallpaper isn't available to you",
                ["Wall"] = "Wall", ["Floor"] = "Floor", ["Ceiling"] = "Ceiling",
                ["Plain"] = "Plain", ["PlainTile"] = "PLAIN\n<size=10>game default</size>",
                ["PlainHelp"] = "Rust's standard wallpaper, no custom design",
                ["SearchHint"] = "Search name, tag or collection, then Enter",
                ["Close"] = "✕  CLOSE",
                ["TabAll"] = "All", ["TabFavs"] = "Favourites", ["TabRecent"] = "Recent",
                ["TabCollections"] = "Collections", ["TabThemes"] = "Themes", ["TabMine"] = "My Wallpapers", ["TabAdmin"] = "Admin",
                ["Filtered"] = "Showing collection:\n<b>{0}</b>", ["ClearFilter"] = "Show everything",
                ["View"] = "VIEW", ["Grid"] = "GRID", ["List"] = "LIST", ["IconSize"] = "ICON SIZE",
                ["EmptyFavs"] = "No favourites yet.\nClick the star on any wallpaper.",
                ["EmptyRecent"] = "Nothing used recently.",
                ["EmptyMine"] = "You haven't added any wallpapers yet.",
                ["EmptySearch"] = "Nothing matches \"{0}\"",
                ["EmptyAll"] = "No wallpapers for this surface yet.",
                ["CollectionsHelp"] = "Collections are set up by the server admins. Click one to browse it.",
                ["NoCollections"] = "No collections for this surface yet.",
                ["ThemesHelp"] = "A theme is a matching wall + floor + ceiling. Click one to load all three into your tool, then use BULK > ALL 3 to do a whole room.",
                ["ThemeSaveHint"] = "Save what's in your tool now as a theme: type a name, then Enter",
                ["ThemeMine"] = "MINE", ["ThemeServer"] = "SERVER",
                ["NoThemes"] = "No themes yet. Pick a wall, floor and ceiling, then save them above.",
                ["ThemeNeedsName"] = "Give the theme a name",
                ["TooManyThemes"] = "You can save up to {0} themes",
                ["ThemeSaved"] = "Saved theme \"{0}\"",
                ["ThemeLoadedToast"] = "Loaded theme \"{0}\" into your tool",
                ["ThemeLoaded"] = "the wall, floor and ceiling in your tool",
                ["PageOf"] = "Page {0} / {1}", ["Count"] = "{0} wallpapers",
                ["Pending"] = "AWAITING APPROVAL", ["Denied"] = "DENIED",
                ["Selected"] = "In your tool: {0}", ["Selected1"] = "Selected: {0}",
                ["Tips"] = "Holding the tool:  {0} next  ·  {1} copy  ·  {2} strip\nOwn keys: F1 → <color=#c8e6a0>bind q wpstudio.ui toggle</color>\nEverything else: the <b>Help</b> tab or /{3} help",
                ["TabHelp"] = "Help",
                ["TabBrowse"] = "Browse Workshop",
                ["HelpBrowse"] = "<color=#8cc63f><b>BROWSE WORKSHOP</b></color>
Search the Steam Workshop without leaving the game: pick a surface, type in the search box, sort by trending, top rated, most used or newest, then click a result to preview it and add it.",
                ["BrowseNoKey"] = "The Workshop browser isn't set up on this server yet.",
                ["BrowseNoKeyAdmin"] = "To turn on the Workshop browser, add a Steam Web API key to the config\n(\"Workshop browser\" → \"Steam Web API key\"), then reload the plugin.\nGet one free at steamcommunity.com/dev/apikey.",
                ["Sort_trend"] = "TRENDING", ["Sort_top"] = "TOP RATED", ["Sort_subs"] = "MOST USED", ["Sort_new"] = "NEWEST",
                ["BrowseFor"] = "Searching the Steam Workshop for {0} wallpapers{1}",
                ["BrowseLoading"] = "Searching the Steam Workshop…",
                ["BrowseNone"] = "Nothing found. Try other words in the search box.",
                ["BrowseTotal"] = "{0} results",
                ["Subs"] = "{0} subscribers", ["Listed"] = "ADDED",
                ["BrowseSelected"] = "From the Workshop",
                ["BrowseWorkshopId"] = "Workshop ID {0}",
                ["BrowseAddServer"] = "ADD TO SERVER ({0})", ["BrowseAddMine"] = "ADD TO MY WALLPAPERS ({0})",
                ["BrowseCollectionHint"] = "Collection for server adds (optional), Enter",
                ["BrowseNote"] = "Adds it for the surface selected at the top. Only items Steam lists publicly show up here; you can still paste any link in My Wallpapers.",
                ["AlreadyListed"] = "ALREADY ON THE SERVER",
                ["AddedServer"] = "Added \"{0}\" to the server ({1})",
                ["HelpNoKey"] = "(no key on this server)",
                ["HelpOpen"] = "<color=#8cc63f><b>OPENING AND CLOSING</b></color>\nHold the Wallpaper Tool and press <b>{0}</b>, or type <b>/{1}</b>. Close it with ✕ or by clicking outside it. Rust doesn't send keys to the server while a menu is open, so to close it with a key, bind one yourself (see Key binds).",
                ["HelpPick"] = "<color=#8cc63f><b>PICKING</b></color>\nClick a wallpaper to load it into your tool, then place it as normal. WALL / FLOOR / CEILING switches the surface. ★ adds a favourite. <b>Plain</b> sets the tool back to Rust's standard wallpaper. Search covers names, tags and collections (press Enter). VIEW and ICON SIZE are saved for you.",
                ["HelpHotkeys"] = "<color=#8cc63f><b>HOTKEYS (while holding the tool)</b></color>\n<b>{0}</b>: next favourite (or recent if you have none)\n<b>{1}</b>: copy the wallpaper you're looking at\n<b>{2}</b>: strip the surface you're looking at (cloth back)",
                ["HelpHotkeysOff"] = "<color=#8cc63f><b>HOTKEYS</b></color>\nTurned off on this server. Use the buttons, chat commands or your own binds.",
                ["HelpAutoOn"] = "<color=#8cc63f><b>AUTO SURFACE: ON</b></color>\nThe tool switches to wall, floor or ceiling to match what you aim at. Turn it off in the left panel or with /{0} auto.",
                ["HelpAutoOff"] = "<color=#8cc63f><b>AUTO SURFACE: OFF</b></color>\nSwitch surfaces yourself, or turn auto on in the left panel or with /{0} auto.",
                ["HelpHud"] = "<color=#8cc63f><b>HOTBAR PANEL</b></color>\nThe panel next to your hotbar shows a picture of what the tool will place. Hide or show it with /{0} hud.",
                ["HelpCost"] = "<color=#8cc63f><b>COST</b></color>\nEvery placement costs <b>{0} {1}</b>, whatever the design. Stripping gives it back.",
                ["HelpCostFree"] = "<color=#8cc63f><b>COST</b></color>\nPlacing wallpaper is <b>free</b> for you on this server.",
                ["HelpUndo"] = "<color=#8cc63f><b>UNDO</b></color>\nEvery placement, strip and bulk job can be undone: your last {0} actions for {1} minutes. Use UNDO 1 / 5 / ALL in the side panel, or /{2} undo [number|all]. Cloth is settled automatically.",
                ["HelpBulk"] = "<color=#8cc63f><b>BULK PAINT / STRIP</b></color>\nLook at your building and press BULK. Choose PAINT or STRIP, which surfaces (ALL 3 uses the theme in your tool) and THIS LEVEL or WHOLE BUILDING. Up to {0} surfaces at a time. Outside faces are skipped.",
                ["HelpThemes"] = "<color=#8cc63f><b>THEMES</b></color>\nA matching wall + floor + ceiling. In the Themes tab, type a name to save what's in your tool (up to {0}), or use /{1} theme <name>. Click a theme to load all three.",
                ["HelpSubmit"] = "<color=#8cc63f><b>MY WALLPAPERS</b></color>\nAdd Steam Workshop wallpapers of your own (up to {1}): open My Wallpapers, paste a Workshop link or ID into the box and press Enter. Or use chat: /{0} add <link> [wall|floor|ceiling].",
                ["HelpSubmitApproval"] = "<color=#8cc63f><b>MY WALLPAPERS</b></color>\nAdd Steam Workshop wallpapers of your own (up to {1}): open My Wallpapers, paste a Workshop link or ID into the box and press Enter, or use chat: /{0} add <link> [wall|floor|ceiling]. An admin approves them first.",
                ["HelpTeam"] = "Your team can use each other's approved ones.",
                ["HelpBinds"] = "<color=#8cc63f><b>KEY BINDS (any key, set once in F1)</b></color>\nbind q wpstudio.ui toggle   <color=#9aa0a0>open/close</color>\nbind c wpstudio.ui copy   <color=#9aa0a0>copy</color>\nbind x wpstudio.ui strip   <color=#9aa0a0>strip</color>\nbind z \"wpstudio.ui undo 1\"   <color=#9aa0a0>undo</color>\nbind v wpstudio.ui next   <color=#9aa0a0>next</color>   ·   bind n wpstudio.ui bulk   <color=#9aa0a0>bulk</color>",
                ["HudKeys"] = "{0} next · {1} copy · {2} strip",
                ["Binds"] = "Key binds (F1 console, any key): bind q wpstudio.ui toggle | bind c wpstudio.ui copy | bind x wpstudio.ui strip | bind z \"wpstudio.ui undo 1\" | bind v wpstudio.ui next | bind b wpstudio.ui prev | bind n wpstudio.ui bulk",
                ["AutoOn"] = "AUTO SURFACE: ON", ["AutoOff"] = "AUTO SURFACE: OFF",
                ["ColName"] = "NAME", ["ColCollection"] = "COLLECTION · TAGS", ["ColFav"] = "★",
                ["Cost"] = "Each placement costs {0} {1} (same for every wallpaper)", ["CostFree"] = "Placements are free for you",
                ["BtnCopy"] = "COPY FROM WALL  (look at it)",
                ["BtnBulk"] = "BULK: PAINT / STRIP A ROOM",
                ["UndoLabel"] = "UNDO HISTORY: {0} action(s)", ["Undo1"] = "UNDO 1", ["Undo5"] = "UNDO 5", ["UndoAll"] = "UNDO ALL",
                ["SubmitBox"] = "Paste a Workshop link or ID, then Enter",
                ["SubmitHint"] = "Add your own {1} wallpaper: paste a Steam Workshop link or ID and press Enter.\nOr in chat: /{0} add <link>",
                ["AdminAdd"] = "ADD WALLPAPERS", ["AdminEdit"] = "EDIT WALLPAPER",
                ["EditOn"] = "EDIT MODE: ON (gears on tiles)", ["EditOff"] = "EDIT MODE: OFF",
                ["FieldId"] = "Workshop ID(s)", ["FieldName"] = "Name (blank = from Steam)", ["FieldCollection"] = "Collection", ["FieldTags"] = "Tags (comma separated)",
                ["AdminTips"] = "Press Enter after typing in each box.\n\nSeveral IDs at once add a batch with the same collection.\n\nLinks can be pasted straight into the ID box. From the console or F1:\nwpstudio.add <link> [wall|floor|ceiling] [collection]\n\nImport a list of IDs from oxide/data/WallpaperStudio/import.json:\nwpstudio.importlist",
                ["Save"] = "SAVE", ["Add"] = "ADD", ["Delete"] = "DELETE", ["Cancel"] = "CANCEL",
                ["Queue"] = "SUBMISSIONS AWAITING APPROVAL ({0})", ["Approve"] = "APPROVE", ["ApproveAdd"] = "APPROVE + ADD", ["Deny"] = "DENY",
                ["Saved"] = "Saved {0}", ["Deleted"] = "Deleted {0}", ["AddedN"] = "Added {0} ({1} already listed)",
                ["BadId"] = "That doesn't look like a Workshop link or ID",
                ["AlreadyMine"] = "You've already added that one", ["TooMany"] = "You can have up to {0} personal wallpapers",
                ["SubmittedOk"] = "Added to My Wallpapers ({0})", ["SubmittedPending"] = "Sent for approval ({0}). It appears in My Wallpapers once approved.",
                ["NotRust"] = "That Workshop item isn't a Rust item",
                ["NewSubmission"] = "{0} submitted a wallpaper for approval",
                ["YourApproved"] = "Your wallpaper \"{0}\" was approved", ["YourDenied"] = "Your wallpaper \"{0}\" was denied",
                ["CopyNothing"] = "Look at a wall, floor or ceiling that has wallpaper",
                ["Copied"] = "Copied {0} ({1})",
                ["StripNothing"] = "Look at a surface with wallpaper to strip it",
                ["Stripped"] = "Stripped {0} surface(s), cloth refunded",
                ["Busy"] = "Still working on the last job, hang on",
                ["LookAtBuilding"] = "Look at part of your building first",
                ["NeedAuth"] = "You need building privilege here",
                ["BulkTitle"] = "BULK PAINT / STRIP",
                ["BulkAction"] = "ACTION", ["BulkSurface"] = "SURFACES", ["BulkScope"] = "WHERE",
                ["ActPaint"] = "PAINT", ["ActStrip"] = "STRIP",
                ["Surf_wall"] = "WALLS", ["Surf_floor"] = "FLOORS", ["Surf_ceiling"] = "CEILINGS", ["Surf_all"] = "ALL 3",
                ["ScopeLevel"] = "THIS LEVEL", ["ScopeBuilding"] = "WHOLE BUILDING",
                ["PaintBody"] = "Apply {0} to {1} surfaces.\n{2} of them have no wallpaper yet. Cost: {3}\nOutside faces are skipped automatically.",
                ["StripBody"] = "Remove wallpaper from {0} surfaces.\nRefund: {1}",
                ["GoPaint"] = "PAINT", ["GoStrip"] = "STRIP",
                ["Free"] = "free",
                ["Broke"] = "You need {0} {1}",
                ["Painted"] = "Painted {0} surfaces", ["PaintedBroke"] = "Ran out of cloth after {0} surfaces",
                ["SkippedOutside"] = "({0} outside faces skipped)",
                ["NothingToUndo"] = "Nothing to undo", ["Undone"] = "Undid {0} action(s), restored {1} surfaces",
                ["UndoSkipped"] = "({0} skipped: changed or destroyed since)",
                ["Cycled"] = "{0}  ({1}/{2} {3})", ["CycleEmpty"] = "Star some {0} wallpapers to cycle through them with {1}",
                ["Usage"] = "/{0}: menu · /{0} add <link> [wall|floor|ceiling] · /{0} theme <name> · /{0} copy · /{0} strip · /{0} bulk · /{0} undo [n|all] · /{0} auto · /{0} hud",
            }, this);
        }

        #endregion
    }
}
