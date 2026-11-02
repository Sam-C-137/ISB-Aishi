using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Servers;
using Path = System.IO.Path;
#if AISHI_SPT41
using SPTarkov.Server.Core.Services.Server;
using SPTarkov.Server.Core.Services.Commerce;
#else
using SPTarkov.Server.Core.Services;
#endif

namespace Aishi;

[Injectable(TypePriority = int.MaxValue - 21)]
#if AISHI_SPT41
public sealed class AishiFenceCatalog(TraderConfig configs) : IOnLoad
#else
public sealed class AishiFenceCatalog(ConfigServer configs) : IOnLoad
#endif
{
    private static readonly HashSet<MongoId> BlueForce = [];
#if AISHI_SPT41
    public Task OnLoadAsync(CancellationToken token)
#else
    public Task OnLoad()
#endif
    {
        var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "db", "CustomItems"), "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var item in document.RootElement.EnumerateObject())
                if (item.Value.TryGetProperty("locales", out var locales) && locales.TryGetProperty("en", out var english) && english.TryGetProperty("name", out var name) && name.GetString()?.Contains("Blue Force Gear", StringComparison.OrdinalIgnoreCase) == true)
                    BlueForce.Add(item.Name);
        }
#if AISHI_SPT41
        var fence = configs.Fence;
#else
        var fence = configs.GetConfig<TraderConfig>().Fence;
#endif
        foreach (var id in BlueForce)
            if (!fence.Blacklist.Contains(id))
                fence.Blacklist.Add(id);
        foreach (var id in AishiItemBlacklistPatch.Policy.ItemIds.Where(AishiItemBlacklistPatch.Policy.Contains))
            if (!fence.Blacklist.Contains(new MongoId(id)))
                fence.Blacklist.Add(new MongoId(id));
        var harmony = new Harmony("com.samc137.aishi.fence-catalog");
        harmony.Patch(AccessTools.Method(typeof(FenceService), "AddItemAssorts"), prefix: new HarmonyMethod(typeof(AishiFenceCatalog), nameof(FilterPool)));
        foreach (var name in new[] { "GetFenceAssorts", "GetRawFenceAssorts", "GetMainFenceAssort", "GetDiscountFenceAssort" })
        {
            var method = AccessTools.Method(typeof(FenceService), name) ?? throw new MissingMethodException("FenceService." + name);
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(AishiFenceCatalog), nameof(FilterReturned)) { priority = Priority.Last });
        }
        return Task.CompletedTask;
    }

    private static void FilterReturned(TraderAssort __result) => Filter(__result);
    private static void FilterPool(TraderAssort __2) => Filter(__2);
    internal static void Filter(TraderAssort? assort)
    {
        if (assort?.Items == null || assort.Items.Count == 0) return;
        var items = assort.Items.ToDictionary(item => item.Id.ToString());
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in assort.Items.Where(item => BlueForce.Contains(item.Template) || AishiItemBlacklistPatch.Policy.Contains(item.Template.ToString())))
        {
            var root = item;
            var visited = new HashSet<string>();
            while (root.ParentId != null && items.TryGetValue(root.ParentId, out var parent) && visited.Add(root.Id.ToString()))
                root = parent;
            removed.Add(root.Id.ToString());
        }
        int count;
        do
        {
            count = removed.Count;
            foreach (var item in assort.Items.Where(item => item.ParentId != null && removed.Contains(item.ParentId)))
                removed.Add(item.Id.ToString());
        } while (count != removed.Count);
        assort.Items.RemoveAll(item => removed.Contains(item.Id.ToString()));
        foreach (var id in removed)
        {
            assort.BarterScheme?.Remove(id);
            assort.LoyalLevelItems?.Remove(id);
        }
    }
}
