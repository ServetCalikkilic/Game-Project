using Rts.Core;
using UnityEngine;

namespace Rts.Game
{
    /// <summary>Test helpers (F1). Debug-only: they bypass normal rules on purpose, so the build flag keeps them out of release builds.</summary>
    public sealed class DebugPanel : MonoBehaviour
    {
        public GameContext Ctx;
        public bool Visible = true;

        void Update()
        {
            var input = RtsInput.Instance;
            if (input != null && input.Pressed(KeyCode.F1)) Visible = !Visible;
        }

        Vec2 Gate(int forPlayer, Zone zone)
        {
            var c = Ctx.Sim.FindCitadel(forPlayer);
            var nodes = Ctx.Sim.Entities.FindAll(e => e.Kind == EntityKind.ResourceNode);
            return ZoneMap.Center(zone, c != null ? c.Pos : default, nodes);
        }

        public void SpawnEnemies(UnitType type, int count, Zone zone)
        {
            int enemy = Ctx.LocalPlayer == 1 ? 2 : 1;
            var at = Gate(Ctx.LocalPlayer, zone);
            for (int i = 0; i < count; i++) Ctx.Sim.SpawnUnit(enemy, type, at + new Vec2(i * 1.6f, 0));
            Ctx.Say($"Spawned {count} enemy {type} at your {zone} (visible only if one of your units/buildings sees them)");
        }

        public void GrantBuildingsAndResources()
        {
            var p = Ctx.Me; p.Wood += 1000; p.Food += 1000; p.Gold += 1000;
            var c = Ctx.Sim.FindCitadel(Ctx.LocalPlayer);
            foreach (var kv in new[] { (BuildingType.Barracks, new Vec2(14, -22)), (BuildingType.ArcheryYard, new Vec2(-14, -22)), (BuildingType.Stable, new Vec2(0, -30)) })
                if (!p.HasBuilding(kv.Item1) && c != null) Ctx.Sim.SpawnBuilding(Ctx.LocalPlayer, kv.Item1, c.Pos + kv.Item2);
            Ctx.Say("+1000 each resource; Barracks, ArcheryYard and Stable placed instantly");
        }

        void OnGUI()
        {
            if (!Visible || Ctx == null || !Ctx.Ready || Ctx.Role == NetRole.Client) return;   // debug cheats act on the authoritative sim only
            float s = RtsHud.Scale;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            var r = new Rect(8, 32, 250, 210);
            GUI.Box(r, "Debug (F1)");
            if (Event.current.type == EventType.Repaint) RtsHud.Block(r);
            GUILayout.BeginArea(new Rect(r.x + 6, r.y + 20, r.width - 12, r.height - 24));
            if (GUILayout.Button("Enemy: 4 Raiders at my NorthGate")) SpawnEnemies(UnitType.Raider, 4, Zone.NorthGate);
            if (GUILayout.Button("Enemy: 3 Archers at my EastGate")) SpawnEnemies(UnitType.Archer, 3, Zone.EastGate);
            if (GUILayout.Button("Grant resources + military buildings")) GrantBuildingsAndResources();
            if (GUILayout.Button("Skip to Age II (instant)")) { Ctx.Me.Age = 2; Ctx.Me.Researched.Add(TechId.Age2IronAge); Ctx.Say("Age II"); }
            GUILayout.BeginHorizontal();
            foreach (float k in new[] { 1f, 3f, 8f }) if (GUILayout.Button("x" + k)) Ctx.TimeScale = k;
            GUILayout.EndHorizontal();
            GUILayout.Label($"Time scale: x{Ctx.TimeScale}");
            GUILayout.EndArea();
            GUI.matrix = old;
        }
    }
}
