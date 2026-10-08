using HideoutRoulette.Server.Roulette;
using Xunit;

namespace HideoutRoulette.Tests;

public class RouletteMathTests
{
    [Fact]
    public void Wheel_has_18_red_18_black_and_one_green()
    {
        var colors = Enumerable.Range(0, RouletteMath.PocketCount).Select(RouletteMath.GetColor).ToList();

        Assert.Equal(18, colors.Count(c => c == PocketColor.Red));
        Assert.Equal(18, colors.Count(c => c == PocketColor.Black));
        Assert.Equal(PocketColor.Green, RouletteMath.GetColor(0));
    }

    [Theory]
    [InlineData(BetTypes.Red, 0, 18)]
    [InlineData(BetTypes.Black, 0, 18)]
    [InlineData(BetTypes.Odd, 0, 18)]
    [InlineData(BetTypes.Even, 0, 18)]
    [InlineData(BetTypes.Low, 0, 18)]
    [InlineData(BetTypes.High, 0, 18)]
    [InlineData(BetTypes.Dozen, 1, 12)]
    [InlineData(BetTypes.Dozen, 3, 12)]
    [InlineData(BetTypes.Column, 1, 12)]
    [InlineData(BetTypes.Column, 3, 12)]
    [InlineData(BetTypes.Straight, 0, 1)]
    [InlineData(BetTypes.Straight, 36, 1)]
    public void Bet_wins_on_expected_number_of_pockets(string type, int value, int expectedWinningPockets)
    {
        var wins = Enumerable.Range(0, RouletteMath.PocketCount).Count(pocket => RouletteMath.IsWinningBet(type, value, pocket));

        Assert.Equal(expectedWinningPockets, wins);
    }

    [Theory]
    [InlineData(BetTypes.Straight, 17)]
    [InlineData(BetTypes.Red, 0)]
    [InlineData(BetTypes.Dozen, 2)]
    [InlineData(BetTypes.Column, 3)]
    public void Every_bet_returns_36_units_across_the_wheel(string type, int value)
    {
        // Standard European payouts all have the same expected return of 36/37
        var totalReturn = Enumerable.Range(0, RouletteMath.PocketCount).Sum(pocket => RouletteMath.GetReturn(type, value, 1, pocket));

        Assert.Equal(36, totalReturn);
    }

    [Theory]
    [InlineData(BetTypes.Column, 1, 34, true)]
    [InlineData(BetTypes.Column, 3, 36, true)]
    [InlineData(BetTypes.Column, 2, 36, false)]
    [InlineData(BetTypes.Dozen, 1, 12, true)]
    [InlineData(BetTypes.Dozen, 2, 13, true)]
    [InlineData(BetTypes.Dozen, 3, 24, false)]
    [InlineData(BetTypes.Low, 0, 18, true)]
    [InlineData(BetTypes.High, 0, 19, true)]
    [InlineData(BetTypes.Even, 0, 0, false)]
    public void Boundary_pockets(string type, int value, int pocket, bool expected)
    {
        Assert.Equal(expected, RouletteMath.IsWinningBet(type, value, pocket));
    }

    [Theory]
    [InlineData(BetTypes.Straight, 37)]
    [InlineData(BetTypes.Straight, -1)]
    [InlineData(BetTypes.Dozen, 0)]
    [InlineData(BetTypes.Column, 4)]
    [InlineData("split", 1)]
    [InlineData(null, 1)]
    public void Invalid_bets_are_rejected(string? type, int value)
    {
        Assert.False(RouletteMath.IsValidBet(type, value));
    }
}

public class RouletteSettlementTests
{
    private static readonly RouletteConfig _config = new()
    {
        MinBet = 1000,
        MaxTotalBet = 100_000,
        MaxBetsPerSpin = 3,
    };

    private static RouletteBet Bet(string type, int value, long amount)
    {
        return new RouletteBet
        {
            Type = type,
            Value = value,
            Amount = amount,
        };
    }

    [Fact]
    public void Valid_request_passes()
    {
        Assert.Null(RouletteSettlement.Validate(_config, "abc", [Bet(BetTypes.Red, 0, 1000)], 1000));
    }

    [Fact]
    public void Rejects_stake_above_balance()
    {
        Assert.NotNull(RouletteSettlement.Validate(_config, "abc", [Bet(BetTypes.Red, 0, 1000), Bet(BetTypes.Odd, 0, 1000)], 1999));
    }

    [Fact]
    public void Rejects_bet_below_minimum_including_negative()
    {
        Assert.NotNull(RouletteSettlement.Validate(_config, "abc", [Bet(BetTypes.Red, 0, 999)], 1_000_000));
        Assert.NotNull(RouletteSettlement.Validate(_config, "abc", [Bet(BetTypes.Red, 0, -5000)], 1_000_000));
    }

    [Fact]
    public void Rejects_total_above_table_maximum()
    {
        Assert.NotNull(RouletteSettlement.Validate(_config, "abc", [Bet(BetTypes.Red, 0, 60_000), Bet(BetTypes.Odd, 0, 60_000)], 1_000_000));
    }

    [Fact]
    public void Rejects_huge_amounts_without_overflowing()
    {
        var bets = new[] { Bet(BetTypes.Red, 0, long.MaxValue), Bet(BetTypes.Odd, 0, long.MaxValue) };

        Assert.NotNull(RouletteSettlement.Validate(_config, "abc", bets, long.MaxValue));
    }

    [Fact]
    public void Rejects_empty_missing_or_too_many_bets()
    {
        Assert.NotNull(RouletteSettlement.Validate(_config, "abc", [], 1_000_000));
        Assert.NotNull(RouletteSettlement.Validate(_config, "abc", null, 1_000_000));
        Assert.NotNull(RouletteSettlement.Validate(_config, "abc", Enumerable.Repeat(Bet(BetTypes.Red, 0, 1000), 4).ToList(), 1_000_000));
    }

    [Fact]
    public void Rejects_bad_spin_id()
    {
        Assert.NotNull(RouletteSettlement.Validate(_config, null, [Bet(BetTypes.Red, 0, 1000)], 1_000_000));
        Assert.NotNull(RouletteSettlement.Validate(_config, new string('x', 65), [Bet(BetTypes.Red, 0, 1000)], 1_000_000));
    }

    [Fact]
    public void Settle_sums_winning_and_losing_bets()
    {
        // Pocket 7 is red and odd, in the first dozen
        var bets = new[] { Bet(BetTypes.Straight, 7, 1000), Bet(BetTypes.Black, 0, 2000), Bet(BetTypes.Dozen, 1, 3000) };

        var (totalStake, totalReturn) = RouletteSettlement.Settle(bets, 7);

        Assert.Equal(6000, totalStake);
        Assert.Equal(36_000 + 0 + 9000, totalReturn);
    }

    [Fact]
    public void Settle_on_zero_loses_outside_bets()
    {
        var (totalStake, totalReturn) = RouletteSettlement.Settle([Bet(BetTypes.Red, 0, 1000), Bet(BetTypes.Even, 0, 1000)], 0);

        Assert.Equal(2000, totalStake);
        Assert.Equal(0, totalReturn);
    }
}
