using System;
using System.Linq;
using System.Reflection;
using AishiKeys.MasterKey;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using SPT.Reflection.Patching;

namespace AishiKeys
{
    public class AishiKeysPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return ActionPatchResolver.ResolveWorldInteractiveActionsMethod();
        }

        [PatchPostfix]
        private static void Postfix(object __result, object[] __args)
        {
            try
            {
                Apply(__result, __args);
            }
            catch (Exception ex)
            {
                AishiKeysPro.AishiKeysMod.Logger?.LogError(
                    "Aishi Keys world-interaction patch failed: " + ex);
            }
        }

        private static void Apply(object result, object[] args)
        {
            GamePlayerOwner owner = args?
                .OfType<GamePlayerOwner>()
                .FirstOrDefault();
            WorldInteractiveObject worldInteractiveObject = args?
                .OfType<WorldInteractiveObject>()
                .FirstOrDefault();

            if (owner == null || worldInteractiveObject == null ||
                worldInteractiveObject.DoorState != EDoorState.Locked)
            {
                return;
            }

            if (worldInteractiveObject is KeycardDoor)
                return;

            GameWorld instance = Singleton<GameWorld>.Instance;
            if (instance == null || instance.MainPlayer == null)
                return;

            if (string.IsNullOrEmpty(worldInteractiveObject.KeyId))
                return;

            if (WorldInteractionUtils.IsBotInteraction(owner) ||
                owner.Player == null ||
                owner.Player.Profile == null ||
                owner.Player.Profile.Inventory == null)
            {
                return;
            }

            string masterKeyTemplateId =
                MasterKeyHelper.GetMasterKey(Config.UltraKeys.AishiMK);

            Item masterKey = owner.Player.Profile.Inventory
                .GetPlayerItems((EPlayerItems)63)
                .FirstOrDefault(item =>
                    item != null &&
                    string.Equals(
                        item.TemplateId,
                        masterKeyTemplateId,
                        StringComparison.Ordinal));

            if (masterKey == null)
                return;

            KeyComponent keyComponent =
                MasterKeyItemResolver.FindKeyComponent(masterKey);
            if (keyComponent == null)
            {
                AishiKeysPro.AishiKeysMod.Logger?.LogWarning(
                    "Aishi Keys could not expose the master-key action because the configured Aishi key item has no KeyComponent.");
                return;
            }

            string name =
                AishiLocalization.Localize("Try") + " " +
                AishiLocalization.Localize(masterKey.ShortName);

            Action callback = new Action(
                new MasterKeyInteraction(
                    worldInteractiveObject,
                    owner,
                    keyComponent).Unlock);

            if (!ActionResultAdapter.TryAddAction(
                    result,
                    name,
                    !worldInteractiveObject.Operatable,
                    callback))
            {
                AishiKeysPro.AishiKeysMod.Logger?.LogWarning(
                    "Aishi Keys could not add the master-key action to the interaction result.");
            }
        }
    }
}
