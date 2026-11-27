using System.Collections.Generic;
using Rts.Core;
using UnityEngine;

namespace Rts.Game
{
    /// <summary>
    /// Single/multi selection (click, shift-click, drag box) and right-click commands (move / attack / gather).
    /// Picking is done in screen space (no colliders). Only the local player's own entities can be selected, and
    /// enemies can only be targeted while visible; the simulation enforces both rules again.
    /// </summary>
    public sealed class SelectionController : MonoBehaviour
    {
        public GameContext Ctx;
        public Camera Cam;
        public bool Dragging { get; private set; }
        public Rect DragRectGui { get; private set; }     // IMGUI space (top-left origin)

        Vector2 _dragStartScreen;
        const float ClickRadiusPx = 30f;
        const float DragThresholdPx = 8f;

        void Update()
        {
            var input = RtsInput.Instance;
            if (Ctx == null || Cam == null || input == null) return;

            if (input.Pressed(KeyCode.Escape)) Ctx.Selection.Clear();
            if (input.Pressed(KeyCode.S)) { Ctx.Sim.OrderStop(Ctx.LocalPlayer, Ctx.Selection); Ctx.Say("Stop"); }

            // drop selection entries that died
            if (Ctx.Selection.Count > 0) Ctx.Selection.RemoveWhere(id => Ctx.Sim.Get(id) == null);

            if (input.LeftDown && !input.OverUi) { _dragStartScreen = input.MousePos; Dragging = true; }
            if (Dragging)
            {
                var a = _dragStartScreen; var b = input.MousePos;
                DragRectGui = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Screen.height - Mathf.Max(a.y, b.y), Mathf.Max(a.x, b.x), Screen.height - Mathf.Min(a.y, b.y));
                if (input.LeftUp)
                {
                    Dragging = false;
                    if ((b - a).magnitude < DragThresholdPx) ClickSelect(b, input.ShiftHeld);
                    else BoxSelect(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)), input.ShiftHeld);
                }
            }
            else if (input.LeftUp) Dragging = false;

            if (input.RightDown && !input.OverUi && Ctx.Selection.Count > 0) RightClick(input.MousePos);
        }

        Vector2? ScreenOf(Entity e)
        {
            var p = Cam.WorldToScreenPoint(GameContext.ToWorld(e.Pos, 0.8f));
            return p.z > 0 ? (Vector2?)new Vector2(p.x, p.y) : null;
        }

        void ClickSelect(Vector2 screen, bool additive)
        {
            Entity best = null; float bestD = ClickRadiusPx;
            foreach (var e in Ctx.Sim.Entities)
            {
                if (!e.Alive || e.OwnerId != Ctx.LocalPlayer || e.Kind == EntityKind.ResourceNode) continue;
                var sp = ScreenOf(e); if (!sp.HasValue) continue;
                float d = Vector2.Distance(sp.Value, screen) - (e.Kind == EntityKind.Building ? 18f : 0f);   // buildings are bigger targets
                if (d < bestD) { bestD = d; best = e; }
            }
            if (!additive) Ctx.Selection.Clear();
            if (best != null) { if (!Ctx.Selection.Add(best.Id) && additive) Ctx.Selection.Remove(best.Id); }
        }

        void BoxSelect(Rect screenRect, bool additive)
        {
            var units = new List<int>(); var buildings = new List<int>();
            foreach (var e in Ctx.Sim.Entities)
            {
                if (!e.Alive || e.OwnerId != Ctx.LocalPlayer || e.Kind == EntityKind.ResourceNode) continue;
                var sp = ScreenOf(e); if (!sp.HasValue || !screenRect.Contains(sp.Value)) continue;
                (e.Kind == EntityKind.Unit ? units : buildings).Add(e.Id);
            }
            if (!additive) Ctx.Selection.Clear();
            foreach (int id in units.Count > 0 ? units : buildings) Ctx.Selection.Add(id);
        }

        Entity PickTarget(Vector2 screen, bool enemies, bool nodes)
        {
            Entity best = null; float bestD = ClickRadiusPx;
            foreach (var e in Ctx.Sim.Entities)
            {
                if (!e.Alive) continue;
                bool isEnemy = e.Kind != EntityKind.ResourceNode && e.OwnerId != Ctx.LocalPlayer;
                if (isEnemy ? !enemies || !Ctx.LocalVisible.CanSee(e.Id) : !(nodes && e.Kind == EntityKind.ResourceNode)) continue;
                var sp = ScreenOf(e); if (!sp.HasValue) continue;
                float d = Vector2.Distance(sp.Value, screen) - (e.Kind == EntityKind.Building ? 18f : 0f);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        void RightClick(Vector2 screen)
        {
            var enemy = PickTarget(screen, true, false);
            if (enemy != null) { Ctx.Say(Ctx.Sim.OrderAttack(Ctx.LocalPlayer, Ctx.Selection, enemy.Id), "Attack"); return; }

            bool hasWorker = false;
            foreach (int id in Ctx.Selection) { var e = Ctx.Sim.Get(id); if (e != null && e.Kind == EntityKind.Unit && e.Unit == UnitType.Worker) hasWorker = true; }
            var node = hasWorker ? PickTarget(screen, false, true) : null;
            if (node != null) { Ctx.Say(Ctx.Sim.OrderGather(Ctx.LocalPlayer, Ctx.Selection, node.Id), "Gather " + node.NodeResource); return; }

            var ray = Cam.ScreenPointToRay(screen);
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float dist))
                Ctx.Say(Ctx.Sim.OrderMove(Ctx.LocalPlayer, Ctx.Selection, GameContext.ToSim(ray.GetPoint(dist))), "Move");
        }
    }
}
