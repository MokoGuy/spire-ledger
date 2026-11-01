using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace SpireLedger;

// Phase 3: one JSON file per run under <user data>/SpireLedger/runs/<start>_<seed>.json, rewritten after
// every finished combat and at game over. Lives outside the steam/ save folders on purpose: the game's
// Steam Cloud sync deletes any local file it does not know there.
//
// The file holds the run (seed, mode, ascension, modifiers, players), every resolved combat with each
// player's full stats, and at game over the result, the badges each player got and the game's own
// per-player counters (DamageDealt, DebuffsApplied) for cross-checking. On "continue" the combats are
// read back and re-folded into the run aggregate, so the run recap survives a game restart.
internal static class RunLedger
{
    public const int Schema = 1;

    private static string? _runId;
    private static JsonObject? _doc;
    private static (int Act, int Floor, string Encounter, string EncounterId) _combat;

    public static string RunsDir => Path.Combine(OS.GetUserDataDir(), "SpireLedger", "runs");

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static FieldInfo? _startTimeField;

    // Stable id of the current run: its start time (kept across save & continue) and seed.
    public static string? CurrentRunId(RunState rs)
    {
        try
        {
            _startTimeField ??= AccessTools.Field(typeof(RunManager), "_startTime");
            long start = _startTimeField?.GetValue(RunManager.Instance) is long l ? l : 0;
            string seed = rs.Rng.StringSeed ?? "noseed";
            // The local player's NetId keeps two instances on one PC (local co-op tests) apart.
            ulong me = 0;
            try { me = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(rs)?.NetId ?? 0; } catch { }
            return $"{start}_{seed}_{me}";
        }
        catch { return null; }
    }

    // Called at combat start: make sure the ledger matches the current run (new run, or continue).
    // Returns the combats already recorded for this run when it was just loaded from disk.
    public static List<SourceSnapshot>? EnsureRun(RunState rs, IReadOnlyList<string> labels, int localSlot)
    {
        string? id = CurrentRunId(rs);
        if (id == null || id == _runId) return null;
        _runId = id;
        List<SourceSnapshot>? restored = null;
        string path = PathFor(id);
        try
        {
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing)
            {
                _doc = existing;
                FillMissingNames(existing);
                restored = ReadCombats(existing, labels, localSlot);
                GD.Print($"[SpireLedger] ledger resumed {id} ({restored.Count} combats)");
                return restored;
            }
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] ledger read failed: {ex.Message}"); }

