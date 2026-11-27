using Rts.Core;
using Rts.Core.Net;
using UnityEngine;

namespace Rts.Game
{
    /// <summary>
    /// The only component the demo scene needs. It builds ground, camera rig and every UI/controller component at runtime.
    ///   AutoStart (default): offline single-process match (Local role), exactly as before.
    ///   AutoStart off: a network launcher (see Rts.Net.Fusion) calls StartHost / StartClient once the session is up.
    /// Host: runs the one authoritative MatchSimulation and replicates fog-filtered state. Client: shows a replica and sends commands.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public Camera MainCamera;      // optional: defaults to Camera.main (created if missing)
        public int LocalPlayer = 1;
        public bool AutoStart = true;

        public GameContext Ctx { get; private set; }
        public EntityViewManager Views { get; private set; }
        public AiPlanPanel AiPanel { get; private set; }
        public DebugPanel Debug { get; private set; }
        public SelectionController Selection { get; private set; }
        public bool Started => Ctx != null;

        RtsCameraController _rig;
        bool _focused;

        void Awake() { if (AutoStart) StartLocal(); }

        public void StartLocal()
        {
            var sim = MapSetup.CreateDemoMatch();
            Begin(NetRole.Local, LocalPlayer, sim, null, null);
        }

        /// <summary>Host Mode: this machine owns the authoritative simulation and plays as player 1.</summary>
        public HostSession StartHost(IHostTransport transport)
        {
            var sim = MapSetup.CreateDemoMatch();
            var host = new HostSession(sim, transport, 1);
            Begin(NetRole.Host, 1, sim, host, null);
            return host;
        }

        /// <summary>Client: shows the replica the host sends; every command goes to the host.</summary>
        public ClientSession StartClient(int playerId, IClientTransport transport)
        {
            var client = new ClientSession(playerId, transport);
            Begin(NetRole.Client, playerId, client.Replica, null, client);
            return client;
        }

        void Begin(NetRole role, int localPlayer, MatchSimulation sim, HostSession host, ClientSession client)
        {
            if (Ctx != null) { Debug_Log("GameBootstrap already started"); return; }
            Ctx = new GameContext { Sim = sim, Role = role, LocalPlayer = localPlayer, Host = host, Client = client };
            Ctx.Commands = role == NetRole.Client ? (ICommandSink)new ClientCommandSink(client) : new LocalCommandSink(sim, () => Ctx.LocalPlayer);
            Ctx.RefreshVisibility();

            BuildWorld();
            var cam = SetupCamera();

            Add<RtsInput>(gameObject);
            var driver = Add<SimulationDriver>(gameObject); driver.Ctx = Ctx;
            Views = Add<EntityViewManager>(new GameObject("Entities")); Views.Ctx = Ctx;
            Views.transform.SetParent(transform, false);

            _rig = Add<RtsCameraController>(gameObject); _rig.Cam = cam;
            TryFocus();

            Selection = Add<SelectionController>(gameObject); Selection.Ctx = Ctx; Selection.Cam = cam;
            var hud = Add<RtsHud>(gameObject); hud.Ctx = Ctx; hud.Cam = cam; hud.Selection = Selection; hud.CameraRig = _rig;
            AiPanel = Add<AiPlanPanel>(gameObject); AiPanel.Ctx = Ctx;
            Debug = Add<DebugPanel>(gameObject); Debug.Ctx = Ctx;
            Ctx.Say(role == NetRole.Client ? "Connected. Waiting for the host's first state..." : "Select your Workers (drag a box), then press Gather. Train more at the Citadel.");
        }

        static void Debug_Log(string m) { UnityEngine.Debug.LogWarning(m); }

        void Update() { if (!_focused) TryFocus(); }

        void TryFocus()
        {
            if (_focused || Ctx == null || _rig == null || !Ctx.Ready) return;
            _rig.FocusOn(GameContext.ToWorld(Ctx.Sim.FindCitadel(Ctx.LocalPlayer).Pos));
            _focused = true;
        }

        static T Add<T>(GameObject go) where T : Component => go.AddComponent<T>();

        void BuildWorld()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(transform, false);
            ground.transform.localScale = new Vector3(MatchSimulation.MapHalf * 2 / 10f, 1, MatchSimulation.MapHalf * 2 / 10f);   // Plane is 10x10
            var col = ground.GetComponent<Collider>(); if (col != null) Destroy(col);
            ground.GetComponent<Renderer>().material.color = new Color(0.32f, 0.45f, 0.28f);

            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var l = new GameObject("Sun").AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = 1.1f;
                l.transform.rotation = Quaternion.Euler(50, -30, 0);
            }
        }

        Camera SetupCamera()
        {
            var cam = MainCamera != null ? MainCamera : Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.transform.rotation = Quaternion.Euler(55, 0, 0);
            cam.transform.position = new Vector3(0, 45, -30);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.1f);
            cam.farClipPlane = 400f;
            return cam;
        }
    }
}
