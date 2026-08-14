using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;

namespace AishiKeys.MasterKey
{
    internal static class MasterKeyUnlockExecutor
    {
        private static readonly Dictionary<Type, MethodInfo> StartInteractionMethods =
            new Dictionary<Type, MethodInfo>();
        private static readonly HashSet<Type> MissingStartInteractionTypes =
            new HashSet<Type>();
        private static readonly object ResolverLock = new object();

        internal static bool UnlockRegularLocal(
            WorldInteractiveObject door,
            string masterKeyTemplateId)
        {
            if (door == null || string.IsNullOrEmpty(masterKeyTemplateId))
                return false;

            string originalKeyId = door.KeyId;

            try
            {
                door.KeyId = masterKeyTemplateId;
                door.Interact(EInteractionType.Unlock);
                return true;
            }
            finally
            {
                door.KeyId = originalKeyId;
            }
        }

        internal static bool UnlockRegularSynchronized(
            WorldInteractiveObject door,
            Player player,
            KeyComponent keyComponent)
        {
            return UnlockWithNativeKeyOperation(door, player, keyComponent);
        }

        internal static bool UnlockKeycard(
            KeycardDoor door,
            Player player,
            KeyComponent keyComponent)
        {
            return UnlockWithNativeKeyOperation(door, player, keyComponent);
        }

        private static bool UnlockWithNativeKeyOperation(
            WorldInteractiveObject door,
            Player player,
            KeyComponent keyComponent)
        {
            if (door == null)
                return false;

            if (keyComponent == null)
            {
                door.Interact(EInteractionType.Unlock);
                return true;
            }

            if (player == null || keyComponent.Item == null)
                return false;

            string originalKeyId = door.KeyId;

            try
            {
                door.KeyId = keyComponent.Item.TemplateId;
                var unlockResult = door.UnlockOperation(keyComponent, player, door);
                door.KeyId = originalKeyId;

                if (unlockResult.Failed || unlockResult.Value == null)
                    return false;

                unlockResult.Value.RaiseEvents(
                    player.InventoryController,
                    CommandStatus.Begin);

                Action completion = () => unlockResult.Value.RaiseEvents(
                    player.InventoryController,
                    CommandStatus.Succeed);

                if (!TryStartInteraction(player, door, unlockResult.Value, completion))
                {
                    door.Interact(unlockResult.Value);
                    completion();
                }

                return true;
            }
            finally
            {
                door.KeyId = originalKeyId;
            }
        }

        private static bool TryStartInteraction(
            Player player,
            WorldInteractiveObject door,
            InteractionResult interactionResult,
            Action completion)
        {
            if (player == null || door == null || interactionResult == null)
                return false;

            MethodInfo method = ResolveStartInteractionMethod(
                player.GetType(),
                door.GetType(),
                interactionResult.GetType());

            if (method == null)
                return false;

            try
            {
                ParameterInfo[] parameters = method.GetParameters();
                object callback = ConvertCallback(completion, parameters[2].ParameterType);
                if (callback == null)
                    return false;

                method.Invoke(player, new[]
                {
                    (object)door,
                    interactionResult,
                    callback
                });
                return true;
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                AishiKeysPro.AishiKeysMod.Logger?.LogWarning(
                    "Aishi Keys native interaction start failed: " + cause.Message);
                return false;
            }
            catch (Exception ex)
            {
                AishiKeysPro.AishiKeysMod.Logger?.LogWarning(
                    "Aishi Keys native interaction start failed: " + ex.Message);
                return false;
            }
        }

        private static MethodInfo ResolveStartInteractionMethod(
            Type playerType,
            Type doorType,
            Type interactionResultType)
        {
            lock (ResolverLock)
            {
                MethodInfo cached;
                if (StartInteractionMethods.TryGetValue(playerType, out cached))
                    return cached;

                if (MissingStartInteractionTypes.Contains(playerType))
                    return null;
            }

            BindingFlags flags = BindingFlags.Instance |
                                 BindingFlags.Public |
                                 BindingFlags.NonPublic |
                                 BindingFlags.DeclaredOnly;

            List<MethodInfo> candidates = new List<MethodInfo>();

            for (Type current = playerType; current != null; current = current.BaseType)
            {
                MethodInfo[] methods;
                try
                {
                    methods = current.GetMethods(flags);
                }
                catch
                {
                    continue;
                }

                candidates.AddRange(methods.Where(method =>
                    IsInteractionStartSignature(
                        method,
                        doorType,
                        interactionResultType)));
            }

            MethodInfo resolved = candidates
                .OrderByDescending(method => string.Equals(
                    method.Name,
                    "StartInteraction",
                    StringComparison.Ordinal))
                .ThenBy(method => GetInheritanceDistance(playerType, method.DeclaringType))
                .ThenBy(method => method.MetadataToken)
                .FirstOrDefault();

            lock (ResolverLock)
            {
                if (resolved != null)
                    StartInteractionMethods[playerType] = resolved;
                else
                    MissingStartInteractionTypes.Add(playerType);
            }

            return resolved;
        }

        private static bool IsInteractionStartSignature(
            MethodInfo method,
            Type doorType,
            Type interactionResultType)
        {
            if (method == null || method.IsStatic || method.ReturnType != typeof(void))
                return false;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 3)
                return false;

            if (!parameters[0].ParameterType.IsAssignableFrom(doorType))
                return false;

            if (!parameters[1].ParameterType.IsAssignableFrom(interactionResultType))
                return false;

            Type callbackType = parameters[2].ParameterType;
            return callbackType == typeof(Action) ||
                   typeof(Delegate).IsAssignableFrom(callbackType);
        }

        private static object ConvertCallback(Action callback, Type targetType)
        {
            if (callback == null || targetType == null)
                return null;

            if (targetType.IsInstanceOfType(callback))
                return callback;

            if (!typeof(Delegate).IsAssignableFrom(targetType))
                return null;

            try
            {
                return callback.Target == null
                    ? Delegate.CreateDelegate(targetType, callback.Method)
                    : Delegate.CreateDelegate(targetType, callback.Target, callback.Method);
            }
            catch
            {
                return null;
            }
        }

        private static int GetInheritanceDistance(Type derivedType, Type baseType)
        {
            int distance = 0;
            for (Type current = derivedType; current != null; current = current.BaseType)
            {
                if (current == baseType)
                    return distance;

                distance++;
            }

            return int.MaxValue;
        }
    }
}
