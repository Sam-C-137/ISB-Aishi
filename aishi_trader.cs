using Aishi_trader;
using Microsoft.Extensions.Logging;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;
using System.Reflection;
using WTTServerCommonLib;
using Path = System.IO.Path;

namespace Aishi;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.samc137.aishi";
    public string Name { get; init; } = "ISB Aishi";
    public string Author { get; init; } = "SamC137";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("2.0.1");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        { "com.wtt.commonlib", new SemanticVersioning.Range(">=3.0.3") },
        { "com.wtt.contentbackport", new SemanticVersioning.Range(">=2.0.0") },
    };
    public string? Url { get; init; } = "";
    public string License { get; init; } = "MIT";
    public bool HasPrepatcher { get; init; } = false;
}

[Injectable(TypePriority = OnLoadOrder.Preload + 90000)]
public sealed class AishiItemPreload(
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    IReadOnlyList<SptMod> modList)
    : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assembly = Assembly.GetExecutingAssembly();

        await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly);
        cancellationToken.ThrowIfCancellationRequested();

        if (HasMod("com.manimal.hackermod"))
        {
            await wttCommon.CustomItemServiceExtended.CreateCustomItems(
                assembly,
                Path.Join("db", "AishiAndManimalHackDevice", "Itens"));
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (HasMod("com.ruafcomehome.tacticaltoaster"))
        {
            await wttCommon.CustomItemServiceExtended.CreateCustomItems(
                assembly,
                Path.Join("db", "AishiAndRuaf", "Itens"));
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (HasMod("com.blackdiv.tacticaltoaster"))
        {
            await wttCommon.CustomItemServiceExtended.CreateCustomItems(
                assembly,
                Path.Join("db", "AishiAndBlackDiv", "Itens"));
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (HasMod("com.untargh.tacticaltoaster"))
        {
            await wttCommon.CustomItemServiceExtended.CreateCustomItems(
                assembly,
                Path.Join("db", "AishiAndUntar", "Itens"));
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private bool HasMod(string modGuid)
    {
        return modList.Any(mod => mod.ModMetadata.ModGuid == modGuid);
    }
}

[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 120)]
public sealed class AishiTraderRegistration(
    ModHelper modHelper,
    ImageRouter imageRouter,
    TraderConfig traderConfig,
    RagfairConfig ragfairConfig,
    InsuranceConfig insuranceConfig,
    QuestConfig questConfig,
    IReadOnlyList<SptMod> modList,
    TimeUtil timeUtil,
    AishiLogger aishiLogger,
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    TradersTable tradersTable,
    TemplateTable templateTable,
    ILogger<AishiTraderRegistration> logger)
    : IOnLoad
{
    private const double InsuranceReturnChancePercent = 80d;

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assembly = Assembly.GetExecutingAssembly();
        var pathToMod = modHelper.GetAbsolutePathToModFolder(assembly);
        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(pathToMod, "db/base.json");
        var traderImagePath = Path.Combine(pathToMod, "db/Aishi.png");

        insuranceConfig.ReturnChancePercent[traderBase.Id] = InsuranceReturnChancePercent;
        imageRouter.AddRoute(traderBase.Avatar.Replace(".png", ""), traderImagePath);
        aishiLogger.SetTraderUpdateTime(
            traderConfig,
            traderBase,
            timeUtil.GetHoursAsSeconds(1),
            timeUtil.GetHoursAsSeconds(2));
        ragfairConfig.Traders.TryAdd(traderBase.Id, true);

        aishiLogger.AddTraderWithEmptyAssortToDb(traderBase);
        ConfigureTraderDialogue(traderBase.Id);
        aishiLogger.AddTraderToLocales(
            traderBase,
            "Aishi",
            "She is the commander of ISB, the Intelligence Support Bureau of USEC, which was responsible for dozens of classified operations throughout the territory of Tarkov during the conflict.");

        var assort = modHelper.GetJsonDataFromFile<TraderAssort>(pathToMod, "db/assort.json");
        aishiLogger.OverwriteTraderAssort(traderBase.Id, assort);

        AishiRepeatables.Initialize(pathToMod, templateTable, logger);
        AddAishiToRepeatableQuests(traderBase);

        cancellationToken.ThrowIfCancellationRequested();
        await RegisterCommonContent(assembly, cancellationToken);
        await RegisterOptionalContent(assembly, cancellationToken);
        wttCommon.CustomRigLayoutService.CreateRigLayouts(assembly);

        logger.LogInformation("\x1b[38;2;200;80;220m[ISB Aishi Loaded] “Do you think the Black Division will negotiate? Join us, and let’s show them our bargaining chip.”\x1b[0m");
    }

    private async Task RegisterCommonContent(Assembly assembly, CancellationToken cancellationToken)
    {
        await wttCommon.CustomQuestService.CreateCustomQuests(assembly);
        cancellationToken.ThrowIfCancellationRequested();
        await wttCommon.CustomLocaleService.CreateCustomLocales(assembly);
        cancellationToken.ThrowIfCancellationRequested();
        await wttCommon.CustomQuestZoneService.CreateCustomQuestZones(assembly);
        cancellationToken.ThrowIfCancellationRequested();
        await wttCommon.CustomLootspawnService.CreateCustomLootSpawns(assembly);
        cancellationToken.ThrowIfCancellationRequested();
        await wttCommon.CustomWeaponPresetService.CreateCustomWeaponPresets(assembly);
        cancellationToken.ThrowIfCancellationRequested();
        await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(assembly);
        cancellationToken.ThrowIfCancellationRequested();
        await wttCommon.CustomAchievementService.CreateCustomAchievements(assembly);
        cancellationToken.ThrowIfCancellationRequested();
        await wttCommon.CustomBuffService.CreateCustomBuffs(assembly);
        cancellationToken.ThrowIfCancellationRequested();
        await wttCommon.CustomVoiceService.CreateCustomVoices(assembly);
        cancellationToken.ThrowIfCancellationRequested();
        await wttCommon.CustomHeadService.CreateCustomHeads(assembly);
        cancellationToken.ThrowIfCancellationRequested();
        await wttCommon.CustomClothingService.CreateCustomClothing(assembly);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task RegisterOptionalContent(Assembly assembly, CancellationToken cancellationToken)
    {
        if (HasMod("com.Luna.LunnayalunaLotus"))
        {
            logger.LogInformation("\x1b[38;2;150;60;220m[ISB Aishi] Looks like \"Lotus\" from Lunnayaluna is installed on the server. Enabling some special content.\x1b[0m");
            await wttCommon.CustomQuestService.CreateCustomQuests(
                assembly,
                Path.Join("db", "AishiAndLotusQuests"));
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (HasMod("com.manimal.hackermod"))
        {
            await wttCommon.CustomQuestService.CreateCustomQuests(
                assembly,
                Path.Join("db", "AishiAndManimalHackDevice", "Quests"));
            cancellationToken.ThrowIfCancellationRequested();
            await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(
                assembly,
                Path.Join("db", "AishiAndManimalHackDevice", "Recipes"));
            cancellationToken.ThrowIfCancellationRequested();
            await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(
                assembly,
                Path.Join("db", "AishiAndManimalHackDevice", "Assort"));
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (HasMod("com.ruafcomehome.tacticaltoaster"))
        {
            await wttCommon.CustomQuestService.CreateCustomQuests(
                assembly,
                Path.Join("db", "AishiAndRuaf", "Quests"));
            cancellationToken.ThrowIfCancellationRequested();
            await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(
                assembly,
                Path.Join("db", "AishiAndRuaf", "Recipes"));
            cancellationToken.ThrowIfCancellationRequested();
            await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(
                assembly,
                Path.Join("db", "AishiAndRuaf", "Assort"));
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (HasMod("com.blackdiv.tacticaltoaster"))
        {
            await wttCommon.CustomQuestService.CreateCustomQuests(
                assembly,
                Path.Join("db", "AishiAndBlackDiv", "Quests"));
            cancellationToken.ThrowIfCancellationRequested();
            await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(
                assembly,
                Path.Join("db", "AishiAndBlackDiv", "Recipes"));
            cancellationToken.ThrowIfCancellationRequested();
            await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(
                assembly,
                Path.Join("db", "AishiAndBlackDiv", "Assort"));
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (HasMod("com.untargh.tacticaltoaster"))
        {
            await wttCommon.CustomQuestService.CreateCustomQuests(
                assembly,
                Path.Join("db", "AishiAndUntar", "Quests"));
            cancellationToken.ThrowIfCancellationRequested();
            await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(
                assembly,
                Path.Join("db", "AishiAndUntar", "Recipes"));
            cancellationToken.ThrowIfCancellationRequested();
            await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(
                assembly,
                Path.Join("db", "AishiAndUntar", "Assort"));
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private void ConfigureTraderDialogue(string traderId)
    {
        if (!tradersTable.TryGetValue(traderId, out var trader))
        {
            return;
        }

        trader.Dialogue["insuranceStart"] =
        [
            "6a31f76e04210c72c1b95804 0",
            "6a31f76e04210c72c1b95804 1",
            "6a31f76e04210c72c1b95804 2",
            "6a31f76e04210c72c1b95804 3"
        ];
        trader.Dialogue["insuranceFound"] =
        [
            "6a31f77004210c72c1b95805 0",
            "6a31f77004210c72c1b95805 1",
            "6a31f77004210c72c1b95805 2",
            "6a31f77004210c72c1b95805 3"
        ];
        trader.Dialogue["insuranceExpired"] =
        [
            "6a31f77204210c72c1b95806 0",
            "6a31f77204210c72c1b95806 1",
            "6a31f77204210c72c1b95806 2",
            "6a31f77204210c72c1b95806 3"
        ];
        trader.Dialogue["insuranceComplete"] =
        [
            "6a31f77504210c72c1b95807 0",
            "6a31f77504210c72c1b95807 1",
            "6a31f77504210c72c1b95807 2",
            "6a31f77504210c72c1b95807 3"
        ];
        trader.Dialogue["insuranceFailed"] =
        [
            "6a31f77704210c72c1b95808 0",
            "6a31f77704210c72c1b95808 1",
            "6a31f77704210c72c1b95808 2",
            "6a31f77704210c72c1b95808 3"
        ];
        trader.Dialogue["insuranceFailedLabs"] =
        [
            "6a31f77904210c72c1b95809 0",
            "6a31f77904210c72c1b95809 1",
            "6a31f77904210c72c1b95809 2",
            "6a31f77904210c72c1b95809 3"
        ];
        trader.Dialogue["insuranceFailedLabyrinth"] =
        [
            "6a31f77c04210c72c1b9580a 0",
            "6a31f77c04210c72c1b9580a 1",
            "6a31f77c04210c72c1b9580a 2",
            "6a31f77c04210c72c1b9580a 3"
        ];
    }

    private void AddAishiToRepeatableQuests(TraderBase traderBase)
    {
        string[] repeatableNames = ["Daily", "Weekly"];

        foreach (var repeatableName in repeatableNames)
        {
            if (!AishiRepeatables.IsEnabled(repeatableName))
            {
                if (AishiRepeatables.ShowRepeatableQuestLogs)
                {
                    logger.LogInformation($"[ISB Aishi] {repeatableName} operational tasks are disabled in AishiRepeatables.json.");
                }

                continue;
            }

            var repeatable = questConfig.RepeatableQuests.FirstOrDefault(config =>
                string.Equals(config.Name, repeatableName, StringComparison.OrdinalIgnoreCase));

            if (repeatable is null)
            {
                logger.LogWarning($"[ISB Aishi] Repeatable quest config '{repeatableName}' was not found.");
                continue;
            }

            var configuredQuestTypes = AishiRepeatables.GetQuestTypes(repeatableName);
            var existingEntry = repeatable.TraderWhitelist.FirstOrDefault(entry => entry.TraderId == traderBase.Id);

            if (existingEntry is not null)
            {
                existingEntry.QuestTypes = configuredQuestTypes;
                if (AishiRepeatables.ShowRepeatableQuestLogs)
                {
                    logger.LogInformation($"[ISB Aishi] Updated Aishi {repeatableName} operational task types: {string.Join(", ", configuredQuestTypes)}.");
                }

                continue;
            }

            var rewardTemplate = repeatable.TraderWhitelist.FirstOrDefault(entry =>
                string.Equals(entry.Name, "peacekeeper", StringComparison.OrdinalIgnoreCase));

            if (rewardTemplate is null)
            {
                logger.LogWarning($"[ISB Aishi] Peacekeeper reward template was not found for {repeatableName} operational tasks.");
                continue;
            }

            repeatable.TraderWhitelist.Add(rewardTemplate with
            {
                TraderId = traderBase.Id,
                Name = "aishi",
                QuestTypes = configuredQuestTypes,
                RewardBaseWhitelist = rewardTemplate.RewardBaseWhitelist.ToArray(),
                RewardCanBeWeapon = false,
                WeaponRewardChancePercent = 0
            });

            if (AishiRepeatables.ShowRepeatableQuestLogs)
            {
                logger.LogInformation($"[ISB Aishi] Added Aishi to {repeatableName} operational tasks with types: {string.Join(", ", configuredQuestTypes)}.");
            }
        }
    }

    private bool HasMod(string modGuid)
    {
        return modList.Any(mod => mod.ModMetadata.ModGuid == modGuid);
    }
}

[Injectable(TypePriority = OnLoadOrder.PostLoad + 120)]
public sealed class EditDatabaseValues(
    ISptLogger<EditDatabaseValues> logger,
    LocationTable locationTable,
    IReadOnlyList<SptMod> modList)
    : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EditLaboratory();
        EditLabyrinth();
        logger.Success("\x1b[38;2;200;80;220m[ISB Aishi KCs Loaded] “Permissions granted. All operators’ credentials are back online.”\x1b[0m");
        return Task.CompletedTask;
    }

    private void EditLaboratory()
    {
        var laboratory = locationTable.Laboratory;
        var updatedList = laboratory.Base.AccessKeys.ToList();
        var updatedListPvE = laboratory.Base.AccessKeysPvE.ToList();

        AddKey(updatedList, updatedListPvE, "69439100a320344f805ba61f");
        AddKey(updatedList, updatedListPvE, "694479f61287f9a2b1060a7f");
        AddKey(updatedList, updatedListPvE, "6944b4b15a0413f1b7f50724");
        AddKey(updatedList, updatedListPvE, "69860f957139b00c69e69cb7");
        AddKey(updatedList, updatedListPvE, "698610e3b949497163d3d742");
        AddKey(updatedList, updatedListPvE, "6a0a5091eb733b78d06f82b2");

        if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.manimal.hackermod"))
        {
            AddKey(updatedList, updatedListPvE, "6a321d846b38e922175d1878");
        }

        laboratory.Base.AccessKeys = updatedList.ToArray();
        laboratory.Base.AccessKeysPvE = updatedListPvE.ToArray();

        foreach (var exit in laboratory.Base.Exits)
        {
            exit.Chance = 100;
            exit.ChancePVE = 100;
        }
    }

    private void EditLabyrinth()
    {
        var labyrinth = locationTable.Labyrinth;
        var updatedList = labyrinth.Base.AccessKeys.ToList();
        var updatedListPvE = labyrinth.Base.AccessKeysPvE.ToList();

        AddKey(updatedList, updatedListPvE, "694479f61287f9a2b1060a7f");
        AddKey(updatedList, updatedListPvE, "6944b4b15a0413f1b7f50724");

        labyrinth.Base.AccessKeys = updatedList.ToArray();
        labyrinth.Base.AccessKeysPvE = updatedListPvE.ToArray();

        foreach (var exit in labyrinth.Base.Exits)
        {
            exit.Chance = 100;
            exit.ChancePVE = 100;
        }
    }

    private static void AddKey(List<string> accessKeys, List<string> accessKeysPvE, string keyId)
    {
        if (!accessKeys.Contains(keyId))
        {
            accessKeys.Add(keyId);
        }

        if (!accessKeysPvE.Contains(keyId))
        {
            accessKeysPvE.Add(keyId);
        }
    }
}
