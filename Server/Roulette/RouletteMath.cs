namespace HideoutRoulette.Server.Roulette;

public enum PocketColor
{
    Green,
    Red,
    Black,
}

public static class BetTypes
{
    public const string Straight = "straight";
    public const string Red = "red";
    public const string Black = "black";
    public const string Odd = "odd";
    public const string Even = "even";
    public const string Low = "low";
    public const string High = "high";
    public const string Dozen = "dozen";
    public const string Column = "column";
}

/// <summary>
///     Rules of a European (single zero) wheel. Pure logic, no server dependencies.
/// </summary>
public static class RouletteMath
{
    public const int PocketCount = 37;

    private static readonly HashSet<int> _redPockets = [1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36];

    public static PocketColor GetColor(int pocket)
    {
        if (pocket == 0)
        {
            return PocketColor.Green;
        }

        return _redPockets.Contains(pocket) ? PocketColor.Red : PocketColor.Black;
    }

    /// <summary>
    ///     Is the bet type known and its value in range
    /// </summary>
    public static bool IsValidBet(string? type, int value)
    {
        return type switch
        {
            BetTypes.Straight => value is >= 0 and < PocketCount,
            BetTypes.Dozen or BetTypes.Column => value is >= 1 and <= 3,
            BetTypes.Red or BetTypes.Black or BetTypes.Odd or BetTypes.Even or BetTypes.Low or BetTypes.High => true,
            _ => false,
        };
    }

    /// <summary>
    ///     Winnings per unit staked (e.g. 35 for a straight bet), not including the returned stake
    /// </summary>
    public static int GetPayoutMultiplier(string type)
    {
        return type switch
        {
            BetTypes.Straight => 35,
            BetTypes.Dozen or BetTypes.Column => 2,
            _ => 1,
        };
    }

    public static bool IsWinningBet(string type, int value, int pocket)
    {
        // Zero only wins for a straight bet on zero
        if (pocket == 0)
        {
            return type == BetTypes.Straight && value == 0;
        }

        return type switch
        {
            BetTypes.Straight => value == pocket,
            BetTypes.Red => GetColor(pocket) == PocketColor.Red,
            BetTypes.Black => GetColor(pocket) == PocketColor.Black,
            BetTypes.Odd => pocket % 2 == 1,
            BetTypes.Even => pocket % 2 == 0,
            BetTypes.Low => pocket <= 18,
            BetTypes.High => pocket >= 19,
            BetTypes.Dozen => (pocket - 1) / 12 + 1 == value,
            BetTypes.Column => (pocket - 1) % 3 + 1 == value,
            _ => false,
        };
    }

    /// <summary>
    ///     Total handed back to the player for a bet: stake plus winnings, or 0 for a losing bet
    /// </summary>
    public static long GetReturn(string type, int value, long amount, int pocket)
    {
        return IsWinningBet(type, value, pocket) ? amount * (GetPayoutMultiplier(type) + 1) : 0;
    }
}
