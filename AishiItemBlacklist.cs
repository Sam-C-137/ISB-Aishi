using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.Server.Core.Extensions;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
#if AISHI_SPT41
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
#else
using SPTarkov.Server.Core.Generators;
#endif
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Bots;
using Path = System.IO.Path;

namespace Aishi;
#if AISHI_SPT41
[Injectable(TypePriority = int.MaxValue - 100)]
#else
[Injectable(TypePriority = int.MaxValue - 100)]
#endif
public sealed class AishiItemBlacklistPatch(ModHelper modHelper, ILogger<AishiItemBlacklistPatch> logger, ItemHelper itemHelper, InventoryHelper inventoryHelper) : IOnLoad
{
    public const string HarmonyId = "com.samc137.aishi.itemblacklist";
    public static AishiItemBlacklist Policy { get; private set; } = new();

#if AISHI_SPT41
    public Task OnLoadAsync(CancellationToken cancellationToken)
#else
    public Task OnLoad()
#endif
    {
#if AISHI_SPT41
        cancellationToken.ThrowIfCancellationRequested();
#endif
        var root = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        try
        {
            Policy = AishiItemBlacklist.Load(root);
            Policy.TemplateExists = id => itemHelper.GetItem(new MongoId(id)).Key;
            Policy.ContainerRepair = RepairContainer;
            Policy.AmmoBoxRepair = RepairAmmoBoxes;
            var harmony = new Harmony(HarmonyId);
            var generation = AccessTools.Method(typeof(BotGenerator), "GenerateBot") ?? throw new MissingMethodException("BotGenerator.GenerateBot");
            var prepared = AccessTools.Method(typeof(BotGenerator), nameof(BotGenerator.PrepareAndGenerateBot)) ?? throw new MissingMethodException("BotGenerator.PrepareAndGenerateBot");
            var batch = AccessTools.Method(typeof(BotController), nameof(BotController.Generate)) ?? throw new MissingMethodException("BotController.Generate");
            harmony.Patch(generation, prefix: new HarmonyMethod(typeof(AishiItemBlacklistPatch), nameof(FilterPools)) { priority = Priority.Last }, postfix: new HarmonyMethod(typeof(AishiItemBlacklistPatch), nameof(FilterGenerated)) { priority = Priority.Last, after = Owners(generation) });
            harmony.Patch(prepared, postfix: new HarmonyMethod(typeof(AishiItemBlacklistPatch), nameof(FilterGenerated)) { priority = Priority.Last, after = Owners(prepared) });
            harmony.Patch(batch, postfix: new HarmonyMethod(typeof(AishiItemBlacklistPatch), nameof(FilterBatch)) { priority = Priority.Last, after = Owners(batch) });
            InstallWorldGuards(harmony);
            AishiRepeatableBlacklist.Install(harmony);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[ISB] Equipment blacklist could not be initialized.");
            throw;
        }

        return Task.CompletedTask;
    }

    private void RepairAmmoBoxes(List<Item> inventory, bool unrestricted)
    {
        foreach (var parent in inventory.ToArray())
        {
            var template = itemHelper.GetItem(parent.Template).Value;
            var stack = template?.Properties?.StackSlots?.FirstOrDefault();
            if (stack?.MaxCount <= 0 || stack?.Properties?.Filters?.FirstOrDefault()?.Filter?.FirstOrDefault() is not MongoId cartridge ||
                cartridge.ToString() == "000000000000000000000000" || !itemHelper.GetItem(cartridge).Key ||
                !unrestricted && (Policy.Contains(parent.Template.ToString()) || Policy.Contains(cartridge.ToString())) ||
                inventory.Any(item => item.ParentId == parent.Id.ToString() && item.SlotId == "cartridges"))
                continue;
            var filled = new List<Item> { parent };
            itemHelper.AddCartridgesToAmmoBox(filled, template!);
            inventory.AddRange(filled.Skip(1));
        }
    }

