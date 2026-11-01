using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SpireLedger;

// When choosing cards (card rewards, "choose a card" screens, the shop), a small box under each offered
// card tells you, for the local player:
//   - how many copies are already in your deck (and how many upgraded);
//   - how many times you've played it this run;
//   - history: in how many of your past runs it was in your final deck and how many of those you won
//     (from the game's own run history files, so it covers runs played without the mod too), and how
//     often you played it per run (from Spire Ledger's run files, which count plays per card).
internal static class CardInsight
{
    private const int RefreshFrames = 20;

    private static CanvasLayer? _layer;
    private static readonly List<(Panel Panel, Label Label)> _cells = new();
    private static readonly List<(Control Node, CardModel Card, bool Above)> _cards = new();
    private static long _frame, _lastScan = -1000;

    private static Type? _rewardScreen, _chooseScreen, _cardHolder, _merchantCard, _ncard;
    private static System.Reflection.FieldInfo? _merchantCardNode;
    private static System.Reflection.PropertyInfo? _holderModel, _ncardModel;

    // ===== History (cached per session, rebuilt when the run changes) =====

    private sealed class Stat { public int Runs, Wins; public long Plays; public int PlayRuns; }
    private static Dictionary<string, Stat>? _history;
    private static ulong _historyFor;

    public static void InvalidateHistory() => _history = null;

