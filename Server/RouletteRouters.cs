using HideoutRoulette.Server.Roulette;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace HideoutRoulette.Server;

public static class RouletteRoutes
{
    public const string SpinAction = "HideoutRouletteSpin";
    public const string Result = "/hideoutroulette/result";
    public const string Config = "/hideoutroulette/config";
}

/// <summary>
///     Handles the spin as an item event so stash changes reach the client through the normal profile changes response
/// </summary>
[Injectable]
public class RouletteItemEventRouter(RouletteController rouletteController) : ItemEventRouterDefinition
{
    protected override List<HandledRoute> GetHandledRoutes()
    {
        return [new HandledRoute(RouletteRoutes.SpinAction, false)];
    }

    protected override ValueTask<ItemEventRouterResponse> HandleItemEventInternal(
        string url,
        PmcData pmcData,
        BaseInteractionRequestData body,
        MongoId sessionID,
        ItemEventRouterResponse output
    )
    {
        return url switch
        {
            RouletteRoutes.SpinAction => new ValueTask<ItemEventRouterResponse>(
                rouletteController.Spin(pmcData, body as RouletteSpinRequest, sessionID, output)
            ),
            _ => throw new Exception($"RouletteItemEventRouter being used when it cant handle route {url}"),
        };
    }
}

[Injectable]
public class RouletteStaticRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, RouletteController rouletteController)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction<RouletteResultRequest>(
                RouletteRoutes.Result,
                (url, info, sessionId, output) =>
                    new ValueTask<string>(httpResponseUtil.NoBody(rouletteController.GetResult(sessionId, info.SpinId)))
            ),
            new RouteAction<EmptyRequestData>(
                RouletteRoutes.Config,
                (url, info, sessionId, output) => new ValueTask<string>(httpResponseUtil.NoBody(rouletteController.Config))
            ),
        ]
    ) { }

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader)]
public class RouletteLoad(JsonUtil jsonUtil) : IOnLoad
{
    public Task OnLoad()
    {
        // Lets the server deserialise our custom item event body
        BaseInteractionRequestDataConverter.RegisterModDataHandler(RouletteRoutes.SpinAction, jsonUtil.Deserialize<RouletteSpinRequest>);

        return Task.CompletedTask;
    }
}
