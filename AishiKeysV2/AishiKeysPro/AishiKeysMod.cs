using System;
using AishiKeys;
using AishiKeys.MasterKey;
using AishiKeys.Networking;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using System.IO;
using System.Reflection;
using EFT;
using EFT.Interactive;
using EFT.UI;
using UnityEngine;

namespace AishiKeysPro
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(FikaPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class AishiKeysMod : BaseUnityPlugin
    {
        public const string PluginGuid = "com.samc137.aishi";
        public const string PluginName = "AishiMasterKeyFunction";
        public const string PluginVersion = "2.0.0";
        public const string FikaPluginGuid = "com.fika.core";

        private const string FikaBridgeAssemblyFileName = "AishiKeysV2.Fika.dll";
        private const string FikaBridgeAssemblyName = "AishiKeysV2.Fika";
        private const string FikaBridgeBootstrapTypeName =
            "AishiKeys.Fika.AishiKeysFikaBootstrap";

        internal static AishiKeysMod Instance { get; private set; }
        internal static ManualLogSource Logger { get; private set; }

        private IAishiKeysNetworkBridge _networkBridge = NoNetworkBridge.Instance;
        private AishiKeysNetworkSyncCoordinator _networkSync;
        private MethodInfo _fikaBridgeShutdownMethod;
        private bool _fikaBridgeInitialized;

        public void Awake()
        {
            Instance = this;
            Logger = base.Logger;

            TryInitializeComponent(
                "world interaction patch",
                () => new AishiKeysPatch().Enable());
            TryInitializeComponent(
                "keycard interaction patch",
                () => new AishiKeycardPatch().Enable());
            TryInitializeComponent(
                "HackerMod integration",
                HackerModVariant.TryInject);

            try
            {
                _networkSync = new AishiKeysNetworkSyncCoordinator(
                    this,
                    _networkBridge,
                    Logger);
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    "Aishi Keys network coordinator initialization failed: " + ex);
            }

            TryInitializeOptionalFikaBridge();

            Logger.LogInfo(
                "Aishi Keys 2.0 initialized. GUID=" +
                Info.Metadata.GUID +
                ", version=" +
                Info.Metadata.Version +
                ".");
        }

        private static void TryInitializeComponent(string componentName, Action initializer)
        {
            if (initializer == null)
                return;

            try
            {
                initializer();
                Logger?.LogInfo("Aishi Keys initialized " + componentName + ".");
            }
            catch (Exception ex)
            {
                Logger?.LogError(
                    "Aishi Keys failed to initialize " + componentName + ": " + ex);
            }
        }

        public void Update()
        {
            _networkSync?.Update();
        }

        public void OnDestroy()
        {
            ShutdownOptionalFikaBridge();
            _networkSync?.Dispose();

            if (_networkBridge != null &&
                !ReferenceEquals(_networkBridge, NoNetworkBridge.Instance))
            {
                _networkBridge.Dispose();
            }

            _networkSync = null;
            _networkBridge = NoNetworkBridge.Instance;

            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        private void TryInitializeOptionalFikaBridge()
        {
            if (!Chainloader.PluginInfos.ContainsKey(FikaPluginGuid))
                return;

            string pluginDirectory = Path.GetDirectoryName(Info.Location);
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                Logger.LogWarning(
                    "Aishi Keys: Fika is installed, but the plugin directory could not be resolved.");
                return;
            }

            string bridgePath = Path.Combine(
                pluginDirectory,
                FikaBridgeAssemblyFileName);

            if (!File.Exists(bridgePath))
            {
                Logger.LogWarning(
                    "Aishi Keys: Fika is installed, but " +
                    FikaBridgeAssemblyFileName +
                    " was not found next to AishiKeysV2.dll. Synchronization is disabled.");
                return;
            }

            try
            {
                Assembly bridgeAssembly = FindLoadedAssembly(FikaBridgeAssemblyName) ??
                    Assembly.LoadFrom(bridgePath);

                Type bootstrapType = bridgeAssembly.GetType(
                    FikaBridgeBootstrapTypeName,
                    true);

                MethodInfo initializeMethod = bootstrapType.GetMethod(
                    "Initialize",
                    BindingFlags.Public | BindingFlags.Static);

                _fikaBridgeShutdownMethod = bootstrapType.GetMethod(
                    "Shutdown",
                    BindingFlags.Public | BindingFlags.Static);

                if (initializeMethod == null || _fikaBridgeShutdownMethod == null)
                {
                    throw new MissingMethodException(
                        FikaBridgeBootstrapTypeName,
                        "Initialize/Shutdown");
                }

                initializeMethod.Invoke(null, new object[] { Logger });
                _fikaBridgeInitialized =
                    _networkBridge != null && _networkBridge.Active;

                if (!_fikaBridgeInitialized)
                {
                    Logger.LogWarning(
                        "Aishi Keys: the optional Fika bridge loaded, but did not become active.");
                }
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                Logger.LogError(
                    "Aishi Keys: optional Fika bridge initialization failed: " +
                    cause.Message);
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    "Aishi Keys: optional Fika bridge initialization failed: " +
                    ex.Message);
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
        }

        private static Assembly FindLoadedAssembly(string assemblyName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null)
                    continue;

                try
                {
                    if (string.Equals(
                        assembly.GetName().Name,
                        assemblyName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return assembly;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private void ShutdownOptionalFikaBridge()
        {
            if (!_fikaBridgeInitialized || _fikaBridgeShutdownMethod == null)
                return;

            try
            {
                _fikaBridgeShutdownMethod.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                Logger.LogWarning(
                    "Aishi Keys: optional Fika bridge shutdown failed: " +
                    cause.Message);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "Aishi Keys: optional Fika bridge shutdown failed: " +
                    ex.Message);
            }
            finally
            {
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
        }

        public static bool InstallNetworkBridge(IAishiKeysNetworkBridge bridge)
        {
            AishiKeysMod instance = Instance;
            if (instance == null || bridge == null || !bridge.Active)
                return false;

            if (instance._networkBridge != null &&
                !ReferenceEquals(instance._networkBridge, NoNetworkBridge.Instance) &&
                !ReferenceEquals(instance._networkBridge, bridge))
            {
                Logger?.LogWarning(
                    "Aishi Keys: another network bridge is already installed.");
                return false;
            }

            instance._networkBridge = bridge;
            instance._networkSync?.SetNetworkBridge(bridge);

            Logger?.LogInfo(
                "Aishi Keys: optional network bridge attached. ready=" +
                bridge.Ready + ", server=" + bridge.IsServer +
                ", headless=" + bridge.IsHeadless + ".");
            return true;
        }

        public static void RemoveNetworkBridge(IAishiKeysNetworkBridge bridge)
        {
            AishiKeysMod instance = Instance;
            if (instance == null || !ReferenceEquals(instance._networkBridge, bridge))
                return;

            instance._networkSync?.SetNetworkBridge(NoNetworkBridge.Instance);
            instance._networkBridge = NoNetworkBridge.Instance;
            Logger?.LogInfo("Aishi Keys: optional network bridge detached.");
        }

        public static bool IsApprovedMasterKeyTemplate(string templateId)
        {
            return !string.IsNullOrEmpty(templateId) &&
                   MasterKeyHelper.AllMasterKeyIds.Contains(templateId);
        }

        internal static bool TryHandleSynchronizedUnlock(
            WorldInteractiveObject door,
            GamePlayerOwner owner,
            string keyTemplateId,
            EFT.InventoryLogic.KeyComponent keyComponent,
            bool keycard)
        {
            AishiKeysMod instance = Instance;
            if (instance == null || instance._networkSync == null)
                return false;

            return instance._networkSync.TryHandleLocalUnlock(
                door,
                owner,
                keyTemplateId,
                keyComponent,
                keycard);
        }

        public static void LogToScreen(
            string message = "",
            EMessageType eMessageType = EMessageType.Info)
        {
            switch (eMessageType)
            {
                case EMessageType.NotiError:
                    ConsoleScreen.LogError("[Aishi Error] " + message);
                    Logger.LogError("[Aishi Error] " + message);
                    AishiNotificationBridge.Display(
                        "[Aishi Error] " + message,
                        0f,
                        1,
                        Color.red);
                    return;

                case EMessageType.NotiWarn:
                    ConsoleScreen.LogWarning("[Aishi Warning] " + message);
                    Logger.LogWarning("[Aishi Warning] " + message);
                    AishiNotificationBridge.Display(
                        "[Aishi Warning] " + message,
                        0f,
                        0,
                        Color.yellow);
                    return;

                case EMessageType.NotiInfo:
                    ConsoleScreen.Log("[Aishi Info] " + message);
                    Logger.LogDebug("[Aishi Info] " + message);
                    AishiNotificationBridge.Display(
                        "[Aishi Info] " + message,
                        0f,
                        0,
                        Color.yellow);
                    return;

                case EMessageType.Error:
                    ConsoleScreen.LogError("[Aishi Error] " + message);
                    Logger.LogError("[Aishi Error] " + message);
                    return;

                case EMessageType.Warning:
                    ConsoleScreen.LogWarning("[Aishi Warning] " + message);
                    Logger.LogWarning("[Aishi Warning] " + message);
                    return;
            }

            ConsoleScreen.Log("[Aishi Info] " + message);
            Logger.LogDebug("[Aishi Info] " + message);
        }
    }
}
