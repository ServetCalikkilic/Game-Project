using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Rts.Core
{
    // Field names are snake_case on purpose: they are the wire format read by the Python
    // AI gateway / MCP server (mcp-server/session_store.py sanitizes against exactly these keys).

    [Serializable] public sealed class SnapshotResources { public int wood, food, gold; }
    [Serializable] public sealed class SnapshotPopulation { public int used, cap; }
    [Serializable] public sealed class SnapshotTechnology { public int age; public string[] researched = new string[0]; public string[] researching = new string[0]; }
    [Serializable] public sealed class SnapshotUnitGroup { public string type; public int count; public string zone; }
    [Serializable] public sealed class SnapshotBuildingGroup { public string type; public int count; }
    [Serializable] public sealed class SnapshotEvent { public string kind; public int seconds_ago; public string zone; public string unit_type; public int count; }

    /// <summary>
    /// Everything the AI may know: the player's own economy/army/tech, recent own events, the semantic phase,
    /// and ENEMY UNITS THAT ARE CURRENTLY VISIBLE (type, count, coarse zone). It has no field for enemy
    /// positions, enemy economy, hidden units or unexplored areas, and it can only be built from a VisibleWorld.
    /// </summary>
    [Serializable]
    public sealed class AiSafeSnapshot
    {
        public string phase;
        public SnapshotResources resources = new SnapshotResources();
        public SnapshotPopulation population = new SnapshotPopulation();
        public SnapshotTechnology technology = new SnapshotTechnology();
        public SnapshotUnitGroup[] own_army = new SnapshotUnitGroup[0];
        public SnapshotBuildingGroup[] own_buildings = new SnapshotBuildingGroup[0];
        public SnapshotUnitGroup[] visible_enemies = new SnapshotUnitGroup[0];
        public SnapshotEvent[] recent_events = new SnapshotEvent[0];
        public double captured_at;   // Unix epoch seconds

        /// <summary>Deterministic JSON matching the MCP server's snapshot allowlist. Culture-invariant.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"phase\":").Append(Q(phase));
            sb.Append(",\"captured_at\":").Append(captured_at.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(",\"resources\":{\"wood\":").Append(resources.wood).Append(",\"food\":").Append(resources.food).Append(",\"gold\":").Append(resources.gold).Append('}');
            sb.Append(",\"population\":{\"used\":").Append(population.used).Append(",\"cap\":").Append(population.cap).Append('}');
            sb.Append(",\"technology\":{\"age\":").Append(technology.age).Append(",\"researched\":").Append(Strs(technology.researched))
              .Append(",\"researching\":").Append(Strs(technology.researching)).Append('}');
            sb.Append(",\"own_army\":").Append(Groups(own_army));
            sb.Append(",\"own_buildings\":[");
            for (int i = 0; i < own_buildings.Length; i++)
                sb.Append(i > 0 ? "," : "").Append("{\"type\":").Append(Q(own_buildings[i].type)).Append(",\"count\":").Append(own_buildings[i].count).Append('}');
            sb.Append(']');
            sb.Append(",\"visible_enemies\":").Append(Groups(visible_enemies));
            sb.Append(",\"recent_events\":[");
            for (int i = 0; i < recent_events.Length; i++)
            {
                var e = recent_events[i];
                sb.Append(i > 0 ? "," : "").Append("{\"kind\":").Append(Q(e.kind)).Append(",\"seconds_ago\":").Append(e.seconds_ago);
                if (!string.IsNullOrEmpty(e.zone)) sb.Append(",\"zone\":").Append(Q(e.zone));
                if (!string.IsNullOrEmpty(e.unit_type)) sb.Append(",\"unit_type\":").Append(Q(e.unit_type));
                if (e.count > 0) sb.Append(",\"count\":").Append(e.count);
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        static string Strs(string[] a) { var parts = new List<string>(); foreach (var s in a) parts.Add(Q(s)); return "[" + string.Join(",", parts) + "]"; }
        static string Groups(SnapshotUnitGroup[] g)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < g.Length; i++)
                sb.Append(i > 0 ? "," : "").Append("{\"type\":").Append(Q(g[i].type)).Append(",\"count\":").Append(g[i].count).Append(",\"zone\":").Append(Q(g[i].zone)).Append('}');
            return sb.Append(']').ToString();
        }
    }

    public static class AiSnapshotBuilder
    {
        public const int MaxEvents = 50;

        /// <param name="own">The requesting player's own state only. Another player's PlayerState is never passed in.</param>
        /// <param name="view">Output of VisibilityService.FilterFor(own player). The fog-of-war boundary.</param>
        /// <param name="ownEvents">Events that belong to the requesting player.</param>
        public static AiSafeSnapshot Build(PlayerState own, VisibleWorld view, IReadOnlyList<GameEvent> ownEvents, float now, Phase phase, double epochSeconds)
        {
            if (own.PlayerId != view.ViewerId) throw new ArgumentException("player state and visible world belong to different players");
            var snap = new AiSafeSnapshot { phase = phase.ToString(), captured_at = epochSeconds };
            snap.resources = new SnapshotResources { wood = own.Wood, food = own.Food, gold = own.Gold };
            snap.population = new SnapshotPopulation { used = own.PopulationUsed, cap = own.PopulationCap };

            var researched = new List<string>(); foreach (var t in own.Researched) researched.Add(WireNames.Wire(t));
            var researching = new List<string>(); foreach (var t in own.Researching) researching.Add(WireNames.Wire(t));
            researched.Sort(StringComparer.Ordinal); researching.Sort(StringComparer.Ordinal);
            snap.technology = new SnapshotTechnology { age = own.Age, researched = researched.ToArray(), researching = researching.ToArray() };

            // Own army grouped by (unit type, coarse zone); own completed buildings by type.
            var army = new Dictionary<(UnitType, Zone), int>();
            var buildings = new Dictionary<BuildingType, int>();
            foreach (var e in view.OwnEntities)
            {
                if (e.Kind == EntityKind.Unit)
                {
                    var key = (e.Unit, ZoneMap.Classify(e.Pos, view.Anchor, view.Nodes));
                    army[key] = army.TryGetValue(key, out int n) ? n + 1 : 1;
                }
                else if (e.Kind == EntityKind.Building && e.Complete)
                    buildings[e.Building] = buildings.TryGetValue(e.Building, out int n) ? n + 1 : 1;
            }
            snap.own_army = ToGroups(army);
            var bl = new List<SnapshotBuildingGroup>();
            foreach (var kv in buildings) bl.Add(new SnapshotBuildingGroup { type = kv.Key.ToString(), count = kv.Value });
            bl.Sort((a, b) => string.CompareOrdinal(a.type, b.type));
            snap.own_buildings = bl.ToArray();

            // Visible enemy UNITS only (buildings are visible in-game but are not part of the AI contract).
            var enemies = new Dictionary<(UnitType, Zone), int>();
            foreach (var c in view.EnemyContacts)
            {
                if (c.Kind != EntityKind.Unit) continue;
                var key = (c.Unit, c.Zone);
                enemies[key] = enemies.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            snap.visible_enemies = ToGroups(enemies);

            var events = new List<SnapshotEvent>();
            for (int i = ownEvents.Count - 1; i >= 0 && events.Count < MaxEvents; i--)
            {
                var ev = ownEvents[i];
                events.Add(new SnapshotEvent
                {
                    kind = WireNames.Wire(ev.Kind),
                    seconds_ago = Math.Max(0, Math.Min(3600, (int)Math.Round(now - ev.Time))),
                    zone = ZoneMap.Classify(ev.Where, view.Anchor, view.Nodes).ToString(),
                    unit_type = ev.Unit.HasValue ? ev.Unit.Value.ToString() : null,
                    count = ev.Unit.HasValue ? Math.Max(1, ev.Count) : 0,
                });
            }
            snap.recent_events = events.ToArray();
            return snap;
        }

        static SnapshotUnitGroup[] ToGroups(Dictionary<(UnitType, Zone), int> map)
        {
            var list = new List<SnapshotUnitGroup>();
            foreach (var kv in map) list.Add(new SnapshotUnitGroup { type = kv.Key.Item1.ToString(), zone = kv.Key.Item2.ToString(), count = kv.Value });
            list.Sort((a, b) => { int c = string.CompareOrdinal(a.type, b.type); return c != 0 ? c : string.CompareOrdinal(a.zone, b.zone); });
            return list.ToArray();
        }
    }
}
