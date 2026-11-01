using System.Collections.Generic;
using System.Linq;
using SpireLedger;
using Xunit;

public class ShareSplitTests
{
    [Fact]
    public void SingleApplierGetsEverything()
    {
        var parts = ShareSplit.Split(7, new Dictionary<int, decimal> { [1] = 3m });
        Assert.Equal(new[] { (1, 7) }, parts);
    }

    [Fact]
    public void SplitsByStacksAndAddsUpExactly()
    {
        // 5 stacks from slot 0, 10 from slot 1: a 9-damage tick splits 3 / 6.
        var parts = ShareSplit.Split(9, new Dictionary<int, decimal> { [0] = 5m, [1] = 10m });
        Assert.Equal(3, parts.Single(p => p.Slot == 0).Amount);
        Assert.Equal(6, parts.Single(p => p.Slot == 1).Amount);
    }

    [Fact]
    public void RemainderGoesToLargestFraction()
    {
        // 10 split 1:2 = 3.33 / 6.67 -> 3 / 7, never 11 total.
        var parts = ShareSplit.Split(10, new Dictionary<int, decimal> { [0] = 1m, [1] = 2m });
        Assert.Equal(10, parts.Sum(p => p.Amount));
        Assert.Equal(7, parts.Single(p => p.Slot == 1).Amount);
    }

    [Fact]
    public void TinyAmountsDropZeroParts()
    {
        var parts = ShareSplit.Split(1, new Dictionary<int, decimal> { [0] = 1m, [1] = 1m, [2] = 1m });
        Assert.Single(parts);
        Assert.Equal(1, parts[0].Amount);
    }

    [Fact]
    public void NoWeightsMeansNoParts()
    {
        Assert.Empty(ShareSplit.Split(5, new Dictionary<int, decimal>()));
        Assert.Empty(ShareSplit.Split(0, new Dictionary<int, decimal> { [0] = 1m }));
    }
}
