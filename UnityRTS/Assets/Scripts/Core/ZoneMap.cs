using System;
using System.Collections.Generic;

namespace Rts.Core
{
    /// <summary>
    /// Converts exact positions into coarse, player-relative zones (the only location detail
    /// that ever reaches the AI). Zones are relative to the viewer's own Citadel:
    /// BaseCenter within 25 units, resource zones near own-side nodes, otherwise a compass gate.
    /// </summary>
    public static class ZoneMap
    {
        public const float BaseRadius = 25f;
        public const float ResourceZoneRadius = 14f;
        public const float GateDistance = 35f;

        public static Zone Classify(Vec2 pos, Vec2 anchor, IReadOnlyList<Entity> nodes)
        {
            if (Vec2.Distance(pos, anchor) <= BaseRadius)
            {
                foreach (var n in nodes)
                {
                    if (!n.Alive || Vec2.Distance(n.Pos, anchor) > BaseRadius + 10) continue;
                    if (Vec2.Distance(n.Pos, pos) <= ResourceZoneRadius * 0.5f) return ZoneOf(n.NodeResource);
                }
                return Zone.BaseCenter;
            }
            var d = pos - anchor;
            if (Math.Abs(d.Y) >= Math.Abs(d.X)) return d.Y >= 0 ? Zone.NorthGate : Zone.SouthGate;
            return d.X >= 0 ? Zone.EastGate : Zone.WestGate;
        }

        public static Zone ZoneOf(ResourceType r) =>
            r == ResourceType.Wood ? Zone.Forest : r == ResourceType.Food ? Zone.FarmArea : Zone.GoldMine;

        /// <summary>World position a MOVE_GROUP to this zone should target.</summary>
        public static Vec2 Center(Zone z, Vec2 anchor, IReadOnlyList<Entity> nodes)
        {
            switch (z)
            {
                case Zone.BaseCenter: return anchor;
                case Zone.NorthGate: return anchor + new Vec2(0, GateDistance);
                case Zone.SouthGate: return anchor + new Vec2(0, -GateDistance);
                case Zone.EastGate: return anchor + new Vec2(GateDistance, 0);
                case Zone.WestGate: return anchor + new Vec2(-GateDistance, 0);
            }
            ResourceType res = z == Zone.Forest ? ResourceType.Wood : z == Zone.FarmArea ? ResourceType.Food : ResourceType.Gold;
            Entity best = null;
            foreach (var n in nodes)
                if (n.Alive && n.NodeResource == res && (best == null || Vec2.Distance(n.Pos, anchor) < Vec2.Distance(best.Pos, anchor)))
                    best = n;
            return best != null ? best.Pos + (anchor - best.Pos) * 0.15f : anchor;
        }
    }

    public static class Formation
    {
        /// <summary>Centered grid offsets so a group arrives as a block instead of stacking on one point.</summary>
        public static Vec2[] Offsets(int count, float spacing = 2f)
        {
            var result = new Vec2[Math.Max(0, count)];
            if (count <= 0) return result;
            int cols = (int)Math.Ceiling(Math.Sqrt(count));
            int rows = (int)Math.Ceiling(count / (double)cols);
            for (int i = 0; i < count; i++)
            {
                int r = i / cols, c = i % cols;
                result[i] = new Vec2((c - (cols - 1) / 2f) * spacing, (r - (rows - 1) / 2f) * spacing);
            }
            return result;
        }
    }
}
