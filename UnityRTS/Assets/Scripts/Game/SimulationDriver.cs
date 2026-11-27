using UnityEngine;

namespace Rts.Game
{
    /// <summary>Advances the authoritative simulation at a fixed 20 Hz step (host-side in a networked build).</summary>
    public sealed class SimulationDriver : MonoBehaviour
    {
        public GameContext Ctx;
        public const float Step = 0.05f;
        float _acc;

        void Update()
        {
            if (Ctx == null) return;
            _acc += Mathf.Min(Time.deltaTime, 0.25f) * Ctx.TimeScale;
            int guard = 0;
            while (_acc >= Step && guard++ < 20) { Ctx.Sim.Tick(Step); _acc -= Step; }
            Ctx.RefreshVisibility();
        }
    }
}
