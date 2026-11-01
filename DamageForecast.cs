using System.Linq;
using System;
using System.Collections.Generic;

namespace SpireLedger;

internal enum ForecastKind { Attack, Dot, SelfCard, NonAttackHpLoss, Hidden }

// One contributing source of predicted HP loss. Attacks are Blockable; DoTs / non-attack HP loss /
// self-damage cards bypass block. Conditional items (e.g. self-damage-if-played) count toward the
// range MAXIMUM only, never the guaranteed minimum.
internal readonly struct ForecastItem
{
    public readonly string Label;
    public readonly ForecastKind Kind;
    public readonly int Hits;
    public readonly int DamagePerHit;
    public readonly bool Blockable;
    public readonly bool Conditional;
    // Max HP the player loses for each hit of this item that gets past block (e.g. an enemy with Paper Cuts).
    public readonly int MaxHpPerUnblockedHit;

    public ForecastItem(string label, ForecastKind kind, int hits, int damagePerHit, bool blockable, bool conditional,
                        int maxHpPerUnblockedHit = 0)
    {
        MaxHpPerUnblockedHit = maxHpPerUnblockedHit;
        Label = label;
        Kind = kind;
        Hits = hits;
        DamagePerHit = damagePerHit;
        Blockable = blockable;
        Conditional = conditional;
    }

    public int Total => Hits * DamagePerHit;
}

internal readonly struct ForecastInputs
{
    public readonly int CurrentHp;
    public readonly int MaxHp;
    public readonly int Block;
    public readonly int OstyAbsorb;   // Osty's (block + HP) — a damage sink in front of the player; 0 if none/dead
    public readonly bool HasHidden;
    public readonly IReadOnlyList<ForecastItem> Items;
    public readonly int EndTurnBlock; // block gained at the end of your turn (Plating, Orichalcum, ...), before enemies act

    public ForecastInputs(int currentHp, int maxHp, int block, int ostyAbsorb, bool hasHidden, IReadOnlyList<ForecastItem> items,
                          int endTurnBlock = 0)
    {
        CurrentHp = currentHp;
        MaxHp = maxHp;
        Block = block;
        OstyAbsorb = ostyAbsorb;
        HasHidden = hasHidden;
        Items = items ?? Array.Empty<ForecastItem>();
        EndTurnBlock = endTurnBlock;
    }
}

internal readonly struct ForecastResult
{
    public readonly int Incoming;   // guaranteed gross threat (attacks + guaranteed unblockable), pre-block
    public readonly int Mitigated;  // block absorbed vs guaranteed attacks
    public readonly int MinLoss;    // guaranteed HP loss after block + Osty
    public readonly int MaxLoss;    // MinLoss + conditional contributions
    public readonly int Block;
    public readonly int OstyAbsorb; // Osty pool in front of the player (block + HP), 0 if none
    public readonly int CurrentHp;
    public readonly int MaxHp;
    public readonly bool HasHidden;
    public readonly IReadOnlyList<ForecastItem> Items;
    public readonly int MaxHpLoss;  // permanent max HP lost this turn (guaranteed hits that get past block)

    public ForecastResult(int incoming, int mitigated, int minLoss, int maxLoss,
        int block, int ostyAbsorb, int currentHp, int maxHp, bool hasHidden, IReadOnlyList<ForecastItem> items,
        int maxHpLoss = 0)
    {
        MaxHpLoss = maxHpLoss;
        Incoming = incoming;
        Mitigated = mitigated;
        MinLoss = minLoss;
        MaxLoss = maxLoss;
        Block = block;
        OstyAbsorb = ostyAbsorb;
        CurrentHp = currentHp;
        MaxHp = maxHp;
        HasHidden = hasHidden;
        Items = items ?? Array.Empty<ForecastItem>();
    }

    // Safe only when nothing can cost HP AND nothing is hidden (an unknown intent might still hurt).
    public bool IsSafe => MaxLoss <= 0 && !HasHidden;
    public bool IsLethal => CurrentHp > 0 && MaxLoss >= CurrentHp;
}

internal static class DamageForecast
{
    private static readonly System.Text.RegularExpressions.Regex FirstNumber = new(@"\d+");

    // Per-hit damage from an intent's display label ("3x3", "12", "0x2"), or null when the label has no
    // number. A 0 is a real value (e.g. an attacker whose Strength is very negative), not "unreadable".
    public static int? ParseIntentLabel(string? label)
    {
        if (string.IsNullOrEmpty(label)) return null;
        var m = FirstNumber.Match(label);
        return m.Success && int.TryParse(m.Value, out int n) ? n : null;
    }

    public static ForecastResult Compute(ForecastInputs inp)
    {
        int guaranteedAttack = 0, guaranteedUnblockable = 0, condAttack = 0, condUnblockable = 0;

        var items = inp.Items;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Kind == ForecastKind.Hidden) continue;
            int t = it.Total;
            if (t <= 0) continue;
            if (it.Conditional)
            {
                if (it.Blockable) condAttack += t; else condUnblockable += t;
            }
            else
            {
                if (it.Blockable) guaranteedAttack += t; else guaranteedUnblockable += t;
            }
        }

        // Attacks are absorbed by the combined pool of player block + Osty (a damage sink in front of the
        // player); DoTs bypass both. The loss total is order-independent, so we pool them. End-of-turn block
        // lands before the enemy turn, so it counts like block already on the player.
        int block = Math.Max(0, inp.Block) + Math.Max(0, inp.EndTurnBlock);
        int osty = Math.Max(0, inp.OstyAbsorb);
        int pool = block + osty;

        int mitigated = Math.Min(pool, guaranteedAttack);
        int guaranteedAttackLoss = guaranteedAttack - mitigated;
        int minLoss = guaranteedAttackLoss + guaranteedUnblockable;

        int poolLeft = pool - mitigated;
        int condAttackLoss = Math.Max(0, condAttack - poolLeft);
        int maxLoss = minLoss + condAttackLoss + condUnblockable;

        int incoming = guaranteedAttack + guaranteedUnblockable;

        return new ForecastResult(incoming, mitigated, minLoss, maxLoss,
            block, osty, inp.CurrentHp, inp.MaxHp, inp.HasHidden, items, MaxHpLoss(items, pool));
    }

    // Permanent max HP loss: replay the guaranteed blockable hits against the block pool, in order —
    // end-of-turn self-damage (blockable DoTs) first, then enemy attacks in enemy order — and count, for
    // items that carry a max-HP penalty, each hit that still deals damage once block runs out.
    private static int MaxHpLoss(IReadOnlyList<ForecastItem> items, int pool)
    {
        int remaining = pool, loss = 0;
        foreach (var it in items.Where(i => !i.Conditional && i.Blockable && i.Kind == ForecastKind.Dot)
                                .Concat(items.Where(i => !i.Conditional && i.Blockable && i.Kind == ForecastKind.Attack)))
        {
            for (int h = 0; h < it.Hits; h++)
            {
                if (it.DamagePerHit <= 0) break;
                if (it.DamagePerHit > remaining) loss += it.MaxHpPerUnblockedHit;
                remaining = Math.Max(0, remaining - it.DamagePerHit);
            }
        }
        return loss;
    }
}
