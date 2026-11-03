using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;

namespace SpireLedger;

// The game's own run history (<account>/{,modded/}profileN/saves/history/*.run): every finished run with
// each player's final deck, relics and result, including runs played without the mod. Read by the card
// history under card choices and by the Ledger's Builds tab. Cached until a run ends (Invalidate).
internal static class GameHistory
{
    private static List<JsonObject>? _runs;
    private static ulong? _localId;

    public static void Invalidate() { _runs = null; _localId = null; }

    // Every distinct run, newest first. The same run can sit in several profiles (copied progress), so runs
    // are counted once by start time + seed.
    public static List<JsonObject> Runs() => _runs ??= Load(Files().ToList());

    // The history files, resolved on the main thread (it asks the game's SaveManager); Load can then run on
    // a worker thread, so the first card choice of a session doesn't stall on reading every run.
    public static List<string> HistoryFiles() => Files().ToList();

    public static List<JsonObject> Load(IEnumerable<string> files)
    {
        var list = new List<JsonObject>();
        var seen = new HashSet<string>();
        try
        {
            foreach (var file in files)
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(file)) is JsonObject run && seen.Add(BuildStats.KeyOf(run))) list.Add(run);
                }
                catch { /* skip a damaged file */ }
            }
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] game history read failed: {ex.Message}"); }
        list.Sort((a, b) => ((long?)b["start_time"] ?? 0).CompareTo((long?)a["start_time"] ?? 0));
        return list;
    }

    // The local player's id in the history files, outside a run too: the account folder's name (the SteamID64
    // on Steam) when it appears in the files, else the id found in the most runs (the local player is in all).
    public static ulong LocalId()
    {
        if (_localId is ulong cached) return cached;
        // Test hook (as in CardInsight): a Steam-less test instance plays as id 1, so read the copied
        // history as the player's own SteamID.
        if (ulong.TryParse(System.Environment.GetEnvironmentVariable("SPIRELEDGER_HISTORY_NETID"), out var forced))
            return (_localId = forced).Value;
        var counts = new Dictionary<ulong, int>();
        foreach (var run in Runs())
            foreach (var p in (run["players"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                if (ulong.TryParse(p["id"]?.ToString(), out var id)) counts[id] = (counts.TryGetValue(id, out var n) ? n : 0) + 1;
        ulong result = 0;
        if (ulong.TryParse(AccountDir()?.Name, out var acc) && counts.ContainsKey(acc)) result = acc;
        else if (counts.Count > 0) result = counts.OrderByDescending(kv => kv.Value).First().Key;
        _localId = result;
        return result;
    }

    // The given player's runs (abandoned ones left out), newest first.
    public static List<HistRun> For(ulong playerId) => For(Runs(), playerId);

    public static List<HistRun> For(IEnumerable<JsonObject> runs, ulong playerId)
        => runs.Select(r => BuildStats.Parse(r, playerId)).Where(r => r != null).Select(r => r!).ToList();

    private static DirectoryInfo? AccountDir()
    {
        try
        {
            string scoped = ProjectSettings.GlobalizePath(SaveManager.Instance.GetProfileScopedPath(""));
            var dir = new DirectoryInfo(scoped.TrimEnd('/', '\\'));
            var account = dir.Parent?.Name == "modded" ? dir.Parent.Parent : dir.Parent;
            return account != null && account.Exists ? account : null;
        }
        catch { return null; }
    }

    private static IEnumerable<string> Files()
    {
        var account = AccountDir();
        if (account == null) yield break;
        foreach (var root in new[] { account.FullName, Path.Combine(account.FullName, "modded") })
            for (int p = 1; p <= 3; p++)
            {
                var h = Path.Combine(root, $"profile{p}", "saves", "history");
                if (!Directory.Exists(h)) continue;
                foreach (var f in Directory.GetFiles(h, "*.run").Where(f => f.EndsWith(".run", StringComparison.OrdinalIgnoreCase))) yield return f;
            }
    }
}

// Ids from the history files ("BASH", "ORICHALCUM", "IRONCLAD") as the game's own localized titles.
internal static class GameNames
{
    private static readonly Dictionary<string, string> _cache = new();
    private static string? _cacheLang;
    private static HashSet<string>? _starterCards, _starterRelics;

    public static string Card(string id) => Title("cards", id);
    public static string Relic(string id) => Title("relics", id);
    public static string Character(string id) => Title("characters", id);

    private static string Title(string table, string id)
    {
        string lang = Loc.French ? "fr" : "en";
        if (_cacheLang != lang) { _cache.Clear(); _cacheLang = lang; }
        string key = table + "." + id;
        if (_cache.TryGetValue(key, out var hit)) return hit;
        string? text = null;
        try { if (LocString.Exists(table, id + ".title")) text = TextHelper.SafeGetText(() => new LocString(table, id + ".title")); }
        catch { }
        return _cache[key] = string.IsNullOrWhiteSpace(text) ? Pretty(id) : text!;
    }

    // The game's own art, small in the Ledger so items are recognized at a glance: a card's portrait
    // (CardModel.PortraitPath, beta art as fallback), a relic's icon, a character's top-panel icon.
    public static Texture2D? CardArt(string id)
    {
        try
        {
            var card = ModelDb.GetByIdOrNull<CardModel>(new ModelId("CARD", id));
            if (card == null) return null;
            return GameAssets.Texture(card.PortraitPath) ?? GameAssets.Texture(card.BetaPortraitPath);
        }
        catch { return null; }
    }

    public static Texture2D? RelicArt(string id)
    {
        object? model = null;
        try { model = ModelDb.GetByIdOrNull<RelicModel>(new ModelId("RELIC", id)); } catch { }
        return GameAssets.RelicIcon(model ?? id);
    }

    public static Texture2D? CharacterArt(string id)
        => GameAssets.Texture($"res://images/ui/top_panel/character_icon_{id.ToLowerInvariant()}.png");

    // "ODDLY_SMOOTH_STONE" -> "Oddly Smooth Stone" when the game has no title for it (removed content).
    private static string Pretty(string id)
        => string.Join(" ", id.Split('_', StringSplitOptions.RemoveEmptyEntries)
                              .Select(w => w.Length > 1 ? char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant() : w));

    // Starting cards and relics of every character, and curses: in nearly every run, so the Builds rankings
    // leave them out.
    public static bool IgnoreCard(string id)
    {
        LoadStarters();
        if (_starterCards!.Contains(id)) return true;
        try { return ModelDb.GetByIdOrNull<CardModel>(new ModelId("CARD", id))?.Type == CardType.Curse; }
        catch { return false; }
    }

    public static bool IgnoreRelic(string id) { LoadStarters(); return _starterRelics!.Contains(id); }

    private static void LoadStarters()
    {
        if (_starterCards != null) return;
        _starterCards = new HashSet<string>(); _starterRelics = new HashSet<string>();
        try
        {
            foreach (var c in ModelDb.AllCharacters)
            {
                try { foreach (var card in c.StartingDeck) _starterCards.Add(card.Id.Entry); } catch { }
                try { foreach (var relic in c.StartingRelics) _starterRelics.Add(relic.Id.Entry); } catch { }
            }
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] starting decks unavailable: {ex.Message}"); }
    }
}
