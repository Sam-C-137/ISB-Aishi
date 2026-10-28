using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AishiKeysPro;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
#if SPT41
using ItemContext = EFT.InventoryLogic.ItemContext;
using Interactions = EFT.UI.ContextInteractions<EFT.InventoryLogic.EItemInfoButton>;
using NotificationManagerClass = EFT.Communications.NotificationManager;

#else
using ItemContext = ItemContextAbstractClass;
using Interactions = ItemInfoInteractionsAbstractClass<EFT.InventoryLogic.EItemInfoButton>;
#endif
namespace AishiKeys
{
    internal static class AnomalyRevealClient
    {
        private sealed class Settings
        {
            public bool? ready;
            public bool removeKTErestrictions;
            public string requiredEquippedItem, profileId;
            public string[] revealed;
            public Dictionary<string, float> charges;
        }

        private sealed class Reply
        {
            public bool success, already;
            public float remaining;
            public string profileId;
            public string[] revealed;
            public string error;
        }

        private static string requiredItem, scope;
        private static bool removeKteRestrictions;
        internal static bool RemoveKteRestrictions { get { EnsureScope(); return removeKteRestrictions; } }
        private static readonly HashSet<string> Revealed = new HashSet<string>(StringComparer.Ordinal);
        private static Dictionary<string, float> pendingCharges;
        private static bool busy, loading;
        private static int generation;
        private static DateTime retryAfter;
        internal static void Initialize()
        {
            _ = Load(null);
        }

        private static bool InRaid => Singleton<GameWorld>.Instantiated && !(Singleton<GameWorld>.Instance is HideoutGameWorld);

        private static Profile CurrentProfile() => InRaid ? Singleton<GameWorld>.Instance.MainPlayer?.Profile : ItemUiContext.Instance?.ClientSession?.GetProfileBySide(ESideType.Pmc);
        private static void EnsureScope()
        {
            var profile = CurrentProfile();
            if (profile != null && (profile.Id != scope || !loading && requiredItem == null && DateTime.UtcNow >= retryAfter))
            {
                scope = profile.Id;
                _ = Load(scope);
            }

            if (!loading && pendingCharges != null && profile?.Id == scope)
            {
                var charges = pendingCharges;
                pendingCharges = null;
                foreach (var item in OwnedItems(profile))
                {
                    if (item.TemplateId != requiredItem || !charges.TryGetValue(item.Id, out var value))
                        continue;
                    var resource = item.GetItemComponent<ResourceComponent>();
                    if (resource != null)
                    {
                        resource.Value = value;
                        item.RaiseRefreshEvent(false, true);
                    }
                }
            }
        }

        private static IEnumerable<Item> OwnedItems(Profile profile)
        {
            var equipment = profile?.Inventory?.Equipment?.GetAllItems() ?? Enumerable.Empty<Item>();
            var stash = profile?.Inventory?.Stash?.GetAllItems() ?? Enumerable.Empty<Item>();
            return equipment.Concat(stash);
        }

        private static async Task Load(string expectedProfile)
        {
            var ticket = ++generation;
            retryAfter = DateTime.UtcNow.AddSeconds(5);
            requiredItem = null;
            removeKteRestrictions = false;
            Revealed.Clear();
            pendingCharges = null;
            busy = false;
            loading = true;
            try
            {
                var json = await RequestHandler.GetJsonAsync("/aishi/anomalous-items/config");
                var config = JsonConvert.DeserializeObject<Settings>(json);
                if (ticket != generation)
                    return;
                removeKteRestrictions = config?.removeKTErestrictions == true;
                if (config?.ready == false)
                    return;
                if (config?.requiredEquippedItem?.Length != 24 || string.IsNullOrEmpty(config.profileId))
                    throw new InvalidOperationException("Invalid anomaly reveal configuration.");
                if (expectedProfile != null && expectedProfile != config.profileId)
                    throw new InvalidOperationException("The active profile changed. Try again.");
                scope = config.profileId;
                requiredItem = config.requiredEquippedItem;
                if (config.revealed != null)
                    Revealed.UnionWith(config.revealed);
                pendingCharges = config.charges;
            }
            catch (Exception error)
            {
                if (ticket == generation)
                    AishiKeysMod.Logger.LogError("Aishi anomaly reveal configuration failed; properties remain hidden: " + error.Message);
            }
            finally
            {
                if (ticket == generation)
                    loading = false;
            }
        }

