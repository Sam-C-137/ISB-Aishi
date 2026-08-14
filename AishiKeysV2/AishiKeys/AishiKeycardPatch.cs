using System;
using System.Collections.Generic;
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
    public class AishiKeycardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return ActionPatchResolver.ResolveKeycardActionsMethod();
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
                    "Aishi Keys keycard patch failed: " + ex);
            }
        }

        private static void Apply(object result, object[] args)
        {
            GamePlayerOwner owner = args?
                .OfType<GamePlayerOwner>()
                .FirstOrDefault();
            KeycardDoor door = args?
                .OfType<KeycardDoor>()
                .FirstOrDefault();

            if (owner == null || door == null ||
                door.DoorState != EDoorState.Locked)
            {
                return;
            }

            GameWorld instance = Singleton<GameWorld>.Instance;
            if (instance == null || instance.MainPlayer == null)
                return;

            if (string.IsNullOrEmpty(door.KeyId) ||
                WorldInteractionUtils.IsBotInteraction(owner) ||
                owner.Player == null ||
                owner.Player.Profile == null ||
                owner.Player.Profile.Inventory == null)
            {
                return;
            }

            List<Item> masterKeys = owner.Player.Profile.Inventory
                .GetPlayerItems((EPlayerItems)63)
                .Where(item =>
                    item != null &&
                    MasterKeyHelper.AllMasterKeyIds.Contains(item.TemplateId))
                .ToList();

            if (masterKeys.Count == 0)
                return;

            List<ValueTuple<KeyComponent, string>> resolvedKeys =
                new List<ValueTuple<KeyComponent, string>>();

            foreach (Item keyItem in masterKeys)
            {
                KeyComponent keyComponent =
                    MasterKeyItemResolver.FindKeyComponent(keyItem);
                if (keyComponent == null)
                    continue;

                resolvedKeys.Add(
                    new ValueTuple<KeyComponent, string>(
                        keyComponent,
                        AishiLocalization.Localize(keyItem.ShortName)));
            }

            if (resolvedKeys.Count == 0)
                return;

            foreach (object action in ActionResultAdapter.EnumerateActions(result))
            {
                string actionName = ActionResultAdapter.GetName(action);
                if (string.IsNullOrEmpty(actionName))
                    continue;

                foreach (ValueTuple<KeyComponent, string> resolved in resolvedKeys)
                {
                    KeyComponent keyComponent = resolved.Item1;
                    string shortName = resolved.Item2;

                    if (string.IsNullOrEmpty(shortName) ||
                        actionName.IndexOf(
                            shortName,
                            StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    Action callback = new Action(
                        new MasterKeyKeycardInteraction(
                            door,
                            owner,
                            keyComponent).Unlock);

                    if (!ActionResultAdapter.TrySetAction(action, callback))
                    {
                        AishiKeysPro.AishiKeysMod.Logger?.LogWarning(
                            "Aishi Keys could not replace keycard action.");
                    }

                    break;
                }
            }
        }
    }
}
