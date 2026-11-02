using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Services.Ragfair;

namespace Aishi;

[Injectable(TypePriority = OnLoadOrder.RagfairCallbacks - 1)]
public sealed class AishiItemEconomyPreload(AishiItemEconomy economy) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        economy.PrepareReferencePrices();
        return Task.CompletedTask;
    }
}

[Injectable(TypePriority = int.MaxValue - 5)]
public sealed class AishiItemEconomy(TemplateTable templates, HandbookHelper handbook, RagfairPriceService ragfairPrices, ModHelper mods, AishiAnomalyConfig config, ILogger<AishiItemEconomy> logger) : IOnLoad
{
    private sealed record Definition(MongoId Source, bool Cosmetic);
    private static readonly HashSet<string> PriceNeutralProperties = new(StringComparer.Ordinal)
    {
        "Prefab", "Name", "ShortName", "Description", "BackgroundColor",
        "CanSellOnRagfair", "CanRequireOnRagfair", "InsuranceDisabled", "IsAlwaysAvailableForInsurance",
        "ExaminedByDefault", "FleaBlacklist", "PushToFleaBlacklist", "addtoItemBlacklist"
    };
    private static readonly HashSet<string> PreservedPrices = new(StringComparer.Ordinal)
    {
        "9b9a352c2f205a6eb7dcc69c", "544dac9ec61f5b779d460600"
    };
    private static readonly HashSet<MongoId> FleaItems = [];
    [ThreadStatic] private static string? fleaValidation;

    internal void PrepareReferencePrices() => SynchronizePrices(false);

    private Dictionary<MongoId, Definition> SynchronizePrices(bool report)
    {
        var root = System.IO.Path.Combine(mods.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "db");
        var definitions = ReadDefinitions(root);
        var prices = templates.Handbook.Items.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        var staticPrices = ragfairPrices.GetAllStaticPrices();
        int synchronized = 0;
        foreach (var (id, definition) in definitions)
        {
            if (!definition.Cosmetic || AishiSvmCapture.HasPriceOverride(id)) continue;
            var source = definition.Source;
            var visited = new HashSet<MongoId> { id };
            while (visited.Add(source) && definitions.TryGetValue(source, out var parent) && parent.Cosmetic && !AishiSvmCapture.HasPriceOverride(source))
                source = parent.Source;
            if (visited.Contains(source) && definitions.TryGetValue(source, out var cyclic) && cyclic.Cosmetic && !AishiSvmCapture.HasPriceOverride(source))
                continue;
            if (!templates.Items.ContainsKey(source)) continue;
            bool changed = false;
            if (prices.TryGetValue(source, out var original) && original.Price is double value && double.IsFinite(value) && value > 0 &&
                prices.TryGetValue(id, out var variant))
            {
                variant.Price = value;
                staticPrices[id] = value;
                changed = true;
            }
            if (templates.Prices.TryGetValue(source, out var flea) && double.IsFinite(flea) && flea > 0)
            {
                templates.Prices[id] = flea;
                changed = true;
            }
            else if (templates.Prices.Remove(id))
                changed = true;
            if (changed) synchronized++;
        }
        if (synchronized > 0)
        {
            var cache = AccessTools.Field(typeof(HandbookHelper), "_handbookPriceCache")
                ?? throw new MissingFieldException(nameof(HandbookHelper), "_handbookPriceCache");
            cache.SetValue(handbook, null);
            if (report) logger.LogInformation("[Aishi] Original-item reference prices synchronized for {Count} cosmetic variants.", synchronized);
        }
        return definitions;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitions = SynchronizePrices(true);
        FleaItems.Clear();
        if (config.Debug.RemoveAllFleaBlacklist)
        {
            foreach (var id in definitions.Keys)
                if (templates.Items.TryGetValue(id, out var item) && item.Properties != null)
                {
                    FleaItems.Add(id);
                    item.Properties.CanSellOnRagfair = true;
                    item.Properties.CanRequireOnRagfair = true;
                }
            var harmony = new Harmony("com.samc137.aishi.flea-access");
            harmony.Patch(AccessTools.Method(typeof(RagfairServerHelper), nameof(RagfairServerHelper.IsItemValidRagfairItem)),
                prefix: new HarmonyMethod(typeof(AishiItemEconomy), nameof(BeforeFleaValidation)),
                finalizer: new HarmonyMethod(typeof(AishiItemEconomy), nameof(AfterFleaValidation)));
            harmony.Patch(AccessTools.Method(typeof(ItemFilterService), nameof(ItemFilterService.IsItemBlacklisted)),
                postfix: new HarmonyMethod(typeof(AishiItemEconomy), nameof(AllowDuringFleaValidation)));
            harmony.Patch(AccessTools.Method(typeof(RagfairServerHelper), "IsItemOnCustomFleaBlacklist"),
                postfix: new HarmonyMethod(typeof(AishiItemEconomy), nameof(AllowCustomFleaItem)));
            harmony.Patch(AccessTools.Method(typeof(RagfairServerHelper), "IsItemCategoryOnCustomFleaBlacklist"),
                postfix: new HarmonyMethod(typeof(AishiItemEconomy), nameof(AllowCustomFleaCategory)));
        }
        return Task.CompletedTask;
    }

