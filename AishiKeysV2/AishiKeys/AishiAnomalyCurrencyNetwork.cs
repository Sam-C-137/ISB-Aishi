using System;
using System.Collections.Generic;
using System.Linq;
using AishiKeys.Networking;
using AishiKeysPro;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using UnityEngine;
#if SPT41
using Factory = EFT.ItemFactory;
using GridSearch = EFT.InventoryLogic.InventoryEquipmentExtension;
using ItemActions = EFT.InventoryLogic.ItemManipulator;
using GridAddress = EFT.InventoryLogic.GridItemAddress;

#else
using Factory = ItemFactoryClass;
using GridSearch = GClass3372;
using ItemActions = InteractionsHandlerClass;
using GridAddress = GClass3393;
#endif
namespace AishiKeys
{
    internal static class AishiAnomalyCurrencyNetwork
    {
        private const string Request = "ACQ|", Result = "ACR|";
        private sealed class Grant
        {
            public string Token, Profile, Item, Container, Source, Template;
            public int Grid, X, Y, Rotation, Before, After, Amount;
            public bool Added;
        }

        private sealed class PendingGrant
        {
            internal string Token, Profile;
            internal float RetryAt;
            internal int Completed;
        }

        private static readonly Dictionary<string, Grant> Receipts = new Dictionary<string, Grant>();
        private static readonly Dictionary<string, float> LastGranted = new Dictionary<string, float>();
        private static readonly Dictionary<string, PendingGrant> Pending = new Dictionary<string, PendingGrant>();
        private static readonly HashSet<string> Applied = new HashSet<string>();
        private static readonly Queue<Grant> Waiting = new Queue<Grant>();
        private static GameWorld world;
        private static IAishiKeysNetworkBridge Network => AishiKeysMod.Instance?.NetworkBridge;

        private static bool CurrentWorld()
        {
            var live = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            if (!ReferenceEquals(world, live))
            {
                world = live;
                Receipts.Clear();
                LastGranted.Clear();
                Applied.Clear();
                Waiting.Clear();
                Pending.Clear();
            }

            return world != null && !(world is HideoutGameWorld);
        }

        internal static bool Allowed(string source, string currency, int amount) => source == AishiAnomalyExtended.Bitcoin && currency == AishiAnomalyExtended.Dollars && amount == 2 || source == AishiAnomalyS2.Egg && currency == AishiAnomalyS2.Euros && amount == 2 || source == AishiAnomalyS2.Badge && currency == AishiAnomalyS2.Roubles && amount == 1000;
        private static int AmountFor(string source, string currency) => Allowed(source, currency, 2) ? 2 : Allowed(source, currency, 1000) ? 1000 : 0;
        internal static bool TryGrant(Player owner, out int granted) => TryGrant(owner, AishiAnomalyExtended.Bitcoin, AishiAnomalyExtended.Dollars, 2, out granted);
        internal static bool TryGrant(Player owner, string source, string currency, int amount, out int granted)
        {
            granted = 0;
            var network = Network;
            if (!AishiNetworkSession.IsNetworkedRaid(network))
                return false;
            if (!network.Ready || !Allowed(source, currency, amount))
                return true;
            if (!CurrentWorld() || owner == null || !owner.IsYourPlayer || network.IsHeadless)
                return true;
            string key = source + ":" + currency;
            if (!Pending.TryGetValue(key, out var pending))
                Pending[key] = pending = new PendingGrant();
            if (pending.Completed > 0)
            {
                granted = pending.Completed;
                pending.Completed = 0;
                return true;
            }

            if (pending.Token == null)
            {
                pending.Token = Guid.NewGuid().ToString("N");
                pending.Profile = owner.ProfileId;
                pending.RetryAt = 0;
            }

            if (Time.realtimeSinceStartup < pending.RetryAt)
                return true;
            pending.RetryAt = Time.realtimeSinceStartup + 2f;
            string payload = Request + pending.Token + "|" + pending.Profile + "|" + source + "|" + currency;
            if (network.IsServer)
                Handle(payload);
            else
                network.SendToServer(payload);
            if (pending.Completed > 0)
            {
                granted = pending.Completed;
                pending.Completed = 0;
            }

            return true;
        }

        internal static void Update()
        {
            if (!CurrentWorld())
                return;
            int count = Waiting.Count;
            while (count-- > 0)
            {
                var grant = Waiting.Dequeue();
                if (Apply(grant))
                    Complete(grant);
                else
                    Waiting.Enqueue(grant);
            }
        }

        internal static bool Handle(string payload)
        {
            if (payload == null || (!payload.StartsWith(Request, StringComparison.Ordinal) && !payload.StartsWith(Result, StringComparison.Ordinal)))
                return false;
            var network = Network;
            if (!CurrentWorld() || network?.Ready != true || payload.Length > 2048)
                return true;
            if (payload.StartsWith(Request, StringComparison.Ordinal))
            {
                if (!network.IsServer)
                    return true;
                var parts = payload.Split('|');
                if (parts.Length != 5 || parts[1].Length != 32 || !parts[1].All(Uri.IsHexDigit) || parts[2].Length > 64)
                    return true;
                int amount = AmountFor(parts[3], parts[4]);
                if (amount == 0)
                    return true;
                string key = parts[2] + ":" + parts[1];
                if (Receipts.TryGetValue(key, out var previous))
                {
                    network.Broadcast(Result + JsonConvert.SerializeObject(previous));
                    return true;
                }

                var owner = FindPlayer(parts[2]);
                if (owner == null || owner.IsAI || owner.HealthController?.IsAlive != true || !Items(owner).Any(i => i.TemplateId == parts[3] && !AishiAnomalyContainment.IsSuppressed(i)))
                    return true;
                string clock = owner.ProfileId + ":" + parts[3];
                if (LastGranted.TryGetValue(clock, out float last) && Time.realtimeSinceStartup - last < 55f)
                    return true;
                var grant = Create(owner, parts[1], parts[3], parts[4], amount);
                if (grant == null)
                    return true;
                Receipts[key] = grant;
                LastGranted[clock] = Time.realtimeSinceStartup;
                Applied.Add(key);
                Complete(grant);
                network.Broadcast(Result + JsonConvert.SerializeObject(grant));
            }
            else if (!network.IsServer)
            {
                Grant grant;
                try
                {
                    grant = JsonConvert.DeserializeObject<Grant>(payload.Substring(Result.Length));
                }
                catch
                {
                    return true;
                }

                if (!Valid(grant) || Applied.Contains(grant.Profile + ":" + grant.Token))
                    return true;
                if (Apply(grant))
                    Complete(grant);
                else if (Waiting.Count < 128 && !Waiting.Any(g => g.Profile == grant.Profile && g.Token == grant.Token))
                    Waiting.Enqueue(grant);
            }

            return true;
        }

