using System.Collections.Generic;
using Rts.Core;
using UnityEngine;

namespace Rts.Game
{
    /// <summary>
    /// Mirrors simulation entities as primitive GameObjects (no colliders; picking is done in screen space).
    /// Fog of war on the view side: enemy entities that are not in the local player's VisibleWorld are not rendered.
    /// </summary>
    public sealed class EntityViewManager : MonoBehaviour
    {
        public GameContext Ctx;
        readonly Dictionary<int, GameObject> _views = new Dictionary<int, GameObject>();
        readonly List<int> _stale = new List<int>();
        public int ViewCount => _views.Count;
        public bool IsRendered(int entityId) => _views.TryGetValue(entityId, out var go) && go.activeSelf;

        static readonly Color P1 = new Color(0.2f, 0.45f, 1f), P2 = new Color(0.9f, 0.2f, 0.2f);

        void LateUpdate()
        {
            if (Ctx == null || Ctx.Sim == null) return;
            if (Ctx.LocalVisible == null) Ctx.RefreshVisibility();
            var vis = Ctx.LocalVisible;

            foreach (var e in Ctx.Sim.Entities)
            {
                if (!e.Alive) continue;
                if (!_views.TryGetValue(e.Id, out var go)) { go = Create(e); _views[e.Id] = go; }
                bool enemy = e.Kind != EntityKind.ResourceNode && e.OwnerId != Ctx.LocalPlayer;
                bool visible = !enemy || vis.CanSee(e.Id);
                if (go.activeSelf != visible) go.SetActive(visible);
                if (!visible) continue;
                Apply(e, go);
            }

            _stale.Clear();
            foreach (var kv in _views)
            {
                var e = Ctx.Sim.Get(kv.Key);
                if (e == null || (e.Kind == EntityKind.ResourceNode && e.Amount <= 0)) _stale.Add(kv.Key);
            }
            foreach (int id in _stale) { Destroy(_views[id]); _views.Remove(id); }
        }

        GameObject Create(Entity e)
        {
            PrimitiveType shape = PrimitiveType.Cube;
            if (e.Kind == EntityKind.Unit) shape = (e.Unit == UnitType.Raider || e.Unit == UnitType.Guardian || e.Unit == UnitType.Ram) ? PrimitiveType.Cube : PrimitiveType.Capsule;
            else if (e.Kind == EntityKind.ResourceNode) shape = e.NodeResource == ResourceType.Gold ? PrimitiveType.Sphere : PrimitiveType.Cylinder;
            var go = GameObject.CreatePrimitive(shape);
            go.name = e.Kind == EntityKind.Unit ? e.Unit + "#" + e.Id : e.Kind == EntityKind.Building ? e.Building + "#" + e.Id : e.NodeResource + "Node#" + e.Id;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            return go;
        }

        static Vector3 Size(Entity e)
        {
            if (e.Kind == EntityKind.Building)
                switch (e.Building)
                {
                    case BuildingType.Citadel: return new Vector3(9, 7, 9);
                    case BuildingType.House: return new Vector3(3.5f, 3, 3.5f);
                    default: return new Vector3(6, 4, 6);
                }
            if (e.Kind == EntityKind.ResourceNode) return e.NodeResource == ResourceType.Gold ? new Vector3(3, 3, 3) : new Vector3(3, 2.5f, 3);
            switch (e.Unit)
            {
                case UnitType.Worker: return new Vector3(0.9f, 0.8f, 0.9f);
                case UnitType.Spearman: return new Vector3(1f, 1.1f, 1f);
                case UnitType.Archer: return new Vector3(0.8f, 1f, 0.8f);
                case UnitType.Raider: return new Vector3(1.2f, 1.3f, 2.2f);
                case UnitType.Guardian: return new Vector3(1.7f, 1.8f, 1.7f);
                default: return new Vector3(2.2f, 1.4f, 3.6f);   // Ram
            }
        }

        void Apply(Entity e, GameObject go)
        {
            var size = Size(e);
            float growth = 1f;
            if (e.Kind == EntityKind.Building && !e.Complete) growth = Mathf.Lerp(0.25f, 1f, e.BuildProgress / Mathf.Max(0.1f, GameData.Buildings[e.Building].BuildTimeS));
            if (e.Kind == EntityKind.ResourceNode) growth = Mathf.Clamp(0.4f + e.Amount / 1500f, 0.4f, 1f);
            var t = go.transform;
            float height = size.y * growth;
            bool capsuleOrCyl = go.name.StartsWith("Worker") || go.name.StartsWith("Spearman") || go.name.StartsWith("Archer");
            t.localScale = capsuleOrCyl ? new Vector3(size.x, size.y, size.z) : new Vector3(size.x, height, size.z);
            t.position = new Vector3(e.Pos.X, height * (capsuleOrCyl ? 1f : 0.5f), e.Pos.Y);

            // face movement/target direction for units
            if (e.Kind == EntityKind.Unit)
            {
                Vec2 target = e.Order == OrderType.Move ? e.MoveTarget : default;
                if (e.Order == OrderType.Attack) { var tg = Ctx.Sim.Get(e.TargetId); if (tg != null) target = tg.Pos; }
                var d = new Vector3(target.X - e.Pos.X, 0, target.Y - e.Pos.Y);
                if ((e.Order == OrderType.Move || e.Order == OrderType.Attack) && d.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(d);
            }

            var r = go.GetComponent<Renderer>();
            Color c;
            if (e.Kind == EntityKind.ResourceNode) c = e.NodeResource == ResourceType.Wood ? new Color(0.1f, 0.5f, 0.15f) : e.NodeResource == ResourceType.Food ? new Color(0.95f, 0.6f, 0.15f) : new Color(1f, 0.85f, 0.1f);
            else
            {
                c = e.OwnerId == 1 ? P1 : P2;
                if (e.Kind == EntityKind.Building) c = Color.Lerp(c, Color.gray, 0.35f);
                if (e.Kind == EntityKind.Building && !e.Complete) c = Color.Lerp(c, Color.white, 0.5f);
                if (e.Kind == EntityKind.Unit && e.Unit == UnitType.Worker) c = Color.Lerp(c, Color.white, 0.45f);
            }
            if (Ctx.Selection.Contains(e.Id)) c = Color.Lerp(c, Color.white, 0.6f);
            r.material.color = c;
        }
    }
}
