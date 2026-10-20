using System;
using System.Collections.Generic;
using System.Linq;
using AishiKeysPro;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using UnityEngine;

namespace AishiKeys
{
    internal static class AishiAnomalyPressNetwork
    {
        private const string Request = "APQ|", Result = "APR|";
        private sealed class Operation
        {
            public string Token, Profile, Item, Door;
            public float Remaining;
            public bool Success;
        }

        private static readonly Dictionary<string, Operation> Receipts = new Dictionary<string, Operation>();
        private static readonly Dictionary<string, Operation> Waiting = new Dictionary<string, Operation>();
        private static bool updating;
        private static readonly List<Operation> waitingSnapshot = new List<Operation>();
        private static GameWorld world;
        private static Operation pending;
        private static float retryAt;
        private static AishiKeys.Networking.IAishiKeysNetworkBridge Network => AishiKeysMod.Instance?.NetworkBridge;

        private static bool CurrentWorld()
        {
            var live = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            if (!ReferenceEquals(world, live))
            {
                world = live;
                Receipts.Clear();
                Waiting.Clear();
                pending = null;
                retryAt = 0f;
            }

            return world != null && !(world is HideoutGameWorld);
        }

        internal static bool TryUnlock(Player player, Item item, Door door)
        {
            if (!AishiAnomalyS2.HasAssignedKey(door))
                return true;
            var network = Network;
            if (!AishiNetworkSession.IsNetworkedRaid(network))
                return false;
            if (!CurrentWorld() || !network.Ready || player == null || !player.IsYourPlayer || network.IsHeadless)
                return true;
            if (pending != null)
                return true;
            string id = WorldInteractionUtils.GetStableInteractiveId(door);
            if (string.IsNullOrEmpty(id))
                return true;
            pending = new Operation
            {
                Token = Guid.NewGuid().ToString("N"),
                Profile = player.ProfileId,
                Item = item.Id,
                Door = id
            };
            retryAt = 0f;
            Update();
            return true;
        }

        internal static void Update()
        {
            if (updating || !CurrentWorld())
                return;
            updating = true;
            try
            {
                waitingSnapshot.AddRange(Waiting.Values);
                foreach (var result in waitingSnapshot)
                    if (Apply(result))
                        Waiting.Remove(result.Profile + ":" + result.Token);
                var network = Network;
                if (network?.Ready != true || pending == null || Time.realtimeSinceStartup < retryAt)
                    return;
                retryAt = Time.realtimeSinceStartup + 1f;
                string payload = Request + JsonConvert.SerializeObject(pending);
                if (network.IsServer)
                    Handle(payload);
                else
                    network.SendToServer(payload);
            }
            finally
            {
                waitingSnapshot.Clear();
                updating = false;
            }
        }

        internal static bool Handle(string payload)
        {
            if (payload == null || (!payload.StartsWith(Request, StringComparison.Ordinal) && !payload.StartsWith(Result, StringComparison.Ordinal)))
                return false;
            var network = Network;
            if (!CurrentWorld() || network?.Ready != true || payload.Length > 4096)
                return true;
            bool request = payload.StartsWith(Request, StringComparison.Ordinal);
            Operation op;
            try
            {
                op = JsonConvert.DeserializeObject<Operation>(payload.Substring(request ? Request.Length : Result.Length));
            }
            catch
            {
                return true;
            }

            if (!Valid(op))
                return true;
            string key = op.Profile + ":" + op.Token;
            if (request)
            {
                if (!network.IsServer)
                    return true;
                if (Receipts.TryGetValue(key, out var previous))
                {
                    network.Broadcast(Result + JsonConvert.SerializeObject(previous));
                    return true;
                }

                var owner = Find(op.Profile);
                var item = FindItem(owner, op.Item);
                var door = WorldInteractionUtils.FindInteractiveByStableId(op.Door) as Door;
                op.Success = false;
                op.Remaining = item?.GetItemComponent<ResourceComponent>()?.Value ?? 0f;
                if (owner != null && !owner.IsAI && owner.HealthController?.IsAlive == true && item?.TemplateId == AishiAnomalyS2.Press && !AishiAnomalyContainment.IsSuppressed(item) && op.Remaining >= 1f && AishiAnomalyS2.HasAssignedKey(door) && door.DoorState == EDoorState.Locked && AishiAnomalyS2.WithinDoorRadius(owner, door, door.GetComponentsInChildren<Collider>(), .5f))
                {
                    if (WorldInteractionUtils.TryApplyAuthoritativeDoorState(door, (int)EDoorState.Shut))
                    {
                        op.Success = true;
                        op.Remaining = Mathf.Max(0f, op.Remaining - 1f);
                        item.GetItemComponent<ResourceComponent>().Value = op.Remaining;
                        item.RaiseRefreshEvent(false, true);
                        AishiAnomalyDuck.Changed(owner, item, op.Remaining);
                    }
                }

                Receipts[key] = op;
                Complete(op);
                network.Broadcast(Result + JsonConvert.SerializeObject(op));
            }
            else if (!network.IsServer)
            {
                Complete(op);
                if (op.Success && !Apply(op) && Waiting.Count < 128)
                    Waiting[key] = op;
            }

            return true;
        }

        private static bool Valid(Operation op) => op != null && op.Token?.Length == 32 && op.Token.All(Uri.IsHexDigit) && op.Profile != null && op.Profile.Length > 0 && op.Profile.Length <= 64 && op.Item?.Length == 24 && op.Item.All(Uri.IsHexDigit) && op.Door != null && op.Door.Length > 0 && op.Door.Length <= 1024 && !float.IsNaN(op.Remaining) && !float.IsInfinity(op.Remaining) && op.Remaining >= 0f && op.Remaining <= 10f;
        private static Player Find(string profile)
        {
            if (world == null)
                return null;
            foreach (var registered in world.RegisteredPlayers)
                if (registered is Player player && player.ProfileId == profile)
                    return player;
            return null;
        }

        private static Item FindItem(Player owner, string id)
        {
            var equipment = owner?.InventoryController?.Inventory?.Equipment;
            if (equipment == null)
                return null;
            foreach (var item in equipment.GetAllItems())
                if (item.Id == id)
                    return item;
            return null;
        }

        private static bool Apply(Operation op)
        {
            var door = WorldInteractionUtils.FindInteractiveByStableId(op.Door) as Door;
            if (!AishiAnomalyS2.HasAssignedKey(door))
                return false;
            if (door.DoorState == EDoorState.Locked && !WorldInteractionUtils.TryApplyAuthoritativeDoorState(door, (int)EDoorState.Shut))
                return false;
            var owner = Find(op.Profile);
            if (owner == null)
                return false;
            var item = FindItem(owner, op.Item);
            if (item == null)
                return op.Remaining <= 0f;
            if (item.TemplateId != AishiAnomalyS2.Press)
                return true;
            AishiAnomalyDuck.Reconcile(owner, item, op.Remaining);
            return true;
        }

        private static void Complete(Operation op)
        {
            if (pending?.Token == op.Token && pending.Profile == op.Profile)
                pending = null;
        }
    }
}
