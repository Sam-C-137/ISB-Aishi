using System;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.Interactive;

namespace AishiKeys
{
    internal static class ActionPatchResolver
    {
        private const string InteractionContextHelperTypeName = "EFT.InteractionContextHelper";
        private const string GetAvailableActionsMethodName = "GetAvailableActions";

        internal static MethodBase ResolveWorldInteractiveActionsMethod()
        {
            return Resolve(typeof(WorldInteractiveObject), false, true);
        }

        internal static MethodBase ResolveKeycardActionsMethod()
        {
            return Resolve(typeof(KeycardDoor), true, false);
        }

        private static MethodBase Resolve(
            Type interactiveType,
            bool preferBool,
            bool rejectKeycard)
        {
            Type helperType = typeof(Player).Assembly.GetType(
                InteractionContextHelperTypeName,
                false);

            if (helperType == null)
            {
                throw new TypeLoadException(
                    "SPT 4.1 type was not found: " + InteractionContextHelperTypeName);
            }

            MethodInfo[] candidates = helperType
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => string.Equals(
                    method.Name,
                    GetAvailableActionsMethodName,
                    StringComparison.Ordinal))
                .Where(method => HasCompatibleParameter(method, typeof(GamePlayerOwner)))
                .Where(method => HasCompatibleParameter(method, interactiveType))
                .Where(method => !rejectKeycard || !HasExactParameter(method, typeof(KeycardDoor)))
                .OrderByDescending(method => HasExactParameter(method, interactiveType))
                .ThenByDescending(method => preferBool && HasExactParameter(method, typeof(bool)))
                .ThenBy(method => method.GetParameters().Length)
                .ThenBy(method => method.MetadataToken)
                .ToArray();

            if (candidates.Length == 0)
            {
                throw new MissingMethodException(
                    helperType.FullName,
                    GetAvailableActionsMethodName + " for " + interactiveType.FullName);
            }

            return candidates[0];
        }

        private static bool HasCompatibleParameter(MethodInfo method, Type expectedType)
        {
            return method.GetParameters().Any(parameter =>
                parameter.ParameterType == expectedType ||
                parameter.ParameterType.IsAssignableFrom(expectedType) ||
                expectedType.IsAssignableFrom(parameter.ParameterType));
        }

        private static bool HasExactParameter(MethodInfo method, Type expectedType)
        {
            return method.GetParameters().Any(parameter =>
                parameter.ParameterType == expectedType);
        }
    }
}
