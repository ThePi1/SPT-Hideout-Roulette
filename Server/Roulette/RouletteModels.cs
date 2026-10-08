using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Utils;

namespace HideoutRoulette.Server.Roulette;

public record RouletteConfig
{
    [JsonPropertyName("minBet")]
    public long MinBet { get; set; } = 1000;

    [JsonPropertyName("maxTotalBet")]
    public long MaxTotalBet { get; set; } = 5_000_000;

    [JsonPropertyName("maxBetsPerSpin")]
    public int MaxBetsPerSpin { get; set; } = 20;
}

/// <summary>
///     Item event sent by the client when the player spins the wheel
/// </summary>
public sealed record RouletteSpinRequest : BaseInteractionRequestData
{
    /// <summary>
    ///     Client generated id, used to fetch the outcome of this spin afterwards
    /// </summary>
    [JsonPropertyName("spinId")]
    public string? SpinId { get; set; }

    [JsonPropertyName("bets")]
    public List<RouletteBet>? Bets { get; set; }
}

public sealed record RouletteBet
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    ///     Pocket for a straight bet, 1-3 for dozen/column, ignored otherwise
    /// </summary>
    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }
}

public sealed record RouletteResultRequest : IRequestData
{
    [JsonPropertyName("spinId")]
    public string? SpinId { get; set; }
}

public sealed record RouletteSpinResult
{
    [JsonPropertyName("spinId")]
    public required string SpinId { get; init; }

    [JsonPropertyName("pocket")]
    public required int Pocket { get; init; }

    [JsonPropertyName("color")]
    public required string Color { get; init; }

    [JsonPropertyName("totalStake")]
    public required long TotalStake { get; init; }

    /// <summary>
    ///     Stake plus winnings handed back across all winning bets
    /// </summary>
    [JsonPropertyName("totalReturn")]
    public required long TotalReturn { get; init; }

    [JsonPropertyName("net")]
    public required long Net { get; init; }

    /// <summary>
    ///     Winnings did not fit in the stash and were sent by mail instead
    /// </summary>
    [JsonPropertyName("paidByMail")]
    public required bool PaidByMail { get; init; }
}
