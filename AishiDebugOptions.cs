using HarmonyLib;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Aishi;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1400)]
public sealed class AishiDebugOptions(TradersTable traders, AishiAnomalyConfig config) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!traders.TryGetValue(DollarServicePolicy.AishiId, out var trader)) return Task.CompletedTask;
        if (config.Debug.InfiniteAssortStock)
        {
            _instance = this;
            ApplyStock(trader.Assort?.Items);
            var harmony = new Harmony("com.samc137.aishi.debug.stock");
            harmony.Patch(AccessTools.Method(typeof(TraderAssortHelper), nameof(TraderAssortHelper.GetAssort)), postfix: new HarmonyMethod(typeof(AishiDebugOptions), nameof(AssortStock)) { priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(TraderAssortHelper), "GetPristineTraderAssorts"), postfix: new HarmonyMethod(typeof(AishiDebugOptions), nameof(PristineStock)) { priority = Priority.Last });
        }
        if (config.Debug.UnlockAllClothes)
            foreach (var suit in trader.Suits ?? []) Unlock(suit);
        return Task.CompletedTask;
    }

    private static AishiDebugOptions? _instance;

    internal static void ApplyStock(IEnumerable<Item>? items)
    {
        foreach (var item in items ?? [])
        {
            if (item.ParentId != "hideout") continue;
            item.Upd ??= new Upd();
            item.Upd.StackObjectsCount = 999999;
            item.Upd.UnlimitedCount = true;
        }
    }

    private static void AssortStock(MongoId traderId, TraderAssort __result)
    {
        if (traderId.ToString() != DollarServicePolicy.AishiId || _instance == null) return;
        ApplyStock(__result?.Items);
        if (_instance.CurrentTrader() is { } trader) ApplyStock(trader.Assort?.Items);
    }

    private Trader? CurrentTrader() => traders.TryGetValue(DollarServicePolicy.AishiId, out var trader) ? trader : null;

    private static void PristineStock(MongoId traderId, List<Item> __result)
    {
        if (traderId.ToString() == DollarServicePolicy.AishiId) ApplyStock(__result);
    }

    internal static void Unlock(Suit suit)
    {
        MongoId[] money = ["5449016a4bdc2d6f028b456f", "5696686a4bdc2da3298b456a", "569668774bdc2da2298b4568"];
        var currency = suit.Requirements?.ItemRequirements?.FirstOrDefault(item => money.Contains(item.Tpl))?.Tpl ?? money[1];
        suit.IsActive = true;
        suit.ExternalObtain = false;
        suit.InternalObtain = true;
        suit.IsHiddenInPVE = false;
        suit.Requirements = new SuitRequirements
        {
            LoyaltyLevel = 1,
            ProfileLevel = 1,
            PrestigeLevel = 0,
            Standing = 0,
            QuestRequirements = [],
            AchievementRequirements = [],
            SkillRequirements = [],
            ItemRequirements = [new ItemRequirement { Tpl = currency, Count = 1 }],
            RequiredTid = DollarServicePolicy.AishiId
        };
    }
}
