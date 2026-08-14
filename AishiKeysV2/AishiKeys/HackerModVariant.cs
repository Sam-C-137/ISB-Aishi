using AishiKeysPro;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Reflection;

namespace AishiKeys
{
    public static class HackerModVariant
    {
        private const string HackerModGuid = "com.manimal.hackermod";
        private const string MeuItemId = "6a321d846b38e922175d1878";
        private const string HackerConstantsTypeName =
            "Manimal.HackerMod.HackerConstants";

        public static void TryInject()
        {
            if (!Chainloader.PluginInfos.TryGetValue(HackerModGuid, out var pluginInfo) ||
                pluginInfo == null ||
                pluginInfo.Instance == null)
            {
                return;
            }

            Assembly hackerAssembly = pluginInfo.Instance.GetType().Assembly;
            Type hackerConstantsType = hackerAssembly.GetType(
                HackerConstantsTypeName,
                false);

            if (hackerConstantsType == null)
                return;

            FieldInfo field = hackerConstantsType.GetField(
                "AllHackerDeviceTpls",
                BindingFlags.Public | BindingFlags.Static);

            if (field == null)
                return;

            string[] current = field.GetValue(null) as string[];
            if (current == null)
                return;

            if (Array.IndexOf(current, MeuItemId) < 0)
            {
                string[] updated = new string[current.Length + 1];
                current.CopyTo(updated, 0);
                updated[current.Length] = MeuItemId;
                field.SetValue(null, updated);
            }

            MethodInfo getCanvasRotation = hackerConstantsType.GetMethod(
                "GetCanvasRotation",
                BindingFlags.Public | BindingFlags.Static);

            if (getCanvasRotation == null)
                return;

            MethodInfo postfix = typeof(HackerModVariant).GetMethod(
                nameof(GetCanvasRotationPostfix),
                BindingFlags.NonPublic | BindingFlags.Static);

            if (postfix == null)
                return;

            Harmony harmony = new Harmony("com.samc137.aishi.hackercolab");
            harmony.Patch(
                getCanvasRotation,
                postfix: new HarmonyMethod(postfix));

            AishiKeysMod.Logger?.LogInfo("Aishi Keys HackerMod integration initialized.");
        }

        private static void GetCanvasRotationPostfix(string tpl, ref float __result)
        {
            if (tpl == MeuItemId)
                __result = 180f;
        }
    }
}
