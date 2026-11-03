using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace SpireLedger;

// One-click import of the unmodded progress into the modded save profile, for players who aren't
// going to run a script.
//
// With mods on, the game reads progress from "modded/profileN" instead of "profileN", so a first
// modded launch starts with nothing unlocked (no Daily Climb, characters, Ascension). On the main
// menu, when the normal profile has more unlocks (epochs) than the modded one, a small dialog offers
// to copy it; the Ledger screen has the same action. The copy goes through the game's own save store
// (ISaveStore.WriteFile), which also writes the Steam Cloud copy, so the startup sync keeps it. The
// previous modded progress is backed up under <user data>/SpireLedger/backups first. The game is then
// restarted to reload every unlock.
internal static class ProgressImport
{
    private static CanvasLayer? _layer;
    private static Control? _root;
    private static readonly List<(Rect2 Rect, Action OnClick)> _hits = new();
    private static bool _mouseWasDown, _checkedThisSession;
    private static float _s = 1f;

    private static string StatePath => Path.Combine(RunLedger.DataDir, "import.json");

    public static bool IsOpen => _layer != null && _layer.Visible;

    // ===== Data =====

    private static ISaveStore? Store()
    {
        try { return AccessTools.Field(typeof(SaveManager), "_saveStore")?.GetValue(SaveManager.Instance) as ISaveStore; }
        catch { return null; }
    }

    private static bool Modded()
    {
        try { return UserDataPathProvider.IsRunningModded; } catch { return false; }
    }

    private static int Profile() => SaveManager.Instance.CurrentProfileId;

    private static string VanillaPath(int profile) => $"profile{profile}/saves/progress.save";

    private static string ModdedPath(int profile) => ProgressSaveManager.GetProgressPathForProfile(profile);

    private static int Epochs(string? json)
    {
        try { return json != null && JsonNode.Parse(json)?["epochs"] is JsonArray a ? a.Count : 0; }
        catch { return 0; }
    }

    // Unlocks (epochs) in a normal or modded profile; -1 when that profile has no progress file.
    private static int Unlocks(bool modded, int profile)
    {
        var store = Store();
        string path = modded ? ModdedPath(profile) : VanillaPath(profile);
        if (store == null || !store.FileExists(path)) return -1;
        return Epochs(store.ReadFile(path));
    }

    // (normal profile unlocks, modded profile unlocks) for the current profile number, or null when not
    // running modded or there is no normal progress to import.
    public static (int Vanilla, int Modded)? Counts()
    {
        if (!Modded() || !SaveManager.Instance.IsProfileInitialized) return null;
        int p = Profile();
        int vanilla = Unlocks(false, p);
        return vanilla < 0 ? null : (vanilla, Math.Max(0, Unlocks(true, p)));
    }

