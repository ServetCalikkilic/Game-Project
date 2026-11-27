using System.Collections.Generic;
using Rts.Core;
using Rts.Core.Net;
using UnityEngine;

namespace Rts.Game
{
    /// <summary>Shared runtime state for the Unity layer (one local viewpoint onto the simulation or its client replica).</summary>
    public sealed class GameContext
    {
        public MatchSimulation Sim;
        public NetRole Role = NetRole.Local;
        public ICommandSink Commands;
        public HostSession Host;         // Host role only
        public ClientSession Client;     // Client role only
        public string NetStatus = "";

        /// <summary>Which player this window controls. Local role: hotseat (Tab) switches it. Host = 1, Client = 2 in network roles.</summary>
        public int LocalPlayer = 1;
        public readonly HashSet<int> Selection = new HashSet<int>();
        public VisibleWorld LocalVisible;     // fog-of-war view of LocalPlayer, refreshed every frame
        public float TimeScale = 1f;
        /// <summary>False until the local player's Citadel exists in Sim (a client waits for the first host state).</summary>
        public bool Ready { get; private set; }

        string _toast; float _toastUntil;
        public string Toast => Time.unscaledTime < _toastUntil ? _toast : null;
        public void Say(string message) { _toast = message; _toastUntil = Time.unscaledTime + 4f; }
        public void Say(CommandResult r, string okMessage = null) { if (!r.Ok) Say(r.Reason); else if (okMessage != null) Say(okMessage); }

        public PlayerState Me => Sim.Players[LocalPlayer];
        public bool IsAuthority => Role != NetRole.Client;

        public void RefreshVisibility()
        {
            Ready = Sim.Players.ContainsKey(LocalPlayer) && Sim.FindCitadel(LocalPlayer) != null;
            LocalVisible = Sim.GetVisible(LocalPlayer);
        }

        public static string RepoRoot => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", ".."));

        public static Vector3 ToWorld(Vec2 p, float y = 0f) => new Vector3(p.X, y, p.Y);
        public static Vec2 ToSim(Vector3 w) => new Vec2(w.x, w.z);
    }
}
