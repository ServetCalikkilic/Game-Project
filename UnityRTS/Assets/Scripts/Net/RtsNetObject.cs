using System;
using System.Collections.Generic;
using Fusion;
using Rts.Core.Net;
using UnityEngine;

namespace Rts.Net
{
    /// <summary>
    /// The one networked object of a match (spawned by the host from a prefab). It is only a thin transport:
    ///   client -> host   RPC_Command(bytes)      handled by HostSession/HostAuthority (the same authoritative path as local play)
    ///   host -> client   RPC_ToPlayer(target, bytes)  fog-filtered state / notices, one player at a time
    /// All game state lives in Rts.Core (MatchSimulation). Host Mode: the host is the State Authority of this object.
    /// Compiled only when the Photon Fusion 2 SDK is installed (scripting define RTS_FUSION).
    /// </summary>
    public sealed class RtsNetObject : NetworkBehaviour, IPlayerJoined, IPlayerLeft, IHostTransport, IClientTransport
    {
        public const int HostPlayerId = 1;
        public const int ClientPlayerId = 2;   // two-player prototype: one remote player

        /// <summary>Raised on a client when the host's net object has been replicated to it.</summary>
        public static event Action<RtsNetObject> ClientObjectSpawned;

        public HostSession Host { get; private set; }
        public ClientSession Client { get; private set; }

        readonly Dictionary<PlayerRef, int> _playerIds = new Dictionary<PlayerRef, int>();
        readonly Dictionary<int, PlayerRef> _playerRefs = new Dictionary<int, PlayerRef>();

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                foreach (var p in Runner.ActivePlayers) Register(p);
            }
            else ClientObjectSpawned?.Invoke(this);
        }

        // ---------------------------------------------------------------- wiring (called by FusionLauncher)
        public void AttachHost(HostSession host)
        {
            Host = host;
            foreach (var kv in _playerIds) host.AddRemotePlayer(kv.Value);   // players that joined before the session existed
        }

        public void AttachClient(ClientSession client) { Client = client; }

        // ---------------------------------------------------------------- players (host side)
        public void PlayerJoined(PlayerRef player)
        {
            if (Object != null && Object.HasStateAuthority) Register(player);
        }

        public void PlayerLeft(PlayerRef player)
        {
            if (!_playerIds.TryGetValue(player, out int id)) return;
            _playerIds.Remove(player);
            _playerRefs.Remove(id);
            Host?.RemoveRemotePlayer(id);
        }

        void Register(PlayerRef player)
        {
            if (player == Runner.LocalPlayer || _playerIds.ContainsKey(player)) return;   // the host plays directly on the simulation
            if (_playerIds.Count >= 1) { Runner.Disconnect(player); return; }              // this prototype is a 2-player match
            _playerIds[player] = ClientPlayerId;
            _playerRefs[ClientPlayerId] = player;
            Host?.AddRemotePlayer(ClientPlayerId);
        }

        // ---------------------------------------------------------------- transport
        /// <summary>Client -> host. The sender's identity is taken from the connection (info.Source), never from the payload.</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Command(byte[] data, RpcInfo info = default)
        {
            if (Host == null || !_playerIds.TryGetValue(info.Source, out int playerId)) return;
            Host.OnCommand(playerId, data);
        }

        /// <summary>Host -> one client. Each chunk is a slice of a fog-filtered state or a notice for that player only.</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        public void RPC_ToPlayer([RpcTarget] PlayerRef target, byte[] data)
        {
            Client?.ReceiveFromHost(data);
        }

        void IHostTransport.SendToPlayer(int playerId, byte[] chunk)
        {
            if (_playerRefs.TryGetValue(playerId, out var player)) RPC_ToPlayer(player, chunk);
        }

        void IClientTransport.SendToHost(byte[] data) { RPC_Command(data); }
    }
}
