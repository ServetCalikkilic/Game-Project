using System.Collections.Generic;
using Rts.Core;
using UnityEngine;

namespace Rts.Game
{
    /// <summary>Shared runtime state for the Unity layer (one local viewpoint onto the authoritative simulation).</summary>
    public sealed class GameContext
    {
        public MatchSimulation Sim;
        /// <summary>Which player this window controls. Hotseat (Tab) switches it; real multiplayer fixes it per client.</summary>
        public int LocalPlayer = 1;
        public readonly HashSet<int> Selection = new HashSet<int>();
        public VisibleWorld LocalVisible;     // fog-of-war view of LocalPlayer, refreshed every frame
        public float TimeScale = 1f;

        string _toast; float _toastUntil;
        public string Toast => Time.unscaledTime < _toastUntil ? _toast : null;
        public void Say(string message) { _toast = message; _toastUntil = Time.unscaledTime + 4f; }
        public void Say(CommandResult r, string okMessage = null) { if (!r.Ok) Say(r.Reason); else if (okMessage != null) Say(okMessage); }

        public PlayerState Me => Sim.Players[LocalPlayer];

        public void RefreshVisibility() { LocalVisible = Sim.GetVisible(LocalPlayer); }

        public static string RepoRoot => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", ".."));

        public static Vector3 ToWorld(Vec2 p, float y = 0f) => new Vector3(p.X, y, p.Y);
        public static Vec2 ToSim(Vector3 w) => new Vec2(w.x, w.z);
    }
}