    private void RepairContainer(List<Item> inventory, Item parent)
    {
        var grids = itemHelper.GetItem(parent.Template).Value?.Properties?.Grids?.ToList();
        if (grids == null || grids.Count == 0)
            return;
        var children = inventory.Where(item => item.ParentId == parent.Id.ToString() && item.GetParsedLocation() != null).ToList();
        if (children.Count == 0)
            return;
        var slots = grids.Where(grid => grid.Properties?.CellsH > 0 && grid.Properties?.CellsV > 0).Select(grid => new { grid, map = inventoryHelper.GetContainerMap(grid.Properties.CellsH.Value, grid.Properties.CellsV.Value, Array.Empty<Item>(), parent.Id) }).ToList();
        var dimensions = children.ToDictionary(item => item.Id, item => inventoryHelper.GetItemSize(item.Template, item.Id, inventory));
        children.Sort((left, right) => (dimensions[right.Id].Item1 * dimensions[right.Id].Item2).CompareTo(dimensions[left.Id].Item1 * dimensions[left.Id].Item2));
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in children)
        {
            var subtree = new List<Item>
            {
                child
            };
            subtree.AddRange(itemHelper.FindAndReturnChildrenByAssort(child.Id, inventory));
            bool placed = false;
            foreach (var slot in slots.OrderBy(slot => slot.grid.Name == child.SlotId ? 0 : 1))
            {
                if (slot.grid.Properties.Filters?.Any(filter => filter.ExcludedFilter?.Any() == true && (filter.ExcludedFilter.Contains(child.Template) || itemHelper.IsOfBaseclasses(child.Template, filter.ExcludedFilter)) || filter.Filter?.Any() == true && !filter.Filter.Contains(child.Template) && !itemHelper.IsOfBaseclasses(child.Template, filter.Filter)) == true)
                    continue;
                if (inventoryHelper.PlaceItemInContainer(slot.map, subtree, parent.Id.ToString(), slot.grid.Name).Success == true)
                {
                    placed = true;
                    break;
                }
            }

            if (!placed)
                foreach (var item in subtree)
                    removed.Add(item.Id.ToString());
        }

        if (removed.Count > 0)
            inventory.RemoveAll(item => removed.Contains(item.Id.ToString()));
    }

    private static string[] Owners(MethodBase target) => Harmony.GetPatchInfo(target)?.Owners.Where(owner => owner != HarmonyId).Distinct().ToArray() ?? [];
    private static void InstallWorldGuards(Harmony harmony)
    {
        var lifecycle = typeof(SPTarkov.Server.Core.Services.InRaid.LocationLifecycleService);
        foreach (var(target, patch)in new[]
        {
            (AccessTools.Method(lifecycle, "GenerateLocationAndLoot"), nameof(FilterLocation)),
            (AccessTools.Method(lifecycle, "StartLocalRaidAsync"), nameof(FilterRaidAsync))
        }

        )
        {
            if (target == null)
                throw new MissingMethodException("Aishi: native world loot boundary is missing.");
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(AishiItemBlacklistPatch), patch) { priority = Priority.Last, after = Owners(target) });
        }
    }

    private static void FilterLocation(SPTarkov.Server.Core.Models.Eft.Common.LocationBase __result) => Policy.FilterWorld(__result);
    private static void FilterRaid(SPTarkov.Server.Core.Models.Eft.Match.StartLocalRaidResponseData __result) => Policy.FilterWorld(__result?.LocationLoot);
    private static void FilterRaidAsync(ref Task<SPTarkov.Server.Core.Models.Eft.Match.StartLocalRaidResponseData> __result) => __result = FinishRaid(__result);
    private static async Task<SPTarkov.Server.Core.Models.Eft.Match.StartLocalRaidResponseData> FinishRaid(Task<SPTarkov.Server.Core.Models.Eft.Match.StartLocalRaidResponseData> task)
    {
        var result = await task.ConfigureAwait(false);
        Policy.FilterWorld(result?.LocationLoot);
        return result!;
    }

    private static string Role(BotGenerationDetails details) => details.EventRole ?? details.Role;
    private static void FilterPools(BotType botJsonTemplate, BotGenerationDetails botGenerationDetails) => Policy.FilterPools(botJsonTemplate, Role(botGenerationDetails));
    private static void FilterGenerated(BotBase __result, BotGenerationDetails botGenerationDetails) => Policy.Sanitize(__result, Role(botGenerationDetails));
    private static void FilterBatch(ref Task<IEnumerable<BotBase?>> __result) => __result = FilterBatchAsync(__result);
    private static async Task<IEnumerable<BotBase?>> FilterBatchAsync(Task<IEnumerable<BotBase?>> task)
    {
        var result = (await task.ConfigureAwait(false)).ToList();
        foreach (var bot in result)
            Policy.Sanitize(bot, bot?.Info?.Settings?.Role);
        return result;
    }
}

