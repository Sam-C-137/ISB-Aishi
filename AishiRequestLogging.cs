using System;

namespace Aishi.Logging
{
    public static class AishiRequestLogPolicy
    {
        public static bool Routine(string message)
        {
            if(message==null)return false;
            foreach(string prefix in new[]{"[Client Request] ","[REQUEST]: ","[REQUEST SUCCESSFUL] "})
            {
                if(!message.StartsWith(prefix,StringComparison.Ordinal))continue;
                string path=message.Substring(prefix.Length);
                return path.StartsWith("/ISB/",StringComparison.OrdinalIgnoreCase)||path.StartsWith("/Aishi/",StringComparison.OrdinalIgnoreCase)||path.StartsWith("/aishi/",StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
    }
}

#if ISB_LABS_CLIENT
namespace Aishi.Logging
{
    public static class AishiRequestLoggingClient
    {
        private static HarmonyLib.Harmony harmony;
        public static void Enable()
        {
            if(harmony!=null)return;
            harmony=new HarmonyLib.Harmony("com.samc137.aishi.routine-request-logging");
            var method=HarmonyLib.AccessTools.Method(typeof(BepInEx.Logging.ManualLogSource),"Log",new[]{typeof(BepInEx.Logging.LogLevel),typeof(object)});
            harmony.Patch(method,prefix:new HarmonyLib.HarmonyMethod(typeof(AishiRequestLoggingClient),nameof(Allow)));
        }
        private static bool Allow(BepInEx.Logging.ManualLogSource __instance,BepInEx.Logging.LogLevel __0,object __1)
        {
            return __instance.SourceName!="RequestHandler"||(__0&(BepInEx.Logging.LogLevel.Error|BepInEx.Logging.LogLevel.Warning|BepInEx.Logging.LogLevel.Fatal))!=0||!AishiRequestLogPolicy.Routine(__1 as string);
        }
        public static void Stop(){harmony?.UnpatchSelf();harmony=null;}
    }
}
#endif

#if ISB_LABS_SERVER
namespace Aishi.Logging
{
    [SPTarkov.DI.Annotations.Injectable(TypePriority=SPTarkov.Server.Core.DI.OnLoadOrder.PostLoad+6)]
    public sealed class AishiRequestLoggingServer(SPTarkov.Common.Logger.SPTLoggerDispatcher dispatcher):SPTarkov.Server.Core.DI.IOnLoad
    {
        public System.Threading.Tasks.Task OnLoadAsync(System.Threading.CancellationToken cancellationToken)
        {
            var handlers=HarmonyLib.AccessTools.Field(typeof(SPTarkov.Common.Logger.SPTLoggerDispatcher),"_logHandlers").GetValue(dispatcher) as System.Collections.Generic.IDictionary<SPTarkov.Common.Models.Logging.LoggerType,SPTarkov.Common.Models.Logging.ILogHandler>;
            if(handlers==null)throw new MissingMemberException("Aishi request logging dispatcher unavailable.");
            foreach(var key in System.Linq.Enumerable.ToArray(handlers.Keys))if(handlers[key] is not Handler)handlers[key]=new Handler(handlers[key]);
            return System.Threading.Tasks.Task.CompletedTask;
        }
        private sealed class Handler(SPTarkov.Common.Models.Logging.ILogHandler inner):SPTarkov.Common.Models.Logging.ILogHandler
        {
            public SPTarkov.Common.Models.Logging.LoggerType LoggerType=>inner.LoggerType;
            public void Log(SPTarkov.Common.Models.Logging.SptLogMessage message,SPTarkov.Common.Models.Logging.BaseSptLoggerReference logger)
            {
                if(message.Exception!=null||message.LogLevel>=Microsoft.Extensions.Logging.LogLevel.Warning||!AishiRequestLogPolicy.Routine(message.Message))inner.Log(message,logger);
            }
            public System.Threading.Tasks.ValueTask DisposeAsync()=>inner.DisposeAsync();
        }
    }
}
#endif
