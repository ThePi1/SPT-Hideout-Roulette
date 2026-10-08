namespace HideoutRoulette.Server.Roulette;

/// <summary>
///     Validates a set of bets and works out what they return for a given pocket. Pure logic.
/// </summary>
public static class RouletteSettlement
{
    public const int MaxSpinIdLength = 64;

    /// <summary>
    ///     Check a spin request against the rules and the players balance
    /// </summary>
    /// <returns>Reason the request is rejected, null when it is acceptable</returns>
    public static string? Validate(RouletteConfig config, string? spinId, IReadOnlyList<RouletteBet>? bets, long balance)
    {
        if (string.IsNullOrWhiteSpace(spinId) || spinId.Length > MaxSpinIdLength)
        {
            return "Invalid spin id";
        }

        if (bets is null || bets.Count == 0)
        {
            return "No bets placed";
        }

        if (bets.Count > config.MaxBetsPerSpin)
        {
            return $"Too many bets, maximum is {config.MaxBetsPerSpin}";
        }

        long totalStake = 0;
        foreach (var bet in bets)
        {
            if (!RouletteMath.IsValidBet(bet.Type, bet.Value))
            {
                return "Invalid bet";
            }

            // Bounding each bet by the table maximum also keeps the running total from overflowing
            if (bet.Amount < config.MinBet || bet.Amount > config.MaxTotalBet)
            {
                return $"Each bet must be between {config.MinBet} and {config.MaxTotalBet} roubles";
            }

            totalStake += bet.Amount;
        }

        if (totalStake > config.MaxTotalBet)
        {
            return $"Total bet exceeds the table maximum of {config.MaxTotalBet} roubles";
        }

        if (totalStake > balance)
        {
            return "Not enough roubles in stash";
        }

        return null;
    }

    /// <summary>
    ///     Work out the total staked and total handed back for already validated bets
    /// </summary>
    public static (long TotalStake, long TotalReturn) Settle(IReadOnlyList<RouletteBet> bets, int pocket)
    {
        long totalStake = 0;
        long totalReturn = 0;
        foreach (var bet in bets)
        {
            totalStake += bet.Amount;
            totalReturn += RouletteMath.GetReturn(bet.Type!, bet.Value, bet.Amount, pocket);
        }

        return (totalStake, totalReturn);
    }
}
