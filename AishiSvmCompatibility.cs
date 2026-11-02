using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;
using System.Text.Json;
using System.Text.Json.Nodes;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Tables;
using Microsoft.Extensions.Logging;

namespace Aishi;
internal static class AishiSvmPolicy
{
    private static readonly Lazy<JsonObject?> ActivePreset = new(ReadPreset);
    internal static JsonObject? Preset => ActivePreset.Value;
    internal static bool FreeLabs => Preset?["Raids"]?["EnableRaids"]?.GetValue<bool>() == true && Preset?["Raids"]?["Removelabkey"]?.GetValue<bool>() == true;

    private static JsonObject? ReadPreset()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ServerValueModifier");
        if (assembly == null || string.IsNullOrEmpty(assembly.Location))
            return null;
        var root = Path.GetDirectoryName(assembly.Location)!;
        var loader = Path.Combine(root, "Loader", "loader.json");
        if (!File.Exists(loader))
            return null;
        string? name;
        try { name = JsonNode.Parse(File.ReadAllText(loader))?["CurrentlySelectedPreset"]?.GetValue<string>(); }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException) { return null; }
        if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name)
            return null;
        var file = Path.Combine(root, "Presets", name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json");
        try { return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) as JsonObject : null; }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException) { return null; }
    }

}

