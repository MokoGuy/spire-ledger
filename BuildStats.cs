using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SpireLedger;

// Build tracking from the game's own run history (.run files, which cover runs played without the mod
// too): for one player, each run's character, final deck and relics, result, and the cards they were
// offered and picked. Pure (no Godot or game references) so it is unit-tested; GameHistory reads the files
// and GameNames turns the ids into the game's localized titles.
internal sealed record HistCard(string Id, int Upgrades);

internal sealed record HistRun(string Key, long StartTime, string Character, bool Win, int Ascension, string Mode, int Players,
                               IReadOnlyList<HistCard> Deck, IReadOnlyList<string> Relics, IReadOnlyList<(string Card, bool Picked)> Offers);

internal sealed class Tally
{
    public int Runs, Wins, Offered, Picked;
    public double WinRate => Runs > 0 ? (double)Wins / Runs : 0;
}

internal enum BuildSort { MostTaken, BestWinRate }

internal sealed class BuildSummary
{
    public int Runs, Wins;
    public double WinRate => Runs > 0 ? (double)Wins / Runs : 0;
    public readonly Dictionary<string, Tally> Characters = new(), Relics = new(), Cards = new();
}

internal static class BuildStats
{
    // A win rate over fewer runs than this says nothing; such entries are left out of "best win rate".
    public const int MinRunsForRate = 3;

    // "CARD.STRIKE_IRONCLAD" -> "STRIKE_IRONCLAD".
    public static string Strip(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        int dot = id.IndexOf('.');
        return dot >= 0 ? id.Substring(dot + 1) : id;
    }

    // Start time + seed: the same run copied into several profiles is counted once.
    public static string KeyOf(JsonObject run) => $"{run["start_time"]}_{run["seed"]}";

    // The given player's side of one .run file; null when abandoned or when they were not in it.
    public static HistRun? Parse(JsonObject run, ulong playerId)
    {
        if ((bool?)run["was_abandoned"] ?? false) return null;
        var players = (run["players"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
        // Solo runs store their only player as id 1, whatever the account: a one-player run is the owner's.
        var me = players.Count == 1 ? players[0] : players.FirstOrDefault(p => p["id"]?.ToString() == playerId.ToString());
        if (me == null) return null;
        string meId = me["id"]?.ToString() ?? "";

        var deck = new List<HistCard>();
        foreach (var c in (me["deck"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            string id = Strip((string?)c["id"]);
            if (id.Length > 0) deck.Add(new HistCard(id, (int?)c["current_upgrade_level"] ?? 0));
        }
        var relics = ((me["relics"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            .Select(r => Strip((string?)r["id"])).Where(r => r.Length > 0).ToList();

        // map_point_history: acts -> floors -> player_stats[] (one per player) -> card_choices[].
        var offers = new List<(string, bool)>();
        foreach (var act in (run["map_point_history"] as JsonArray)?.OfType<JsonArray>() ?? Enumerable.Empty<JsonArray>())
            foreach (var floor in act.OfType<JsonObject>())
                foreach (var ps in (floor["player_stats"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                {
                    if (ps["player_id"]?.ToString() != meId) continue;
                    foreach (var ch in (ps["card_choices"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                    {
                        string id = Strip((string?)ch["card"]?["id"]);
                        if (id.Length > 0) offers.Add((id, (bool?)ch["was_picked"] ?? false));
                    }
                }

        return new HistRun(KeyOf(run), (long?)run["start_time"] ?? 0, Strip((string?)me["character"]), (bool?)run["win"] ?? false,
                           (int?)run["ascension"] ?? 0, (string?)run["game_mode"] ?? "standard", players.Count, deck, relics, offers);
    }

    // Per character, relic and card: in how many runs it was there at the end and how many of those were won.
    // Cards also count how often they were offered and picked. `ignoreCard`/`ignoreRelic` drop the starting
    // cards and relics (and curses), which are in nearly every run and would only echo the overall rate.
    public static BuildSummary Summarize(IEnumerable<HistRun> runs, Func<string, bool>? ignoreCard = null, Func<string, bool>? ignoreRelic = null)
    {
        var s = new BuildSummary();
        static Tally T(Dictionary<string, Tally> d, string id) => d.TryGetValue(id, out var t) ? t : d[id] = new Tally();
        foreach (var run in runs)
        {
            s.Runs++; if (run.Win) s.Wins++;
            var ch = T(s.Characters, run.Character); ch.Runs++; if (run.Win) ch.Wins++;
            foreach (var r in run.Relics.Distinct())
            {
                if (ignoreRelic?.Invoke(r) ?? false) continue;
                var t = T(s.Relics, r); t.Runs++; if (run.Win) t.Wins++;
            }
            foreach (var c in run.Deck.Select(c => c.Id).Distinct())
            {
                if (ignoreCard?.Invoke(c) ?? false) continue;
                var t = T(s.Cards, c); t.Runs++; if (run.Win) t.Wins++;
            }
            foreach (var (card, picked) in run.Offers)
            {
                if (ignoreCard?.Invoke(card) ?? false) continue;
                var t = T(s.Cards, card); t.Offered++; if (picked) t.Picked++;
            }
        }
        return s;
    }

    // Entries that ended in at least one deck, most taken first, or best win rate first (enough runs only).
    public static List<KeyValuePair<string, Tally>> Rank(Dictionary<string, Tally> d, BuildSort sort)
    {
        var list = d.Where(kv => kv.Value.Runs > 0);
        return (sort == BuildSort.BestWinRate
                ? list.Where(kv => kv.Value.Runs >= MinRunsForRate)
                      .OrderByDescending(kv => kv.Value.WinRate).ThenByDescending(kv => kv.Value.Runs)
                : list.OrderByDescending(kv => kv.Value.Runs).ThenByDescending(kv => kv.Value.WinRate))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
    }

    // Wins over runs of a whole list. Relics and cards pile up in long runs, so the runs that have any given
    // one are more often won than runs overall; an entry is better than its peers when it beats this pooled
    // rate, not the overall one.
    public static double PooledRate(IEnumerable<Tally> tallies)
    {
        long runs = 0, wins = 0;
        foreach (var t in tallies) { runs += t.Runs; wins += t.Wins; }
        return runs > 0 ? (double)wins / runs : 0;
    }

    // Final deck as "id -> (copies, upgraded copies)", most copies first, for a compact deck list.
    public static List<(string Id, int Copies, int Upgraded)> GroupDeck(IEnumerable<HistCard> deck)
        => deck.GroupBy(c => c.Id).Select(g => (g.Key, g.Count(), g.Count(c => c.Upgrades > 0)))
               .OrderByDescending(g => g.Item2).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
}
