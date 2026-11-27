using Rts.Core;
using UnityEngine;

namespace Rts.Game
{
    /// <summary>
    /// The only component the demo scene needs. It builds the match, ground, camera rig and every UI/controller component at runtime,
    /// so there is nothing else to wire by hand. Local, single-process: the simulation here is the state authority
    /// (Photon Fusion Host Mode would run it on the host; see docs/unity_setup.md).
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public Camera MainCamera;      // optional: defaults to Camera.main (created if missing)
        public int LocalPlayer = 1;

        public GameContext Ctx { get; private set; }
        public EntityViewManager Views { get; private set; }
        public AiPlanPanel AiPanel { get; private set; }
        public DebugPanel Debug { get; private set; }
        public SelectionController Selection { get; private set; }

        void Awake()
        {
            var sim = MapSetup.CreateDemoMatch();
            Ctx = new GameContext { Sim = sim, LocalPlayer = LocalPlayer };
            Ctx.RefreshVisibility();

            BuildWorld();
            var cam = SetupCamera();

            var input = Add<RtsInput>(gameObject);
            var driver = Add<SimulationDriver>(gameObject); driver.Ctx = Ctx;
            Views = Add<EntityViewManager>(new GameObject("Entities")); Views.Ctx = Ctx;
            Views.transform.SetParent(transform, false);

            var rig = Add<RtsCameraController>(gameObject); rig.Cam = cam;
            var citadel = sim.FindCitadel(LocalPlayer);
            rig.FocusOn(GameContext.ToWorld(citadel.Pos));

            Selection = Add<SelectionController>(gameObject); Selection.Ctx = Ctx; Selection.Cam = cam;
            var hud = Add<RtsHud>(gameObject); hud.Ctx = Ctx; hud.Cam = cam; hud.Selection = Selection; hud.CameraRig = rig;
            AiPanel = Add<AiPlanPanel>(gameObject); AiPanel.Ctx = Ctx;
            Debug = Add<DebugPanel>(gameObject); Debug.Ctx = Ctx;
            Ctx.Say("Select your Workers (drag a box), then press Gather. Train more at the Citadel.");
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
