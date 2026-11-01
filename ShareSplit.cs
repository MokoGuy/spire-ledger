using System;
using System.Collections.Generic;

namespace SpireLedger;

// Pure helper (no game references, so the unit tests can compile it on its own).
public static class ShareSplit
{
    // Split `amount` across slots by weight (largest remainder, so the parts add up exactly).
    public static List<(int Slot, int Amount)> Split(int amount, Dictionary<int, decimal> shares)
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
}
