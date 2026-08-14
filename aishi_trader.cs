using Aishi_trader;
using SPTarkov.Common.Models.Logging;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.Hideout;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;
using System.Collections;
using System.Reflection;
using WTTServerCommonLib;
using WTTServerCommonLib.Services;
using Path = System.IO.Path;

namespace Aishi;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.samc137.aishi";
    public string Name { get; init; } = "ISB Aishi";
    public string Author { get; init; } = "SamC137";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("2.0.0");
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

[Injectable(TypePriority = OnLoadOrder.PostLoad + 120)]
public class EditDatabaseValues(
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

        logger.Success("\x1b[38;2;200;80;220m[ISB Aishi KCs Loaded] \u201cPermissions granted. All operators\u2019 credentials are back online.\u201d\x1b[0m");

        return Task.CompletedTask;
    }

    private void EditLaboratory()
    {
        var laboratory = locationTable.Laboratory;

        var updatedList = laboratory.Base.AccessKeys.ToList();
        var updatedListPvE = laboratory.Base.AccessKeysPvE.ToList();

        updatedList.Add("69439100a320344f805ba61f");
        updatedListPvE.Add("69439100a320344f805ba61f");
        updatedList.Add("694479f61287f9a2b1060a7f");
        updatedListPvE.Add("694479f61287f9a2b1060a7f");
        updatedList.Add("6944b4b15a0413f1b7f50724");
        updatedListPvE.Add("6944b4b15a0413f1b7f50724");
        updatedList.Add("69860f957139b00c69e69cb7");
        updatedListPvE.Add("69860f957139b00c69e69cb7");
        updatedList.Add("698610e3b949497163d3d742");
        updatedListPvE.Add("698610e3b949497163d3d742");
        updatedList.Add("6a0a5091eb733b78d06f82b2");
        updatedListPvE.Add("6a0a5091eb733b78d06f82b2");

        if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.manimal.hackermod"))
        {
            updatedList.Add("6a321d846b38e922175d1878");
            updatedListPvE.Add("6a321d846b38e922175d1878");
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

        updatedList.Add("694479f61287f9a2b1060a7f");
        updatedListPvE.Add("694479f61287f9a2b1060a7f");
        updatedList.Add("6944b4b15a0413f1b7f50724");
        updatedListPvE.Add("6944b4b15a0413f1b7f50724");

        labyrinth.Base.AccessKeys = updatedList.ToArray();
        labyrinth.Base.AccessKeysPvE = updatedListPvE.ToArray();

        foreach (var exit in labyrinth.Base.Exits)
        {
            exit.Chance = 100;
            exit.ChancePVE = 100;
        }
    }

    [Injectable(TypePriority = OnLoadOrder.PostLoad + 720)]
    public class AddTraderWithAssortJson(
        ModHelper modHelper,
        ImageRouter imageRouter,
        TraderConfig traderConfig,
        RagfairConfig ragfairConfig,
        InsuranceConfig insuranceConfig,
        QuestConfig questConfig,
        IReadOnlyList<SptMod> modList,
        TimeUtil timeUtil,
        AishiLogger AishiLogger,
        WTTServerCommonLib.WTTServerCommonLib wttCommon,
        TradersTable tradersTable,
        TemplateTable templateTable,
        ILogger<AddTraderWithAssortJson> logger)
        : IOnLoad
    {
        private const double InsuranceReturnChancePercent = 80d;

        private readonly TraderConfig _traderConfig = traderConfig;
        private readonly RagfairConfig _ragfairConfig = ragfairConfig;
        private readonly InsuranceConfig _insuranceConfig = insuranceConfig;
        private readonly QuestConfig _questConfig = questConfig;

        public async Task OnLoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

            var traderImagePath = Path.Combine(pathToMod, "db/Aishi.png");

            var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(pathToMod, "db/base.json");

            _insuranceConfig.ReturnChancePercent[traderBase.Id] = InsuranceReturnChancePercent;

            imageRouter.AddRoute(traderBase.Avatar.Replace(".png", ""), traderImagePath);
            AishiLogger.SetTraderUpdateTime(_traderConfig, traderBase, timeUtil.GetHoursAsSeconds(1), timeUtil.GetHoursAsSeconds(2));

            _ragfairConfig.Traders.TryAdd(traderBase.Id, true);

            AishiLogger.AddTraderWithEmptyAssortToDb(traderBase);
            AishiRepeatables.Initialize(pathToMod, templateTable, logger);
            AddAishiToRepeatableQuests(traderBase);

            if (tradersTable.TryGetValue(traderBase.Id, out var trader))
            {
                trader.Dialogue["insuranceStart"] = [
                "6a31f76e04210c72c1b95804 0",
                "6a31f76e04210c72c1b95804 1",
                "6a31f76e04210c72c1b95804 2",
                "6a31f76e04210c72c1b95804 3"
                ];
                trader.Dialogue["insuranceFound"] = [
                "6a31f77004210c72c1b95805 0",
                "6a31f77004210c72c1b95805 1",
                "6a31f77004210c72c1b95805 2",
                "6a31f77004210c72c1b95805 3"
                ];
                trader.Dialogue["insuranceExpired"] = [
                "6a31f77204210c72c1b95806 0",
                "6a31f77204210c72c1b95806 1",
                "6a31f77204210c72c1b95806 2",
                "6a31f77204210c72c1b95806 3"
                ];
                trader.Dialogue["insuranceComplete"] = [
                "6a31f77504210c72c1b95807 0",
                "6a31f77504210c72c1b95807 1",
                "6a31f77504210c72c1b95807 2",
                "6a31f77504210c72c1b95807 3"
                ];
                trader.Dialogue["insuranceFailed"] = [
                "6a31f77704210c72c1b95808 0",
                "6a31f77704210c72c1b95808 1",
                "6a31f77704210c72c1b95808 2",
                "6a31f77704210c72c1b95808 3"
                ];
                trader.Dialogue["insuranceFailedLabs"] = [
                "6a31f77904210c72c1b95809 0",
                "6a31f77904210c72c1b95809 1",
                "6a31f77904210c72c1b95809 2",
                "6a31f77904210c72c1b95809 3"
                ];
                trader.Dialogue["insuranceFailedLabyrinth"] = [
                "6a31f77c04210c72c1b9580a 0",
                "6a31f77c04210c72c1b9580a 1",
                "6a31f77c04210c72c1b9580a 2",
                "6a31f77c04210c72c1b9580a 3"
                ];
            }

            var assembly = Assembly.GetExecutingAssembly();

            AishiLogger.AddTraderToLocales(traderBase, "Aishi", "She is the commander of ISB, the Intelligence Support Bureau of USEC, which was responsible for dozens of classified operations throughout the territory of Tarkov during the conflict.");

            var assort = modHelper.GetJsonDataFromFile<TraderAssort>(pathToMod, "db/assort.json");

            AishiLogger.OverwriteTraderAssort(traderBase.Id, assort);

            logger.LogInformation("\x1b[38;2;200;80;220m[ISB Aishi Loaded] \u201cDo you think the Black Division will negotiate? Join us, and let\u2019s show them our bargaining chip.\u201d\x1b[0m");

            await wttCommon.CustomQuestService.CreateCustomQuests(assembly);
            await wttCommon.CustomLocaleService.CreateCustomLocales(assembly);
            await wttCommon.CustomQuestZoneService.CreateCustomQuestZones(assembly);
            await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly);
            await wttCommon.CustomLootspawnService.CreateCustomLootSpawns(assembly);
            await wttCommon.CustomWeaponPresetService.CreateCustomWeaponPresets(assembly);
            await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(assembly);
            await wttCommon.CustomAchievementService.CreateCustomAchievements(assembly);
            await wttCommon.CustomBuffService.CreateCustomBuffs(assembly);
            await wttCommon.CustomVoiceService.CreateCustomVoices(assembly);
            await wttCommon.CustomHeadService.CreateCustomHeads(assembly);
            await wttCommon.CustomClothingService.CreateCustomClothing(assembly);

            if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.Luna.LunnayalunaLotus"))
            {
                logger.LogInformation("\x1b[38;2;150;60;220m[ISB Aishi] Looks like \"Lotus\" from Lunnayaluna is installed on the server. Enabling some special content.\x1b[0m");
                await wttCommon.CustomQuestService.CreateCustomQuests(assembly,
                    Path.Join("db", "AishiAndLotusQuests"));
            }

            if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.manimal.hackermod"))
            {
                await wttCommon.CustomQuestService.CreateCustomQuests(assembly,
                    Path.Join("db", "AishiAndManimalHackDevice", "Quests"));
                await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(assembly,
                    Path.Join("db", "AishiAndManimalHackDevice", "Recipes"));
                await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly,
                    Path.Join("db", "AishiAndManimalHackDevice", "Itens"));
                await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly,
                    Path.Join("db", "AishiAndManimalHackDevice", "Assort"));
            }

            if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.ruafcomehome.tacticaltoaster"))
            {
                await wttCommon.CustomQuestService.CreateCustomQuests(assembly,
                    Path.Join("db", "AishiAndRuaf", "Quests"));
                await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(assembly,
                    Path.Join("db", "AishiAndRuaf", "Recipes"));
                await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly,
                    Path.Join("db", "AishiAndRuaf", "Itens"));
                await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly,
                    Path.Join("db", "AishiAndRuaf", "Assort"));
            }

            if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.blackdiv.tacticaltoaster"))
            {
                await wttCommon.CustomQuestService.CreateCustomQuests(assembly,
                    Path.Join("db", "AishiAndBlackDiv", "Quests"));
                await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(assembly,
                    Path.Join("db", "AishiAndBlackDiv", "Recipes"));
                await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly,
                    Path.Join("db", "AishiAndBlackDiv", "Itens"));
                await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly,
                    Path.Join("db", "AishiAndBlackDiv", "Assort"));
            }

            if (modList.Any(mod => mod.ModMetadata.ModGuid == "com.untargh.tacticaltoaster"))
            {
                await wttCommon.CustomQuestService.CreateCustomQuests(assembly,
                    Path.Join("db", "AishiAndUntar", "Quests"));
                await wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(assembly,
                    Path.Join("db", "AishiAndUntar", "Recipes"));
                await wttCommon.CustomItemServiceExtended.CreateCustomItems(assembly,
                    Path.Join("db", "AishiAndUntar", "Itens"));
                await wttCommon.CustomAssortSchemeService.CreateCustomAssortSchemes(assembly,
                    Path.Join("db", "AishiAndUntar", "Assort"));
            }

            wttCommon.CustomRigLayoutService.CreateRigLayouts(assembly);
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

                var repeatable = _questConfig.RepeatableQuests.FirstOrDefault(config =>
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

    }
}