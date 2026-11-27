using System.Collections.Generic;

namespace Rts.Core
{
    /// <summary>What a viewer knows about one enemy: type and coarse zone only. No id, no position, no owner economy.</summary>
    public readonly struct EnemyContact
    {
        public readonly EntityKind Kind;
        public readonly UnitType Unit;
        public readonly BuildingType Building;
        public readonly Zone Zone;
        public EnemyContact(EntityKind kind, UnitType unit, BuildingType building, Zone zone)
        { Kind = kind; Unit = unit; Building = building; Zone = zone; }
    }

    /// <summary>
    /// The fog-of-war boundary. It can only be constructed by VisibilityService, and the AI snapshot
    /// builder accepts nothing but a VisibleWorld, so hidden enemy state cannot reach the AI by type design.
    /// </summary>
    public sealed class VisibleWorld
    {
        public int ViewerId { get; }
        public Vec2 Anchor { get; }
        public IReadOnlyList<Entity> OwnEntities { get; }
        public IReadOnlyList<EnemyContact> EnemyContacts { get; }
        /// <summary>Ids of enemy entities currently visible. For local rendering/targeting only, never for the AI.</summary>
        public IReadOnlyCollection<int> VisibleEnemyIds { get; }
        public IReadOnlyList<Entity> Nodes { get; }

        internal VisibleWorld(int viewer, Vec2 anchor, List<Entity> own, List<EnemyContact> contacts, HashSet<int> ids, List<Entity> nodes)
        { ViewerId = viewer; Anchor = anchor; OwnEntities = own; EnemyContacts = contacts; VisibleEnemyIds = ids; _ids = ids; Nodes = nodes; }

        public bool CanSee(int entityId) => _ids.Contains(entityId);
        readonly HashSet<int> _ids;
    }

    public static class VisibilityService
    {
        /// <summary>An enemy is visible only while within the vision radius of one of the viewer's own units or buildings.</summary>
        public static VisibleWorld FilterFor(int viewerId, IReadOnlyList<Entity> all)
        {
            var own = new List<Entity>();
            var nodes = new List<Entity>();
            var others = new List<Entity>();
            Vec2 anchor = default;
            bool haveAnchor = false;
            foreach (var e in all)
            {
                if (!e.Alive) continue;
                if (e.Kind == EntityKind.ResourceNode) { nodes.Add(e); continue; }
                if (e.OwnerId == viewerId)
                {
                    own.Add(e);
                    if (e.Kind == EntityKind.Building && e.Building == BuildingType.Citadel) { anchor = e.Pos; haveAnchor = true; }
                }
                else others.Add(e);
            }
            if (!haveAnchor && own.Count > 0) anchor = own[0].Pos;

            var contacts = new List<EnemyContact>();
            var ids = new HashSet<int>();
            foreach (var enemy in others)
            {
                bool seen = false;
                foreach (var src in own)
                {
                    float v = src.Vision;
                    float dx = enemy.Pos.X - src.Pos.X, dy = enemy.Pos.Y - src.Pos.Y;
                    if (dx * dx + dy * dy <= v * v) { seen = true; break; }
                }
                if (!seen) continue;
                ids.Add(enemy.Id);
                contacts.Add(new EnemyContact(enemy.Kind, enemy.Unit, enemy.Building, ZoneMap.Classify(enemy.Pos, anchor, nodes)));
            }
            return new VisibleWorld(viewerId, anchor, own, contacts, ids, nodes);
        }
    }
}