    private static Dictionary<string, Stat> History(ulong localId)
    {
        if (_history != null && _historyFor == localId) return _history;
        var stats = new Dictionary<string, Stat>();
        Stat S(string id) => stats.TryGetValue(id, out var s) ? s : stats[id] = new Stat();
        try
        {
            // 1. The game's run history: final deck + result, every profile (normal and modded). The same run
            //    can be in several profiles (copied progress), so runs are counted once by start time + seed.
            var seen = new HashSet<string>();
            foreach (var file in GameHistoryFiles())
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(file)) is not JsonObject run) continue;
                    if ((bool?)run["was_abandoned"] ?? false) continue;
                    if (!seen.Add($"{run["start_time"]}_{run["seed"]}")) continue;
                    bool win = (bool?)run["win"] ?? false;
                    var me = (run["players"] as JsonArray)?.OfType<JsonObject>()
                        .FirstOrDefault(p => p["id"]?.ToString() == localId.ToString());
                    if (me?["deck"] is not JsonArray deck) continue;
                    foreach (var id in deck.OfType<JsonObject>().Select(c => Strip((string?)c["id"])).Where(i => i != null).Distinct())
                    {
                        var s = S(id!); s.Runs++; if (win) s.Wins++;
                    }
                }
                catch { /* skip a damaged file */ }
            }
            // 2. Spire Ledger's run files: plays per card for the local player.
            foreach (var run in RunLedger.LoadAll())
            {
                var players = (run["players"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new();
                int slot = players.FindIndex(p => (bool?)p["is_local"] ?? false);
                if (slot < 0) continue;
                var perRun = new Dictionary<string, long>();
                foreach (var c in (run["combats"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                    foreach (var p in (c["players"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                        if ((int?)p["slot"] == slot)
                            foreach (var (id, n) in RunLedger.ReadCardPlays(p["card_plays"]))
                                perRun[id] = (perRun.TryGetValue(id, out var cur) ? cur : 0) + n;
                foreach (var (id, n) in perRun) { var s = S(id); s.Plays += n; s.PlayRuns++; }
            }
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] card history failed: {ex.Message}"); }
        _historyFor = localId;
        return _history = stats;
    }

    private static string? Strip(string? id) => id == null ? null : id.StartsWith("CARD.") ? id.Substring(5) : id;

    // <user data>/<platform>/<account>/{,modded/}profileN/saves/history/*.run
    private static IEnumerable<string> GameHistoryFiles()
    {
        string scoped = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath(""));
        var dir = new DirectoryInfo(scoped.TrimEnd('/', '\\'));
        var account = dir.Parent?.Name == "modded" ? dir.Parent.Parent : dir.Parent;
        if (account == null || !account.Exists) yield break;
        foreach (var root in new[] { account.FullName, Path.Combine(account.FullName, "modded") })
            for (int p = 1; p <= 3; p++)
            {
                var h = Path.Combine(root, $"profile{p}", "saves", "history");
                if (!Directory.Exists(h)) continue;
                foreach (var f in Directory.GetFiles(h, "*.run").Where(f => f.EndsWith(".run", StringComparison.OrdinalIgnoreCase))) yield return f;
            }
    }

    // ===== Overlay =====

    // Called every frame by the mod's Tick, out of combat.
    public static void Tick(bool inCombat, Func<string, long> runPlays)
    {
        _frame++;
        try
        {
            if (inCombat || !RunManager.Instance.IsInProgress) { Hide(); return; }
            if (_frame - _lastScan >= RefreshFrames) { _lastScan = _frame; Scan(); }
            if (_cards.Count == 0) { Hide(); return; }

            var rs = RunManager.Instance.DebugOnlyGetState();
            Player? me = rs != null ? LocalContext.GetMe(rs) : null;
            if (me == null) { Hide(); return; }
            // Test hook for demo screenshots: read the card history as another player (a Steam id).
            ulong historyId = ulong.TryParse(System.Environment.GetEnvironmentVariable("SPIRELEDGER_HISTORY_NETID"), out var hid) ? hid : me.NetId;
            var history = History(historyId);

            if (_layer == null)
            {
                _layer = new CanvasLayer { Layer = 106, Name = "SpireLedgerCardInsight" };
                ((SceneTree)Engine.GetMainLoop()).Root.AddChild(_layer);
            }
            _layer.Visible = true;
            float sc = UiTheme.Scale(_layer.GetViewport().GetVisibleRect().Size);

            int i = 0;
            foreach (var (node, card, above) in _cards)
            {
                if (!GodotObject.IsInstanceValid(node) || !node.IsVisibleInTree()) continue;
                while (_cells.Count <= i)
                {
                    var panel = UiTheme.MakePanel(0.9f, 1f);
                    var label = UiTheme.MakeLabel(13, UiTheme.Cream);
                    if (GameAssets.Enabled && GameAssets.BodyFont is Font f) label.AddThemeFontOverride("font", f);
                    label.HorizontalAlignment = HorizontalAlignment.Center;
                    _layer.AddChild(panel); _layer.AddChild(label);
                    _cells.Add((panel, label));
                }
                var (pnl, lbl) = _cells[i++];
                lbl.Text = Describe(me, card, runPlays, history);
                lbl.AddThemeFontSizeOverride("font_size", Math.Max(9, (int)Math.Round(14 * sc)));
                var r = RectOf(node);
                var size = new Vector2(Math.Max(r.Size.X, lbl.GetMinimumSize().X + 16f * sc), lbl.GetMinimumSize().Y + 10f * sc);
                var pos = new Vector2(r.Position.X + (r.Size.X - size.X) / 2f, above ? r.Position.Y - size.Y - 8f * sc : r.End.Y + 8f * sc);
                pnl.Visible = lbl.Visible = true;
                pnl.Position = pos; pnl.Size = size;
                lbl.Position = pos + new Vector2(0f, 5f * sc); lbl.Size = new Vector2(size.X, 0f);
            }
            for (; i < _cells.Count; i++) _cells[i].Panel.Visible = _cells[i].Label.Visible = false;
        }
        catch { Hide(); }
    }

    private static string Describe(Player me, CardModel card, Func<string, long> runPlays, Dictionary<string, Stat> history)
    {
        string id = card.Id.Entry;
        var copies = me.Deck.Cards.Where(c => c.Id.Entry == id).ToList();
        int upgraded = copies.Count(c => c.IsUpgraded);
        string deck = copies.Count == 0 ? Loc.T("ci_not_in_deck")
            : upgraded > 0 ? Loc.T(upgraded == 1 ? "ci_in_deck_up1" : "ci_in_deck_up", copies.Count, upgraded) : Loc.T("ci_in_deck", copies.Count);
        var lines = new List<string> { deck };
        long now = runPlays(id);
        if (copies.Count > 0 || now > 0) lines.Add(Loc.T("ci_played_run", now));
        if (history.TryGetValue(id, out var h) && (h.Runs > 0 || h.PlayRuns > 0))
        {
            var parts = new List<string>();
            if (h.Runs > 0) parts.Add(Loc.T(h.Runs == 1 ? "ci_hist_runs1" : "ci_hist_runs", h.Runs, h.Wins, (int)Math.Round(100.0 * h.Wins / h.Runs)));
            if (h.PlayRuns > 0) parts.Add(Loc.T("ci_hist_plays", (h.Plays / (double)h.PlayRuns).ToString("0.#")));
            lines.Add(string.Join(" · ", parts));
        }
        else lines.Add(Loc.T("ci_hist_none"));
        return string.Join("\n", lines);
    }

    // Visible offered cards: holders inside the reward / choose-a-card screens, and shop cards.
    private static void Scan()
    {
        _cards.Clear();
        _rewardScreen ??= AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen");
        _chooseScreen ??= AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NChooseACardSelectionScreen");
        _cardHolder ??= AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Cards.Holders.NCardHolder");
        _merchantCard ??= AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantCard");
        _ncard ??= AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Cards.NCard");
        _holderModel ??= _cardHolder != null ? AccessTools.Property(_cardHolder, "CardModel") : null;
        _ncardModel ??= _ncard != null ? AccessTools.Property(_ncard, "Model") : null;
        _merchantCardNode ??= _merchantCard != null ? AccessTools.Field(_merchantCard, "_cardNode") : null;

        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        foreach (var screen in Find(root, t => t == _rewardScreen || t == _chooseScreen))
        {
            if (screen is not Control sc || !sc.IsVisibleInTree()) continue;
            foreach (var holder in Find(screen, t => _cardHolder != null && _cardHolder.IsAssignableFrom(t)))
                if (holder is Control hc && hc.IsVisibleInTree() && _holderModel?.GetValue(holder) is CardModel m)
                    _cards.Add((CardFrame(hc), m, false));
        }
        foreach (var slot in Find(root, t => t == _merchantCard))
        {
            if (slot is not Control sl || !sl.IsVisibleInTree()) continue;
            if (_merchantCardNode?.GetValue(slot) is Control cardNode && cardNode.IsVisibleInTree() && _ncardModel?.GetValue(cardNode) is CardModel m)
                _cards.Add((CardFrame(cardNode), m, true));
        }
    }

    // The card's visible frame (the NCard's "Frame" texture) — the holder's own rect is a small box at the
    // card's center, which put the info over the card text.
    private static Control CardFrame(Control holderOrCard)
        => holderOrCard.FindChild("Frame", true, false) as Control ?? holderOrCard;

    private static IEnumerable<Node> Find(Node node, Func<Type, bool> match)
    {
        foreach (var child in node.GetChildren())
        {
            if (match(child.GetType())) yield return child;
            foreach (var d in Find(child, match)) yield return d;
        }
    }

    private static Rect2 RectOf(Control c)
    {
        var xf = c.GetGlobalTransformWithCanvas();
        return new Rect2(xf.Origin, c.Size * xf.Scale);
    }

    private static void Hide()
    {
        if (_layer != null) _layer.Visible = false;
    }
}
