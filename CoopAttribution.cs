using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace SpireLedger;

// Co-op attribution and per-player stats fed by the game's own CombatHistory.
//
// Damage with no direct dealer (Poison, Doom, …) used to be credited to the local player, so in co-op
// each PC credited itself with the teammate's poison. Every power application goes through
// CombatHistory.PowerReceived(combatState, power, amount, applier); we record, per (target, power
// type), how many stacks each player applied, and split that power's damage between them in the same
// proportion. A power with a single applier is credited to that player; one with no known player
// applier falls back to the power's own Applier, then to the old local-player behaviour.
//
// The same history hooks feed the Phase 2 stats (effects applied, cards played, energy spent, block
// given to allies). CombatHistory runs on every peer for every player, so both PCs see identical data.
public static partial class DamageChartsMod
{
    // (target creature, power type name) -> slot -> stacks applied by that player this combat.
    private static readonly Dictionary<(Creature Target, string Power), Dictionary<int, decimal>> _powerShares = new();

    // Shares of the power whose hook is about to deal damage (set in PowerSourcePrefix, same frame).
    private static Dictionary<int, decimal>? _activeShares;

    private static void ClearShares()
    {
        _powerShares.Clear();
        _activeShares = null;
    }

    // Player slot owning this creature: the player itself, or the owner of a pet/summon (e.g. Osty).
    private static int SlotOfCreature(Creature? c)
    {
        if (c == null) return -1;
        try
        {
            if (c.IsPlayer) return SlotOf(c.Player);
            if (c.IsPet) return SlotOf(c.PetOwner);
        }
        catch { }
        return -1;
    }

    // Who should be credited for damage dealt by this power: stacks applied per player, or the applier.
    private static Dictionary<int, decimal>? SharesFor(PowerModel? power)
    {
        if (power == null) return null;
        try
        {
            if (power.Owner != null && _powerShares.TryGetValue((power.Owner, power.GetType().Name), out var s) && s.Count > 0)
                return s;
            int slot = SlotOfCreature(power.Applier);
            if (slot >= 0) return new Dictionary<int, decimal> { [slot] = 1m };
        }
        catch { }
        return null;
    }

    private static Dictionary<int, decimal>? SharesFor(Creature target, string powerType)
        => _powerShares.TryGetValue((target, powerType), out var s) && s.Count > 0 ? s : null;

    // Split `amount` across slots by weight (largest remainder, so the parts add up exactly).
    internal static List<(int Slot, int Amount)> Split(int amount, Dictionary<int, decimal> shares)
    {
        var result = new List<(int, int)>();
        decimal total = 0;
        foreach (var w in shares.Values) if (w > 0) total += w;
        if (total <= 0 || amount <= 0) return result;
        var parts = new List<(int Slot, int Floor, decimal Rem)>();
        int assigned = 0;
        foreach (var (slot, w) in shares)
        {
            if (w <= 0) continue;
            decimal exact = amount * w / total;
            int floor = (int)Math.Floor(exact);
            parts.Add((slot, floor, exact - floor));
            assigned += floor;
        }
        parts.Sort((a, b) => b.Rem != a.Rem ? b.Rem.CompareTo(a.Rem) : a.Slot.CompareTo(b.Slot));
        for (int i = 0; i < parts.Count; i++)
        {
            int extra = i < amount - assigned ? 1 : 0;
            if (parts[i].Floor + extra > 0) result.Add((parts[i].Slot, parts[i].Floor + extra));
        }
        return result;
    }

    // ===== CombatHistory hooks =====

