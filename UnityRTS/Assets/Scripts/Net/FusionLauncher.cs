using System;
using Fusion;
using Rts.Game;
using UnityEngine;

namespace Rts.Net
{
    /// <summary>
    /// Lobby + session start for Photon Fusion 2 Host Mode.
    ///   Host:   StartGame(GameMode.Host)   -> spawn RtsNetObject -> GameBootstrap.StartHost  (authoritative simulation runs here)
    ///   Client: StartGame(GameMode.Client) -> wait for RtsNetObject -> GameBootstrap.StartClient (replica + commands to the host)
    /// Scene setup: one GameObject with this component and a GameBootstrap (AutoStart = false). See docs/unity_setup.md.
    /// </summary>
    public sealed class FusionLauncher : MonoBehaviour
    {
        public GameBootstrap Bootstrap;
        public NetworkObject NetObjectPrefab;          // prefab with NetworkObject + RtsNetObject (menu Rts > Network > Create Net Prefab and Scene)
        public string SessionName = "rts-room";

        NetworkRunner _runner;
        string _status = "Not connected.";
        bool _busy;

        void Awake()
        {
            if (Bootstrap == null) Bootstrap = GetComponent<GameBootstrap>();
            if (Bootstrap != null) Bootstrap.AutoStart = false;
        }

        void OnGUI()
        {
            if (Bootstrap == null || Bootstrap.Started) return;
            var r = new Rect(Screen.width / 2f - 190, Screen.height / 2f - 110, 380, 220);
            GUI.Box(r, "RTS - Photon Fusion 2 (Host Mode)");
            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 28, r.width - 24, r.height - 36));
            GUILayout.Label("Session name");
            SessionName = GUILayout.TextField(SessionName, 32);
            GUI.enabled = !_busy;
            if (GUILayout.Button("Host a match (you are player 1)", GUILayout.Height(32))) StartHostSession();
            if (GUILayout.Button("Join a match (you are player 2)", GUILayout.Height(32))) StartClientSession();
            if (GUILayout.Button("Play offline (local hotseat)", GUILayout.Height(26))) Bootstrap.StartLocal();
            GUI.enabled = true;
            GUILayout.Label(_status);
            GUILayout.EndArea();
        }

        NetworkRunner NewRunner()
        {
            var runner = gameObject.AddComponent<NetworkRunner>();
            runner.ProvideInput = false;     // RTS commands travel as RPCs, not as Fusion input
            return runner;
        }

        /// <summary>The default scene manager lives in Fusion.Unity; found by name so this file compiles against either SDK layout.</summary>
        INetworkSceneManager FindSceneManager()
        {
            var type = Type.GetType("Fusion.NetworkSceneManagerDefault, Fusion.Unity");
            return type == null ? null : gameObject.AddComponent(type) as INetworkSceneManager;
        }

        public async void StartHostSession()
        {
            _busy = true; _status = "Starting host...";
            try
            {
                _runner = NewRunner();
                var result = await _runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Host,
                    SessionName = SessionName,
                    PlayerCount = 2,
                    SceneManager = FindSceneManager(),
                });
                if (!result.Ok) { _status = "Host failed: " + result.ShutdownReason; _busy = false; return; }

                var spawned = _runner.Spawn(NetObjectPrefab);
                var net = spawned.GetComponent<RtsNetObject>();
                var host = Bootstrap.StartHost(net);
                net.AttachHost(host);
                _status = "Hosting '" + SessionName + "'. Waiting for player 2...";
            }
            catch (Exception ex) { _status = "Host error: " + ex.Message; Debug.LogException(ex); }
            _busy = false;
        }

        public async void StartClientSession()
        {
            _busy = true; _status = "Joining...";
            try
            {
                RtsNetObject.ClientObjectSpawned += OnClientObjectSpawned;
                _runner = NewRunner();
                var result = await _runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Client,
                    SessionName = SessionName,
                    SceneManager = FindSceneManager(),
                });
                if (!result.Ok) { _status = "Join failed: " + result.ShutdownReason; RtsNetObject.ClientObjectSpawned -= OnClientObjectSpawned; }
                else _status = "Connected. Waiting for the host's match...";
            }
            catch (Exception ex) { _status = "Join error: " + ex.Message; Debug.LogException(ex); }
            _busy = false;
        }

        void OnClientObjectSpawned(RtsNetObject net)
        {
            RtsNetObject.ClientObjectSpawned -= OnClientObjectSpawned;
            var client = Bootstrap.StartClient(RtsNetObject.ClientPlayerId, net);
            net.AttachClient(client);
        }

        void OnDestroy() { RtsNetObject.ClientObjectSpawned -= OnClientObjectSpawned; }
    }
}
