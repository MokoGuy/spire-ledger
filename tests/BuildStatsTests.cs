using System.Linq;
using System.Text.Json.Nodes;
using SpireLedger;
using Xunit;

public class BuildStatsTests
{
    // A trimmed two-player .run file, as the game writes it.
    private static JsonObject Run(bool win, string character = "CHARACTER.IRONCLAD", string relic = "RELIC.ORICHALCUM",
                                  string card = "CARD.BASH", bool abandoned = false, long start = 100) => JsonNode.Parse($$"""
    {
      "start_time": {{start}}, "seed": "S{{start}}", "win": {{(win ? "true" : "false")}}, "was_abandoned": {{(abandoned ? "true" : "false")}},
      "ascension": 4, "game_mode": "standard",
      "players": [
        { "id": 1, "character": "{{character}}",
          "deck": [ { "id": "CARD.STRIKE_IRONCLAD" }, { "id": "CARD.STRIKE_IRONCLAD", "current_upgrade_level": 1 }, { "id": "{{card}}" } ],
          "relics": [ { "id": "RELIC.BURNING_BLOOD" }, { "id": "{{relic}}" } ] },
        { "id": 2, "character": "CHARACTER.SILENT", "deck": [ { "id": "CARD.NEUTRALIZE" } ], "relics": [] }
      ],
      "map_point_history": [ [
        { "player_stats": [
          { "player_id": 1, "card_choices": [ { "card": { "id": "{{card}}" }, "was_picked": true }, { "card": { "id": "CARD.ANGER" }, "was_picked": false } ] },
          { "player_id": 2, "card_choices": [ { "card": { "id": "CARD.ANGER" }, "was_picked": true } ] } ] }
      ] ]
    }
    """)!.AsObject();

    [Fact]
    public void ParsesOnlyTheGivenPlayer()
    {
        var r = BuildStats.Parse(Run(true), 1)!;
        Assert.Equal("IRONCLAD", r.Character);
        Assert.Equal(2, r.Players);
        Assert.Equal(new[] { "BURNING_BLOOD", "ORICHALCUM" }, r.Relics);
        Assert.Equal(3, r.Deck.Count);
        Assert.Equal(new[] { ("BASH", true), ("ANGER", false) }, r.Offers);
        Assert.Equal("100_S100", r.Key);
    }

    [Fact]
    public void SkipsAbandonedRunsAndAbsentPlayers()
    {
        Assert.Null(BuildStats.Parse(Run(false, abandoned: true), 1));
        Assert.Null(BuildStats.Parse(Run(true), 99));
    }

    [Fact]
    public void CountsRunsWinsAndPicks()
    {
        var runs = new[] { Run(true), Run(false, start: 2), Run(true, relic: "RELIC.ANCHOR", card: "CARD.CLEAVE", start: 3) }
            .Select(j => BuildStats.Parse(j, 1)!).ToList();
        var s = BuildStats.Summarize(runs, ignoreCard: c => c.StartsWith("STRIKE"), ignoreRelic: r => r == "BURNING_BLOOD");

        Assert.Equal(3, s.Runs); Assert.Equal(2, s.Wins);
        Assert.False(s.Relics.ContainsKey("BURNING_BLOOD"));
        Assert.False(s.Cards.ContainsKey("STRIKE_IRONCLAD"));
        Assert.Equal((2, 1), (s.Relics["ORICHALCUM"].Runs, s.Relics["ORICHALCUM"].Wins));
        var anger = s.Cards["ANGER"];
        Assert.Equal((0, 3, 0), (anger.Runs, anger.Offered, anger.Picked)); // the teammate's pick is not ours
        Assert.Equal((2, 2), (s.Cards["BASH"].Offered, s.Cards["BASH"].Picked));
    }

    [Fact]
    public void RanksByRunsOrByWinRateWithEnoughRuns()
    {
        var runs = Enumerable.Range(0, 3).Select(i => Run(i < 1, relic: "RELIC.ANCHOR", start: i))          // ANCHOR 1/3
            .Concat(Enumerable.Range(3, 3).Select(i => Run(true, relic: "RELIC.LANTERN", start: i)))         // LANTERN 3/3
            .Append(Run(true, relic: "RELIC.VAJRA", start: 9))                                               // VAJRA 1/1
            .Select(j => BuildStats.Parse(j, 1)!).ToList();
        var s = BuildStats.Summarize(runs, ignoreRelic: r => r == "BURNING_BLOOD");

        // Same run count: the better win rate comes first.
        Assert.Equal(new[] { "LANTERN", "ANCHOR", "VAJRA" }, BuildStats.Rank(s.Relics, BuildSort.MostTaken).Select(kv => kv.Key));
        Assert.Equal(new[] { "LANTERN", "ANCHOR" }, BuildStats.Rank(s.Relics, BuildSort.BestWinRate).Select(kv => kv.Key));
        Assert.DoesNotContain(BuildStats.Rank(s.Cards, BuildSort.MostTaken), kv => kv.Key == "ANGER"); // offered, never taken
    }

    [Fact]
    public void SoloRunsBelongToTheOwnerWhateverTheId()
    {
        var solo = Run(true);
        var players = solo["players"]!.AsArray();
        players.RemoveAt(1);
        Assert.Equal("IRONCLAD", BuildStats.Parse(solo, 76561198000000000)!.Character);
    }

    [Fact]
    public void PooledRateWeighsByRuns()
    {
        var a = new Tally { Runs = 3, Wins = 3 };
        var b = new Tally { Runs = 1, Wins = 0 };
        Assert.Equal(0.75, BuildStats.PooledRate(new[] { a, b }));
    }

    [Fact]
    public void PicksFavoriteBestAndWorst()
    {
        var d = new System.Collections.Generic.Dictionary<string, Tally>
        {
            ["A"] = new Tally { Runs = 10, Wins = 5 },
            ["B"] = new Tally { Runs = 4, Wins = 4 },
            ["C"] = new Tally { Runs = 2, Wins = 2 },  // perfect but too few runs
            ["D"] = new Tally { Runs = 6, Wins = 1 },
        };
        Assert.Equal("A", BuildStats.Favorite(d)!.Value.Key);
        Assert.Equal("B", BuildStats.Best(d, 3)!.Value.Key);
        Assert.Equal("D", BuildStats.Worst(d, 3)!.Value.Key);
        Assert.Null(BuildStats.Best(d, 20));
        Assert.Equal(3, BuildStats.HighlightMinRuns(12));
        Assert.Equal(14, BuildStats.HighlightMinRuns(136));
    }

    [Fact]
    public void GroupsDeckCopies()
    {
        var deck = BuildStats.Parse(Run(true), 1)!.Deck;
        Assert.Equal(new[] { ("STRIKE_IRONCLAD", 2, 1), ("BASH", 1, 0) }, BuildStats.GroupDeck(deck));
    }
}