    private static void TryApplyHistoryHooks()
    {
        try
        {
            var hist = AccessTools.TypeByName("MegaCrit.Sts2.Core.Combat.History.CombatHistory");
            if (hist == null) { GD.Print("[SpireLedger] CombatHistory not found; co-op attribution disabled"); return; }
            var h = new Harmony("com.spireledger.history");
            int n = 0;
            foreach (var (method, postfix) in new[]
            {
                ("PowerReceived", nameof(PowerReceivedPostfix)),
                ("CardPlayFinished", nameof(CardPlayFinishedPostfix)),
                ("EnergySpent", nameof(EnergySpentPostfix)),
                ("BlockGained", nameof(BlockGainedPostfix)),
            })
            {
                var m = AccessTools.Method(hist, method);
                if (m == null) { GD.Print($"[SpireLedger] CombatHistory.{method} not found (game changed?)"); continue; }
                h.Patch(m, postfix: new HarmonyMethod(typeof(DamageChartsMod).GetMethod(postfix, BindingFlags.NonPublic | BindingFlags.Static)));
                n++;
            }
            GD.Print($"[SpireLedger] history hooks applied ({n}/4)");
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] history hooks skipped: {ex.GetType().Name}: {ex.Message}"); }
    }

    private static void PowerReceivedPostfix(PowerModel power, decimal amount, Creature? applier)
    {
        if (_disabled) return;
        try
        {
            if (power?.Owner == null || amount == 0) return;
            int slot = SlotOfCreature(applier);
            if (slot < 0) return;
            var target = power.Owner;
            bool onEnemy = !target.IsPlayer && !target.IsPet;
            if (onEnemy && amount > 0)
            {
                var key = (target, power.GetType().Name);
                if (!_powerShares.TryGetValue(key, out var map)) { map = new Dictionary<int, decimal>(); _powerShares[key] = map; }
                map.TryGetValue(slot, out var cur);
                map[slot] = cur + amount;
            }
            // Effects applied: debuffs on enemies and anything given to an ally (not self-buffs).
            if (onEnemy || SlotOfCreature(target) != slot)
            {
                string name = TextHelper.SafeGetText(() => power.Title) ?? power.GetType().Name;
                string? icon = null;
                try { if (power.PackedIconPath is string p && ResourceLoader.Exists(p)) icon = p; } catch { }
                _tracker.AddEffect(slot, name, (long)Math.Round(Math.Abs(amount)), icon);
            }
        }
        catch { /* never disrupt combat */ }
    }

    private static void CardPlayFinishedPostfix(object cardPlay)
    {
        if (_disabled) return;
        try
        {
            if (cardPlay is not MegaCrit.Sts2.Core.Entities.Cards.CardPlay play || play.IsAutoPlay) return;
            if (!play.IsFirstInSeries) return; // a card replayed N times counts once
            int slot = SlotOf(play.Card?.Owner);
            if (slot >= 0) _tracker.AddCardPlayed(slot);
        }
        catch { }
    }

    private static void EnergySpentPostfix(int amount, Player player)
    {
        if (_disabled) return;
        try
        {
            int slot = SlotOf(player);
            if (slot >= 0 && amount > 0) _tracker.AddEnergy(slot, amount);
        }
        catch { }
    }

    // Block a player's card gave to another player (e.g. a co-op defend on the teammate).
    private static void BlockGainedPostfix(Creature receiver, int amount, object? cardPlay)
    {
        if (_disabled) return;
        try
        {
            if (amount <= 0 || receiver == null || !receiver.IsPlayer) return;
            if (cardPlay is not MegaCrit.Sts2.Core.Entities.Cards.CardPlay play) return;
            int giver = SlotOf(play.Card?.Owner);
            int taker = SlotOf(receiver.Player);
            if (giver >= 0 && taker >= 0 && giver != taker) _tracker.AddBlockGiven(giver, amount);
        }
        catch { }
    }
}

// End of run: ScoreUtility.GetBadges(run, playerId, won) runs on the game-over screen for the local
// player. We ask it for every other player too, so the run history knows who got which badge, and keep
// the game's own per-player counters next to ours.
public static partial class DamageChartsMod
{
    private static bool _inBadges;

    // NetId -> badge ids, for the end-of-run recap.
    internal static readonly Dictionary<ulong, List<string>> LastBadges = new();

