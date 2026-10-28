using System;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
#if SPT41
using ItemContext = EFT.InventoryLogic.ItemContext;
using Interactions = EFT.UI.ContextInteractions<EFT.InventoryLogic.EItemInfoButton>;
using Controller = EFT.InventoryLogic.ItemController;
using NotificationManagerClass = EFT.Communications.NotificationManager;

#else
using ItemContext=ItemContextAbstractClass;
using Interactions=ItemInfoInteractionsAbstractClass<EFT.InventoryLogic.EItemInfoButton>;
using Controller=TraderControllerClass;
#endif
namespace AishiKeys
{
    [HarmonyPatch(typeof(ItemUiContext), "GetItemContextInteractions")]
    internal static class AishiTapeConversion
    {
        private static readonly FieldInfo ControllerField = AccessTools.Field(typeof(ItemUiContext), 
#if SPT41
        "_itemController");
#else
            "traderControllerClass");
#endif
        private static bool busy;
        internal static bool InStash(Item item)
        {
            if (item == null || busy || (Singleton<GameWorld>.Instantiated && !(Singleton<GameWorld>.Instance is HideoutGameWorld)) || item.Owner == null || item.Owner.Locked)
                return false;
            var stash = ItemUiContext.Instance?.ClientSession?.GetProfileBySide(ESideType.Pmc)?.Inventory?.Stash;
            return TapeStashTraversal.IsLooseInStash(item, stash, ParentOf, IsWeapon);
        }

        private static Item ParentOf(Item item) => item.CurrentAddress?.Container?.ParentItem;
        private static bool IsWeapon(Item item) => item is Weapon;
        internal static bool IsTapeEligible(Item item)
        {
            return AishiMagazineVariants.IsTape(item) && InStash(item);
        }

        private static void Postfix(ItemContext __0, Interactions __result)
        {
            var item = __0?.Item;
            if (__result == null || !IsTapeEligible(item))
                return;
            __result.method_2("aishi_change_tape", "Change Tape", () => Open(item), null);
        }

        private static void Open(Item item)
        {
            if (!IsTapeEligible(item) || !AishiMagazineVariants.Families.TryGetValue(item.TemplateId, out var family))
                return;
            var ui = ItemUiContext.Instance;
            var picker = ui.GetComponent<AishiTapeMenu>() ?? ui.gameObject.AddComponent<AishiTapeMenu>();
            picker.Open(family.Name, family.Variants.Where(v => AishiMagazineVariants.IsColor(v.TagColor)).Select(v => new TapeOption { Label = v.Label, Color = v.Color, Selected = v.TagColor == AishiMagazineVariants.ColorCode(item), Select = () => Change(item, v.TagColor) }).ToArray(), null);
        }

        private static async void Change(Item item, int color)
        {
            if (!IsTapeEligible(item) || !AishiMagazineVariants.IsColor(color) || AishiMagazineVariants.ColorCode(item) == color)
                return;
            var tag = item.GetItemComponent<TagComponent>();
            var controller = ControllerField.GetValue(ItemUiContext.Instance) as Controller;
            if (tag == null || controller == null)
                return;
            busy = true;
            try
            {
                var result = await controller.TryRunNetworkTransaction(tag.Set(tag.Name ?? string.Empty, color, true), null);
                if (!result.Succeed)
                    throw new InvalidOperationException(result.Error?.ToString() ?? "Inventory transaction rejected");
            }
            catch (Exception error)
            {
                AishiKeysPro.AishiKeysMod.Logger.LogError("Tape color: " + error);
                NotificationManagerClass.DisplayWarningNotification("aishi_tape_change_failed".Localized());
            }
            finally
            {
                busy = false;
            }
        }
    }
}