    public static bool Import(int source, int target, out string message)
    {
        message = "";
        try
        {
            var store = Store();
            if (store == null || !Modded()) { message = "save store unavailable"; return false; }
            int p = target;
            string? vanilla = store.FileExists(VanillaPath(source)) ? store.ReadFile(VanillaPath(source)) : null;
            if (string.IsNullOrWhiteSpace(vanilla)) { message = "no normal progress"; return false; }

            string? current = store.FileExists(ModdedPath(p)) ? store.ReadFile(ModdedPath(p)) : null;
            if (!string.IsNullOrEmpty(current))
            {
                var dir = Path.Combine(OS.GetUserDataDir(), "SpireLedger", "backups");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"modded-profile{p}-progress-{DateTime.Now:yyyyMMdd-HHmmss}.save"), current);
            }
            store.WriteFile(ModdedPath(p), vanilla);
            // The active profile is reloaded now; another one is read when the player switches to it.
            if (p == Profile()) SaveManager.Instance.InitProgressData();
            GD.Print($"[SpireLedger] imported normal profile {source} into modded profile {p} ({Epochs(vanilla)} epochs)");
            return true;
        }
        catch (Exception ex) { message = ex.Message; GD.PrintErr($"[SpireLedger] progress import failed: {ex.Message}"); return false; }
    }

    private static bool Declined(int profile)
    {
        try { return File.Exists(StatePath) && JsonNode.Parse(File.ReadAllText(StatePath))?[$"declined_profile{profile}"]?.GetValue<bool>() == true; }
        catch { return false; }
    }

    private static void SetDeclined(int profile)
    {
        try
        {
            var o = File.Exists(StatePath) ? JsonNode.Parse(File.ReadAllText(StatePath)) as JsonObject ?? new JsonObject() : new JsonObject();
            o[$"declined_profile{profile}"] = true;
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, o.ToJsonString());
        }
        catch { }
    }

    // ===== Main-menu check (called each frame by the mod's Tick, cheap after the first look) =====

    public static void Tick(bool runActive)
    {
        try
        {
            MenuButtonTick();
            if (IsOpen) { HandleMouse(); return; }
            if (_checkedThisSession || runActive) return;
            var tree = (SceneTree)Engine.GetMainLoop();
            if (tree.Root.FindChild("MainMenu", true, false) is not Control menu || !menu.IsVisibleInTree()) return;
            _checkedThisSession = true;
            var c = Counts();
            if (c == null || c.Value.Vanilla <= c.Value.Modded || Declined(Profile())) return;
            Open(fromLedger: false);
        }
        catch { _checkedThisSession = true; }
    }

    // ===== Main-menu button under the profile selector =====

    // The Ledger is in the Compendium, which an empty modded profile hasn't unlocked yet, so the import
    // is also reachable from a small button right under "Profile N · click to change" on the main menu.
    // It follows that button's visibility (shown only on the root menu, hidden in submenus).
    private static CanvasLayer? _btnLayer;
    private static Panel? _btnPanel;
    private static Label? _btnLabel;
    private static Rect2 _btnRect;
    private static bool _btnMouseWasDown;
    private static Control? _profileButton;

    private static void MenuButtonTick()
    {
        bool show = false;
        try
        {
            if (Modded() && !IsOpen && !LedgerScreen.IsOpen && SaveManager.Instance.IsProfileInitialized)
            {
                var tree = (SceneTree)Engine.GetMainLoop();
                if (_profileButton == null || !GodotObject.IsInstanceValid(_profileButton))
                    _profileButton = tree.Root.FindChild("ChangeProfileButton", true, false) as Control;
                show = _profileButton != null && _profileButton.IsVisibleInTree();
            }
            if (!show)
            {
                if (_btnLayer != null) _btnLayer.Visible = false;
                // Keep the press state current while hidden, so the first click after it reappears counts.
                _btnMouseWasDown = Input.IsMouseButtonPressed(MouseButton.Left);
                return;
            }

            var tree2 = (SceneTree)Engine.GetMainLoop();
            if (_btnLayer == null)
            {
                _btnLayer = new CanvasLayer { Layer = 110, Name = "SpireLedgerImportButton" };
                tree2.Root.AddChild(_btnLayer);
                _btnPanel = UiTheme.MakePanel(0.85f, 1f);
                _btnLayer.AddChild(_btnPanel);
                _btnLabel = UiTheme.MakeLabel(16, UiTheme.Cream);
                if (GameAssets.Enabled && GameAssets.KreonBold is Font f) _btnLabel.AddThemeFontOverride("font", f);
                _btnLayer.AddChild(_btnLabel);
            }
            _btnLayer.Visible = true;
            var vp = _btnLayer.GetViewport().GetVisibleRect().Size;
            float s = UiTheme.Scale(vp);
            var r = _profileButton!.GetGlobalRect();
            _btnLabel!.Text = "⇩ " + Loc.T("import_menu_button");
            _btnLabel.AddThemeFontSizeOverride("font_size", Math.Max(10, (int)Math.Round(15 * s)));
            var size = new Vector2(_btnLabel.GetMinimumSize().X + 28f * s, 36f * s);
            _btnRect = new Rect2(new Vector2(r.Position.X, r.End.Y + 6f * s), size);
            var mouse = _btnLayer.GetViewport().GetMousePosition();
            bool hover = _btnRect.HasPoint(mouse);
            _btnPanel!.Position = _btnRect.Position; _btnPanel.Size = _btnRect.Size;
            _btnPanel.Modulate = hover ? new Color(1.25f, 1.2f, 1f) : Colors.White;
            _btnLabel.Position = _btnRect.Position + new Vector2(14f * s, 7f * s);
            _btnLabel.Modulate = hover ? UiTheme.Gold : Colors.White;

            bool down = Input.IsMouseButtonPressed(MouseButton.Left);
            if (down && !_btnMouseWasDown && hover) Open(fromLedger: true);
            _btnMouseWasDown = down;
        }
        catch { if (_btnLayer != null) _btnLayer.Visible = false; }
    }

    // ===== Dialog =====

    private static int _source = 1, _target = 1;
    private static bool _fromLedger;

    // Opens the import dialog with the current profile number as both source and target.
    public static void Open(bool fromLedger)
    {
        if (!Modded() || !SaveManager.Instance.IsProfileInitialized) return;
        _source = _target = Profile();
        _fromLedger = fromLedger;
        ShowOffer();
    }

    private static void ShowOffer()
    {
        int v = Unlocks(false, _source), m = Unlocks(true, _target);
        string Count(int n) => n < 0 ? Loc.T("profile_empty") : Loc.T("unlocks_n", n);
        var selectors = new List<(string Caption, string Value, Action Prev, Action Next)>
        {
            (Loc.T("import_source"), $"{Loc.T("profile_n", _source)} — {Count(v)}",
                () => { _source = _source == 1 ? 3 : _source - 1; ShowOffer(); }, () => { _source = _source % 3 + 1; ShowOffer(); }),
            (Loc.T("import_target"), $"{Loc.T("profile_n", _target)}{(_target == Profile() ? " · " + Loc.T("active") : "")} — {Count(m)}",
                () => { _target = _target == 1 ? 3 : _target - 1; ShowOffer(); }, () => { _target = _target % 3 + 1; ShowOffer(); }),
        };
        var buttons = new List<(string, Action)>();
        if (v >= 0)
            buttons.Add((Loc.T("import_yes"), () =>
            {
                if (Import(_source, _target, out var err)) ShowDone();
                else Build(Loc.T("import_title"), Loc.T("import_failed", err), new() { (Loc.T("ok"), Close) });
            }));
        buttons.Add((Loc.T("import_later"), Close));
        if (!_fromLedger) buttons.Add((Loc.T("import_never"), () => { SetDeclined(Profile()); Close(); }));
        Build(Loc.T("import_title"), Loc.T("import_body_short"), buttons, selectors);
    }

    private static void ShowDone()
    {
        Build(Loc.T("import_done_title"), Loc.T("import_done_body"), new List<(string, Action)>
        {
            (Loc.T("quit_game"), () => ((SceneTree)Engine.GetMainLoop()).Quit()),
            (Loc.T("ok"), Close),
        });
    }

    private static void Close()
    {
        if (_layer != null) _layer.Visible = false;
    }

    private static void Build(string title, string body, List<(string Label, Action OnClick)> buttons,
                              List<(string Caption, string Value, Action Prev, Action Next)>? selectors = null)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        if (_layer == null)
        {
            _layer = new CanvasLayer { Layer = 130, Name = "SpireLedgerImport" };
            tree.Root.AddChild(_layer);
        }
        _root?.QueueFree();
        _hits.Clear();
        _layer.Visible = true;
        _mouseWasDown = true;

        var vp = _layer.GetViewport().GetVisibleRect().Size;
        _s = UiTheme.Scale(vp);
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetSize(vp);
        _layer.AddChild(_root);
        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f) };
        _root.AddChild(dim);
        dim.SetSize(vp);

        int rows = selectors?.Count ?? 0;
        var size = new Vector2(Math.Min(760f * _s, vp.X - 40f), (330f + rows * 52f) * _s);
        var pos = (vp - size) / 2f;
        var panel = UiTheme.MakePanel(0.97f, _s);
        panel.Position = pos; panel.Size = size;
        _root.AddChild(panel);

        float pad = 30f * _s;
        Label(title, 24, UiTheme.Gold, true, pos + new Vector2(pad, pad * 0.8f), size.X - pad * 2f);
        Label(body, 16, UiTheme.Cream, false, pos + new Vector2(pad, pad * 0.8f + 46f * _s), size.X - pad * 2f, wrap: true);

        // Profile pickers: caption, then ◀ value ▶ (arrows cycle profiles 1-3).
        float sy = pos.Y + 190f * _s;
        foreach (var (caption, value, prev, next) in selectors ?? new())
        {
            Label(caption, 14, new Color(UiTheme.Cream, 0.65f), true, new Vector2(pos.X + pad, sy + 10f * _s), 230f * _s);
            float ax = pos.X + pad + 240f * _s, aw = 40f * _s, ah = 40f * _s;
            Arrow("◀", new Vector2(ax, sy), aw, ah, prev);
            Label(value, 16, UiTheme.Cream, true, new Vector2(ax + aw + 12f * _s, sy + 8f * _s), size.X - pad - (ax - pos.X) - aw * 2f - 24f * _s);
            Arrow("▶", new Vector2(pos.X + size.X - pad - aw, sy), aw, ah, next);
            sy += 52f * _s;
        }

        float bw = (size.X - pad * 2f - (buttons.Count - 1) * 14f * _s) / buttons.Count, bh = 50f * _s;
        for (int i = 0; i < buttons.Count; i++)
        {
            var bp = pos + new Vector2(pad + i * (bw + 14f * _s), size.Y - bh - pad * 0.8f);
            var bg = new ColorRect { Color = i == 0 ? new Color(UiTheme.Gold, 0.28f) : new Color(1f, 1f, 1f, 0.08f), Position = bp, Size = new Vector2(bw, bh) };
            _root.AddChild(bg);
            var l = Label(buttons[i].Label, 17, i == 0 ? UiTheme.Gold : UiTheme.Cream, true, bp, bw);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.Position = bp + new Vector2(0f, 12f * _s);
            _hits.Add((new Rect2(bp, new Vector2(bw, bh)), buttons[i].OnClick));
        }
    }

    private static Label Label(string text, int size, Color color, bool bold, Vector2 pos, float width, bool wrap = false)
    {
        var l = UiTheme.MakeLabel(Math.Max(8, (int)Math.Round(size * _s)), color);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(width, 0f); }
        if (GameAssets.Enabled && (bold ? GameAssets.KreonBold : GameAssets.BodyFont) is Font f) l.AddThemeFontOverride("font", f);
        l.Text = text;
        _root!.AddChild(l);
        l.Position = pos;
        l.Size = new Vector2(width, 0f);
        return l;
    }

    private static void Arrow(string glyph, Vector2 p, float w, float h, Action onClick)
    {
        _root!.AddChild(new ColorRect { Color = new Color(1f, 1f, 1f, 0.08f), Position = p, Size = new Vector2(w, h) });
        var l = Label(glyph, 18, UiTheme.Gold, true, p + new Vector2(0f, 6f * _s), w);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        _hits.Add((new Rect2(p, new Vector2(w, h)), onClick));
    }

    private static void HandleMouse()
    {
        if (_root == null) return;
        bool down = Input.IsMouseButtonPressed(MouseButton.Left);
        if (down && !_mouseWasDown)
        {
            var p = _root.GetViewport().GetMousePosition();
            foreach (var (rect, onClick) in _hits.ToArray())
                if (rect.HasPoint(p)) { onClick(); break; }
        }
        _mouseWasDown = down;
    }
}