        _doc = NewRun(rs, id);
        Save();
        GD.Print($"[SpireLedger] ledger started {id}");
        return null;
    }

    public static void NoteCombatStart(RunState rs, CombatState cs)
    {
        try
        {
            string title = TextHelper.SafeGetText(() => cs.Encounter!.Title) ?? "?";
            string id = cs.Encounter?.Id.Entry ?? "?";
            _combat = (rs.CurrentActIndex + 1, rs.TotalFloor, title, id);
        }
        catch { _combat = (0, 0, "?", "?"); }
    }

    public static void AddCombat(SourceSnapshot src, int rounds)
    {
        if (_doc == null) return;
        try
        {
            NoteModVersions();
            var combats = _doc["combats"] as JsonArray ?? new JsonArray();
            _doc["combats"] = combats;
            var players = new JsonArray();
            for (int s = 0; s < src.PlayerCount && s < src.PerPlayer.Length; s++) players.Add(WritePlayer(s, src.PerPlayer[s]));
            combats.Add(new JsonObject
            {
                ["index"] = combats.Count + 1,
                ["act"] = _combat.Act,
                ["floor"] = _combat.Floor,
                ["encounter"] = _combat.Encounter,
                ["encounter_id"] = _combat.EncounterId,
                ["rounds"] = rounds,
                ["players"] = players,
            });
            Save();
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] ledger combat write failed: {ex.Message}"); }
    }

    // Each player's Spire Ledger version, when known (ours, and teammates' from their hello message).
    private static void NoteModVersions()
    {
        if (_doc?["players"] is not JsonArray ps) return;
        foreach (var p in ps.OfType<JsonObject>())
        {
            if ((bool?)p["is_local"] ?? false) p["mod_version"] = ModVersion.Version;
            else if (ulong.TryParse((string?)p["net_id"], out var id) && ModVersion.PeerVersions.TryGetValue(id, out var v)) p["mod_version"] = v;
        }
    }

    // Game over: result, badges per player (NetId -> badge ids) and the game's own counters.
    public static void SetResult(bool won, int floor, JsonObject badges, JsonObject badgeTitles, JsonObject gameStats)
    {
        if (_doc == null) return;
        _doc["result"] = new JsonObject
        {
            ["won"] = won,
            ["floor"] = floor,
            ["badges"] = badges,
            ["badge_titles"] = badgeTitles,
            ["game_stats"] = gameStats,
            ["ended_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        Save();
    }

    public static JsonObject? Current => _doc;

    private static MegaCrit.Sts2.Core.Platform.PlatformType? Platform()
    {
        try { return RunManager.Instance.NetService.Platform; } catch { return null; }
    }

    // The player's platform name (Steam persona name in a Steam game), or null if unknown.
    public static string? PlayerName(ulong netId)
    {
        try
        {
            var platform = Platform();
            if (platform == null) return null;
            string name = MegaCrit.Sts2.Core.Platform.PlatformUtil.GetPlayerNameRaw(platform.Value, netId);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch { return null; }
    }

    // Runs recorded before names were stored get them on the next combat of that run.
    private static void FillMissingNames(JsonObject doc)
    {
        if (doc["players"] is not JsonArray ps) return;
        foreach (var p in ps.OfType<JsonObject>())
            if (p["name"] == null && ulong.TryParse((string?)p["net_id"], out var id) && PlayerName(id) is string n)
                p["name"] = n;
    }

    private static string PathFor(string id)
    {
        var safe = new string(id.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        return Path.Combine(RunsDir, safe + ".json");
    }

    private static void Save()
    {
        if (_doc == null || _runId == null) return;
        try
        {
            Directory.CreateDirectory(RunsDir);
            _doc["updated_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string path = PathFor(_runId), tmp = path + ".tmp";
            File.WriteAllText(tmp, _doc.ToJsonString(Indented));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] ledger save failed: {ex.Message}"); }
    }

    private static JsonObject NewRun(RunState rs, string id)
    {
        var players = new JsonArray();
        Player? me = null;
        try { me = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(rs); } catch { }
        for (int i = 0; i < rs.Players.Count; i++)
        {
            var p = rs.Players[i];
            players.Add(new JsonObject
            {
                ["slot"] = i,
                ["net_id"] = p.NetId.ToString(),
                ["name"] = PlayerName(p.NetId),
                ["platform"] = Platform()?.ToString(),
                ["character"] = TextHelper.SafeGetText(() => p.Character.Title) ?? "?",
                ["character_id"] = p.Character.Id.Entry,
                ["is_local"] = ReferenceEquals(p, me),
            });
        }
        var modifiers = new JsonArray();
        try { foreach (var m in rs.Modifiers) modifiers.Add(m.Id.Entry); } catch { }
        long start = 0;
        try { start = long.Parse(id.Split('_')[0]); } catch { }
        return new JsonObject
        {
            ["schema"] = Schema,
            ["id"] = id,
            ["seed"] = rs.Rng.StringSeed,
            ["start_time"] = start,
            ["game_mode"] = rs.GameMode.ToString(),
            ["ascension"] = rs.AscensionLevel,
            ["modifiers"] = modifiers,
            ["players"] = players,
            ["combats"] = new JsonArray(),
        };
    }

    private static JsonObject WritePlayer(int slot, PlayerSources ps) => new()
    {
        ["slot"] = slot,
        ["dealt"] = ps.DealtTotal,
        ["taken"] = ps.TakenTotal,
        ["healed"] = ps.HealTotal,
        ["block"] = ps.BlockTotal,
        ["block_given"] = ps.BlockGiven,
        ["cards_played"] = ps.CardsPlayed,
        ["energy_spent"] = ps.EnergySpent,
        ["effects_total"] = ps.EffectsTotal,
        ["debuffs_applied"] = ps.DebuffsApplied,
        ["biggest_hit"] = ps.BiggestHit,
        ["biggest_hit_source"] = ps.BiggestHitSource,
        ["dealt_by_source"] = Entries(ps.Dealt),
        ["taken_by_source"] = Entries(ps.Taken),
        ["effects"] = Entries(ps.Effects),
        ["potions"] = Entries(ps.Potions),
        ["card_plays"] = CardPlaysJson(ps.CardPlays),
    };

    private static JsonObject CardPlaysJson(Dictionary<string, long> plays)
    {
        var o = new JsonObject();
        foreach (var (id, n) in plays) o[id] = n;
        return o;
    }

    internal static Dictionary<string, long> ReadCardPlays(JsonNode? n)
    {
        var d = new Dictionary<string, long>();
        if (n is JsonObject o)
            foreach (var kv in o) if (kv.Value != null) d[kv.Key] = (long)kv.Value;
        return d;
    }

    private static JsonArray Entries(List<SourceEntry> list)
    {
        var a = new JsonArray();
        foreach (var e in list) a.Add(new JsonObject { ["name"] = e.Name, ["total"] = e.Total, ["icon"] = e.Icon });
        return a;
    }

    private static List<SourceEntry> ReadEntries(JsonNode? n)
    {
        var list = new List<SourceEntry>();
        if (n is not JsonArray a) return list;
        foreach (var e in a)
            if (e is JsonObject o)
                list.Add(new SourceEntry((string?)o["name"] ?? "?", (long?)o["total"] ?? 0, (string?)o["icon"]));
        return list;
    }

    internal static PlayerSources ReadPlayer(JsonObject o) => new()
    {
        DealtTotal = (long?)o["dealt"] ?? 0,
        TakenTotal = (long?)o["taken"] ?? 0,
        HealTotal = (long?)o["healed"] ?? 0,
        BlockTotal = (long?)o["block"] ?? 0,
        BlockGiven = (long?)o["block_given"] ?? 0,
        CardsPlayed = (long?)o["cards_played"] ?? 0,
        EnergySpent = (long?)o["energy_spent"] ?? 0,
        EffectsTotal = (long?)o["effects_total"] ?? 0,
        DebuffsApplied = (long?)o["debuffs_applied"] ?? 0,
        BiggestHit = (long?)o["biggest_hit"] ?? 0,
        BiggestHitSource = (string?)o["biggest_hit_source"] ?? "",
        Dealt = ReadEntries(o["dealt_by_source"]),
        Taken = ReadEntries(o["taken_by_source"]),
        Effects = ReadEntries(o["effects"]),
        Potions = ReadEntries(o["potions"]),
        CardPlays = ReadCardPlays(o["card_plays"]),
    };

    private static List<SourceSnapshot> ReadCombats(JsonObject doc, IReadOnlyList<string> labels, int localSlot)
    {
        var result = new List<SourceSnapshot>();
        if (doc["combats"] is not JsonArray combats) return result;
        foreach (var c in combats)
        {
            if (c is not JsonObject co || co["players"] is not JsonArray ps) continue;
            var per = new PlayerSources[labels.Count];
            for (int i = 0; i < per.Length; i++) per[i] = new PlayerSources();
            foreach (var p in ps)
                if (p is JsonObject po && (int?)po["slot"] is int slot && slot >= 0 && slot < per.Length)
                    per[slot] = ReadPlayer(po);
            result.Add(new SourceSnapshot(per, labels.ToArray(), per.Length, localSlot, Array.Empty<LogEntry>()));
        }
        return result;
    }

    // All saved runs, newest first (for the Ledger screen).
    public static List<JsonObject> LoadAll()
    {
        var list = new List<JsonObject>();
        try
        {
            if (!Directory.Exists(RunsDir)) return list;
            foreach (var f in Directory.GetFiles(RunsDir, "*.json"))
            {
                try { if (JsonNode.Parse(File.ReadAllText(f)) is JsonObject o) list.Add(o); }
                catch { /* skip a damaged file */ }
            }
        }
        catch { }
        list.Sort((a, b) => ((long?)b["start_time"] ?? 0).CompareTo((long?)a["start_time"] ?? 0));
        return list;
    }
}
