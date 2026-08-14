using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Generators.RepeatableQuests;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Repeatable;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils.Collections;
using System.Text.Json;
using IOPath = System.IO.Path;

namespace Aishi;

public static class AishiRepeatables
{
    public const string TraderId = "690766de550bc322a810ea1e";

    private const string HarmonyId = "com.samc137.aishi.repeatablesquestssys";

    private static readonly string[] SupportedQuestTypes = ["Elimination", "Completion", "Exploration"];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly object Sync = new();

    private static ILogger? _logger;
    private static TemplateTable? _templateTable;
    private static AishiRepeatablesConfig? _config;
    private static bool _initialized;

    [ThreadStatic]
    private static DailyExtraGenerationContext? _dailyExtraGeneration;

    [ThreadStatic]
    private static bool _forceGuaranteedDailyAishi;

    [ThreadStatic]
    private static CompletionGenerationContext? _completionGeneration;

    public static void Initialize(string pathToMod, TemplateTable templateTable, ILogger logger)
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            _logger = logger;
            _templateTable = templateTable;

            var configPath = IOPath.Combine(pathToMod, "db", "AishiRepeatables.json");

            try
            {
                if (!File.Exists(configPath))
                {
                    logger.LogWarning($"[ISB Aishi] Repeatable config not found: {configPath}. Aishi repeatables will stay disabled.");
                    _config = new AishiRepeatablesConfig { Enabled = false };
                    _initialized = true;
                    return;
                }

                var json = File.ReadAllText(configPath);
                _config = JsonSerializer.Deserialize<AishiRepeatablesConfig>(json, JsonOptions);

                if (_config is null)
                {
                    logger.LogWarning("[ISB Aishi] AishiRepeatables.json could not be deserialized. Aishi repeatables will stay disabled.");
                    _config = new AishiRepeatablesConfig { Enabled = false };
                    _initialized = true;
                    return;
                }

                NormalizeConfig(_config);

                if (_config.Enabled)
                {
                    var harmony = new Harmony(HarmonyId);
                    harmony.CreateClassProcessor(typeof(RewardGeneratorPatch)).Patch();
                    harmony.CreateClassProcessor(typeof(EliminationGeneratorPatch)).Patch();
                    harmony.CreateClassProcessor(typeof(CompletionGeneratorPatch)).Patch();
                    harmony.CreateClassProcessor(typeof(CompletionItemPoolPatch)).Patch();
                    harmony.CreateClassProcessor(typeof(ExplorationGeneratorPatch)).Patch();
                    harmony.CreateClassProcessor(typeof(TraderSelectionPatch)).Patch();
                    harmony.CreateClassProcessor(typeof(DailyQuestCountPatch)).Patch();
                    harmony.CreateClassProcessor(typeof(RandomDailyGenerationPatch)).Patch();
                    harmony.CreateClassProcessor(typeof(RandomQuestTypePatch)).Patch();

                    LogInformation("[ISB Aishi] Aishi daily/weekly quests loaded.");

                    if (_config.ForceAishiTempQuests)
                    {
                        LogInformation("[ISB Aishi] forceAishiTempQuests is ENABLED. Eligible Daily/Weekly quest types will be forced to Aishi.");
                    }

                    if (_config.GuaranteedDailyQuests.Max > 0)
                    {
                        LogInformation(
                            "[ISB Aishi] Guaranteed extra Daily quests set: {Min}-{Max} Aishi quest(s) per Daily reset.",
                            _config.GuaranteedDailyQuests.Min,
                            _config.GuaranteedDailyQuests.Max);
                    }
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[ISB Aishi] Failed to initialize Aishi repeatables. Aishi repeatables will stay disabled.");
                _config = new AishiRepeatablesConfig { Enabled = false };
                _initialized = true;
            }
        }
    }

    public static bool ShowRepeatableQuestLogs => _config?.ShowRepeatableQuestLogs == true;

    public static bool IsEnabled(string repeatableName)
    {
        var preset = GetPreset(repeatableName);
        return _config?.Enabled == true && preset?.Enabled == true;
    }

    public static HashSet<string> GetQuestTypes(string repeatableName)
    {
        var preset = GetPreset(repeatableName);

        if (preset?.QuestTypes is null || preset.QuestTypes.Count == 0)
        {
            return ["Elimination"];
        }

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in preset.QuestTypes)
        {
            var normalized = SupportedQuestTypes.FirstOrDefault(candidate =>
                string.Equals(candidate, type, StringComparison.OrdinalIgnoreCase));

            if (normalized is not null)
            {
                result.Add(normalized);
            }
        }

        return result.Count > 0 ? result : ["Elimination"];
    }

    private static void LogInformation(string message, params object?[] args)
    {
        if (ShowRepeatableQuestLogs)
        {
            _logger?.LogInformation(message, args);
        }
    }

    private static void LogDebug(string message, params object?[] args)
    {
        if (ShowRepeatableQuestLogs)
        {
            _logger?.LogDebug(message, args);
        }
    }

    private static RepeatablePreset? GetPreset(string repeatableName)
    {
        if (_config is null)
        {
            return null;
        }

        if (string.Equals(repeatableName, "Daily", StringComparison.OrdinalIgnoreCase))
        {
            return _config.Daily;
        }

        if (string.Equals(repeatableName, "Weekly", StringComparison.OrdinalIgnoreCase))
        {
            return _config.Weekly;
        }

        return null;
    }

    private static void NormalizeConfig(AishiRepeatablesConfig config)
    {
        config.GuaranteedDailyQuests ??= new IntRange { Min = 1, Max = 2 };
        NormalizeRange(config.GuaranteedDailyQuests, 0);

        NormalizePreset(config.Daily);
        NormalizePreset(config.Weekly);
    }

    private static void NormalizePreset(RepeatablePreset preset)
    {
        preset.QuestTypes ??= ["Elimination"];
        preset.Images ??= [];
        preset.Reroll ??= new RerollPreset();
        preset.Reroll.ChangeCost ??= [];

        preset.Elimination ??= new EliminationPreset();
        preset.Elimination.Targets ??= [];
        preset.Elimination.BodyParts ??= [];
        preset.Elimination.Locations ??= ["any"];
        preset.Elimination.DistLocationBlacklist ??= [];
        preset.Elimination.WeaponCategoryRequirements ??= [];
        preset.Elimination.WeaponRequirements ??= [];
        preset.Elimination.ConditionOverrides ??= new AdvancedEliminationConditionPreset();
        preset.Elimination.ConditionOverrides.WeaponCalibers ??= [];
        preset.Elimination.ConditionOverrides.Daytime ??= new DaytimePreset();

        foreach (var target in preset.Elimination.Targets)
        {
            target.Data ??= new TargetDataPreset();
        }

        foreach (var bodyPart in preset.Elimination.BodyParts)
        {
            bodyPart.Data ??= [];
        }

        foreach (var requirement in preset.Elimination.WeaponCategoryRequirements)
        {
            requirement.Data ??= [];
        }

        foreach (var requirement in preset.Elimination.WeaponRequirements)
        {
            requirement.Data ??= [];
        }

        preset.Completion ??= new CompletionPreset();
        preset.Completion.RequiredItemTypeBlacklist ??= [];
        preset.Completion.ItemsWhitelist ??= [];
        preset.Completion.ItemsBlacklist ??= [];
        preset.Completion.RequestedBulletCount ??= new IntRange { Min = 15, Max = 40 };

        foreach (var levelPool in preset.Completion.ItemsWhitelist)
        {
            levelPool.ItemIds ??= [];
        }

        foreach (var levelPool in preset.Completion.ItemsBlacklist)
        {
            levelPool.ItemIds ??= [];
        }

        preset.Exploration ??= new ExplorationPreset();
        preset.Exploration.Locations ??= [];
        preset.Exploration.SpecificExits ??= new SpecificExitsPreset();
        preset.Exploration.SpecificExits.PassageRequirementWhitelist ??= [];

        preset.Rewards ??= new RewardPreset();
        preset.Rewards.Xp ??= new IntRange();
        preset.Rewards.Money ??= new MoneyRange();
        preset.Rewards.Standing ??= new DoubleRange();
        preset.Rewards.ItemRewards ??= new ItemRewardPreset();
        preset.Rewards.ItemRewards.Pool ??= [];

        (preset.Elimination.MinKills, preset.Elimination.MaxKills) = NormalizePair(
            preset.Elimination.MinKills, preset.Elimination.MaxKills, 1);
        (preset.Elimination.MinPmcKills, preset.Elimination.MaxPmcKills) = NormalizePair(
            preset.Elimination.MinPmcKills, preset.Elimination.MaxPmcKills, 1);
        (preset.Elimination.MinBossKills, preset.Elimination.MaxBossKills) = NormalizePair(
            preset.Elimination.MinBossKills, preset.Elimination.MaxBossKills, 1);
        NormalizeRange(preset.Completion.RequestedItemCount, 1);
        NormalizeRange(preset.Completion.UniqueItemCount, 1);
        NormalizeRange(preset.Completion.RequestedBulletCount, 1);
        NormalizeRange(preset.Completion.RequiredItemMinDurabilityMinMax, 0);
        NormalizeRange(preset.Rewards.Xp, 0);
        NormalizeRange(preset.Rewards.Money, 0);
        NormalizeRange(preset.Rewards.ItemRewards, 0);

        if (preset.Elimination.Targets.Count == 0)
        {
            preset.Elimination.Targets.Add(new TargetPreset
            {
                Key = "Savage",
                RelativeProbability = 1,
                Data = new TargetDataPreset { IsBoss = false, IsPmc = false }
            });
        }

        if (preset.Elimination.Locations.Count == 0)
        {
            preset.Elimination.Locations.Add("any");
        }

        (preset.Exploration.MinExtracts, preset.Exploration.MaxExtracts) = NormalizePair(
            preset.Exploration.MinExtracts, preset.Exploration.MaxExtracts, 1);
        (preset.Exploration.MinExtractsWithSpecificExit, preset.Exploration.MaxExtractsWithSpecificExit) = NormalizePair(
            preset.Exploration.MinExtractsWithSpecificExit,
            preset.Exploration.MaxExtractsWithSpecificExit,
            1);

        if (preset.Completion.UniqueItemCount.Max > preset.Completion.RequestedItemCount.Max)
        {
            preset.Completion.UniqueItemCount.Max = preset.Completion.RequestedItemCount.Max;
        }

        if (preset.Completion.UniqueItemCount.Min > preset.Completion.UniqueItemCount.Max)
        {
            preset.Completion.UniqueItemCount.Min = preset.Completion.UniqueItemCount.Max;
        }
    }

    private static (int Min, int Max) NormalizePair(int min, int max, int floor)
    {
        min = Math.Max(floor, min);
        max = Math.Max(floor, max);

        if (max < min)
        {
            (min, max) = (max, min);
        }

        return (min, max);
    }

    private static void NormalizeRange(IntRange range, int floor)
    {
        range.Min = Math.Max(floor, range.Min);
        range.Max = Math.Max(floor, range.Max);

        if (range.Max < range.Min)
        {
            (range.Min, range.Max) = (range.Max, range.Min);
        }
    }

    private static string? GetQuestImage(RepeatablePreset preset, string questType)
    {
        foreach (var (type, image) in preset.Images)
        {
            if (string.Equals(type, questType, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(image))
            {
                return image;
            }
        }

        return null;
    }

    private static void ApplyCommonQuestSettings(RepeatableQuest quest, RepeatablePreset preset, string repeatableName, string questType)
    {
        var image = GetQuestImage(preset, questType);
        if (!string.IsNullOrWhiteSpace(image))
        {
            quest.Image = image;
        }

        quest.ChangeCost = preset.Reroll.ChangeCost
            .Where(cost => MongoId.IsValidMongoId(cost.TemplateId) && cost.Count >= 0)
            .Select(cost => new ChangeCost
            {
                TemplateId = new MongoId(cost.TemplateId),
                Count = cost.Count
            })
            .ToList();

        LogDebug(
            "[ISB Aishi] Applied custom {RepeatableName} {QuestType} image/reroll settings.",
            repeatableName,
            questType);
    }

    private static Dictionary<string, List<Reward>>? BuildRewards(RepeatablePreset preset, string repeatableName)
    {
        try
        {
            var rewards = new Dictionary<string, List<Reward>>
            {
                ["Success"] = [],
                ["Started"] = [],
                ["Fail"] = []
            };

            var rewardIndex = -1;

            var xp = NextInclusive(preset.Rewards.Xp.Min, preset.Rewards.Xp.Max);
            if (xp > 0)
            {
                rewards["Success"].Add(new Reward
                {
                    Id = new MongoId(),
                    Unknown = false,
                    GameMode = [],
                    AvailableInGameEditions = [],
                    Index = rewardIndex++,
                    Value = xp,
                    Type = RewardType.Experience
                });
            }

            var moneyAmount = NextInclusive(preset.Rewards.Money.Min, preset.Rewards.Money.Max);
            if (moneyAmount > 0)
            {
                var currencyTpl = GetCurrencyTpl(preset.Rewards.Money.Currency);
                rewards["Success"].Add(CreateItemReward(currencyTpl, moneyAmount, rewardIndex++, false));
            }

            var standing = NextDouble(preset.Rewards.Standing.Min, preset.Rewards.Standing.Max);
            standing = Math.Round(standing, 3, MidpointRounding.AwayFromZero);
            if (standing > 0)
            {
                rewards["Success"].Add(new Reward
                {
                    Id = new MongoId(),
                    Unknown = false,
                    GameMode = [],
                    AvailableInGameEditions = [],
                    Target = TraderId,
                    Value = standing,
                    Type = RewardType.TraderStanding,
                    Index = rewardIndex++
                });
            }

            foreach (var item in ChooseItemRewards(preset.Rewards.ItemRewards))
            {
                rewards["Success"].Add(CreateItemReward(item.Tpl, item.Amount, rewardIndex++, true));
            }

            LogInformation(
                "[ISB Aishi] Custom {RepeatableName} rewards generated: {Xp} XP, {Money} {Currency}, {Standing} standing, {ItemCount} custom item reward(s).",
                repeatableName,
                xp,
                moneyAmount,
                preset.Rewards.Money.Currency,
                standing,
                rewards["Success"].Count(reward => reward.Type == RewardType.Item) - (moneyAmount > 0 ? 1 : 0));

            return rewards;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ISB Aishi] Failed to build custom repeatable rewards.");
            return null;
        }
    }

    private static List<ChosenRewardItem> ChooseItemRewards(ItemRewardPreset itemConfig)
    {
        var result = new List<ChosenRewardItem>();

        var pool = itemConfig.Pool
            .Where(item => item.Weight > 0 && IsValidItemTpl(item.Tpl, "reward"))
            .ToList();

        if (pool.Count == 0)
        {
            return result;
        }

        var desiredCount = NextInclusive(itemConfig.Min, itemConfig.Max);
        if (!itemConfig.AllowDuplicates)
        {
            desiredCount = Math.Min(desiredCount, pool.Count);
        }

        var workingPool = new List<ItemRewardEntry>(pool);

        for (var i = 0; i < desiredCount && workingPool.Count > 0; i++)
        {
            var chosen = DrawWeighted(workingPool);
            if (chosen is null)
            {
                break;
            }

            result.Add(new ChosenRewardItem
            {
                Tpl = new MongoId(chosen.Tpl),
                Amount = NextInclusive(chosen.MinAmount, chosen.MaxAmount)
            });

            if (!itemConfig.AllowDuplicates)
            {
                workingPool.Remove(chosen);
            }
        }

        return result;
    }

    private static ItemRewardEntry? DrawWeighted(List<ItemRewardEntry> pool)
    {
        var totalWeight = pool.Sum(item => Math.Max(0, item.Weight));
        if (totalWeight <= 0)
        {
            return null;
        }

        var roll = Random.Shared.NextDouble() * totalWeight;

        foreach (var item in pool)
        {
            roll -= Math.Max(0, item.Weight);
            if (roll <= 0)
            {
                return item;
            }
        }

        return pool[^1];
    }

    private static bool IsValidItemTpl(string tpl, string purpose)
    {
        if (!MongoId.IsValidMongoId(tpl))
        {
            _logger?.LogWarning("[ISB Aishi] Invalid {Purpose} TPL in AishiRepeatables.json: {Tpl}", purpose, tpl);
            return false;
        }

        if (_templateTable is null)
        {
            return true;
        }

        var mongoId = new MongoId(tpl);
        if (_templateTable.Items.ContainsKey(mongoId))
        {
            return true;
        }

        _logger?.LogWarning("[ISB Aishi] {Purpose} TPL does not exist in the item database: {Tpl}", purpose, tpl);
        return false;
    }

    private static Reward CreateItemReward(MongoId tpl, double amount, int index, bool foundInRaid)
    {
        var rootId = new MongoId();

        return new Reward
        {
            Id = new MongoId(),
            Unknown = false,
            GameMode = [],
            AvailableInGameEditions = [],
            Index = index,
            Target = rootId,
            Value = amount,
            IsEncoded = false,
            FindInRaid = foundInRaid,
            Type = RewardType.Item,
            Items =
            [
                new Item
                {
                    Id = rootId,
                    Template = tpl,
                    Upd = new Upd
                    {
                        StackObjectsCount = amount,
                        SpawnedInSession = foundInRaid
                    }
                }
            ]
        };
    }

    private static MongoId GetCurrencyTpl(string currency)
    {
        if (string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            return Money.EUROS;
        }

        if (string.Equals(currency, "RUB", StringComparison.OrdinalIgnoreCase))
        {
            return Money.ROUBLES;
        }

        return Money.DOLLARS;
    }

    private static int NextInclusive(int min, int max)
    {
        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (min == max)
        {
            return min;
        }

        return Random.Shared.Next(min, max + 1);
    }

    private static double NextDouble(double min, double max)
    {
        if (max < min)
        {
            (min, max) = (max, min);
        }

        if (Math.Abs(max - min) < double.Epsilon)
        {
            return min;
        }

        return min + Random.Shared.NextDouble() * (max - min);
    }

    private static bool RollChance(double chance)
    {
        return chance > 0 && Random.Shared.NextDouble() * 100d < Math.Clamp(chance, 0d, 100d);
    }

    private static void ApplyEliminationPreset(
        int pmcLevel,
        RepeatablePreset preset,
        ref QuestTypePool questTypePool,
        ref RepeatableQuestConfig repeatableConfig)
    {
        var settings = preset.Elimination;
        if (settings.Targets.Count == 0)
        {
            return;
        }

        var baseConfig = repeatableConfig.QuestConfig.Elimination.FirstOrDefault(config =>
            pmcLevel >= config.LevelRange.Min && pmcLevel <= config.LevelRange.Max);

        baseConfig ??= repeatableConfig.QuestConfig.Elimination.FirstOrDefault();
        if (baseConfig is null)
        {
            _logger?.LogWarning("[ISB Aishi] No elimination config exists for {RepeatableName}.", repeatableConfig.Name);
            return;
        }

        var targets = settings.Targets
            .Where(target => !string.IsNullOrWhiteSpace(target.Key) && target.RelativeProbability > 0)
            .Select(target => new ProbabilityObject<string, BossInfo>(
                target.Key,
                target.RelativeProbability,
                new BossInfo
                {
                    IsBoss = target.Data.IsBoss,
                    IsPmc = target.Data.IsPmc || string.Equals(target.Key, "AnyPmc", StringComparison.OrdinalIgnoreCase)
                }))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        var bodyParts = BuildStringProbabilityList(settings.BodyParts);
        if (bodyParts.Count == 0)
        {
            bodyParts = baseConfig.BodyParts;
        }

        var weaponCategories = BuildMongoProbabilityList(settings.WeaponCategoryRequirements, "weapon category requirement");
        if (weaponCategories.Count == 0)
        {
            weaponCategories = baseConfig.WeaponCategoryRequirements;
        }

        var weaponRequirements = BuildMongoProbabilityList(settings.WeaponRequirements, "weapon requirement");
        if (weaponRequirements.Count == 0)
        {
            weaponRequirements = baseConfig.WeaponRequirements;
        }

        var locationNames = BuildEliminationLocationPool(settings.Locations, repeatableConfig.Locations);
        var hasSpecificLocation = locationNames.Any(location => !string.Equals(location, "any", StringComparison.OrdinalIgnoreCase));

        var customElimination = baseConfig with
        {
            Targets = targets,
            BodyPartChance = Math.Clamp(settings.BodyPartChance, 0, 100),
            BodyParts = bodyParts,
            SpecificLocationChance = hasSpecificLocation ? Math.Clamp(settings.SpecificLocationChance, 0, 100) : 0,
            DistLocationBlacklist = new HashSet<string>(settings.DistLocationBlacklist, StringComparer.OrdinalIgnoreCase),
            DistanceProbability = Math.Clamp(settings.DistProb, 0d, 100d),
            MinDistance = Math.Max(0d, Math.Min(settings.MinDist, settings.MaxDist)),
            MaxDistance = Math.Max(settings.MinDist, settings.MaxDist),
            MinKills = settings.MinKills,
            MaxKills = settings.MaxKills,
            MinPmcKills = settings.MinPmcKills,
            MaxPmcKills = settings.MaxPmcKills,
            MinBossKills = settings.MinBossKills,
            MaxBossKills = settings.MaxBossKills,
            WeaponRequirementChance = Math.Clamp(settings.WeaponRequirementChance, 0, 100),
            WeaponCategoryRequirementChance = Math.Clamp(settings.WeaponCategoryRequirementChance, 0, 100),
            WeaponCategoryRequirements = weaponCategories,
            WeaponRequirements = weaponRequirements
        };

        if (customElimination.WeaponCategoryRequirementChance > 0)
        {
            _logger?.LogWarning(
                "[ISB Aishi] weaponCategoryRequirementChance is enabled for {RepeatableName}.",
                repeatableConfig.Name);
        }

        var targetLocations = targets.ToDictionary(
            target => target.Key!,
            _ => new TargetLocation { Locations = new List<string>(locationNames) },
            StringComparer.OrdinalIgnoreCase);

        questTypePool = questTypePool with
        {
            Types = new List<string>(questTypePool.Types),
            Pool = questTypePool.Pool with
            {
                Elimination = new EliminationPool
                {
                    Targets = targetLocations
                }
            }
        };

        repeatableConfig = repeatableConfig with
        {
            QuestConfig = repeatableConfig.QuestConfig with
            {
                Elimination = [customElimination]
            }
        };
    }

    private static List<ProbabilityObject<string, List<string>>> BuildStringProbabilityList(List<WeightedStringListPreset> entries)
    {
        var result = new List<ProbabilityObject<string, List<string>>>();

        foreach (var entry in entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.RelativeProbability > 0))
        {
            var data = entry.Data
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (data.Count == 0)
            {
                continue;
            }

            result.Add(new ProbabilityObject<string, List<string>>(entry.Key, entry.RelativeProbability, data));
        }

        return result;
    }

    private static List<ProbabilityObject<string, List<MongoId>>> BuildMongoProbabilityList(
        List<WeightedMongoListPreset> entries,
        string purpose)
    {
        var result = new List<ProbabilityObject<string, List<MongoId>>>();

        foreach (var entry in entries.Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.RelativeProbability > 0))
        {
            var data = entry.Data
                .Where(value => MongoId.IsValidMongoId(value))
                .Select(value => new MongoId(value))
                .Distinct()
                .ToList();

            if (data.Count == 0)
            {
                if (entry.Data.Count > 0)
                {
                    _logger?.LogWarning("[ISB Aishi] {Purpose} entry '{Key}' contains no valid MongoId values.", purpose, entry.Key);
                }

                continue;
            }

            result.Add(new ProbabilityObject<string, List<MongoId>>(entry.Key, entry.RelativeProbability, data));
        }

        return result;
    }

    private static List<string> BuildEliminationLocationPool(
        List<string> configuredLocations,
        Dictionary<ELocationName, List<string>> availableLocations)
    {
        var locations = new List<string>();

        foreach (var configured in configuredLocations)
        {
            if (string.Equals(configured, "any", StringComparison.OrdinalIgnoreCase))
            {
                if (!locations.Contains("any", StringComparer.OrdinalIgnoreCase))
                {
                    locations.Add("any");
                }

                continue;
            }

            if (!Enum.TryParse<ELocationName>(configured, true, out var parsed))
            {
                _logger?.LogWarning("[ISB Aishi] Unknown elimination location in AishiRepeatables.json: {Location}", configured);
                continue;
            }

            if (!availableLocations.ContainsKey(parsed))
            {
                _logger?.LogWarning("[ISB Aishi] Location is not available for this repeatable config: {Location}", configured);
                continue;
            }

            var name = parsed.ToString();
            if (!locations.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                locations.Add(name);
            }
        }

        if (locations.Count == 0)
        {
            locations.Add("any");
        }

        if (!locations.Contains("any", StringComparer.OrdinalIgnoreCase))
        {
            locations.Insert(0, "any");
        }

        return locations;
    }

    private static void ApplyAdvancedEliminationCondition(RepeatableQuest quest, AdvancedEliminationConditionPreset settings)
    {
        var counterCreator = quest.Conditions.AvailableForFinish?
            .FirstOrDefault(condition => string.Equals(condition.ConditionType, "CounterCreator", StringComparison.OrdinalIgnoreCase));

        if (counterCreator is null)
        {
            return;
        }

        counterCreator.OneSessionOnly = settings.OneSessionOnly;

        var killCondition = counterCreator.Counter?.Conditions?
            .FirstOrDefault(condition => string.Equals(condition.ConditionType, "Kills", StringComparison.OrdinalIgnoreCase));

        if (killCondition is null)
        {
            return;
        }

        killCondition.ResetOnSessionEnd = settings.ResetOnSessionEnd;

        if (settings.WeaponCalibers.Count > 0 && RollChance(settings.WeaponCaliberChance))
        {
            killCondition.WeaponCaliber = settings.WeaponCalibers
                .Where(caliber => !string.IsNullOrWhiteSpace(caliber))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (RollChance(settings.DaytimeChance))
        {
            killCondition.Daytime = new DaytimeCounter
            {
                From = Math.Clamp(settings.Daytime.From, 0, 24),
                To = Math.Clamp(settings.Daytime.To, 0, 24)
            };
        }
    }

    private static void ApplyCompletionPreset(
        int pmcLevel,
        RepeatablePreset preset,
        ref RepeatableQuestConfig repeatableConfig)
    {
        var settings = preset.Completion;
        var baseConfig = repeatableConfig.QuestConfig.CompletionConfig.FirstOrDefault(config =>
            pmcLevel >= config.LevelRange.Min && pmcLevel <= config.LevelRange.Max);

        baseConfig ??= repeatableConfig.QuestConfig.CompletionConfig.FirstOrDefault();
        if (baseConfig is null)
        {
            _logger?.LogWarning("[ISB Aishi] No completion config exists for {RepeatableName}.", repeatableConfig.Name);
            return;
        }

        var blacklist = settings.RequiredItemTypeBlacklist
            .Where(MongoId.IsValidMongoId)
            .Select(value => new MongoId(value))
            .ToHashSet();

        var customCompletion = baseConfig with
        {
            RequestedItemCount = new MinMax<int>(settings.RequestedItemCount.Min, settings.RequestedItemCount.Max),
            UniqueItemCount = new MinMax<int>(settings.UniqueItemCount.Min, settings.UniqueItemCount.Max),
            RequestedBulletCount = new MinMax<int>(settings.RequestedBulletCount.Min, settings.RequestedBulletCount.Max),
            UseWhitelist = false,
            UseBlacklist = false,
            RequiredItemsAreFiR = settings.RequiredItemsAreFiR,
            RequiredItemMinDurabilityMinMax = new MinMax<int>(
                settings.RequiredItemMinDurabilityMinMax.Min,
                settings.RequiredItemMinDurabilityMinMax.Max),
            RequiredItemTypeBlacklist = blacklist
        };

        repeatableConfig = repeatableConfig with
        {
            QuestConfig = repeatableConfig.QuestConfig with
            {
                CompletionConfig = [customCompletion]
            }
        };
    }

    private static HashSet<MongoId>? BuildCompletionWhitelist(CompletionPreset settings, int pmcLevel)
    {
        if (!settings.UseWhitelist || settings.ItemsWhitelist.Count == 0)
        {
            return null;
        }

        return BuildLevelItemPool(settings.ItemsWhitelist, pmcLevel, "completion itemsWhitelist");
    }

    private static HashSet<MongoId>? BuildCompletionBlacklist(CompletionPreset settings, int pmcLevel)
    {
        if (!settings.UseBlacklist || settings.ItemsBlacklist.Count == 0)
        {
            return null;
        }

        return BuildLevelItemPool(settings.ItemsBlacklist, pmcLevel, "completion itemsBlacklist");
    }

    private static HashSet<MongoId> BuildLevelItemPool(
        List<LevelItemPoolPreset> levelPools,
        int pmcLevel,
        string purpose)
    {
        var result = new HashSet<MongoId>();

        foreach (var levelPool in levelPools.Where(pool => pmcLevel >= pool.MinPlayerLevel))
        {
            foreach (var tpl in levelPool.ItemIds)
            {
                if (IsValidItemTpl(tpl, purpose))
                {
                    result.Add(new MongoId(tpl));
                }
            }
        }

        return result;
    }

    private static void ApplyExplorationPreset(
        int pmcLevel,
        RepeatablePreset preset,
        ref QuestTypePool questTypePool,
        ref RepeatableQuestConfig repeatableConfig)
    {
        var settings = preset.Exploration;
        var baseConfig = repeatableConfig.QuestConfig.ExplorationConfig.FirstOrDefault(config =>
            pmcLevel >= config.LevelRange.Min && pmcLevel <= config.LevelRange.Max);

        baseConfig ??= repeatableConfig.QuestConfig.ExplorationConfig.FirstOrDefault();
        if (baseConfig is null)
        {
            _logger?.LogWarning("[ISB Aishi] No exploration config exists for {RepeatableName}.", repeatableConfig.Name);
            return;
        }

        var passageWhitelist = settings.SpecificExits.PassageRequirementWhitelist.Count > 0
            ? new HashSet<string>(settings.SpecificExits.PassageRequirementWhitelist, StringComparer.OrdinalIgnoreCase)
            : baseConfig.SpecificExits.PassageRequirementWhitelist;

        var customExploration = baseConfig with
        {
            MinimumExtracts = settings.MinExtracts,
            MaximumExtracts = settings.MaxExtracts,
            MinimumExtractsWithSpecificExit = settings.MinExtractsWithSpecificExit,
            MaximumExtractsWithSpecificExit = settings.MaxExtractsWithSpecificExit,
            SpecificExits = baseConfig.SpecificExits with
            {
                Chance = Math.Clamp(settings.SpecificExits.Chance, 0d, 100d),
                PassageRequirementWhitelist = passageWhitelist
            }
        };

        var existingLocations = questTypePool.Pool.Exploration.Locations;
        if (existingLocations is not null && settings.Locations.Count > 0)
        {
            var useVanillaLocations = settings.Locations.Any(location =>
                string.Equals(location, "any", StringComparison.OrdinalIgnoreCase));

            if (!useVanillaLocations)
            {
                var filtered = new Dictionary<ELocationName, List<string>>();

                foreach (var configured in settings.Locations)
                {
                    if (!Enum.TryParse<ELocationName>(configured, true, out var parsed))
                    {
                        _logger?.LogWarning("[ISB Aishi] Unknown exploration location in AishiRepeatables.json: {Location}", configured);
                        continue;
                    }

                    if (existingLocations.TryGetValue(parsed, out var targets))
                    {
                        filtered[parsed] = new List<string>(targets);
                    }
                }

                if (filtered.Count > 0)
                {
                    questTypePool = questTypePool with
                    {
                        Pool = questTypePool.Pool with
                        {
                            Exploration = questTypePool.Pool.Exploration with
                            {
                                Locations = filtered
                            }
                        }
                    };
                }
                else
                {
                    _logger?.LogWarning("[ISB Aishi] No configured exploration locations were valid.");
                }
            }
        }

        repeatableConfig = repeatableConfig with
        {
            QuestConfig = repeatableConfig.QuestConfig with
            {
                ExplorationConfig = [customExploration]
            }
        };
    }

    [HarmonyPatch(typeof(RepeatableQuestRewardGenerator), nameof(RepeatableQuestRewardGenerator.GenerateReward))]
    private static class RewardGeneratorPatch
    {
        private static void Postfix(
            MongoId traderId,
            RepeatableQuestConfig repeatableConfig,
            ref Dictionary<string, List<Reward>>? __result)
        {
            if (!traderId.Equals(TraderId))
            {
                return;
            }

            var preset = GetPreset(repeatableConfig.Name);
            if (_config?.Enabled != true || preset?.Enabled != true)
            {
                return;
            }

            var customRewards = BuildRewards(preset, repeatableConfig.Name);
            if (customRewards is not null)
            {
                __result = customRewards;
            }
        }
    }

    [HarmonyPatch(typeof(RepeatableQuestController), "DrawRandomTraderId")]
    private static class TraderSelectionPatch
    {
        private static void Postfix(
            Dictionary<MongoId, TraderInfo> traderInfos,
            string questType,
            RepeatableQuestConfig repeatableConfig,
            ref MongoId __result)
        {
            if (_config?.Enabled != true)
            {
                return;
            }

            var preset = GetPreset(repeatableConfig.Name);
            if (preset?.Enabled != true)
            {
                return;
            }

            var configuredQuestTypes = GetQuestTypes(repeatableConfig.Name);
            if (!configuredQuestTypes.Contains(questType))
            {
                return;
            }

            var aishiId = new MongoId(TraderId);
            var whitelistEntry = repeatableConfig.TraderWhitelist.FirstOrDefault(entry => entry.TraderId == aishiId);
            var aishiInWhitelist = whitelistEntry is not null && whitelistEntry.QuestTypes.Contains(questType);
            var aishiUnlocked = traderInfos.TryGetValue(aishiId, out var traderInfo)
                && traderInfo.Unlocked.GetValueOrDefault(false);

            var forceTempQuest = _config.ForceAishiTempQuests;
            var forceGuaranteedDaily = _forceGuaranteedDailyAishi
                && string.Equals(repeatableConfig.Name, "Daily", StringComparison.OrdinalIgnoreCase);

            if (forceTempQuest || forceGuaranteedDaily)
            {
                var forceSource = forceGuaranteedDaily ? "guaranteedDailyQuests" : "forceAishiTempQuests";

                if (!aishiInWhitelist)
                {
                    _logger?.LogWarning(
                        "[ISB Aishi] {ForceSource} could not force {RepeatableName} {QuestType}: Aishi is not in the eligible trader whitelist.",
                        forceSource,
                        repeatableConfig.Name,
                        questType);
                    return;
                }

                if (!aishiUnlocked)
                {
                    _logger?.LogWarning(
                        "[ISB Aishi] {ForceSource} could not force {RepeatableName} {QuestType}: Aishi is locked in the current profile.",
                        forceSource,
                        repeatableConfig.Name,
                        questType);
                    return;
                }

                __result = aishiId;
                LogInformation(
                    "[ISB Aishi] {ForceSource} forced {RepeatableName} {QuestType} operational task to Aishi.",
                    forceSource,
                    repeatableConfig.Name,
                    questType);
                return;
            }

            if (__result == aishiId)
            {
                LogInformation(
                    "[ISB Aishi] RNG selected Aishi for {RepeatableName} {QuestType} operational task.",
                    repeatableConfig.Name,
                    questType);
            }
        }
    }

    [HarmonyPatch(typeof(EliminationQuestGenerator), nameof(EliminationQuestGenerator.Generate))]
    private static class EliminationGeneratorPatch
    {
        private static void Prefix(
            int pmcLevel,
            MongoId traderId,
            ref QuestTypePool questTypePool,
            ref RepeatableQuestConfig repeatableConfig)
        {
            if (!traderId.Equals(TraderId))
            {
                return;
            }

            var preset = GetPreset(repeatableConfig.Name);
            if (_config?.Enabled != true || preset?.Enabled != true || !GetQuestTypes(repeatableConfig.Name).Contains("Elimination"))
            {
                return;
            }

            try
            {
                ApplyEliminationPreset(pmcLevel, preset, ref questTypePool, ref repeatableConfig);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ISB Aishi] Failed to apply custom elimination settings.");
            }
        }

        private static void Postfix(
            MongoId traderId,
            RepeatableQuestConfig repeatableConfig,
            ref RepeatableQuest? __result)
        {
            if (__result is null || !traderId.Equals(TraderId))
            {
                return;
            }

            var preset = GetPreset(repeatableConfig.Name);
            if (_config?.Enabled != true || preset?.Enabled != true)
            {
                return;
            }

            ApplyAdvancedEliminationCondition(__result, preset.Elimination.ConditionOverrides);
            ApplyCommonQuestSettings(__result, preset, repeatableConfig.Name, "Elimination");
        }
    }

    [HarmonyPatch(typeof(CompletionQuestGenerator), nameof(CompletionQuestGenerator.Generate))]
    private static class CompletionGeneratorPatch
    {
        private static void Prefix(
            int pmcLevel,
            MongoId traderId,
            ref RepeatableQuestConfig repeatableConfig)
        {
            _completionGeneration = null;

            if (!traderId.Equals(TraderId))
            {
                return;
            }

            var preset = GetPreset(repeatableConfig.Name);
            if (_config?.Enabled != true || preset?.Enabled != true || !GetQuestTypes(repeatableConfig.Name).Contains("Completion"))
            {
                return;
            }

            try
            {
                ApplyCompletionPreset(pmcLevel, preset, ref repeatableConfig);
                _completionGeneration = new CompletionGenerationContext
                {
                    PmcLevel = pmcLevel,
                    Preset = preset
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ISB Aishi] Failed to apply custom completion settings.");
            }
        }

        private static void Postfix(
            MongoId traderId,
            RepeatableQuestConfig repeatableConfig,
            ref RepeatableQuest? __result)
        {
            try
            {
                if (__result is null || !traderId.Equals(TraderId))
                {
                    return;
                }

                var preset = GetPreset(repeatableConfig.Name);
                if (_config?.Enabled != true || preset?.Enabled != true)
                {
                    return;
                }

                ApplyCommonQuestSettings(__result, preset, repeatableConfig.Name, "Completion");
            }
            finally
            {
                _completionGeneration = null;
            }
        }
    }

    [HarmonyPatch(typeof(CompletionQuestGenerator), "GetItemsToRetrievePool")]
    private static class CompletionItemPoolPatch
    {
        private static void Postfix(ref HashSet<MongoId> __result)
        {
            var context = _completionGeneration;
            if (context is null)
            {
                return;
            }

            var settings = context.Preset.Completion;
            var whitelist = BuildCompletionWhitelist(settings, context.PmcLevel);
            var blacklist = BuildCompletionBlacklist(settings, context.PmcLevel);

            if (whitelist is not null)
            {
                if (whitelist.Count == 0)
                {
                    __result.Clear();
                    _logger?.LogError(
                        "[ISB Aishi] Completion useWhitelist is enabled, but itemsWhitelist contains no valid items for PMC level {PmcLevel}.",
                        context.PmcLevel);
                    return;
                }

                __result.IntersectWith(whitelist);
            }

            if (blacklist is not null && blacklist.Count > 0)
            {
                __result.ExceptWith(blacklist);
            }

            if (__result.Count == 0 && (whitelist is not null || blacklist is not null))
            {
                _logger?.LogError(
                    "[ISB Aishi] Completion objective pool became empty after Aishi itemsWhitelist/itemsBlacklist filtering.");
            }
        }
    }

    [HarmonyPatch(typeof(ExplorationQuestGenerator), nameof(ExplorationQuestGenerator.Generate))]
    private static class ExplorationGeneratorPatch
    {
        private static void Prefix(
            int pmcLevel,
            MongoId traderId,
            ref QuestTypePool questTypePool,
            ref RepeatableQuestConfig repeatableConfig)
        {
            if (!traderId.Equals(TraderId))
            {
                return;
            }

            var preset = GetPreset(repeatableConfig.Name);
            if (_config?.Enabled != true || preset?.Enabled != true || !GetQuestTypes(repeatableConfig.Name).Contains("Exploration"))
            {
                return;
            }

            try
            {
                ApplyExplorationPreset(pmcLevel, preset, ref questTypePool, ref repeatableConfig);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[ISB Aishi] Failed to apply custom exploration settings.");
            }
        }

        private static void Postfix(
            MongoId traderId,
            RepeatableQuestConfig repeatableConfig,
            ref RepeatableQuest? __result)
        {
            if (__result is null || !traderId.Equals(TraderId))
            {
                return;
            }

            var preset = GetPreset(repeatableConfig.Name);
            if (_config?.Enabled != true || preset?.Enabled != true)
            {
                return;
            }

            ApplyCommonQuestSettings(__result, preset, repeatableConfig.Name, "Exploration");
        }
    }

    [HarmonyPatch(typeof(RepeatableQuestController), "GetQuestCount")]
    private static class DailyQuestCountPatch
    {
        private static void Postfix(
            RepeatableQuestConfig repeatableConfig,
            SptProfile fullProfile,
            ref int __result)
        {
            _dailyExtraGeneration = null;
            _forceGuaranteedDailyAishi = false;

            if (_config?.Enabled != true
                || !string.Equals(repeatableConfig.Name, "Daily", StringComparison.OrdinalIgnoreCase)
                || _config.Daily.Enabled != true)
            {
                return;
            }

            var range = _config.GuaranteedDailyQuests;
            if (range.Max <= 0)
            {
                return;
            }

            var aishiId = new MongoId(TraderId);
            if (!fullProfile.CharacterData.PmcData.TradersInfo.TryGetValue(aishiId, out var aishiInfo)
                || !aishiInfo.Unlocked.GetValueOrDefault(false))
            {
                LogInformation("[ISB Aishi] Guaranteed Daily extras skipped because Aishi is locked for this profile.");
                return;
            }

            var extraCount = NextInclusive(range.Min, range.Max);
            if (extraCount <= 0)
            {
                return;
            }

            var baseQuestCount = __result;
            _dailyExtraGeneration = new DailyExtraGenerationContext
            {
                BaseRandomSlots = Math.Max(0, baseQuestCount - 3),
                ExtraSlots = extraCount
            };

            __result += extraCount;

            LogInformation(
                "[ISB Aishi] Daily reset will generate {ExtraCount} guaranteed extra Aishi quest(s). Quest count: {BaseCount} -> {TotalCount}.",
                extraCount,
                baseQuestCount,
                __result);
        }
    }

    [HarmonyPatch(typeof(RepeatableQuestController), "TryGenerateRandomRepeatable")]
    private static class RandomDailyGenerationPatch
    {
        private static void Prefix(RepeatableQuestConfig repeatableConfig)
        {
            _forceGuaranteedDailyAishi = false;

            var context = _dailyExtraGeneration;
            if (context is null
                || !string.Equals(repeatableConfig.Name, "Daily", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            context.RandomCallsSeen++;
            if (context.RandomCallsSeen <= context.BaseRandomSlots || context.ExtraCallsStarted >= context.ExtraSlots)
            {
                return;
            }

            context.ExtraCallsStarted++;
            _forceGuaranteedDailyAishi = true;

            LogInformation(
                "[ISB Aishi] Generating guaranteed Aishi Daily extra {Current}/{Total}.",
                context.ExtraCallsStarted,
                context.ExtraSlots);
        }

        private static void Postfix()
        {
            _forceGuaranteedDailyAishi = false;

            if (_dailyExtraGeneration is not null
                && _dailyExtraGeneration.ExtraCallsStarted >= _dailyExtraGeneration.ExtraSlots)
            {
                _dailyExtraGeneration = null;
            }
        }
    }

    [HarmonyPatch(typeof(RepeatableQuestController), nameof(RepeatableQuestController.PickAndGenerateRandomRepeatableQuest))]
    private static class RandomQuestTypePatch
    {
        private static void Prefix(
            RepeatableQuestConfig repeatableConfig,
            ref QuestTypePool questTypePool)
        {
            if (!_forceGuaranteedDailyAishi
                || !string.Equals(repeatableConfig.Name, "Daily", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var configuredTypes = GetQuestTypes("Daily");
            var allowedTypes = questTypePool.Types
                .Where(type => configuredTypes.Contains(type))
                .ToList();

            if (allowedTypes.Count == 0)
            {
                _logger?.LogWarning(
                    "[ISB Aishi] No configured Aishi quest type is available for a guaranteed Daily extra.");
                return;
            }

            questTypePool = questTypePool with
            {
                Types = allowedTypes
            };
        }
    }

}

public sealed class AishiRepeatablesConfig
{
    public bool Enabled { get; set; } = true;
    public bool ForceAishiTempQuests { get; set; }
    public bool ShowRepeatableQuestLogs { get; set; }
    public IntRange GuaranteedDailyQuests { get; set; } = new() { Min = 1, Max = 2 };
    public RepeatablePreset Daily { get; set; } = new();
    public RepeatablePreset Weekly { get; set; } = new();
}

public sealed class RepeatablePreset
{
    public bool Enabled { get; set; } = true;
    public List<string> QuestTypes { get; set; } = ["Elimination"];
    public Dictionary<string, string> Images { get; set; } = [];
    public RerollPreset Reroll { get; set; } = new();
    public EliminationPreset Elimination { get; set; } = new();
    public CompletionPreset Completion { get; set; } = new();
    public ExplorationPreset Exploration { get; set; } = new();
    public RewardPreset Rewards { get; set; } = new();
}

public sealed class RerollPreset
{
    public List<RerollCostEntry> ChangeCost { get; set; } = [];
}

public sealed class RerollCostEntry
{
    public string TemplateId { get; set; } = "5696686a4bdc2da3298b456a";
    public int Count { get; set; } = 100;
}

public sealed class EliminationPreset
{
    public List<TargetPreset> Targets { get; set; } = [];
    public int BodyPartChance { get; set; }
    public List<WeightedStringListPreset> BodyParts { get; set; } = [];
    public int SpecificLocationChance { get; set; }

    public List<string> Locations { get; set; } = ["any"];
    public List<string> DistLocationBlacklist { get; set; } = [];
    public double DistProb { get; set; }
    public double MaxDist { get; set; } = 100;
    public double MinDist { get; set; } = 20;
    public int MaxKills { get; set; } = 8;
    public int MinKills { get; set; } = 3;
    public int MaxBossKills { get; set; } = 2;
    public int MinBossKills { get; set; } = 1;
    public int MaxPmcKills { get; set; } = 8;
    public int MinPmcKills { get; set; } = 3;
    public int WeaponRequirementChance { get; set; }
    public int WeaponCategoryRequirementChance { get; set; }
    public List<WeightedMongoListPreset> WeaponCategoryRequirements { get; set; } = [];
    public List<WeightedMongoListPreset> WeaponRequirements { get; set; } = [];

    public AdvancedEliminationConditionPreset ConditionOverrides { get; set; } = new();
}

public sealed class TargetPreset
{
    public string Key { get; set; } = "Savage";
    public double RelativeProbability { get; set; } = 1;
    public TargetDataPreset Data { get; set; } = new();
}

public sealed class TargetDataPreset
{
    public bool IsBoss { get; set; }
    public bool IsPmc { get; set; }
}

public sealed class WeightedStringListPreset
{
    public string Key { get; set; } = string.Empty;
    public double RelativeProbability { get; set; } = 1;
    public List<string> Data { get; set; } = [];
}

public sealed class WeightedMongoListPreset
{
    public string Key { get; set; } = string.Empty;
    public double RelativeProbability { get; set; } = 1;
    public List<string> Data { get; set; } = [];
}

public sealed class AdvancedEliminationConditionPreset
{
    public bool OneSessionOnly { get; set; }
    public bool ResetOnSessionEnd { get; set; }
    public double WeaponCaliberChance { get; set; }
    public List<string> WeaponCalibers { get; set; } = [];
    public double DaytimeChance { get; set; }
    public DaytimePreset Daytime { get; set; } = new();
}

public sealed class DaytimePreset
{
    public int From { get; set; }
    public int To { get; set; }
}

public sealed class CompletionPreset
{
    public IntRange RequestedItemCount { get; set; } = new() { Min = 1, Max = 3 };
    public IntRange UniqueItemCount { get; set; } = new() { Min = 1, Max = 1 };
    public IntRange RequestedBulletCount { get; set; } = new() { Min = 15, Max = 40 };
    public bool UseWhitelist { get; set; }
    public bool UseBlacklist { get; set; }
    public bool RequiredItemsAreFiR { get; set; } = true;
    public IntRange RequiredItemMinDurabilityMinMax { get; set; } = new() { Min = 0, Max = 100 };
    public List<string> RequiredItemTypeBlacklist { get; set; } = [];

    public List<LevelItemPoolPreset> ItemsWhitelist { get; set; } = [];
    public List<LevelItemPoolPreset> ItemsBlacklist { get; set; } = [];
}

public sealed class LevelItemPoolPreset
{
    public int MinPlayerLevel { get; set; } = 1;
    public List<string> ItemIds { get; set; } = [];
}

public sealed class ExplorationPreset
{
    public List<string> Locations { get; set; } = [];
    public int MinExtracts { get; set; } = 1;
    public int MaxExtracts { get; set; } = 3;
    public int MinExtractsWithSpecificExit { get; set; } = 1;
    public int MaxExtractsWithSpecificExit { get; set; } = 2;
    public SpecificExitsPreset SpecificExits { get; set; } = new();
}

public sealed class SpecificExitsPreset
{
    public double Chance { get; set; }
    public List<string> PassageRequirementWhitelist { get; set; } = [];
}

public sealed class RewardPreset
{
    public IntRange Xp { get; set; } = new();
    public MoneyRange Money { get; set; } = new();
    public DoubleRange Standing { get; set; } = new();
    public ItemRewardPreset ItemRewards { get; set; } = new();
}

public class IntRange
{
    public int Min { get; set; }
    public int Max { get; set; }
}

public sealed class MoneyRange : IntRange
{
    public string Currency { get; set; } = "USD";
}

public sealed class DoubleRange
{
    public double Min { get; set; }
    public double Max { get; set; }
}

public sealed class ItemRewardPreset : IntRange
{
    public bool AllowDuplicates { get; set; }
    public List<ItemRewardEntry> Pool { get; set; } = [];
}

public sealed class ItemRewardEntry
{
    public string Tpl { get; set; } = string.Empty;
    public double Weight { get; set; } = 1;
    public int MinAmount { get; set; } = 1;
    public int MaxAmount { get; set; } = 1;
}

internal sealed class DailyExtraGenerationContext
{
    public int BaseRandomSlots { get; init; }
    public int ExtraSlots { get; init; }
    public int RandomCallsSeen { get; set; }
    public int ExtraCallsStarted { get; set; }
}

internal sealed class CompletionGenerationContext
{
    public int PmcLevel { get; init; }
    public required RepeatablePreset Preset { get; init; }
}

internal sealed class ChosenRewardItem
{
    public required MongoId Tpl { get; init; }
    public required int Amount { get; init; }
}
