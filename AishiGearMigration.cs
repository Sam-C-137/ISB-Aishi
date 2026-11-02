using System.Text.Json.Nodes;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils;

namespace Aishi;
internal static class DuplicateHpcRecovery
{
    internal const string Obsolete = "4c969317ea41597aa0403b6c";
    internal const string Original = "63737f448b28897f2802b874";
    internal const string ObsoleteTube = "afadfe2d75485c78ad0bbbb3";
    internal const string OriginalTube = "5c793fb92e221644f31bfb64";
    internal static int Repair(JsonNode? node)
    {
        int changed = 0;
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && (text == Obsolete || text == ObsoleteTube))
                {
                    obj[pair.Key] = text == ObsoleteTube ? OriginalTube : Original;
                    changed++;
                }
                else
                    changed += Repair(pair.Value);
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text) && (text == Obsolete || text == ObsoleteTube))
                {
                    array[i] = text == ObsoleteTube ? OriginalTube : Original;
                    changed++;
                }
                else
                    changed += Repair(array[i]);
            }
        }

        return changed;
    }
}

[Injectable(TypePriority = OnLoadOrder.SaveCallbacks - 1)]
public sealed class AishiGearMigration(SaveServer saves, JsonUtil json) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var harmony = new Harmony("com.samc137.aishi.duplicate-hpc-recovery");
        harmony.Patch(AccessTools.Method(typeof(ProfileMigrationService), nameof(ProfileMigrationService.MigrateAndValidateProfile)), prefix: new HarmonyMethod(typeof(AishiGearMigration), nameof(BeforeProfileValidation)));
        foreach (var pair in saves.GetProfiles().ToArray())
        {
            var serialized = json.Serialize(pair.Value);
            if (!serialized.Contains(DuplicateHpcRecovery.Obsolete, StringComparison.Ordinal) && !serialized.Contains(DuplicateHpcRecovery.ObsoleteTube, StringComparison.Ordinal) && !serialized.Contains(AishiHammerheadSlotMigration.Template, StringComparison.Ordinal) && !serialized.Contains(AishiQuestConditionIds.Previous, StringComparison.Ordinal))
                continue;
            var node = JsonNode.Parse(serialized)!;
            if (Repair(node) == 0)
                continue;
            var recovered = json.Deserialize<SptProfile>(node.ToJsonString())!;
            foreach (var property in typeof(SptProfile).GetProperties().Where(p => p.CanRead && p.CanWrite))
                property.SetValue(pair.Value, property.GetValue(recovered));
        }

        return Task.CompletedTask;
    }

    private static int Repair(JsonNode node) => DuplicateHpcRecovery.Repair(node) + AishiHammerheadSlotMigration.Repair(node) + AishiQuestConditionIds.Repair(node);

    private static void BeforeProfileValidation(JsonObject __0) => Repair(__0);
}