    private Dictionary<MongoId, Definition> ReadDefinitions(string db)
        {
            var result = new Dictionary<MongoId, Definition>();
            var options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
            foreach (var relative in new[] { "CustomItems", "AishiAndManimalHackDevice/Itens", "AishiAndRuaf/Itens", "AishiAndUntar/Itens" })
            {
                var directory = System.IO.Path.Combine(db, relative);
                if (!Directory.Exists(directory)) continue;
                foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file), options);
                    if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                    foreach (var entry in document.RootElement.EnumerateObject())
                    {
                        var data = entry.Value;
                        if (!MongoId.IsValidMongoId(entry.Name) || !templates.Items.ContainsKey(entry.Name) || data.ValueKind != JsonValueKind.Object ||
                            !data.TryGetProperty("itemTplToClone", out var parent) || parent.ValueKind != JsonValueKind.String ||
                            !MongoId.IsValidMongoId(parent.GetString() ?? "")) continue;
                        bool cosmetic = data.TryGetProperty("overrideProperties", out var overrides) && overrides.ValueKind == JsonValueKind.Object &&
                            overrides.EnumerateObject().All(property => PriceNeutralProperties.Contains(property.Name));
                        if (data.TryGetProperty("syncOriginalPrices", out var setting) && setting.ValueKind == JsonValueKind.False) cosmetic = false;
                        if (PreservedPrices.Contains(entry.Name)) cosmetic = false;
                        if (AishiAnomalyPolicy.Ids.Contains(entry.Name)) cosmetic = false;
                        if (data.TryGetProperty("locales", out var locales) && locales.TryGetProperty("en", out var english) &&
                            english.TryGetProperty("name", out var name) &&
                            (name.GetString()?.Contains("Blue Force Gear", StringComparison.OrdinalIgnoreCase) == true ||
                             name.GetString()?.StartsWith("KTE ", StringComparison.Ordinal) == true))
                            cosmetic = false;
                        result[entry.Name] = new(parent.GetString()!, cosmetic);
                    }
                }
            }
            return result;
        }
    private static void BeforeFleaValidation(KeyValuePair<bool, TemplateItem> __0, out string? __state)
    {
        __state = fleaValidation;
        fleaValidation = __0.Key && __0.Value != null && FleaItems.Contains(__0.Value.Id) ? __0.Value.Id.ToString() : null;
    }

    private static Exception? AfterFleaValidation(Exception? __exception, string? __state)
    {
        fleaValidation = __state;
        return __exception;
    }

    private static void AllowDuringFleaValidation(MongoId __0, ref bool __result)
    {
        if (fleaValidation != null && __0.ToString() == fleaValidation) __result = false;
    }

    private static void AllowCustomFleaItem(MongoId __0, ref bool __result)
    {
        if (FleaItems.Contains(__0)) __result = false;
    }

    private static void AllowCustomFleaCategory(ref bool __result)
    {
        if (fleaValidation != null) __result = false;
    }
}
