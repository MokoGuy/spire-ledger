using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace SpireLedger;

// Phase 5: a "Ledger" (Registre) button next to "Statistics" in the Compendium opens this full-screen
// view of the run history written by RunLedger: records across all runs, a paged list of runs, and the
// selected run's party totals, badges and fights.
//
// The button is a copy of the game's own Statistics button (same frame, font and hover animation),
// placed just above it. The screen is drawn on its own CanvasLayer like the other views, with
// poll-based mouse handling (GUI input doesn't reliably reach controls on these layers).
internal static class LedgerScreen
{
    private const int PageSize = 9;

    private static CanvasLayer? _layer;
    private static Control? _root;
    private static readonly List<(Rect2 Rect, Action OnClick)> _hits = new();
    private static List<JsonObject> _all = new();   // every recorded run, newest first
    private static List<JsonObject> _runs = new();  // _all filtered by the selected teammate
    private static string? _mate;                   // teammate key (sorted net ids) or null for all runs
    private static int _selected, _page;
    private static bool _mouseWasDown, _escWasDown;
    private static float _s = 1f;

    public static bool IsOpen => _layer != null && _layer.Visible;

    // ===== Compendium button =====

    public static void TryApplyHook()
    {
        try
        {
            var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NCompendiumSubmenu");
            var ready = t != null ? AccessTools.Method(t, "_Ready") : null;
            if (ready == null) { GD.Print("[SpireLedger] NCompendiumSubmenu._Ready not found; no Ledger button"); return; }
            new Harmony("com.spireledger.compendium").Patch(ready,
                postfix: new HarmonyMethod(typeof(LedgerScreen).GetMethod(nameof(CompendiumReadyPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
            GD.Print("[SpireLedger] compendium hook applied");
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] compendium hook skipped: {ex.Message}"); }
    }

    private static void CompendiumReadyPostfix(object __instance)
    {
        try
        {
            var field = AccessTools.Field(__instance.GetType(), "_statisticsButton");
            if (field?.GetValue(__instance) is not Control stats || stats.GetParent() is not Node parent) return;
            if (parent.HasNode("SpireLedgerButton")) return;
            // Duplicate without signals (14 = groups | scripts | use-instantiation) so it doesn't open Statistics.
            if (stats.Duplicate(14) is not Control button) return;
            button.Name = "SpireLedgerButton";
            parent.AddChild(button);
            button.Position = stats.Position - new Vector2(0f, stats.Size.Y + 24f);
            Callable.From(() => SetButtonLabel(button)).CallDeferred();
            if (button is NClickableControl clickable)
                clickable.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => Open()));
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] Ledger button failed: {ex.Message}"); }
    }

    private static void SetButtonLabel(Control button)
    {
        try
        {
            var label = button.GetNodeOrNull("Label");
            label?.Call("SetTextAutoSize", Loc.T("ledger"));
            if (button.GetNodeOrNull("Icon") is TextureRect icon) icon.Modulate = UiTheme.Gold;
        }
        catch { }
    }

    // ===== Screen =====

    public static void Open()
    {
        try
        {
            _all = RunLedger.LoadAll();
            _mate = null;
            _runs = _all;
            _selected = 0; _page = 0;
            if (_layer == null)
            {
                var tree = (SceneTree)Engine.GetMainLoop();
                _layer = new CanvasLayer { Layer = 120, Name = "SpireLedgerScreen" };
                tree.Root.AddChild(_layer);
            }
            _layer.Visible = true;
            _mouseWasDown = true; // ignore the click that opened us
            Rebuild();
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] Ledger open failed: {ex.Message}"); }
    }

    public static void Close()
    {
        if (_layer != null) _layer.Visible = false;
    }

    // Called every frame by the mod's Tick.
    public static void Tick()
    {
        if (!IsOpen || _root == null || ProgressImport.IsOpen) return; // the import dialog sits on top
        try
        {
            bool esc = Input.IsKeyPressed(Key.Escape);
            if (esc && !_escWasDown) { Close(); _escWasDown = true; return; }
            _escWasDown = esc;

            bool down = Input.IsMouseButtonPressed(MouseButton.Left);
            if (down && !_mouseWasDown)
            {
                var pos = _root.GetViewport().GetMousePosition();
                foreach (var (rect, onClick) in _hits.ToList())
                    if (rect.HasPoint(pos)) { onClick(); break; }
            }
            _mouseWasDown = down;
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] Ledger tick: {ex.Message}"); }
    }

    private static void Rebuild()
    {
        if (_layer == null) return;
        _root?.QueueFree();
        _hits.Clear();
        var vp = _layer.GetViewport().GetVisibleRect().Size;
        _s = UiTheme.Scale(vp);
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetSize(vp);
        _layer.AddChild(_root);

        var backdrop = new ColorRect { Color = new Color(0.04f, 0.05f, 0.08f, 1f) };
        _root.AddChild(backdrop);
        backdrop.SetSize(vp);

        float pad = Math.Max(32f, vp.X * 0.04f);
        var title = Text($"{Loc.T("ledger_title")}   v{ModVersion.Version}", 30, UiTheme.Gold, bold: true);
        title.Position = new Vector2(pad, pad * 0.7f);

        ImportButton(pad, vp);
        if (_runs.Count == 0)
        {
            Text(Loc.T("no_runs"), 18, UiTheme.Cream).Position = new Vector2(pad, pad * 0.7f + 70f * _s);
            BackButton(pad, vp);
            return;
        }

        float y = pad * 0.7f + 52f * _s;
        y = Teammates(pad, y, vp.X - pad * 2f) + 14f * _s;
        if (_mate != null) y = MateComparison(pad, y, vp.X - pad * 2f) + 14f * _s;
        y = Records(pad, y, vp.X - pad * 2f) + 18f * _s;

        float listW = (vp.X - pad * 3f) * 0.38f;
        float detailX = pad * 2f + listW;
        RunList(pad, y, listW, vp.Y - y - pad * 2.2f);
        RunDetail(detailX, y, vp.X - detailX - pad, vp.Y - y - pad * 2.2f, _runs[Math.Clamp(_selected, 0, _runs.Count - 1)]);
        BackButton(pad, vp);
    }

    // ----- Records across all runs -----

    private static float Records(float x, float y, float width)
    {
        long bestRun = 0, bestHit = 0, bestFight = 0;
        string runWho = "", hitWho = "", fightWho = "";
        foreach (var run in _runs)
        {
            var labels = Labels(run);
            var totals = Totals(run);
            for (int s = 0; s < totals.Length; s++)
                if (totals[s].Dealt > bestRun) { bestRun = totals[s].Dealt; runWho = $"{Label(labels, s)} · {When(run)}"; }
            foreach (var c in Combats(run))
                foreach (var p in Players(c))
                {
                    int slot = (int?)p["slot"] ?? 0;
                    long hit = (long?)p["biggest_hit"] ?? 0, dealt = (long?)p["dealt"] ?? 0;
                    if (hit > bestHit) { bestHit = hit; hitWho = $"{Label(labels, slot)} · {(string?)p["biggest_hit_source"]}"; }
                    if (dealt > bestFight) { bestFight = dealt; fightWho = $"{Label(labels, slot)} · {(string?)c["encounter"]}"; }
                }
        }
        var head = Text(Loc.T("records").ToUpperInvariant(), 16, UiTheme.Gold, bold: true);
        head.Position = new Vector2(x, y);
        var cells = new[] { (Loc.T("record_dealt"), bestRun, runWho), (Loc.T("record_fight"), bestFight, fightWho), (Loc.T("record_hit"), bestHit, hitWho) };
        float cellW = width / cells.Length, top = y + 26f * _s;
        for (int i = 0; i < cells.Length; i++)
        {
            float cx = x + i * cellW;
            Text(cells[i].Item1, 13, Dim, bold: true).Position = new Vector2(cx, top);
            Text(cells[i].Item2.ToString(), 26, UiTheme.Cream, numeric: true).Position = new Vector2(cx, top + 18f * _s);
            Text(cells[i].Item3, 13, Dim).Position = new Vector2(cx, top + 52f * _s);
        }
        return top + 74f * _s;
    }

    // ----- Paged run list -----

    private static void RunList(float x, float y, float width, float height)
    {
        int pages = Math.Max(1, (_runs.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);
        float rowH = Math.Min(64f * _s, (height - 40f * _s) / PageSize);
        for (int i = 0; i < PageSize; i++)
        {
            int idx = _page * PageSize + i;
            if (idx >= _runs.Count) break;
            var run = _runs[idx];
            float ry = y + i * rowH;
            var rect = new Rect2(x, ry, width, rowH - 6f * _s);
            var bg = new ColorRect { Color = idx == _selected ? new Color(UiTheme.Gold, 0.18f) : new Color(1f, 1f, 1f, 0.04f), Position = rect.Position, Size = rect.Size };
            _root!.AddChild(bg);
            int captured = idx;
            _hits.Add((rect, () => { _selected = captured; Rebuild(); }));

            var result = run["result"] as JsonObject;
            string outcome = result == null ? Loc.T("in_progress") : ((bool?)result["won"] ?? false) ? Loc.T("won") : Loc.T("lost");
            var outcomeColor = result == null ? Dim : ((bool?)result["won"] ?? false) ? new Color(0.45f, 0.85f, 0.45f) : UiTheme.Red;
            Text($"{When(run)} · {Mode(run)} · A{(int?)run["ascension"] ?? 0}", 15, UiTheme.Cream, bold: true).Position = new Vector2(x + 10f * _s, ry + 4f * _s);
            Text(string.Join(" + ", Labels(run)), 13, Dim).Position = new Vector2(x + 10f * _s, ry + 26f * _s);
            var o = Text(outcome, 14, outcomeColor, bold: true);
            o.Position = new Vector2(x + width - 140f * _s, ry + 4f * _s);
            Text(Loc.T("fights", Combats(run).Count), 13, Dim).Position = new Vector2(x + width - 140f * _s, ry + 26f * _s);
        }
        if (pages > 1)
        {
            float py = y + PageSize * rowH + 4f * _s;
            Arrow("◀", new Vector2(x, py), () => { _page = Math.Max(0, _page - 1); Rebuild(); });
            Text(Loc.T("page", _page + 1, pages), 15, UiTheme.Cream).Position = new Vector2(x + 50f * _s, py + 4f * _s);
            Arrow("▶", new Vector2(x + 130f * _s, py), () => { _page = Math.Min(pages - 1, _page + 1); Rebuild(); });
        }
    }

    // ----- Selected run -----

    private static void RunDetail(float x, float y, float width, float height, JsonObject run)
    {
        var labels = Labels(run);
        var result = run["result"] as JsonObject;
        string header = $"{Mode(run)} · {Loc.T("ascension", (int?)run["ascension"] ?? 0)} · {Loc.T("seed", (string?)run["seed"] ?? "?")}";
        if (result != null) header += $" · {(((bool?)result["won"] ?? false) ? Loc.T("won") : Loc.T("lost"))} · {Loc.T("floor", (int?)result["floor"] ?? 0)}";
        Text(header, 17, UiTheme.Gold, bold: true).Position = new Vector2(x, y);

        // Party totals over the run.
        var totals = Totals(run);
        string[] caps = { Loc.T("dealt"), Loc.T("debuffs"), Loc.T("block_given"), Loc.T("block"), Loc.T("taken"), Loc.T("healed"), Loc.T("cards"), Loc.T("energy"), Loc.T("biggest") };
        float nameW = width * 0.22f, cellW = (width - nameW) / caps.Length, capY = y + 32f * _s;
        for (int c = 0; c < caps.Length; c++) Text(caps[c], 11, Dim, bold: true).Position = new Vector2(x + nameW + c * cellW, capY);
        float ry = capY + 20f * _s;
        var badges = result?["badge_titles"] as JsonObject ?? result?["badges"] as JsonObject;
        var players = run["players"] as JsonArray;
        for (int s = 0; s < totals.Length; s++)
        {
            var color = UiTheme.Players[Math.Min(s, UiTheme.Players.Length - 1)];
            Text(Label(labels, s), 15, color, bold: true).Position = new Vector2(x, ry);
            long[] v = { totals[s].Dealt, totals[s].Effects, totals[s].BlockGiven, totals[s].Block, totals[s].Taken, totals[s].Healed, totals[s].Cards, totals[s].Energy, totals[s].Biggest };
            for (int c = 0; c < v.Length; c++)
            {
                bool best = v[c] > 0 && totals.Length > 1;
                for (int o = 0; o < totals.Length && best; o++)
                {
                    long ov = o == s ? -1 : c switch { 0 => totals[o].Dealt, 1 => totals[o].Effects, 2 => totals[o].BlockGiven, 3 => totals[o].Block, 4 => totals[o].Taken, 5 => totals[o].Healed, 6 => totals[o].Cards, 7 => totals[o].Energy, _ => totals[o].Biggest };
                    if (o != s && (c == 4 ? ov < v[c] : ov > v[c])) best = false;
                }
                Text(v[c].ToString(), 15, best ? UiTheme.Gold : UiTheme.Cream, numeric: true).Position = new Vector2(x + nameW + c * cellW, ry);
            }
            ry += 24f * _s;
            if (badges != null && players != null && s < players.Count)
            {
                string netId = (string?)players[s]?["net_id"] ?? "";
                var list = badges[netId] as JsonArray;
                string text = list == null || list.Count == 0 ? Loc.T("no_badge") : string.Join(", ", list.Select(b => (string?)b ?? ""));
                Text(Loc.T("badges_list", text), 12, Dim).Position = new Vector2(x + 12f * _s, ry - 2f * _s);
                ry += 20f * _s;
            }
        }

        // Fights, newest last; as many as fit.
        ry += 14f * _s;
        Text(Loc.T("fight").ToUpperInvariant(), 14, UiTheme.Gold, bold: true).Position = new Vector2(x, ry);
        ry += 24f * _s;
        var combats = Combats(run);
        float lineH = 21f * _s;
        int fit = Math.Max(0, (int)((y + height - ry) / lineH));
        int start = Math.Max(0, combats.Count - fit);
        for (int i = start; i < combats.Count; i++)
        {
            var c = combats[i];
            string per = string.Join("   ", Players(c).Select(p => $"{Short(labels, (int?)p["slot"] ?? 0)} {(long?)p["dealt"] ?? 0}"));
            Text($"{(int?)c["index"]}. {Loc.T("floor", (int?)c["floor"] ?? 0)} · {(string?)c["encounter"]} · {Loc.T("rounds_n", (int?)c["rounds"] ?? 0)}", 13, UiTheme.Cream).Position = new Vector2(x, ry);
            Text(per, 13, Dim, numeric: true).Position = new Vector2(x + width * 0.62f, ry);
            ry += lineH;
        }
    }

    // ===== Data helpers =====

    private struct Tot { public long Dealt, Effects, BlockGiven, Block, Taken, Healed, Cards, Energy, Biggest; }

    private static Tot[] Totals(JsonObject run)
    {
        int n = (run["players"] as JsonArray)?.Count ?? 1;
        var t = new Tot[Math.Max(1, n)];
        foreach (var c in Combats(run))
            foreach (var p in Players(c))
            {
                int s = (int?)p["slot"] ?? 0;
                if (s < 0 || s >= t.Length) continue;
                t[s].Dealt += (long?)p["dealt"] ?? 0;
                t[s].Effects += (long?)p["debuffs_applied"] ?? 0; // runs before 0.3 have no count: 0
                t[s].BlockGiven += (long?)p["block_given"] ?? 0;
                t[s].Block += (long?)p["block"] ?? 0;
                t[s].Taken += (long?)p["taken"] ?? 0;
                t[s].Healed += (long?)p["healed"] ?? 0;
                t[s].Cards += (long?)p["cards_played"] ?? 0;
                t[s].Energy += (long?)p["energy_spent"] ?? 0;
                t[s].Biggest = Math.Max(t[s].Biggest, (long?)p["biggest_hit"] ?? 0);
            }
        return t;
    }

    private static List<JsonObject> Combats(JsonObject run)
        => (run["combats"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();

    private static IEnumerable<JsonObject> Players(JsonObject combat)
        => (combat["players"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();

    private static List<string> Labels(JsonObject run)
    {
        var list = new List<string>();
        if (run["players"] is JsonArray ps)
            foreach (var p in ps.OfType<JsonObject>())
            {
                string character = (string?)p["character"] ?? "?";
                string? name = (string?)p["name"];
                string label = ps.Count > 1 && name != null ? $"{name} ({character})" : character;
                list.Add(label + (((bool?)p["is_local"] ?? false) && ps.Count > 1 ? $" · {Loc.T("you")}" : ""));
            }
        return list;
    }

    // ===== Teammates =====

    // The other players of a run, as a stable key (their net ids — Steam ids on Steam — sorted) and a
    // display name. Solo runs have no key.
    private static (string Key, string Name)? Mate(JsonObject run)
    {
        if (run["players"] is not JsonArray ps || ps.Count < 2) return null;
        var others = ps.OfType<JsonObject>().Where(p => !((bool?)p["is_local"] ?? false)).ToList();
        if (others.Count == 0) return null;
        string key = string.Join("+", others.Select(p => (string?)p["net_id"] ?? "?").OrderBy(x => x));
        string name = string.Join(" + ", others.Select(p => (string?)p["name"] ?? (string?)p["character"] ?? "?"));
        return (key, name);
    }

    // Filter chips: "All (N)" then one chip per teammate, most played first.
    private static float Teammates(float x, float y, float width)
    {
        var mates = _all.Select(Mate).Where(m => m != null).Select(m => m!.Value)
            .GroupBy(m => m.Key).Select(g => (g.Key, Name: g.Last().Name, Count: g.Count()))
            .OrderByDescending(m => m.Count).ToList();
        if (mates.Count == 0) return y - 14f * _s;
        float cx = x, h = 34f * _s;
        void Chip(string text, bool active, Action onClick)
        {
            float w = (text.Length * 9.5f + 28f) * _s;
            if (cx + w > x + width) return;
            var rect = new Rect2(cx, y, w, h);
            _root!.AddChild(new ColorRect { Color = active ? new Color(UiTheme.Gold, 0.3f) : new Color(1f, 1f, 1f, 0.07f), Position = rect.Position, Size = rect.Size });
            Text(text, 14, active ? UiTheme.Gold : UiTheme.Cream, bold: true).Position = new Vector2(cx + 14f * _s, y + 7f * _s);
            _hits.Add((rect, onClick));
            cx += w + 10f * _s;
        }
        Chip(Loc.T("all_runs", _all.Count), _mate == null, () => SetMate(null));
        foreach (var m in mates)
        {
            var key = m.Key;
            Chip(Loc.T("with_mate", m.Name, m.Count), _mate == key, () => SetMate(key));
        }
        return y + h;
    }

    private static void SetMate(string? key)
    {
        _mate = key;
        _runs = key == null ? _all : _all.Where(r => Mate(r)?.Key == key).ToList();
        _selected = 0; _page = 0;
        Rebuild();
    }

    // You vs the selected teammate over all your runs together: runs, wins, then per-run averages.
    private static float MateComparison(float x, float y, float width)
    {
        int runs = _runs.Count, wins = _runs.Count(r => (bool?)r["result"]?["won"] ?? false);
        var me = new Tot(); var mate = new Tot();
        string mateName = "";
        foreach (var run in _runs)
        {
            var t = Totals(run);
            var ps = (run["players"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
            for (int s = 0; s < t.Length && s < ps.Count; s++)
            {
                bool local = (bool?)ps[s]["is_local"] ?? false;
                ref Tot dst = ref local ? ref me : ref mate;
                dst.Dealt += t[s].Dealt; dst.Effects += t[s].Effects; dst.BlockGiven += t[s].BlockGiven;
                dst.Taken += t[s].Taken; dst.Healed += t[s].Healed;
                dst.Biggest = Math.Max(dst.Biggest, t[s].Biggest);
                if (!local) mateName = (string?)ps[s]["name"] ?? (string?)ps[s]["character"] ?? "?";
            }
        }
        Text(Loc.T("mate_summary", mateName, runs, wins), 16, UiTheme.Gold, bold: true).Position = new Vector2(x, y);
        string[] caps = { Loc.T("avg_dealt"), Loc.T("debuffs"), Loc.T("avg_given"), Loc.T("avg_taken"), Loc.T("biggest") };
        long Avg(long v) => runs > 0 ? (long)Math.Round((double)v / runs) : 0;
        long[] a = { Avg(me.Dealt), Avg(me.Effects), Avg(me.BlockGiven), Avg(me.Taken), me.Biggest };
        long[] b = { Avg(mate.Dealt), Avg(mate.Effects), Avg(mate.BlockGiven), Avg(mate.Taken), mate.Biggest };
        float nameW = width * 0.18f, cellW = (width - nameW) / caps.Length, top = y + 28f * _s;
        for (int c = 0; c < caps.Length; c++) Text(caps[c], 11, Dim, bold: true).Position = new Vector2(x + nameW + c * cellW, top);
        void Row(string label, long[] v, long[] other, Color color, float ry)
        {
            Text(label, 15, color, bold: true).Position = new Vector2(x, ry);
            for (int c = 0; c < v.Length; c++)
            {
                bool best = c == 3 ? v[c] < other[c] : v[c] > other[c];
                Text(v[c].ToString(), 15, best ? UiTheme.Gold : UiTheme.Cream, numeric: true).Position = new Vector2(x + nameW + c * cellW, ry);
            }
        }
        Row(Loc.T("you"), a, b, UiTheme.Players[0], top + 18f * _s);
        Row(mateName, b, a, UiTheme.Players[1], top + 40f * _s);
        return top + 62f * _s;
    }

    private static string Label(List<string> labels, int slot) => slot >= 0 && slot < labels.Count ? labels[slot] : $"P{slot + 1}";

    private static string Short(List<string> labels, int slot) => Loc.T("player_short", slot + 1);

    private static string When(JsonObject run)
    {
        long t = (long?)run["start_time"] ?? 0;
        return t > 0 ? DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime().ToString("dd/MM HH:mm") : "?";
    }

    private static string Mode(JsonObject run) => Loc.T("mode_" + ((string?)run["game_mode"] ?? "Standard"));

    // ===== Node helpers =====

    private static readonly Color Dim = new(UiTheme.Cream.R, UiTheme.Cream.G, UiTheme.Cream.B, 0.62f);

    private static Label Text(string text, int size, Color color, bool bold = false, bool numeric = false)
    {
        var l = UiTheme.MakeLabel(Math.Max(8, (int)Math.Round(size * _s)), color);
        if (GameAssets.Enabled)
        {
            var font = numeric ? GameAssets.NumericFont : bold ? GameAssets.KreonBold : GameAssets.BodyFont;
            if (font != null) l.AddThemeFontOverride("font", font);
        }
        l.Text = text;
        _root!.AddChild(l);
        return l;
    }

    private static void Arrow(string glyph, Vector2 pos, Action onClick)
    {
        var size = new Vector2(40f * _s, 32f * _s);
        _root!.AddChild(new ColorRect { Color = new Color(1f, 1f, 1f, 0.08f), Position = pos, Size = size });
        var l = Text(glyph, 18, UiTheme.Gold, bold: true);
        l.Position = pos + new Vector2(10f * _s, 2f * _s);
        _hits.Add((new Rect2(pos, size), onClick));
    }

    // Bottom-right: copy the unmodded progress into this modded profile (see ProgressImport).
    private static void ImportButton(float pad, Vector2 vp)
    {
        var counts = ProgressImport.Counts();
        if (counts == null) return;
        var size = new Vector2(380f * _s, 54f * _s);
        var pos = new Vector2(vp.X - pad - size.X, vp.Y - size.Y - pad * 0.6f);
        var panel = UiTheme.MakePanel(0.9f, _s);
        panel.Position = pos; panel.Size = size;
        _root!.AddChild(panel);
        var l = Text(Loc.T("import_button"), 16, UiTheme.Cream, bold: true);
        l.Position = pos + new Vector2(18f * _s, 14f * _s);
        _hits.Add((new Rect2(pos, size), () => ProgressImport.Open(fromLedger: true)));
    }

    private static void BackButton(float pad, Vector2 vp)
    {
        var size = new Vector2(150f * _s, 54f * _s);
        var pos = new Vector2(pad, vp.Y - size.Y - pad * 0.6f);
        var panel = UiTheme.MakePanel(0.9f, _s);
        panel.Position = pos; panel.Size = size;
        _root!.AddChild(panel);
        var l = Text($"← {Loc.T("back")}", 18, UiTheme.Gold, bold: true);
        l.Position = pos + new Vector2(18f * _s, 12f * _s);
        _hits.Add((new Rect2(pos, size), Close));
    }
}