public sealed class AishiItemBlacklist
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    private readonly HashSet<string> blocked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> clones = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> valid = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> isbRoles = new(StringComparer.OrdinalIgnoreCase);
    private bool enabled = true;
    private readonly HashSet<string> questItems = new(StringComparer.OrdinalIgnoreCase);
    private bool blockWorld = true, preserveExplicit = true;
    private static readonly HashSet<string> blackDivisionRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "848420",
        "848421",
        "848422",
        "848423",
        "848424",
        "848426",
        "blackDivLead",
        "blackDivAssault",
        "blackDivBreacher",
        "blackDivSupport",
        "bossWedge",
        "blackDivIb",
        "blackDivGhostBusterLead",
        "blackDivGhostBuster01",
        "blackDivGhostBuster02",
        "blackDivGhostBuster03",
        "blackDivGhostBuster04",
        "bossWedgeGhostBuster"
    };
    public bool AllowBlackDivision { get; private set; } = true;
    public IReadOnlySet<string> ItemIds => blocked;
    public Action<List<Item>, Item>? ContainerRepair { get; set; }
    public Action<List<Item>, bool>? AmmoBoxRepair { get; set; }
    public Func<string, bool>? TemplateExists { get; set; }

    public static AishiItemBlacklist Load(string root)
    {
        var policy = new AishiItemBlacklist();
        for (int i = 13701; i <= 13716; i++)
            policy.isbRoles.Add(i.ToString());
        using (var roles = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aishi.ISBBotTypes.json"))
        {
            if (roles == null)
                throw new InvalidDataException("ISB bot role registry is missing.");
            using var document = JsonDocument.Parse(roles);
            CollectRoles(document.RootElement, policy.isbRoles);
        }

        var configPath = Path.Combine(root, "db", "ISBEquipmentBlacklist.jsonc");
        using var config = JsonDocument.Parse(File.ReadAllText(configPath), DocumentOptions);
        var settings = config.RootElement;
        if (settings.TryGetProperty("enabled", out var enabledValue))
            policy.enabled = enabledValue.GetBoolean();
        if (settings.TryGetProperty("allowBlackDivision", out var bd))
            policy.AllowBlackDivision = bd.GetBoolean();
        var modsRoot = Directory.GetParent(root)!.FullName;
        var serverRoot = Directory.GetParent(Directory.GetParent(modsRoot)!.FullName)!.FullName;
        var parents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(serverRoot, "SPT_Data", "database", "templates", "items.json"))))
            foreach (var entry in native.RootElement.EnumerateObject())
            {
                policy.valid.Add(entry.Name);
                if (entry.Value.TryGetProperty("_parent", out var parent) && parent.ValueKind == JsonValueKind.String)
                    parents[entry.Name] = parent.GetString()!;
            }

        bool auto = !settings.TryGetProperty("autoIncludeIsbItems", out var autoValue) || autoValue.GetBoolean();
        foreach (var owner in Directory.EnumerateDirectories(modsRoot).Where(p => Path.GetFileName(p).StartsWith("ISB-Aishi", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).StartsWith("ISB-SpecialForces", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(p).StartsWith("ISBSpecialForces", StringComparison.OrdinalIgnoreCase)))
        {
            var folder = Path.Combine(owner, "db");
            if (!Directory.Exists(folder))
                continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file), DocumentOptions);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var entry in document.RootElement.EnumerateObject())
                {
                    if (!MongoId.IsValidMongoId(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object || !entry.Value.TryGetProperty("itemTplToClone", out var clone))
                        continue;
                    policy.clones[entry.Name] = clone.GetString()!;
                    if (entry.Value.TryGetProperty("overrideProperties", out var properties) && properties.TryGetProperty("QuestItem", out var quest) && quest.ValueKind == JsonValueKind.True)
                        policy.questItems.Add(entry.Name);
                    if (auto && !policy.valid.Contains(entry.Name))
                        policy.blocked.Add(entry.Name);
                }
            }
        }

        foreach (var id in policy.clones.Keys)
        {
            policy.valid.Add(id);
            var cursor = id;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (visited.Add(cursor))
            {
                if (cursor is "644120aa86ffbe10ee032b6f" or "65649eb40bf0ed77b8044453")
                {
                    policy.blocked.Remove(id);
                    break;
                }

                if (policy.clones.TryGetValue(cursor, out var clone))
                    cursor = clone;
                else if (parents.TryGetValue(cursor, out var parent))
                    cursor = parent;
                else
                    break;
            }
        }

        if (settings.TryGetProperty("categories", out var categories))
            foreach (var category in categories.EnumerateObject())
                foreach (var id in category.Value.EnumerateArray())
                    policy.blocked.Add(id.GetString()!);
        if (settings.TryGetProperty("itemIds", out var ids))
            foreach (var id in ids.EnumerateArray())
                policy.blocked.Add(id.GetString()!);
        var allowedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (settings.TryGetProperty("allowedItemIDs", out var allowed))
            CollectItemIds(allowed, allowedIds);
        else if (settings.TryGetProperty("excludedItemIds", out var legacy))
            CollectItemIds(legacy, allowedIds);
        policy.blocked.ExceptWith(allowedIds);
        if (settings.TryGetProperty("blockWorldLoot", out var world))
            policy.blockWorld = world.GetBoolean();
        if (settings.TryGetProperty("preserveExplicitEventLoot", out var explicitLoot))
            policy.preserveExplicit = explicitLoot.GetBoolean();
        foreach (var id in policy.blocked)
            if (!MongoId.IsValidMongoId(id))
                throw new InvalidDataException("Invalid item ID in AishiItemBlacklist: " + id);
        return policy;
    }

    private static void CollectItemIds(JsonElement element, HashSet<string> ids)
    {
        if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || !MongoId.IsValidMongoId(item.GetString()))
                    throw new InvalidDataException("Invalid item ID in allowedItemIDs.");
                ids.Add(item.GetString()!);
            }
        else if (element.ValueKind == JsonValueKind.Object)
            foreach (var category in element.EnumerateObject())
                CollectItemIds(category.Value, ids);
        else
            throw new InvalidDataException("allowedItemIDs must be an array or an object of category arrays.");
    }

    private static void CollectRoles(JsonElement element, HashSet<string> roles)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var entry in element.EnumerateObject())
            {
                if (entry.Name.StartsWith("ISB", StringComparison.OrdinalIgnoreCase))
                    roles.Add(entry.Name);
                CollectRoles(entry.Value, roles);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var entry in element.EnumerateArray())
                CollectRoles(entry, roles);
        else if (element.ValueKind == JsonValueKind.String && element.GetString()!.StartsWith("ISB", StringComparison.OrdinalIgnoreCase))
            roles.Add(element.GetString()!);
    }

    public bool Allows(string? role) => !enabled || role != null && (isbRoles.Contains(role) || role.Equals("chaosInsurgency", StringComparison.OrdinalIgnoreCase) || role == "13901" || AllowBlackDivision && blackDivisionRoles.Contains(role));
    public bool Contains(string id) => enabled && blocked.Contains(id);
    private string? Replacement(string id)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (blocked.Contains(id))
        {
            if (!visited.Add(id) || !clones.TryGetValue(id, out var clone))
                return null;
            id = clone;
        }

        return (TemplateExists?.Invoke(id) ?? valid.Contains(id)) ? id : null;
    }

    public void FilterPools(BotType template, string? role)
    {
        if (Allows(role) || !enabled || template?.BotInventory == null)
            return;
        var inventory = template.BotInventory;
        if (inventory.Equipment != null)
            foreach (var pool in inventory.Equipment.Values)
                Filter(pool);
        if (inventory.Ammo != null)
            foreach (var pool in inventory.Ammo.Values)
                Filter(pool);
        if (inventory.Items != null)
        {
            Filter(inventory.Items.Backpack);
            Filter(inventory.Items.Pockets);
            Filter(inventory.Items.SecuredContainer);
            Filter(inventory.Items.TacticalVest);
            Filter(inventory.Items.SpecialLoot);
        }

        if (inventory.Mods != null)
        {
            foreach (var id in inventory.Mods.Keys.Where(id => blocked.Contains(id.ToString())).ToArray())
            {
                var replacement = Replacement(id.ToString());
                if (replacement != null && !inventory.Mods.ContainsKey(replacement))
                    inventory.Mods[replacement] = inventory.Mods[id];
                inventory.Mods.Remove(id);
            }
            foreach (var slots in inventory.Mods.Values)
                foreach (var pool in slots.Values)
                    foreach (var id in pool.Where(id => blocked.Contains(id.ToString())).ToArray())
                    {
                        pool.Remove(id);
                        var replacement = Replacement(id.ToString());
                        if (replacement != null) pool.Add(replacement);
                    }
        }
    }

    private void Filter(Dictionary<MongoId, double>? pool)
    {
        if (pool == null)
            return;
        var forbidden = pool.Where(row => blocked.Contains(row.Key.ToString())).ToArray();
        foreach (var row in forbidden)
            pool.Remove(row.Key);
        if (pool.Count == 0)
            foreach (var row in forbidden)
            {
                var replacement = Replacement(row.Key.ToString());
                if (replacement != null)
                    pool[new MongoId(replacement)] = row.Value;
            }
    }

    public void FilterWorld(SPTarkov.Server.Core.Models.Eft.Common.LocationBase? location)
    {
        if (!enabled || !blockWorld || location?.Loot == null)
            return;
        var points = location.Loot.ToList();
        foreach (var point in points.ToArray())
        {
            if (point?.Items == null)
                continue;
            bool preserve = preserveExplicit && (point.Id?.StartsWith("ISB_CB_LOOT_", StringComparison.Ordinal) == true || point.Id?.StartsWith("ISB_CB_CASE_", StringComparison.Ordinal) == true || point.Items.Any(item => item.Id.ToString() == point.Root && questItems.Contains(item.Template.ToString())));
            var rows = point.Items.ToList();
            var removed = preserve ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : rows.Where(item => blocked.Contains(item.Template.ToString())).Select(item => item.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool changed;
            do
            {
                changed = false;
                foreach (var item in rows)
                    if (item.ParentId != null && removed.Contains(item.ParentId.ToString()))
                        changed |= removed.Add(item.Id.ToString());
            }
            while (changed);
            if (point.Root != null && removed.Contains(point.Root))
            {
                points.Remove(point);
                continue;
            }

            var filtered = rows.Where(item => !removed.Contains(item.Id.ToString())).ToList();
            if (AmmoBoxRepair != null)
            {
                var expanded = filtered.Cast<Item>().ToList();
                AmmoBoxRepair(expanded, preserve);
                foreach (var cartridge in expanded.Skip(filtered.Count))
                    filtered.Add(new SPTarkov.Server.Core.Models.Eft.Common.SptLootItem
                    {
                        Id = cartridge.Id, Template = cartridge.Template, ParentId = cartridge.ParentId,
                        SlotId = cartridge.SlotId, Location = cartridge.Location, Upd = cartridge.Upd
                    });
            }
            point.Items = filtered;
        }

        location.Loot = points;
    }

    public void Sanitize(BotBase? bot, string? role)
    {
        if (!enabled || bot?.Inventory?.Items == null)
            return;
        var items = bot.Inventory.Items;
        if (Allows(role))
        {
            AmmoBoxRepair?.Invoke(items, true);
            return;
        }
        List<Item>? replaced = null;
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!blocked.Contains(item.Template.ToString()))
                continue;
            var replacement = Replacement(item.Template.ToString());
            if (replacement != null)
            {
                item.Template = new MongoId(replacement);
                (replaced ??= new()).Add(item);
            }
            else
                removed.Add(item.Id.ToString());
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var item in items)
                if (item.ParentId != null && removed.Contains(item.ParentId.ToString()))
                    changed |= removed.Add(item.Id.ToString());
        }
        while (changed);
        items.RemoveAll(item => removed.Contains(item.Id.ToString()));
        if (replaced != null && ContainerRepair != null)
            foreach (var parent in replaced)
                if (items.Contains(parent))
                    ContainerRepair(items, parent);
        AmmoBoxRepair?.Invoke(items, false);
    }
}
