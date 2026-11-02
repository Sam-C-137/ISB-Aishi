using HarmonyLib;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Helpers.Quest;
using System.Text.Json.Nodes;

namespace Aishi;
[HarmonyPatch(typeof(QuestHelper), nameof(QuestHelper.ResetQuestState))]
internal static class AishiQuestRestart
{
    [HarmonyPostfix]
    private static void AfterReset(QuestHelper __instance, PmcData pmcData, QuestStatusEnum newQuestState, MongoId questId)
    {
        if (newQuestState != QuestStatusEnum.Started)
            return;
        var quest = __instance.GetQuestFromDb(questId, pmcData);
        if (quest == null || quest.TraderId != "690766de550bc322a810ea1e" || quest.Restartable != true)
            return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (quest.Conditions != null)
            foreach (var condition in (quest.Conditions.AvailableForFinish ?? []).Concat(quest.Conditions.AvailableForStart ?? []).Concat(quest.Conditions.Fail ?? []))
            {
                ids.Add(condition.Id.ToString());
                if (!string.IsNullOrEmpty(condition.Counter?.Id))
                    ids.Add(condition.Counter.Id);
            }

        if (pmcData.TaskConditionCounters != null)
            foreach (var pair in pmcData.TaskConditionCounters)
                if (pair.Value.SourceId == questId || ids.Contains(pair.Key.ToString()))
                    pair.Value.Value = 0;
        var stats = pmcData.Stats?.Eft;
        if (stats?.DroppedItems != null)
            stats.DroppedItems = stats.DroppedItems.Where(x => x.QuestId != questId).ToList();
        if (stats?.FoundInRaidItems != null)
            stats.FoundInRaidItems = stats.FoundInRaidItems.Where(x => x.QuestId != questId).ToList();
    }
}

internal static class AishiQuestConditionIds
{
    internal const string Quest = "690ad6e0f4aac3c28195d97c";
    internal const string Previous = "668858720221bbe5f306b397";
    internal const string Current = "ddfadd6f6cc0c7b1c7b6ef9f";

    internal static int Repair(JsonNode? profile)
    {
        if (profile?["characters"]?["pmc"]?["Quests"] is not JsonArray quests)
            return 0;
        int changed = 0;
        foreach (var quest in quests.OfType<JsonObject>())
        {
            if (quest["qid"]?.GetValue<string>() != Quest || quest["completedConditions"] is not JsonArray completed)
                continue;
            for (int index = completed.Count - 1; index >= 0; index--)
            {
                if (completed[index]?.GetValue<string>() != Previous)
                    continue;
                if (completed.Any(id => id?.GetValue<string>() == Current))
                    completed.RemoveAt(index);
                else
                    completed[index] = Current;
                changed++;
            }
        }
        return changed;
    }
}
