using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using HideoutRoulette.Server.Roulette;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;

namespace HideoutRoulette.Server;

[Injectable(InjectionType.Singleton)]
public class RouletteController(
    ISptLogger<RouletteController> logger,
    HttpResponseUtil httpResponseUtil,
    InventoryHelper inventoryHelper,
    ItemHelper itemHelper,
    MailSendService mailSendService,
    ModHelper modHelper
)
{
    private const int DefaultRoubleStackSize = 500000;

    public RouletteConfig Config { get; } =
        modHelper.GetJsonDataFromFile<RouletteConfig>(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "config.json");

    // Outcome of each players most recent spin, the client fetches it after the item event completes
    private readonly ConcurrentDictionary<MongoId, RouletteSpinResult> _lastResults = new();

    /// <summary>
    ///     Handle HideoutRouletteSpin event: take the stake, roll the wheel and pay out
    /// </summary>
    public ItemEventRouterResponse Spin(PmcData pmcData, RouletteSpinRequest? request, MongoId sessionId, ItemEventRouterResponse output)
    {
        var roubleStacks = GetRoubleStacksInStash(pmcData);
        var balance = roubleStacks.Sum(stack => (long)stack.GetItemStackSize());

        var error = RouletteSettlement.Validate(Config, request?.SpinId, request?.Bets, balance);
        if (error is not null)
        {
            return httpResponseUtil.AppendErrorToOutput(output, error);
        }

        var pocket = RandomNumberGenerator.GetInt32(RouletteMath.PocketCount);
        var (totalStake, totalReturn) = RouletteSettlement.Settle(request!.Bets!, pocket);
        var net = totalReturn - totalStake;

        var paidByMail = false;
        if (net < 0)
        {
            RemoveRoubles(pmcData, roubleStacks, -net, sessionId, output);
        }
        else if (net > 0)
        {
            paidByMail = AddRoubles(pmcData, roubleStacks, net, sessionId, output);
        }

        _lastResults[sessionId] = new RouletteSpinResult
        {
            SpinId = request.SpinId!,
            Pocket = pocket,
            Color = RouletteMath.GetColor(pocket).ToString().ToLowerInvariant(),
            TotalStake = totalStake,
            TotalReturn = totalReturn,
            Net = net,
            PaidByMail = paidByMail,
        };

        logger.Debug($"Roulette: {sessionId} staked {totalStake}, pocket {pocket}, net {net}");

        return output;
    }

    /// <summary>
    ///     Get the outcome of a spin, null when the id does not match the players most recent spin
    /// </summary>
    public RouletteSpinResult? GetResult(MongoId sessionId, string? spinId)
    {
        return _lastResults.TryGetValue(sessionId, out var result) && result.SpinId == spinId ? result : null;
    }

    private static List<Item> GetRoubleStacksInStash(PmcData pmcData)
    {
        return pmcData.Inventory!.Items!.Where(item => item.Template == Money.ROUBLES && pmcData.IsItemInStash(item)).ToList();
    }

    private void RemoveRoubles(PmcData pmcData, List<Item> roubleStacks, long amount, MongoId sessionId, ItemEventRouterResponse output)
    {
        var remaining = amount;
        foreach (var stack in roubleStacks)
        {
            var toRemove = (int)Math.Min(stack.GetItemStackSize(), remaining);
            inventoryHelper.RemoveItemByCount(pmcData, stack.Id, toRemove, sessionId, output);

            remaining -= toRemove;
            if (remaining <= 0)
            {
                break;
            }
        }
    }

    /// <returns>True when some of the money did not fit in the stash and was mailed instead</returns>
    private bool AddRoubles(PmcData pmcData, List<Item> roubleStacks, long amount, MongoId sessionId, ItemEventRouterResponse output)
    {
        var maxStackSize = itemHelper.GetItem(Money.ROUBLES).Value?.Properties?.StackMaxSize ?? DefaultRoubleStackSize;

        // Top up existing stacks first so a win rarely needs free stash space
        var remaining = amount;
        foreach (var stack in roubleStacks)
        {
            var space = maxStackSize - stack.GetItemStackSize();
            if (space <= 0)
            {
                continue;
            }

            var toAdd = Math.Min(space, remaining);
            stack.Upd!.StackObjectsCount = stack.GetItemStackSize() + toAdd;
            output.ProfileChanges[sessionId].Items!.ChangedItems!.Add(stack);

            remaining -= toAdd;
            if (remaining <= 0)
            {
                return false;
            }
        }

        var newStacks = itemHelper.SplitStackIntoSeparateItems(
            new Item
            {
                Id = new MongoId(),
                Template = Money.ROUBLES,
                Upd = new Upd { StackObjectsCount = remaining },
            }
        );

        if (inventoryHelper.CanPlaceItemsInInventory(sessionId, newStacks))
        {
            var addRequest = new AddItemsDirectRequest
            {
                ItemsWithModsToAdd = newStacks,
                FoundInRaid = false,
                UseSortingTable = false,
            };
            inventoryHelper.AddItemsToStash(sessionId, addRequest, pmcData, output);

            return false;
        }

        // Stash is full, never drop winnings on the floor
        mailSendService.SendSystemMessageToPlayer(sessionId, "Roulette winnings", newStacks.SelectMany(stack => stack).ToList());

        return true;
    }
}
