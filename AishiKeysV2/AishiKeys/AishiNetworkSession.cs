using AishiKeys.Networking;
using Comfort.Common;
using EFT;
using System;

namespace AishiKeys
{
    internal static class AishiNetworkSession
    {
        internal static bool IsNetworkedRaid(IAishiKeysNetworkBridge network)
        {
            if (network?.Active != true || !Singleton<AbstractGame>.Instantiated)
                return false;
            var game = Singleton<AbstractGame>.Instance;
            if (game == null || game is LocalGame)
                return false;
            for (var type = game.GetType(); type != null; type = type.BaseType)
                if (type.Namespace?.StartsWith("Fika.", StringComparison.Ordinal) == true)
                    return true;
            return false;
        }
    }
}