        private static bool Valid(Grant g) => g != null && Allowed(g.Source, g.Template, g.Amount) && g.Token?.Length == 32 && g.Token.All(Uri.IsHexDigit) && g.Item?.Length == 24 && g.Item.All(Uri.IsHexDigit) && g.Profile != null && g.Profile.Length <= 64 && g.Before >= 0 && g.After > 0 && (long)g.After - g.Before == g.Amount && g.Grid >= 0 && g.Grid < 20 && g.X >= 0 && g.Y >= 0 && g.Rotation >= 0 && g.Rotation <= 1;
        private static Player FindPlayer(string profile) => world?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p => p.ProfileId == profile);
        private static IEnumerable<Item> Items(Player player) => player.InventoryController?.Inventory?.Equipment?.GetAllItems() ?? Enumerable.Empty<Item>();
        private static Grant Create(Player player, string token, string source, string currency, int amount)
        {
            var inventory = player.InventoryController;
            var item = Items(player).FirstOrDefault(i => i.TemplateId == currency && i.StackObjectsCount <= i.StackMaxSize - amount);
            var grant = new Grant
            {
                Token = token,
                Profile = player.ProfileId,
                Source = source,
                Template = currency,
                Amount = amount,
                Before = item?.StackObjectsCount ?? 0
            };
            if (item == null)
            {
                item = Singleton<Factory>.Instance.CreateItem(Guid.NewGuid().ToString("N").Substring(0, 24), currency, null);
                item.StackObjectsCount = amount;
                item.SpawnedInSession = true;
                var place = GridSearch.GetPrioritizedGridsForLoot(inventory.Inventory.Equipment, item).Select(grid => grid.FindLocationForItem(item)).FirstOrDefault(p => p != null && !ItemActions.Add(item, p, inventory, true).Failed);
                if (!(place is GridAddress address))
                    return null;
                var added = ItemActions.Add(item, address, inventory, false);
                if (added.Failed)
                    return null;
                added.Value.RaiseEvents(inventory, CommandStatus.Succeed);
                grant.Added = true;
                grant.Container = address.Grid.ParentItem.Id;
                grant.Grid = Array.IndexOf(((CompoundItem)address.Grid.ParentItem).Grids, address.Grid);
                grant.X = address.LocationInGrid.x;
                grant.Y = address.LocationInGrid.y;
                grant.Rotation = (int)address.LocationInGrid.r;
            }
            else
            {
                item.StackObjectsCount += amount;
                item.RaiseRefreshEvent(false, true);
            }

            grant.Item = item.Id;
            grant.After = item.StackObjectsCount;
            return grant;
        }

        private static bool Apply(Grant grant)
        {
            if (!Valid(grant))
                return true;
            string key = grant.Profile + ":" + grant.Token;
            if (Applied.Contains(key))
                return true;
            var player = FindPlayer(grant.Profile);
            var inventory = player?.InventoryController;
            if (inventory == null)
                return false;
            var items = Items(player).ToArray();
            var item = items.FirstOrDefault(i => i.Id == grant.Item);
            if (item != null)
            {
                if (item.TemplateId != grant.Template || grant.After > item.StackMaxSize)
                    return false;
                if (item.StackObjectsCount == grant.Before)
                    item.StackObjectsCount += grant.Amount;
                else if (item.StackObjectsCount != grant.After)
                    return false;
                item.RaiseRefreshEvent(false, true);
            }
            else
            {
                if (!grant.Added)
                    return false;
                var parent = items.FirstOrDefault(i => i.Id == grant.Container) as CompoundItem;
                if (parent?.Grids == null || grant.Grid >= parent.Grids.Length)
                    return false;
                item = Singleton<Factory>.Instance.CreateItem(grant.Item, grant.Template, null);
                if (grant.After > item.StackMaxSize)
                    return false;
                item.StackObjectsCount = grant.After;
                item.SpawnedInSession = true;
                var address = parent.Grids[grant.Grid].CreateItemAddress(new LocationInGrid { x = grant.X, y = grant.Y, r = (ItemRotation)grant.Rotation });
                var added = ItemActions.Add(item, address, inventory, false);
                if (added.Failed)
                    return false;
                added.Value.RaiseEvents(inventory, CommandStatus.Succeed);
            }

            Applied.Add(key);
            return true;
        }

        private static void Complete(Grant grant)
        {
            if (!Pending.TryGetValue(grant.Source + ":" + grant.Template, out var pending) || grant.Token != pending.Token || grant.Profile != pending.Profile)
                return;
            pending.Completed += grant.Amount;
            pending.Token = pending.Profile = null;
        }
    }
}
