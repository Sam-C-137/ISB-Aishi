using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AishiKeysPro;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
#if SPT41
using ItemActions = EFT.InventoryLogic.ItemManipulator;
using NotificationManagerClass = EFT.Communications.NotificationManager;
#else
using ItemActions = InteractionsHandlerClass;
#endif

namespace AishiKeys
{
    [HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
    internal static class AishiHammerheadPower
    {
        internal const string SlotName = "mod_equipment_003";
        internal const string GreenBattery = "5e2aedd986f7746d404f3aa4";
        internal const string BlueBattery = "4d57eab7724cdd35474d84eb";
        internal const string V3Battery = "264f194ae30547c55dfbf0d9";
        private sealed class Charge
        {
            internal string Helmet, Battery, Profile;
            internal int Uses;
            internal string Encode() => "HHP|POWER|" + Profile + "|" + Helmet + "|" + Battery + "|" + Uses;
        }
        private static readonly Dictionary<string, Charge> Powered = new Dictionary<string, Charge>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Charge> Pending = new Dictionary<string, Charge>(StringComparer.Ordinal);
        private static readonly HashSet<string> Acknowledged = new HashSet<string>(StringComparer.Ordinal);
        private static GameWorld _world;
        private static bool _started, _busy, _synced;
        private static float _nextCheck, _nextSend;
        private static float _nextNotification;
        private static int _generation;
        private static AishiKeys.Networking.IAishiKeysNetworkBridge Network => AishiKeysMod.Instance?.NetworkBridge;
        private static bool NetworkedRaid => Network?.Active == true && Singleton<AbstractGame>.Instantiated && IsNetworkedGame(Singleton<AbstractGame>.Instance?.GetType());

        internal static bool IsNetworkedGame(Type type)
        {
            for (; type != null; type = type.BaseType)
                if (type.Namespace?.StartsWith("Fika.", StringComparison.Ordinal) == true) return true;
            return false;
        }

        private static bool Rechargeable(Item battery) => battery != null && (battery.TemplateId == BlueBattery || battery.TemplateId == V3Battery);

        private static void Postfix(GameWorld __instance)
        {
            Reset(__instance);
            _started = !(__instance is HideoutGameWorld);
        }

        private static void Reset(GameWorld world)
        {
            if (ReferenceEquals(_world, world)) return;
            var helmets = _world == null || Powered.Count == 0 ? Array.Empty<Item>() : _world.RegisteredPlayers.OfType<Player>()
                .SelectMany(player => player.Equipment?.GetAllItems() ?? Enumerable.Empty<Item>()).Where(item => Powered.ContainsKey(item.Id)).ToArray();
            _generation++;
            _world = world;
            _started = _busy = _synced = false;
            _nextCheck = _nextSend = 0;
            Powered.Clear();
            Pending.Clear();
            Acknowledged.Clear();
            foreach (var helmet in helmets) helmet.RaiseRefreshEvent(false, true);
        }

        internal static bool IsCharged(Item helmet) => _started && AishiHammerhead.IsHelmet(helmet) && Powered.ContainsKey(helmet.Id);

        internal static bool HasPower(Item helmet) => AishiHammerhead.IsHelmet(helmet) &&
            (!_started || Powered.ContainsKey(helmet.Id));

        internal static bool CanUseLamp(Item helmet) => AishiHammerhead.IsHelmet(helmet) &&
            (_started ? HasPower(helmet) : Remaining(Battery(helmet)) > 0);

        internal static void NotifyEmpty()
        {
            if (Time.unscaledTime < _nextNotification) return;
            _nextNotification = Time.unscaledTime + 2f;
            NotificationManagerClass.DisplayWarningNotification("aishi_helmet_out_of_power".Localized());
        }

        internal static Item Battery(Item helmet) => (helmet as CompoundItem)?.Slots.FirstOrDefault(slot => slot.ID == SlotName)?.ContainedItem;

        internal static int Remaining(Item battery)
        {
            if (battery == null || battery.StackObjectsCount < 1) return 0;
            if (battery.TemplateId == GreenBattery) return 1;
            var key = battery.GetItemComponent<KeyComponent>();
            return Rechargeable(battery) && key != null && key.NumberOfUsages >= 0 ? Math.Max(0, key.Template.MaximumNumberOfUsage - key.NumberOfUsages) : 0;
        }

        internal static void Update()
        {
            var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            Reset(world);
            if (!_started || world == null) return;
            var network = Network;
            bool networked = NetworkedRaid;
            if (!networked || network.IsServer) _synced = true;
            if (networked && network.Ready && Time.unscaledTime >= _nextSend)
            {
                _nextSend = Time.unscaledTime + 2f;
                if (!_synced) network.SendToServer("HHP|SYNC");
                foreach (var change in Powered.Values)
                    if (!Acknowledged.Contains(change.Helmet))
                    {
                        if (network.IsServer) { network.Broadcast(change.Encode()); Acknowledged.Add(change.Helmet); }
                        else network.SendToServer(change.Encode());
                    }
            }
            foreach (var change in Pending.Values.ToArray())
                if (ApplyCharge(change)) Pending.Remove(change.Helmet);
            if (!_synced || _busy || Time.unscaledTime < _nextCheck || network?.IsHeadless == true) return;
            _nextCheck = Time.unscaledTime + .5f;
            var player = world.MainPlayer;
            if (player?.IsYourPlayer != true || player.HealthController?.IsAlive != true || player.InventoryController == null) return;
            var helmet = player.Equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem;
            if (!AishiHammerhead.IsHelmet(helmet)) return;
            if (HasPower(helmet))
            {
                AishiHammerheadVision.Ensure(player);
                return;
            }
            var battery = Battery(helmet);
            if (Remaining(battery) > 0)
            {
                _busy = true;
                _ = Consume(player, helmet, battery, _generation);
            }
            else
            {
                var light = helmet.GetItemComponent<LightComponent>();
                if (light?.IsActive == true)
                {
                    light.IsActive = false;
                    player.SendHeadlightsPacket(true);
                }
                var toggle = helmet.GetItemComponent<NightVisionComponent>()?.Togglable;
                if (toggle?.On == true)
                {
                    _busy = true;
                    _ = SwitchOff(player, toggle, _generation);
                }
            }
        }

        private static async Task SwitchOff(Player player, TogglableComponent toggle, int generation)
        {
            try { await player.InventoryController.TryRunNetworkTransaction(toggle.Set(false, true, false), null); }
            catch (Exception error) { AishiKeysMod.Logger?.LogError("Hammerhead power switch failed: " + error.GetBaseException().Message); }
            finally { if (generation == _generation) _busy = false; }
        }

        private static async Task Consume(Player player, Item helmet, Item battery, int generation)
        {
            try
            {
                int remaining = Remaining(battery);
                if (remaining <= 0 || !ReferenceEquals(Battery(helmet), battery) || HasPower(helmet)) return;
                var change = new Charge { Helmet = helmet.Id, Battery = battery.Id, Profile = player.ProfileId, Uses = Rechargeable(battery) ? battery.GetItemComponent<KeyComponent>().NumberOfUsages + 1 : 1 };
                if (remaining > 1)
                {
                    battery.GetItemComponent<KeyComponent>().NumberOfUsages = change.Uses;
                    battery.RaiseRefreshEvent(false, true);
                }
                else
                {
                    var result = await player.InventoryController.TryRunNetworkTransaction(ItemActions.Remove(battery, player.InventoryController, true), null);
                    if (result == null || !result.Succeed) return;
                }
                if (generation != _generation) return;
                Powered[helmet.Id] = change;
                helmet.RaiseRefreshEvent(false, true);
                _nextSend = 0;
                AishiHammerheadVision.Ensure(player);
            }
            catch (Exception error) { AishiKeysMod.Logger?.LogError("Hammerhead battery consumption failed: " + error.GetBaseException().Message); }
            finally { if (generation == _generation) _busy = false; }
        }

        internal static bool Handle(string payload)
        {
            if (!payload.StartsWith("HHP|", StringComparison.Ordinal)) return false;
            if (!_started || !NetworkedRaid || Network?.Ready != true) return true;
            var parts = payload.Split('|');
            if (parts.Length == 2 && parts[1] == "SYNC" && Network.IsServer)
            {
                foreach (var charge in Powered.Values) Network.Broadcast(charge.Encode());
                Network.Broadcast("HHP|READY");
                return true;
            }
            if (parts.Length == 2 && parts[1] == "READY" && !Network.IsServer) { _synced = true; return true; }
            if (parts.Length != 6 || parts[1] != "POWER" || parts[2].Length > 64 || parts[3].Length != 24 || parts[4].Length != 24 || !int.TryParse(parts[5], out int uses) || uses < 1 || uses > 5) return true;
            var change = new Charge { Profile = parts[2], Helmet = parts[3], Battery = parts[4], Uses = uses };
            if (Powered.TryGetValue(change.Helmet, out var previous))
            {
                if (previous.Battery != change.Battery || previous.Profile != change.Profile || previous.Uses != change.Uses) return true;
                change = previous;
            }
            else Powered.Add(change.Helmet, change);
            if (!ApplyCharge(change)) Pending[change.Helmet] = change;
            if (Network.IsServer) Network.Broadcast(change.Encode());
            Acknowledged.Add(change.Helmet);
            return true;
        }

        private static bool ApplyCharge(Charge charge)
        {
            var player = _world?.RegisteredPlayers.OfType<Player>().FirstOrDefault(p => p.ProfileId == charge.Profile);
            var equipment = player?.InventoryController?.Inventory?.Equipment;
            if (equipment == null) return false;
            equipment.GetAllItems().FirstOrDefault(item => item.Id == charge.Helmet)?.RaiseRefreshEvent(false, true);
            var battery = equipment.GetAllItems().FirstOrDefault(item => item.Id == charge.Battery);
            if (battery == null) return true;
            if (battery.TemplateId != GreenBattery && !Rechargeable(battery)) return true;
            var key = battery.GetItemComponent<KeyComponent>();
            if (Rechargeable(battery) && key != null && charge.Uses < key.Template.MaximumNumberOfUsage)
            {
                key.NumberOfUsages = Math.Max(key.NumberOfUsages, charge.Uses);
                battery.RaiseRefreshEvent(false, true);
                return true;
            }
            var result = ItemActions.RemoveWithoutRestrictions(battery, player.InventoryController);
            if (result.Failed) return false;
            result.Value.RaiseEvents(player.InventoryController, CommandStatus.Succeed);
            return true;
        }
    }
}