        internal static Item Equipped()
        {
            EnsureScope();
            if (string.IsNullOrEmpty(requiredItem) || loading)
                return null;
            var pockets = CurrentProfile()?.Inventory?.Equipment?.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem;
            if (pockets?.Slots == null)
                return null;
            foreach (var slot in pockets.Slots)
                if (slot != null && IsSpecialSlot(slot.ID) && slot.ContainedItem?.TemplateId == requiredItem)
                    return slot.ContainedItem;
            return null;
        }

        internal static bool IsSpecialSlot(string slot) => slot?.StartsWith("SpecialSlot", StringComparison.Ordinal) == true;
        internal static bool HasAnalysisCharge(Item item) => item?.GetItemComponent<ResourceComponent>()?.Value >= 1f;
        internal static bool IsEquipped() => Equipped() != null;
        internal static bool IsRevealed(string template)
        {
            EnsureScope();
            return !loading && Revealed.Contains(template);
        }

        internal static bool CanReveal(Item item) => !busy && item != null && AishiInventoryDescriptions.IsAnomaly(item.TemplateId) && !IsRevealed(item.TemplateId) && HasAnalysisCharge(Equipped()) && item.Owner != null && !item.Owner.Locked && OwnedItems(CurrentProfile()).Any(owned => owned.Id == item.Id && owned.TemplateId == item.TemplateId);
        internal static async void Reveal(Item item)
        {
            if (!CanReveal(item))
                return;
            var detector = Equipped();
            var resource = detector.GetItemComponent<ResourceComponent>();
            var ticket = generation;
            var profileId = scope;
            busy = true;
            try
            {
                if (!InRaid)
                {
                    var session = ItemUiContext.Instance?.ClientSession;
                    if (session == null)
                        throw new InvalidOperationException("The inventory session is not ready. Try again.");
                    await session.FlushOperationQueue();
                    EnsureScope();
                    if (ticket != generation || profileId != scope)
                        return;
                    var equipped = Equipped();
                    if (equipped == null || equipped.Id != detector.Id)
                        throw new InvalidOperationException("Equip the analyser in a special slot.");
                    if (!HasAnalysisCharge(equipped))
                        throw new InvalidOperationException("The analyser has no charges remaining.");
                }

                var request = JsonConvert.SerializeObject(new { detector = detector.Id, anomaly = item.Id, template = item.StringTemplateId, raid = InRaid, remaining = resource.Value, profileId });
                var json = await RequestHandler.PostJsonAsync("/aishi/anomalous-items/reveal", request);
                var reply = JsonConvert.DeserializeObject<Reply>(json);
                EnsureScope();
                if (ticket != generation || profileId != scope)
                    return;
                if (reply?.success != true)
                    throw new InvalidOperationException(reply?.error ?? "The server rejected the analysis.");
                if (reply.profileId != profileId || float.IsNaN(reply.remaining) || float.IsInfinity(reply.remaining) || reply.remaining < 0f || reply.remaining > 5f)
                    throw new InvalidOperationException("Invalid analysis response.");
                resource.Value = reply.remaining;
                detector.RaiseRefreshEvent(false, true);
                if (reply.revealed != null)
                    Revealed.UnionWith(reply.revealed);
                item.RaiseRefreshEvent(false, true);
                NotificationManagerClass.DisplayWarningNotification("aishi_anomaly_revealed".Localized());
            }
            catch (Exception error)
            {
                if (ticket != generation)
                    return;
                AishiKeysMod.Logger.LogError("Aishi anomaly analysis: " + error.Message);
                NotificationManagerClass.DisplayWarningNotification(error.Message);
            }
            finally
            {
                if (ticket == generation)
                    busy = false;
            }
        }
    }

    [HarmonyPatch(typeof(ItemUiContext), "GetItemContextInteractions")]
    internal static class AishiRevealPropertiesMenu
    {
        private static void Postfix(ItemContext __0, Interactions __result)
        {
            var item = __0?.Item;
            if (__result != null && AnomalyRevealClient.CanReveal(item))
                __result.method_2("aishi_reveal_properties", "Reveal Properties", () => AnomalyRevealClient.Reveal(item), null);
        }
    }
}
