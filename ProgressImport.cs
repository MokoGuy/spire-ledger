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

    private static string StatePath => Path.Combine(OS.GetUserDataDir(), "SpireLedger", "import.json");

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

    // (normal profile unlocks, modded profile unlocks), or null when there is nothing to import.
    public static (int Vanilla, int Modded)? Counts()
    {
        if (!Modded() || !SaveManager.Instance.IsProfileInitialized) return null;
        var store = Store();
        if (store == null) return null;
        int p = Profile();
        string? vanilla = store.FileExists(VanillaPath(p)) ? store.ReadFile(VanillaPath(p)) : null;
        if (vanilla == null) return null;
        string? modded = store.FileExists(ModdedPath(p)) ? store.ReadFile(ModdedPath(p)) : null;
        return (Epochs(vanilla), Epochs(modded));
    }

    public static bool Import(out string message)
    {
        message = "";
        try
        {
            var store = Store();
            if (store == null || !Modded()) { message = "save store unavailable"; return false; }
            int p = Profile();
            string? vanilla = store.ReadFile(VanillaPath(p));
            if (string.IsNullOrWhiteSpace(vanilla)) { message = "no normal progress"; return false; }

            string? current = store.FileExists(ModdedPath(p)) ? store.ReadFile(ModdedPath(p)) : null;
            if (!string.IsNullOrEmpty(current))
            {
                var dir = Path.Combine(OS.GetUserDataDir(), "SpireLedger", "backups");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"modded-profile{p}-progress-{DateTime.Now:yyyyMMdd-HHmmss}.save"), current);
            }
            store.WriteFile(ModdedPath(p), vanilla);
            SaveManager.Instance.InitProgressData();
            GD.Print($"[SpireLedger] imported normal progress into modded profile {p} ({Epochs(vanilla)} epochs)");
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
            if (IsOpen) { HandleMouse(); return; }
            if (_checkedThisSession || runActive) return;
            var tree = (SceneTree)Engine.GetMainLoop();
            if (tree.Root.FindChild("MainMenu", true, false) is not Control menu || !menu.IsVisibleInTree()) return;
            _checkedThisSession = true;
            var c = Counts();
            if (c == null || c.Value.Vanilla <= c.Value.Modded || Declined(Profile())) return;
            ShowOffer(c.Value.Vanilla, c.Value.Modded, fromLedger: false);
        }
        catch { _checkedThisSession = true; }
    }

    // ===== Dialog =====

    public static void ShowOffer(int vanilla, int modded, bool fromLedger)
    {
        Build(Loc.T("import_title"), Loc.T("import_body", vanilla, modded), new List<(string, Action)>
        {
            (Loc.T("import_yes"), () =>
            {
                if (Import(out var err)) ShowDone();
                else Build(Loc.T("import_title"), Loc.T("import_failed", err), new() { (Loc.T("ok"), Close) });
            }),
            (Loc.T("import_later"), Close),
            (fromLedger ? Loc.T("cancel") : Loc.T("import_never"), () => { if (!fromLedger) SetDeclined(Profile()); Close(); }),
        });
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

    private static void Build(string title, string body, List<(string Label, Action OnClick)> buttons)
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

        var size = new Vector2(Math.Min(760f * _s, vp.X - 40f), 330f * _s);
        var pos = (vp - size) / 2f;
        var panel = UiTheme.MakePanel(0.97f, _s);
        panel.Position = pos; panel.Size = size;
        _root.AddChild(panel);

        float pad = 30f * _s;
        Label(title, 24, UiTheme.Gold, true, pos + new Vector2(pad, pad * 0.8f), size.X - pad * 2f);
        Label(body, 16, UiTheme.Cream, false, pos + new Vector2(pad, pad * 0.8f + 46f * _s), size.X - pad * 2f, wrap: true);

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
