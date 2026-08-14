using AishiKeys.MasterKey;
using AishiKeys.Networking;
using AishiKeysPro;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AishiKeys
{
    internal sealed class AishiKeysNetworkSyncCoordinator
    {
        private const string ProtocolVersion = "2";
        private const string RequestMessage = "REQ";
        private const string PrepareMessage = "PREPARE";
        private const string ResultMessage = "RESULT";
        private const string RestoreMessage = "RESTORE";
        private const float LocalKeycardStartDelay = 0.20f;
        private const float PrepareReplicationDelay = 0.90f;
        private const float ResultStateDelay = 0.20f;
        private const float ResultRetryDelay = 0.35f;
        private const float OverrideHoldAfterResult = 2.50f;
        private const float OverrideSafetyTimeout = 6.00f;

        private readonly AishiKeysMod _plugin;
        private IAishiKeysNetworkBridge _network;
        private readonly ManualLogSource _log;
        private readonly HashSet<string> _processedRequests =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, DoorKeyOverride> _doorOverrides =
            new Dictionary<string, DoorKeyOverride>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingLocalKeycard> _pendingLocalKeycards =
            new Dictionary<string, PendingLocalKeycard>(StringComparer.Ordinal);

        private sealed class DoorKeyOverride
        {
            internal string RequestId;
            internal string DoorId;
            internal WorldInteractiveObject Door;
            internal string OriginalKeyId;
            internal string TemporaryKeyId;
        }

        private sealed class PendingLocalKeycard
        {
            internal string RequestId;
            internal string DoorId;
            internal KeycardDoor Door;
            internal Player Player;
            internal KeyComponent KeyComponent;
            internal bool Started;
        }

        private sealed class ValidatedRequest
        {
            internal WorldInteractiveObject Door;
            internal Player Player;
            internal string KeyTemplateId;
            internal bool Keycard;
            internal KeyComponent KeyComponent;
        }

        internal AishiKeysNetworkSyncCoordinator(
            AishiKeysMod plugin,
            IAishiKeysNetworkBridge network,
            ManualLogSource log)
        {
            _plugin = plugin;
            _network = network ?? NoNetworkBridge.Instance;
            _log = log;
        }

        internal void SetNetworkBridge(IAishiKeysNetworkBridge network)
        {
            RestoreAllDoorOverrides();
            _processedRequests.Clear();
            _pendingLocalKeycards.Clear();
            _network = network ?? NoNetworkBridge.Instance;
        }

        internal bool TryHandleLocalUnlock(
            WorldInteractiveObject door,
            GamePlayerOwner owner,
            string keyTemplateId,
            KeyComponent localKeyComponent,
            bool keycard)
        {
            if (_network == null || !_network.Active || !_network.Ready)
                return false;

            if (door == null || owner == null || owner.Player == null)
                return false;

            string doorId = WorldInteractionUtils.GetStableInteractiveId(door);
            string profileId = owner.Player.Profile != null
                ? owner.Player.Profile.Id
                : string.Empty;

            if (string.IsNullOrEmpty(doorId) ||
                string.IsNullOrEmpty(profileId) ||
                string.IsNullOrEmpty(keyTemplateId))
            {
                return false;
            }

            string requestId = Guid.NewGuid().ToString("N");

            if (keycard && door is KeycardDoor && localKeyComponent != null)
            {
                _pendingLocalKeycards[requestId] = new PendingLocalKeycard
                {
                    RequestId = requestId,
                    DoorId = doorId,
                    Door = (KeycardDoor)door,
                    Player = owner.Player,
                    KeyComponent = localKeyComponent
                };
            }

            if (_network.IsServer)
            {
                _processedRequests.Add(requestId);
                BeginAuthoritativeRequest(
                    requestId,
                    doorId,
                    profileId,
                    keyTemplateId,
                    keycard,
                    door,
                    owner.Player);
                return true;
            }

            string payload = BuildRequest(
                requestId,
                doorId,
                profileId,
                keyTemplateId,
                keycard);

            if (!_network.SendToServer(payload))
            {
                _pendingLocalKeycards.Remove(requestId);
                _log.LogWarning(
                    "Aishi Keys Fika: request could not be sent; using the native local interaction fallback.");
                return false;
            }

            _log.LogInfo(
                "Aishi Keys Fika: master-key request sent to host. " +
                "door='" + doorId + "', keycard=" + keycard + ".");
            return true;
        }

        internal void Update()
        {
            if (_network == null || !_network.Active)
                return;

            _network.DrainReceivedPayloads(HandlePayload);
        }

        internal void Dispose()
        {
            RestoreAllDoorOverrides();
            _processedRequests.Clear();
            _pendingLocalKeycards.Clear();
        }

        private void HandlePayload(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return;

            string[] parts = payload.Split('|');
            if (parts.Length < 3 ||
                !string.Equals(parts[0], ProtocolVersion, StringComparison.Ordinal))
            {
                return;
            }

            if (string.Equals(parts[1], RequestMessage, StringComparison.Ordinal))
            {
                if (_network.IsServer)
                    HandleRequest(parts);
                return;
            }

            if (string.Equals(parts[1], PrepareMessage, StringComparison.Ordinal))
            {
                if (!_network.IsServer)
                    HandlePrepare(parts);
                return;
            }

            if (string.Equals(parts[1], ResultMessage, StringComparison.Ordinal))
            {
                HandleResult(parts);
                return;
            }

            if (string.Equals(parts[1], RestoreMessage, StringComparison.Ordinal))
            {
                if (!_network.IsServer)
                    HandleRestore(parts);
            }
        }

        private void HandleRequest(string[] parts)
        {
            if (parts.Length != 8)
                return;

            string requestId = Decode(parts[2]);
            string doorId = Decode(parts[3]);
            string profileId = Decode(parts[4]);
            string keyTemplateId = Decode(parts[5]);
            bool keycard;

            if (!TryParseBool(parts[6], out keycard) ||
                !string.Equals(parts[7], "END", StringComparison.Ordinal))
            {
                return;
            }

            if (string.IsNullOrEmpty(requestId) || !_processedRequests.Add(requestId))
                return;

            WorldInteractiveObject door =
                WorldInteractionUtils.FindInteractiveByStableId(doorId);
            Player player = FindPlayer(profileId);

            BeginAuthoritativeRequest(
                requestId,
                doorId,
                profileId,
                keyTemplateId,
                keycard,
                door,
                player);
        }

        private void BeginAuthoritativeRequest(
            string requestId,
            string doorId,
            string profileId,
            string keyTemplateId,
            bool keycard,
            WorldInteractiveObject door,
            Player player)
        {
            ValidatedRequest request;
            string failureReason;

            if (!TryValidateRequest(
                    door,
                    player,
                    keyTemplateId,
                    keycard,
                    out request,
                    out failureReason))
            {
                BroadcastResult(
                    requestId,
                    doorId,
                    profileId,
                    keyTemplateId,
                    keycard,
                    false,
                    door != null ? (int)door.DoorState : -1,
                    failureReason);
                return;
            }

            _plugin.StartCoroutine(ExecuteAuthoritativeRequest(
                requestId,
                doorId,
                profileId,
                request));
        }

        private IEnumerator ExecuteAuthoritativeRequest(
            string requestId,
            string doorId,
            string profileId,
            ValidatedRequest request)
        {
            bool localPrepared = ApplyTemporaryKeyOverride(
                requestId,
                doorId,
                request.KeyTemplateId,
                request.Door);

            bool prepareSent = _network.Broadcast(BuildPrepare(
                requestId,
                doorId,
                profileId,
                request.KeyTemplateId,
                request.Keycard));

            _log.LogInfo(
                "Aishi Keys Fika: authority prepared synchronized master-key interaction. " +
                "door='" + doorId + "', keycard=" + request.Keycard +
                ", localPrepared=" + localPrepared +
                ", prepareBroadcast=" + prepareSent + ".");

            if (request.Keycard)
                StartPreparedLocalKeycard(requestId, doorId, profileId);

            yield return new WaitForSeconds(PrepareReplicationDelay);

            bool success;
            string failureReason = string.Empty;

            if (request.Door == null)
            {
                success = false;
                failureReason = "door disappeared before authoritative execution";
            }
            else if (request.Door.DoorState != EDoorState.Locked)
            {
                success = true;
            }
            else if (request.Keycard)
            {
                success = MasterKeyUnlockExecutor.UnlockKeycard(
                    (KeycardDoor)request.Door,
                    request.Player,
                    request.KeyComponent);

                if (!success)
                    failureReason = "keycard unlock operation failed";
            }
            else
            {
                success = MasterKeyUnlockExecutor.UnlockRegularSynchronized(
                    request.Door,
                    request.Player,
                    request.KeyComponent);

                if (!success)
                    failureReason = "regular unlock interaction failed";
            }

            int state = request.Door != null ? (int)request.Door.DoorState : -1;
            BroadcastResult(
                requestId,
                doorId,
                profileId,
                request.KeyTemplateId,
                request.Keycard,
                success,
                state,
                failureReason);

            if (success)
            {
                yield return new WaitForSeconds(ResultStateDelay);

                if (request.Door != null && _network.Ready && _network.IsServer)
                {
                    int followUpState = (int)request.Door.DoorState;
                    _network.Broadcast(BuildResult(
                        requestId,
                        doorId,
                        profileId,
                        request.KeyTemplateId,
                        request.Keycard,
                        true,
                        followUpState));
                }
            }

            yield return new WaitForSeconds(OverrideHoldAfterResult);

            if (_network.Ready && _network.IsServer)
                _network.Broadcast(BuildRestore(requestId, doorId));

            RestoreTemporaryKeyOverride(requestId, doorId);
        }

        private bool TryValidateRequest(
            WorldInteractiveObject door,
            Player player,
            string keyTemplateId,
            bool keycard,
            out ValidatedRequest request,
            out string failureReason)
        {
            request = null;
            failureReason = string.Empty;

            if (door == null)
            {
                failureReason = "door was not found on authority";
                return false;
            }

            if (player == null || player.Profile == null)
            {
                failureReason = "requesting player was not found on authority";
                return false;
            }

            if (door.DoorState != EDoorState.Locked)
            {
                failureReason = "door is no longer locked";
                return false;
            }

            if (string.IsNullOrEmpty(door.KeyId))
            {
                failureReason = "door has no key identifier";
                return false;
            }

            if (keycard)
            {
                if (!(door is KeycardDoor))
                {
                    failureReason = "request type does not match the door";
                    return false;
                }

                if (!MasterKeyHelper.AllMasterKeyIds.Contains(keyTemplateId))
                {
                    failureReason = "template is not an approved ultra key";
                    return false;
                }
            }
            else
            {
                if (door is KeycardDoor)
                {
                    failureReason = "regular master-key request targeted a keycard door";
                    return false;
                }

                if (!string.Equals(
                        keyTemplateId,
                        MasterKeyHelper.GetMasterKey(Config.UltraKeys.AishiMK),
                        StringComparison.Ordinal))
                {
                    failureReason = "template is not the Aishi master key";
                    return false;
                }
            }

            KeyComponent keyComponent = null;
            bool requireAuthoritativeInventory =
                !_network.IsHeadless && IsLocalProfile(player.Profile.Id);

            
            
            
            Item keyItem = player.Profile.Inventory != null
                ? player.Profile.Inventory
                    .GetPlayerItems((EPlayerItems)63)
                    .FirstOrDefault(item =>
                        item != null &&
                        string.Equals(item.TemplateId, keyTemplateId, StringComparison.Ordinal))
                : null;

            if (keyItem != null)
                keyComponent = MasterKeyItemResolver.FindKeyComponent(keyItem);

            if (requireAuthoritativeInventory && keyItem == null)
            {
                failureReason = "requesting local authority player does not possess the reported key";
                return false;
            }

            if (requireAuthoritativeInventory && keyComponent == null)
            {
                failureReason = "reported key has no KeyComponent";
                return false;
            }

            request = new ValidatedRequest
            {
                Door = door,
                Player = player,
                KeyTemplateId = keyTemplateId,
                Keycard = keycard,
                KeyComponent = keyComponent
            };

            return true;
        }

        private void BroadcastResult(
            string requestId,
            string doorId,
            string profileId,
            string keyTemplateId,
            bool keycard,
            bool success,
            int doorState,
            string failureReason)
        {
            bool sent = _network.Broadcast(BuildResult(
                requestId,
                doorId,
                profileId,
                keyTemplateId,
                keycard,
                success,
                doorState));

            if (success)
            {
                _log.LogInfo(
                    "Aishi Keys Fika: authority accepted master-key interaction. " +
                    "door='" + doorId + "', keycard=" + keycard +
                    ", state=" + doorState + ", broadcast=" + sent + ".");
            }
            else
            {
                _log.LogWarning(
                    "Aishi Keys Fika: authority rejected master-key interaction. " +
                    "door='" + doorId + "', reason='" + failureReason +
                    "', broadcast=" + sent + ".");
            }
        }

        private void HandlePrepare(string[] parts)
        {
            if (parts.Length != 8)
                return;

            string requestId = Decode(parts[2]);
            string doorId = Decode(parts[3]);
            string profileId = Decode(parts[4]);
            string keyTemplateId = Decode(parts[5]);
            bool keycard;

            if (!TryParseBool(parts[6], out keycard) ||
                !string.Equals(parts[7], "END", StringComparison.Ordinal))
            {
                return;
            }

            WorldInteractiveObject door =
                WorldInteractionUtils.FindInteractiveByStableId(doorId);
            bool applied = ApplyTemporaryKeyOverride(
                requestId,
                doorId,
                keyTemplateId,
                door);

            _log.LogInfo(
                "Aishi Keys Fika: client prepared replicated master-key interaction. " +
                "door='" + doorId + "', player='" + profileId +
                "', keycard=" + keycard + ", applied=" + applied + ".");

            if (applied)
                _plugin.StartCoroutine(AutoRestoreTemporaryKeyOverride(requestId, doorId));

            if (keycard)
                StartPreparedLocalKeycard(requestId, doorId, profileId);
        }

        private void HandleResult(string[] parts)
        {
            if (parts.Length != 10)
                return;

            string requestId = Decode(parts[2]);
            string doorId = Decode(parts[3]);
            string profileId = Decode(parts[4]);
            string keyTemplateId = Decode(parts[5]);
            bool keycard;
            bool success;
            int doorState;

            if (!TryParseBool(parts[6], out keycard) ||
                !TryParseBool(parts[7], out success) ||
                !int.TryParse(parts[8], out doorState) ||
                !string.Equals(parts[9], "END", StringComparison.Ordinal))
            {
                return;
            }

            if (_network.IsServer)
                return;

            if (!success)
            {
                _pendingLocalKeycards.Remove(requestId);
                RestoreTemporaryKeyOverride(requestId, doorId);
                return;
            }

            _log.LogInfo(
                "Aishi Keys Fika: client received authoritative master-key result. " +
                "door='" + doorId + "', keycard=" + keycard +
                ", state=" + doorState + ".");

            _plugin.StartCoroutine(ApplyAuthoritativeResult(
                requestId,
                doorId,
                profileId,
                keyTemplateId,
                keycard,
                doorState));
        }

        private void HandleRestore(string[] parts)
        {
            if (parts.Length != 5 ||
                !string.Equals(parts[4], "END", StringComparison.Ordinal))
            {
                return;
            }

            string requestId = Decode(parts[2]);
            string doorId = Decode(parts[3]);
            _pendingLocalKeycards.Remove(requestId);
            RestoreTemporaryKeyOverride(requestId, doorId);
        }

        private IEnumerator AutoRestoreTemporaryKeyOverride(
            string requestId,
            string doorId)
        {
            yield return new WaitForSeconds(OverrideSafetyTimeout);
            RestoreTemporaryKeyOverride(requestId, doorId);
        }

        private IEnumerator ApplyAuthoritativeResult(
            string requestId,
            string doorId,
            string profileId,
            string keyTemplateId,
            bool keycard,
            int doorState)
        {
            yield return new WaitForSeconds(ResultStateDelay);

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                WorldInteractiveObject door =
                    WorldInteractionUtils.FindInteractiveByStableId(doorId);
                if (door == null)
                {
                    _log.LogWarning(
                        "Aishi Keys Fika: synchronized door result could not be applied because " +
                        "the local door was not found. door='" + doorId + "'.");
                    yield break;
                }

                int localState = (int)door.DoorState;
                bool needsCorrection =
                    door.DoorState == EDoorState.Locked ||
                    (doorState >= 0 && localState != doorState);

                if (!needsCorrection)
                {
                    _pendingLocalKeycards.Remove(requestId);
                    yield break;
                }

                bool applied = WorldInteractionUtils.TryApplyAuthoritativeDoorState(
                    door,
                    doorState);
                int correctedState = (int)door.DoorState;

                _log.LogInfo(
                    "Aishi Keys Fika: applying authoritative door-state correction. " +
                    "attempt=" + attempt + ", applied=" + applied +
                    ", door='" + doorId + "', player='" + profileId +
                    "', key='" + keyTemplateId + "', keycard=" + keycard +
                    ", previousState=" + localState +
                    ", correctedState=" + correctedState +
                    ", authorityState=" + doorState + ".");

                if (door.DoorState != EDoorState.Locked &&
                    (doorState < 0 || (int)door.DoorState == doorState))
                {
                    _pendingLocalKeycards.Remove(requestId);
                    yield break;
                }

                yield return new WaitForSeconds(ResultRetryDelay);
            }

            _pendingLocalKeycards.Remove(requestId);
        }

        private void StartPreparedLocalKeycard(
            string requestId,
            string doorId,
            string profileId)
        {
            if (!IsLocalProfile(profileId))
                return;

            PendingLocalKeycard pending;
            if (!_pendingLocalKeycards.TryGetValue(requestId, out pending) ||
                pending == null || pending.Started)
            {
                return;
            }

            pending.Started = true;
            pending.DoorId = doorId;
            _plugin.StartCoroutine(ExecutePreparedLocalKeycard(pending));
        }

        private IEnumerator ExecutePreparedLocalKeycard(PendingLocalKeycard pending)
        {
            yield return new WaitForSeconds(LocalKeycardStartDelay);

            bool success = false;
            if (pending != null &&
                pending.Door != null &&
                pending.Player != null &&
                pending.KeyComponent != null)
            {
                success = MasterKeyUnlockExecutor.UnlockKeycard(
                    pending.Door,
                    pending.Player,
                    pending.KeyComponent);
            }

            _log.LogInfo(
                "Aishi Keys Fika: requester executed prepared keycard animation. " +
                "door='" + (pending != null ? pending.DoorId : string.Empty) +
                "', success=" + success + ".");

            if (pending != null)
                _pendingLocalKeycards.Remove(pending.RequestId);
        }

        private bool IsLocalProfile(string profileId)
        {
            if (_network.IsHeadless || string.IsNullOrEmpty(profileId))
                return false;

            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            Player mainPlayer = gameWorld != null ? gameWorld.MainPlayer : null;

            return mainPlayer != null &&
                   mainPlayer.Profile != null &&
                   string.Equals(
                       mainPlayer.Profile.Id,
                       profileId,
                       StringComparison.Ordinal);
        }

        private bool ApplyTemporaryKeyOverride(
            string requestId,
            string doorId,
            string temporaryKeyId,
            WorldInteractiveObject door)
        {
            if (string.IsNullOrEmpty(requestId) ||
                string.IsNullOrEmpty(doorId) ||
                string.IsNullOrEmpty(temporaryKeyId))
            {
                return false;
            }

            if (door == null)
                door = WorldInteractionUtils.FindInteractiveByStableId(doorId);

            if (door == null)
                return false;

            DoorKeyOverride existing;
            if (_doorOverrides.TryGetValue(doorId, out existing))
            {
                existing.RequestId = requestId;
                existing.Door = door;
                existing.TemporaryKeyId = temporaryKeyId;
                door.KeyId = temporaryKeyId;
                return true;
            }

            _doorOverrides[doorId] = new DoorKeyOverride
            {
                RequestId = requestId,
                DoorId = doorId,
                Door = door,
                OriginalKeyId = door.KeyId,
                TemporaryKeyId = temporaryKeyId
            };

            door.KeyId = temporaryKeyId;
            return true;
        }

        private void RestoreTemporaryKeyOverride(string requestId, string doorId)
        {
            if (string.IsNullOrEmpty(doorId))
                return;

            DoorKeyOverride lease;
            if (!_doorOverrides.TryGetValue(doorId, out lease))
                return;

            if (!string.IsNullOrEmpty(requestId) &&
                !string.Equals(lease.RequestId, requestId, StringComparison.Ordinal))
            {
                return;
            }

            if (lease.Door != null)
                lease.Door.KeyId = lease.OriginalKeyId;

            _doorOverrides.Remove(doorId);
        }

        private void RestoreAllDoorOverrides()
        {
            foreach (DoorKeyOverride lease in _doorOverrides.Values)
            {
                if (lease != null && lease.Door != null)
                    lease.Door.KeyId = lease.OriginalKeyId;
            }

            _doorOverrides.Clear();
        }

        private Player FindPlayer(string profileId)
        {
            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null || string.IsNullOrEmpty(profileId))
                return null;

            if (gameWorld.AllAlivePlayersList != null)
            {
                foreach (Player player in gameWorld.AllAlivePlayersList)
                {
                    if (player != null &&
                        player.Profile != null &&
                        string.Equals(player.Profile.Id, profileId, StringComparison.Ordinal))
                    {
                        return player;
                    }
                }
            }

            Player mainPlayer = gameWorld.MainPlayer;
            if (mainPlayer != null &&
                mainPlayer.Profile != null &&
                string.Equals(mainPlayer.Profile.Id, profileId, StringComparison.Ordinal))
            {
                return mainPlayer;
            }

            return null;
        }

        private static string BuildRequest(
            string requestId,
            string doorId,
            string profileId,
            string keyTemplateId,
            bool keycard)
        {
            return string.Join(
                "|",
                ProtocolVersion,
                RequestMessage,
                Encode(requestId),
                Encode(doorId),
                Encode(profileId),
                Encode(keyTemplateId),
                keycard ? "1" : "0",
                "END");
        }

        private static string BuildPrepare(
            string requestId,
            string doorId,
            string profileId,
            string keyTemplateId,
            bool keycard)
        {
            return string.Join(
                "|",
                ProtocolVersion,
                PrepareMessage,
                Encode(requestId),
                Encode(doorId),
                Encode(profileId),
                Encode(keyTemplateId),
                keycard ? "1" : "0",
                "END");
        }

        private static string BuildResult(
            string requestId,
            string doorId,
            string profileId,
            string keyTemplateId,
            bool keycard,
            bool success,
            int doorState)
        {
            return string.Join(
                "|",
                ProtocolVersion,
                ResultMessage,
                Encode(requestId),
                Encode(doorId),
                Encode(profileId),
                Encode(keyTemplateId),
                keycard ? "1" : "0",
                success ? "1" : "0",
                doorState.ToString(),
                "END");
        }

        private static string BuildRestore(string requestId, string doorId)
        {
            return string.Join(
                "|",
                ProtocolVersion,
                RestoreMessage,
                Encode(requestId),
                Encode(doorId),
                "END");
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string Decode(string value)
        {
            try
            {
                return Encoding.UTF8.GetString(
                    Convert.FromBase64String(value ?? string.Empty));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryParseBool(string value, out bool result)
        {
            if (string.Equals(value, "1", StringComparison.Ordinal))
            {
                result = true;
                return true;
            }

            if (string.Equals(value, "0", StringComparison.Ordinal))
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }
    }
}