    private static void TryApplyBadgeHook()
    {
        try
        {
            var su = AccessTools.TypeByName("MegaCrit.Sts2.Core.Runs.ScoreUtility");
            var m = su != null ? AccessTools.Method(su, "GetBadges") : null;
            if (m == null) { GD.Print("[SpireLedger] ScoreUtility.GetBadges not found; badges not recorded"); return; }
            new Harmony("com.spireledger.badges").Patch(m,
                postfix: new HarmonyMethod(typeof(DamageChartsMod).GetMethod(nameof(GetBadgesPostfix), BindingFlags.NonPublic | BindingFlags.Static)));
            GD.Print("[SpireLedger] badge hook applied");
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] badge hook skipped: {ex.Message}"); }
    }

    private static void GetBadgesPostfix(MegaCrit.Sts2.Core.Saves.SerializableRun run, ulong playerId, bool won,
                                         List<MegaCrit.Sts2.Core.Models.Badges.Badge> __result)
    {
        if (_inBadges || run?.Players == null) return;
        _inBadges = true;
        try
        {
            LastBadges.Clear();
            LastBadgeTitles.Clear();
            var badges = new System.Text.Json.Nodes.JsonObject();
            var stats = new System.Text.Json.Nodes.JsonObject();
            var titlesJson = new System.Text.Json.Nodes.JsonObject();
            foreach (var p in run.Players)
            {
                var list = p.NetId == playerId ? __result
                    : MegaCrit.Sts2.Core.Runs.ScoreUtility.GetBadges(run, p.NetId, won);
                var ids = new List<string>();
                var titles = new List<string>();
                foreach (var b in list) { ids.Add(b.Id); titles.Add(BadgeTitle(b)); }
                LastBadges[p.NetId] = ids;
                LastBadgeTitles[p.NetId] = titles;
                var arr = new System.Text.Json.Nodes.JsonArray();
                foreach (var id in ids) arr.Add(id);
                badges[p.NetId.ToString()] = arr;
                var tarr = new System.Text.Json.Nodes.JsonArray();
                foreach (var t in titles) tarr.Add(t);
                titlesJson[p.NetId.ToString()] = tarr;
                stats[p.NetId.ToString()] = new System.Text.Json.Nodes.JsonObject
                {
                    ["damage_dealt"] = p.ExtraFields?.DamageDealt ?? 0,
                    ["debuffs_applied"] = p.ExtraFields?.DebuffsApplied ?? 0,
                };
            }
            RunLedger.SetResult(won, run.FloorReached, badges, titlesJson, stats);
            OnRunEnded();
            GD.Print($"[SpireLedger] run result recorded (won={won}, badges for {LastBadges.Count} player(s))");
        }
        catch (Exception ex) { GD.PrintErr($"[SpireLedger] badge capture failed: {ex.Message}"); }
        finally { _inBadges = false; }
    }
}

public static partial class DamageChartsMod
{
    // Phase 4: open the run recap on the game-over screen, with the badges each player got.
    private static void OnRunEnded()
    {
        if (_recapEnabled && _run.HasData()) _recapVisible = true;
    }
}

public static partial class DamageChartsMod
{
    // Localized badge titles per player slot for the recap, or null before the run has ended.
    internal static readonly Dictionary<ulong, List<string>> LastBadgeTitles = new();

    private static string[]? PartyBadgeNames()
    {
        if (LastBadgeTitles.Count == 0) return null;
        var names = new string[_players.Count];
        for (int s = 0; s < _players.Count; s++)
            names[s] = LastBadgeTitles.TryGetValue(_players[s].NetId, out var t) ? string.Join(", ", t) : "";
        return names;
    }

    // Badge title as the game-over screen shows it (rarity-specific title when the game has one).
    private static string BadgeTitle(MegaCrit.Sts2.Core.Models.Badges.Badge b)
    {
        try
        {
            string prefix = b.Rarity.ToString().ToLowerInvariant();
            foreach (var key in new[] { $"{b.Id}.{prefix}Title", $"{b.Id}.title" })
                if (MegaCrit.Sts2.Core.Localization.LocString.Exists("badges", key))
                    return new MegaCrit.Sts2.Core.Localization.LocString("badges", key).GetFormattedText();
        }
        catch { }
        return b.Id;
    }
}
