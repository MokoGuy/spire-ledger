using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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
    private static Task<Dictionary<string, Stat>>? _pending;
    private static ulong _pendingFor;

    public static void InvalidateHistory() { _history = null; _pending = null; GameHistory.Invalidate(); }

    // The history is built on a worker thread (it reads every run file), started as soon as a run is in
    // progress so it is ready by the first card choice; null until then (the box shows no history line).
    private static Dictionary<string, Stat>? History(ulong localId)
    {
        if (_history != null && _historyFor == localId) return _history;
        if (_pending == null || _pendingFor != localId)
        {
            // Paths come from Godot / the game's SaveManager: resolve them here, on the main thread.
            var files = GameHistory.HistoryFiles();
            string runsDir = RunLedger.RunsDir;
            _pendingFor = localId;
            _pending = Task.Run(() => Build(localId, files, runsDir));
        }
        if (!_pending.IsCompleted) return null;
        _history = _pending.IsCompletedSuccessfully ? _pending.Result : new Dictionary<string, Stat>();
        _historyFor = localId;
        _pending = null;
        return _history;
    }

    private static Dictionary<string, Stat> Build(ulong localId, List<string> files, string runsDir)
    {
        var stats = new Dictionary<string, Stat>();
        Stat S(string id) => stats.TryGetValue(id, out var s) ? s : stats[id] = new Stat();
        try
        {
            // 1. The game's run history: final deck + result, every profile (normal and modded).
            foreach (var run in GameHistory.For(GameHistory.Load(files), localId))
                foreach (var id in run.Deck.Select(c => c.Id).Distinct())
                {
                    var s = S(id); s.Runs++; if (run.Win) s.Wins++;
                }
            // 2. Spire Ledger's run files: plays per card for the local player.
            foreach (var run in RunLedger.LoadAll(runsDir))
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
        return stats;
    }

    // ===== Overlay =====

    // Shown unless turned off (I by default, saved in the config as "card_insight").
    public static bool Enabled = true;

    // Where each card's box was last drawn from: the card's resting center and how many frames it has
    // been still. A card that leaves its resting spot (picked, flying to the deck) hides its box.
    private static readonly Dictionary<ulong, (Vector2 Home, Vector2 Last, int Still)> _motion = new();

    // Called every frame by the mod's Tick. `menuOpen`: a game menu (deck view, settings, popup) is on top;
    // `topOverlay`: the top screen of the game's overlay stack, if any — a card under another screen is
    // not shown.
    public static void Tick(bool inCombat, Func<string, long> runPlays, bool menuOpen, Node? topOverlay)
    {
        _frame++;
        try
        {
            if (!RunManager.Instance.IsInProgress) { Hide(); return; }
            var rs = RunManager.Instance.DebugOnlyGetState();
            Player? me = rs != null ? LocalContext.GetMe(rs) : null;
            if (me == null) { Hide(); return; }
            // Test hook for demo screenshots: read the card history as another player (a Steam id).
            ulong historyId = ulong.TryParse(System.Environment.GetEnvironmentVariable("SPIRELEDGER_HISTORY_NETID"), out var hid) ? hid : me.NetId;
            // Started as soon as a run is in progress (even in combat), so it is ready by the first choice.
            var history = History(historyId);

            if (!Enabled || inCombat || menuOpen) { Hide(); return; }
            if (_frame - _lastScan >= RefreshFrames) { _lastScan = _frame; Scan(); }
            if (_cards.Count == 0) { Hide(); return; }

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
                if (topOverlay != null && GodotObject.IsInstanceValid(topOverlay) && topOverlay != node && !topOverlay.IsAncestorOf(node)) continue;
                var r = RectOf(node);
                if (Moving(node, r)) continue;
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
                string text = Describe(me, card, runPlays, history);
                if (lbl.Text != text) lbl.Text = text;   // re-shaping the label every frame is not free
                lbl.AddThemeFontSizeOverride("font_size", Math.Max(9, (int)Math.Round(14 * sc)));
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

    // True while the card is away from where it rested (picked and flying to the deck, sliding in): its
    // center is more than a quarter of its width from the resting spot. A hover zoom grows the card around
    // about the same center, so it keeps its box. The resting spot moves once the card has been still
    // (under a pixel per frame) for 5 frames.
    private static bool Moving(Control node, Rect2 r)
    {
        ulong id = node.GetInstanceId();
        var c = r.GetCenter();
        if (!_motion.TryGetValue(id, out var m)) { _motion[id] = (c, c, 0); return true; }
        int still = (c - m.Last).Length() < 1f ? m.Still + 1 : 0;
        var rest = still >= 5 ? c : m.Home;
        _motion[id] = (rest, c, still);
        return (c - rest).Length() > Math.Max(12f, r.Size.X * 0.25f);
    }

    private static string Describe(Player me, CardModel card, Func<string, long> runPlays, Dictionary<string, Stat>? history)
    {
        string id = card.Id.Entry;
        var copies = me.Deck.Cards.Where(c => c.Id.Entry == id).ToList();
        int upgraded = copies.Count(c => c.IsUpgraded);
        string deck = copies.Count == 0 ? Loc.T("ci_not_in_deck")
            : upgraded > 0 ? Loc.T(upgraded == 1 ? "ci_in_deck_up1" : "ci_in_deck_up", copies.Count, upgraded) : Loc.T("ci_in_deck", copies.Count);
        var lines = new List<string> { deck };
        long now = runPlays(id);
        if (copies.Count > 0 || now > 0) lines.Add(Loc.T("ci_played_run", now));
        if (history == null) { /* still loading: no history line yet */ }
        else if (history.TryGetValue(id, out var h) && (h.Runs > 0 || h.PlayRuns > 0))
        {
            var parts = new List<string>();
            if (h.Runs > 0) parts.Add(Loc.T(h.Runs == 1 ? "ci_hist_runs1" : "ci_hist_runs", h.Runs, h.Wins, (int)Math.Round(100.0 * h.Wins / h.Runs)));
            if (h.PlayRuns > 0) parts.Add(Loc.T("ci_hist_plays", (h.Plays / (double)h.PlayRuns).ToString("0.#")));
            lines.Add(string.Join(" · ", parts));
        }
        else lines.Add(Loc.T("ci_hist_none"));
        return string.Join("\n", lines);
    }

    // Visible offered cards: holders inside the reward / choose-a-card screens, and shop cards. One pass
    // over the scene tree that skips hidden branches (it used to walk the whole tree three times, with an
    // allocation per node, every 20 frames).
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

        var stack = new Stack<(Node Node, bool InScreen)>();
        stack.Push((((SceneTree)Engine.GetMainLoop()).Root, false));
        while (stack.Count > 0)
        {
            var (node, inScreen) = stack.Pop();
            if (node is CanvasItem ci && !ci.Visible) continue;            // hidden: nothing below is shown
            if (node is CanvasLayer cl && !cl.Visible) continue;
            var t = node.GetType();
            if (t == _rewardScreen || t == _chooseScreen) inScreen = true;
            if (inScreen && _cardHolder != null && _cardHolder.IsAssignableFrom(t) && node is Control hc
                && _holderModel?.GetValue(node) is CardModel hm)
                _cards.Add((CardFrame(hc), hm, false));
            else if (t == _merchantCard && _merchantCardNode?.GetValue(node) is Control cardNode
                     && cardNode.IsVisibleInTree() && _ncardModel?.GetValue(cardNode) is CardModel mm)
            {
                _cards.Add((CardFrame(cardNode), mm, true));
                continue;                                                  // a shop slot holds no other card
            }
            int n = node.GetChildCount();
            for (int k = n - 1; k >= 0; k--) stack.Push((node.GetChild(k), inScreen));
        }
        // Forget the motion of cards that are gone.
        if (_motion.Count > 64)
        {
            var live = new HashSet<ulong>(_cards.Select(c => c.Node.GetInstanceId()));
            foreach (var id in _motion.Keys.Where(k => !live.Contains(k)).ToList()) _motion.Remove(id);
        }
    }

    // The card's visible frame (the NCard's "Frame" texture) — the holder's own rect is a small box at the
    // card's center, which put the info over the card text.
    private static Control CardFrame(Control holderOrCard)
        => holderOrCard.FindChild("Frame", true, false) as Control ?? holderOrCard;

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