[Injectable(TypePriority = int.MaxValue - 200)]
public sealed class AishiSvmCompatibility(TemplateTable templates, TradersTable traders, JsonUtil json, AishiAnomalyConfig config) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AishiSvmCapture.Replay(templates, json);
        ApplyLateContent(templates, traders, AishiSvmPolicy.Preset?["Traders"] as JsonObject);
        ApplyServices(templates, traders, AishiSvmPolicy.Preset?["Services"] as JsonObject);
        PreserveSpecialSlots(templates, new HashSet<MongoId> { "6aa5ff857a930fba7793141b", config.Reveal.RequiredEquippedItem });
        return Task.CompletedTask;
    }

    internal static void ApplyLateContent(TemplateTable templates, TradersTable traders, JsonObject? options)
    {
        if (options?["EnableTraders"]?.GetValue<bool>() != true || !traders.TryGetValue(DollarServicePolicy.AishiId, out var trader)) return;
        bool Enabled(string key) => options[key]?.GetValue<bool>() == true;
        double Number(string key, double fallback = 1) => options[key]?.GetValue<double>() ?? fallback;
        if (!AishiSvmCapture.TraderProcessed)
        {
            foreach (var level in trader.Base.LoyaltyLevels ?? [])
            {
                if (Enabled("TradersLevel")) level.MinLevel = 1;
                if (Enabled("TradersCurrency")) level.MinSalesSum = 0;
                if (Enabled("TradersStanding")) level.MinStanding = 0;
            }
            if (Enabled("UnlockQuestAssort") && trader.QuestAssort?.ContainsKey("success") == true) trader.QuestAssort["success"] = [];
            HashSet<MongoId> money = ["5449016a4bdc2d6f028b456f", "569668774bdc2da2298b4568", "5696686a4bdc2da3298b456a", "5d235b4d86f7742e017bc88a", "6656560053eaaa7a23349c86"];
            foreach (var item in trader.Assort.Items.Where(item => item.ParentId == "hideout"))
            {
                var payment = trader.Assort.BarterScheme.GetValueOrDefault(item.Id)?.FirstOrDefault()?.FirstOrDefault();
                if (payment == null || item.Upd == null) continue;
                bool currency = money.Contains(payment.Template);
                double stock = Number(currency ? "CurrencyOffers" : "BarterOffers");
                double limit = Number(currency ? "CurrencyRestrictions" : "BarterRestrictions");
                item.Upd.StackObjectsCount *= stock;
                if (item.Upd.StackObjectsCount >= 999999 && item.Upd.UnlimitedCount != null && (!currency || !Enabled("RandomizeAssort")))
                { item.Upd.StackObjectsCount = 999999; item.Upd.UnlimitedCount = true; }
                else if (Enabled("RandomizeAssort") && item.Upd.UnlimitedCount != null)
                { item.Upd.UnlimitedCount = false; item.Upd.StackObjectsCount = Random.Shared.Next(1440) * stock; }
                else
                {
                    if (item.Upd.StackObjectsCount < 1 && stock > 0) item.Upd.StackObjectsCount = 1;
                    item.Upd.UnlimitedCount = false;
                }
                item.Upd.BuyRestrictionMax = (int?)(item.Upd.BuyRestrictionMax * limit);
                if (item.Upd.BuyRestrictionMax < 1 && limit > 0) item.Upd.BuyRestrictionMax = 1;
            }
        }
        foreach (var quest in templates.Quests.Values.Where(quest => quest.TraderId == DollarServicePolicy.AishiId && !AishiSvmCapture.QuestsProcessed.Contains(quest.Id)))
        {
            if (quest.Conditions == null) continue;
            foreach (var condition in quest.Conditions.AvailableForStart ?? [])
                if (Enabled("RemoveTimeCondition") && condition.AvailableAfter != null) condition.AvailableAfter = 0;
            foreach (var condition in quest.Conditions.AvailableForFinish ?? [])
            {
                condition.PlantTime *= Number("PlantingTime");
                if (Enabled("FIRRestrictsQuests") && condition.OnlyFoundInRaid != null) condition.OnlyFoundInRaid = false;
            }
            if (Enabled("AllQuestsAvailable")) quest.Conditions.AvailableForStart = [];
        }
    }

    internal static void ApplyServices(TemplateTable templates, TradersTable traders, JsonObject? services)
    {
        if (services == null || !traders.TryGetValue(DollarServicePolicy.AishiId, out var trader)) return;
        if (services["EnableInsurance"]?.GetValue<bool>() == true && services["ReturnChancePrapor"]?.GetValue<double>() == 0 && services["ReturnChanceTherapist"]?.GetValue<double>() == 0 && trader.Base?.Insurance != null)
            trader.Base.Insurance.Availability = false;
        bool anySide = services["ClothesAnySide"]?.GetValue<bool>() == true;
        bool unlock = services["ClothesLevelUnlock"]?.GetValue<bool>() == true;
        bool free = services["ClothesFree"]?.GetValue<bool>() == true;
        foreach (var suit in trader.Suits ?? [])
        {
            if (anySide && templates.Customization.TryGetValue(suit.SuiteId, out var customization) && customization.Properties != null)
            {
                customization.Properties.AvailableAsDefault = true;
                customization.Properties.Game = ["eft"];
                customization.Properties.Side = ["Bear", "Usec", "Savage"];
                customization.Properties.ProfileVersions = [];
            }
            if (unlock)
            {
                suit.IsHiddenInPVE = false;
                if (suit.Requirements != null)
                {
                    suit.Requirements.LoyaltyLevel = 1;
                    suit.Requirements.ProfileLevel = 1;
                    suit.Requirements.Standing = 0;
                    suit.Requirements.PrestigeLevel = 0;
                    suit.Requirements.QuestRequirements = [];
                    suit.Requirements.AchievementRequirements = [];
                }
            }
            if (free && suit.Requirements != null) suit.Requirements.ItemRequirements = [];
        }
    }

    internal static void PreserveSpecialSlots(TemplateTable templates, HashSet<MongoId> allowed)
    {
        allowed.RemoveWhere(id => !templates.Items.ContainsKey(id));
        if (allowed.Count == 0) return;
        foreach (var item in templates.Items.Values)
            foreach (var slot in item.Properties?.Slots ?? [])
            {
                if (slot.Name?.StartsWith("SpecialSlot", StringComparison.OrdinalIgnoreCase) != true || slot.Properties == null) continue;
                var filters = slot.Properties.Filters?.ToList();
                if (filters == null || filters.Count == 0) continue;
                foreach (var filter in filters)
                {
                    filter.Filter ??= [];
                    filter.Filter.UnionWith(allowed);
                }
            }
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload + 4)]
public sealed class AishiSvmCapture(TemplateTable templates, TradersTable traders, ModHelper mods, JsonUtil json, ILogger<AishiSvmCapture> logger) : IOnLoad
{
    private static TemplateTable table = null!;
    private static JsonUtil serializer = null!;
    private static TradersTable traderTable = null!;
    internal static bool TraderProcessed;
    internal static readonly HashSet<MongoId> QuestsProcessed = [];
    private static readonly HashSet<MongoId> owned = [];
    private static readonly Dictionary<MongoId, JsonObject> changes = [];
    private static readonly Dictionary<MongoId, double?> prices = [];
    private static readonly HashSet<MongoId> priceOverrides = [];
    internal static bool HasPriceOverride(MongoId id) => priceOverrides.Contains(id);
    private sealed record Snapshot(Dictionary<MongoId, JsonObject> Items, Dictionary<MongoId, double?> Prices);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(value => value.GetName().Name == "ServerValueModifier");
        if (assembly == null) return Task.CompletedTask;
        var targets = new[] { ("Items", "ItemsSection"), ("CSM", "CSMSection"), ("Services", "ServicesSection"), ("Advanced", "ItemChangerSection"), ("Traders", "TradersSection") }
            .Select(target => assembly.GetType("ServerValueModifier.Sections." + target.Item1)?.GetMethod(target.Item2, BindingFlags.Public | BindingFlags.Instance)).ToArray();
        if (targets.Any(method => method == null))
        {
            logger.LogWarning("[Aishi] SVM section API differs from the supported version; item preservation hooks were not installed. Native SVM changes remain active.");
            return Task.CompletedTask;
        }
        table = templates;
        serializer = json;
        traderTable = traders;
        var db = Path.Combine(mods.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "db");
        foreach (var folder in new[] { "CustomItems", "AishiAndManimalHackDevice/Itens", "AishiAndRuaf/Itens", "AishiAndUntar/Itens" }.Select(path => Path.Combine(db, path)).Where(Directory.Exists))
        foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
            foreach (var entry in document.RootElement.EnumerateObject())
                if (entry.Value.ValueKind == JsonValueKind.Object && entry.Value.TryGetProperty("itemTplToClone", out _) && MongoId.IsValidMongoId(entry.Name)) owned.Add(new MongoId(entry.Name));
        }
        var harmony = new Harmony("com.samc137.aishi.svm-values");
        foreach (var method in targets.Take(4))
        {
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(AishiSvmCapture), nameof(Before)), postfix: new HarmonyMethod(typeof(AishiSvmCapture), nameof(After)));
        }
        harmony.Patch(targets[4], prefix: new HarmonyMethod(typeof(AishiSvmCapture), nameof(BeforeTraders)));
        return Task.CompletedTask;
    }

    private static void BeforeTraders()
    {
        TraderProcessed = traderTable.ContainsKey(DollarServicePolicy.AishiId);
        QuestsProcessed.UnionWith(table.Quests.Keys);
    }

    private static void Before(out Snapshot __state)
    {
        __state = new(owned.Where(id => table.Items.TryGetValue(id, out var item) && item.Properties != null).ToDictionary(id => id, id => JsonNode.Parse(serializer.Serialize(table.Items[id].Properties))!.AsObject()),
            table.Handbook.Items.Where(item => owned.Contains(item.Id)).ToDictionary(item => item.Id, item => item.Price));
    }

    private static void After(Snapshot __state)
    {
        foreach (var (id, previous) in __state.Items)
        {
            if (!table.Items.TryGetValue(id, out var item)) continue;
            var current = JsonNode.Parse(serializer.Serialize(item.Properties))!.AsObject();
            foreach (var field in previous.Select(pair => pair.Key).Union(current.Select(pair => pair.Key)))
            {
                var value = current[field];
                if (JsonNode.DeepEquals(previous[field], value)) continue;
                if (!changes.TryGetValue(id, out var delta)) changes[id] = delta = new JsonObject();
                delta[field] = value?.DeepClone();
            }
        }
        foreach (var item in table.Handbook.Items.Where(item => owned.Contains(item.Id)))
            if (__state.Prices.TryGetValue(item.Id, out var previous) && previous != item.Price)
            {
                prices[item.Id] = item.Price;
                priceOverrides.Add(item.Id);
            }
    }

    internal static void Replay(TemplateTable templates, JsonUtil json)
    {
        foreach (var (id, delta) in changes)
        {
            if (!templates.Items.TryGetValue(id, out var item)) continue;
            var properties = JsonNode.Parse(json.Serialize(item.Properties))!.AsObject();
            foreach (var (field, value) in delta) properties[field] = value?.DeepClone();
            item.Properties = json.Deserialize<TemplateItemProperties>(properties.ToJsonString())!;
        }
        foreach (var item in templates.Handbook.Items)
            if (prices.TryGetValue(item.Id, out var price)) item.Price = price;
        changes.Clear();
        prices.Clear();
    }
}
