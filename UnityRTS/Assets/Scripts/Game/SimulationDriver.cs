using UnityEngine;

namespace Rts.Game
{
    /// <summary>
    /// Local/Host: advances the authoritative simulation at a fixed 20 Hz step and (Host) replicates fog-filtered state to clients.
    /// Client: never simulates; it only drains host notices (command errors, AI plan results).
    /// </summary>
    public sealed class SimulationDriver : MonoBehaviour
    {
        public GameContext Ctx;
        public const float Step = 0.05f;
        float _acc;

        void Update()
        {
            if (Ctx == null) return;
            if (Ctx.Role == NetRole.Client)
            {
                while (Ctx.Client.TryDequeueNotice(out var notice)) Ctx.Say(FirstLine(notice));
                Ctx.NetStatus = Ctx.Client.HasState ? $"Client P{Ctx.LocalPlayer} - host state #{Ctx.Client.StatesApplied}" : "Client - waiting for host state...";
            }
            else
            {
                float scale = Ctx.Role == NetRole.Host ? 1f : Ctx.TimeScale;      // never change the pace of a shared match
                _acc += Mathf.Min(Time.deltaTime, 0.25f) * scale;
                int guard = 0;
                while (_acc >= Step && guard++ < 20)
                {
                    Ctx.Sim.Tick(Step);
                    if (Ctx.Host != null) Ctx.Host.Replicate(Step);
                    _acc -= Step;
                }
                if (Ctx.Role == NetRole.Host) Ctx.NetStatus = "Host P1 - remote players: " + string.Join(",", Ctx.Host.RemotePlayers);
            }
            Ctx.RefreshVisibility();
        }

        static string FirstLine(string s) { int i = s.IndexOf('\n'); return i < 0 ? s : s.Substring(0, i); }
    }
}
