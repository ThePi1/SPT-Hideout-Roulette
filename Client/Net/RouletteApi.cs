using System;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace HideoutRoulette.Client.Net
{
    public class RouletteBet
    {
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("value")]
        public int Value;

        [JsonProperty("amount")]
        public long Amount;
    }

    public class RouletteConfig
    {
        [JsonProperty("minBet")]
        public long MinBet = 1000;

        [JsonProperty("maxTotalBet")]
        public long MaxTotalBet = 5000000;

        [JsonProperty("maxBetsPerSpin")]
        public int MaxBetsPerSpin = 20;
    }

    public class RouletteSpinResult
    {
        [JsonProperty("spinId")]
        public string SpinId;

        [JsonProperty("pocket")]
        public int Pocket;

        [JsonProperty("color")]
        public string Color;

        [JsonProperty("totalStake")]
        public long TotalStake;

        [JsonProperty("totalReturn")]
        public long TotalReturn;

        [JsonProperty("net")]
        public long Net;

        [JsonProperty("paidByMail")]
        public bool PaidByMail;
    }

    /// <summary>
    ///     Item event body, sent through the games own operation queue so the stash updates from the response
    /// </summary>
    public sealed class RouletteSpinPayload : GClass3472
    {
        [JsonProperty("Action")]
        public string Action = "HideoutRouletteSpin";

        [JsonProperty("spinId")]
        public string SpinId;

        [JsonProperty("bets")]
        public RouletteBet[] Bets;
    }

    public static class RouletteApi
    {
        private const string ResultRoute = "/hideoutroulette/result";
        private const string ConfigRoute = "/hideoutroulette/config";

        /// <summary>
        ///     Table limits from the server, null if they could not be read
        /// </summary>
        public static async Task<RouletteConfig> GetConfig()
        {
            try
            {
                var json = await RequestHandler.PostJsonAsync(ConfigRoute, "{}");

                return string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<RouletteConfig>(json);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to fetch roulette config: {e}");

                return null;
            }
        }

        /// <summary>
        ///     Place bets and spin. The callback receives the result, or null and an error message.
        /// </summary>
        public static void Spin(RouletteBet[] bets, Action<RouletteSpinResult, string> callback)
        {
            if (!Singleton<ClientApplication<ISession>>.Instantiated)
            {
                callback(null, "not connected to the server");

                return;
            }

            var spinId = Guid.NewGuid().ToString("N");
            var payload = new RouletteSpinPayload { SpinId = spinId, Bets = bets };

            Singleton<ClientApplication<ISession>>
                .Instance.GetClientBackEndSession()
                .SendOperationRightNow(
                    payload,
                    operationResult =>
                    {
                        if (operationResult.Failed)
                        {
                            callback(null, operationResult.Error ?? "the server rejected the bet");

                            return;
                        }

                        FetchResult(spinId, callback);
                    }
                );
        }

        private static async void FetchResult(string spinId, Action<RouletteSpinResult, string> callback)
        {
            RouletteSpinResult result = null;
            try
            {
                var json = await RequestHandler.PostJsonAsync(ResultRoute, JsonConvert.SerializeObject(new { spinId }));
                result = string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<RouletteSpinResult>(json);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to fetch spin result: {e}");
            }

            callback(result, result == null ? "could not read the spin result" : null);
        }
    }
}
