using System;
using System.Collections.Generic;

namespace Rts.Core.Net
{
    public sealed class ReplicatedPlayer
    {
        public int Id, Wood, Food, Gold, Age, PopulationUsed, ResearchedMask, ResearchingMask;
        public int[] Completed = new int[Enum.GetValues(typeof(BuildingType)).Length];
    }

    public sealed class ReplicatedEntity
    {
        public int Id, Owner;
        public EntityKind Kind;
        public int Sub;                         // UnitType / BuildingType / ResourceType index, depending on Kind
        public float X, Y, Hp, MaxHp;
        public bool Complete = true;
        public bool Full;                       // own entity: orders, queues, carry. Enemies carry no such detail.
        public int Order, TargetId, Carry, CarryType;
        public float MoveX, MoveY, BuildProgress, ResearchRemaining;
        public int ResearchTech = -1;
        public List<KeyValuePair<int, float>> Queue = new List<KeyValuePair<int, float>>();
        public int Amount;                      // resource nodes
    }

    /// <summary>
    /// One player's fog-filtered view of the match as sent by the host. It contains the receiving player's own
    /// economy and entities, enemy entities that player can currently SEE (type, position, health only), and
    /// resource nodes the player knows about. The opponent's economy, hidden units and orders are never in it.
    /// </summary>
    public sealed class ReplicatedState
    {
        public float Time;
        public bool Ended;
        public int Winner;
        public Phase Phase;
        public ReplicatedPlayer Me = new ReplicatedPlayer();
        public List<ReplicatedEntity> Entities = new List<ReplicatedEntity>();
    }

    public static class StateCodec
    {
        public const int MaxEntities = 2048;
        public const int MaxQueue = 40;
        public const float NodeKnownBaseRadius = 30f;
        public const byte Version = 1;

        // ------------------------------------------------------------------ capture (host)
        public static ReplicatedState Capture(MatchSimulation sim, int playerId)
        {
            var view = sim.GetVisible(playerId);
            var me = sim.Players[playerId];
            var s = new ReplicatedState { Time = sim.Time, Ended = sim.Result.Ended, Winner = sim.Result.WinnerId, Phase = sim.ComputePhase(playerId) };
            s.Me = new ReplicatedPlayer { Id = playerId, Wood = me.Wood, Food = me.Food, Gold = me.Gold, Age = me.Age, PopulationUsed = me.PopulationUsed };
            foreach (var t in me.Researched) s.Me.ResearchedMask |= 1 << (int)t;
            foreach (var t in me.Researching) s.Me.ResearchingMask |= 1 << (int)t;
            foreach (var kv in me.Completed) s.Me.Completed[(int)kv.Key] = kv.Value;

            foreach (var e in view.OwnEntities) s.Entities.Add(ToRecord(e, true));
            foreach (int id in view.VisibleEnemyIds)
            {
                var e = sim.Get(id);
                if (e != null) s.Entities.Add(ToRecord(e, false));
            }
            foreach (var n in view.Nodes)
                if (n.Alive && NodeKnown(n, view)) s.Entities.Add(ToRecord(n, true));
            return s;
        }

        /// <summary>A node is replicated when it is near the player's own base or inside the vision of an own entity.</summary>
        internal static bool NodeKnown(Entity node, VisibleWorld view)
        {
            if (Vec2.Distance(node.Pos, view.Anchor) <= NodeKnownBaseRadius) return true;
            foreach (var o in view.OwnEntities)
                if (Vec2.Distance(node.Pos, o.Pos) <= o.Vision) return true;
            return false;
        }

        static ReplicatedEntity ToRecord(Entity e, bool full)
        {
            var r = new ReplicatedEntity { Id = e.Id, Owner = e.OwnerId, Kind = e.Kind, X = e.Pos.X, Y = e.Pos.Y, Hp = e.Hp, MaxHp = e.MaxHp, Complete = e.Complete, Full = full };
            switch (e.Kind)
            {
                case EntityKind.Unit: r.Sub = (int)e.Unit; break;
                case EntityKind.Building: r.Sub = (int)e.Building; break;
                default: r.Sub = (int)e.NodeResource; r.Amount = e.Amount; break;
            }
            if (!full) return r;
            if (e.Kind == EntityKind.Unit)
            {
                r.Order = (int)e.Order; r.MoveX = e.MoveTarget.X; r.MoveY = e.MoveTarget.Y; r.TargetId = e.TargetId;
                r.Carry = e.Carry; r.CarryType = (int)e.CarryType;
            }
            else if (e.Kind == EntityKind.Building)
            {
                r.BuildProgress = e.BuildProgress;
                foreach (var q in e.Queue) { if (r.Queue.Count >= MaxQueue) break; r.Queue.Add(new KeyValuePair<int, float>((int)q.Unit, q.Remaining)); }
                if (e.ResearchTech.HasValue) { r.ResearchTech = (int)e.ResearchTech.Value; r.ResearchRemaining = e.ResearchRemaining; }
            }
            return r;
        }

        // ------------------------------------------------------------------ binary format
        public static byte[] Encode(ReplicatedState s)
        {
            var w = new ByteWriter().U8(Version).F32(s.Time).Bool(s.Ended).U8(s.Winner).U8((int)s.Phase);
            var m = s.Me;
            w.U8(m.Id).I32(m.Wood).I32(m.Food).I32(m.Gold).U8(m.Age).I32(m.PopulationUsed).U8(m.ResearchedMask).U8(m.ResearchingMask);
            for (int i = 0; i < m.Completed.Length; i++) w.U8(Math.Min(255, m.Completed[i]));
            w.U16(s.Entities.Count);
            foreach (var e in s.Entities)
            {
                w.I32(e.Id).U8(e.Owner).U8((int)e.Kind).U8(e.Sub).F32(e.X).F32(e.Y).F32(e.Hp).F32(e.MaxHp);
                w.U8((e.Complete ? 1 : 0) | (e.Full ? 2 : 0));
                if (e.Kind == EntityKind.ResourceNode) { w.I32(e.Amount); continue; }
                if (!e.Full) continue;
                if (e.Kind == EntityKind.Unit) w.U8(e.Order).F32(e.MoveX).F32(e.MoveY).I32(e.TargetId).U8(e.Carry).U8(e.CarryType);
                else
                {
                    w.F32(e.BuildProgress).U8(e.Queue.Count);
                    foreach (var q in e.Queue) w.U8(q.Key).F32(q.Value);
                    w.U8(e.ResearchTech < 0 ? 255 : e.ResearchTech).F32(e.ResearchRemaining);
                }
            }
            return w.ToArray();
        }

        /// <summary>Decodes untrusted bytes. Never throws: malformed input returns false with an error.</summary>
        public static bool TryDecode(byte[] data, out ReplicatedState state, out string error)
        {
            state = null; error = null;
            try
            {
                var r = new ByteReader(data);
                if (r.U8() != Version) throw new FormatException("unsupported version");
                var s = new ReplicatedState { Time = r.F32(), Ended = r.Bool(), Winner = r.U8() };
                s.Phase = Enum<Phase>(r.U8());
                var m = s.Me;
                m.Id = r.U8(); m.Wood = r.I32(); m.Food = r.I32(); m.Gold = r.I32(); m.Age = r.U8(); m.PopulationUsed = r.I32();
                m.ResearchedMask = r.U8(); m.ResearchingMask = r.U8();
                if (m.Age < 1 || m.Age > 2) throw new FormatException("bad age");
                for (int i = 0; i < m.Completed.Length; i++) m.Completed[i] = r.U8();
                int count = r.U16();
                if (count > MaxEntities) throw new FormatException("too many entities");
                for (int i = 0; i < count; i++)
                {
                    var e = new ReplicatedEntity { Id = r.I32(), Owner = r.U8() };
                    e.Kind = Enum<EntityKind>(r.U8());
                    e.Sub = r.U8();
                    e.X = r.F32(); e.Y = r.F32(); e.Hp = r.F32(); e.MaxHp = r.F32();
                    int flags = r.U8();
                    e.Complete = (flags & 1) != 0; e.Full = (flags & 2) != 0;
                    switch (e.Kind)
                    {
                        case EntityKind.Unit: Enum<UnitType>(e.Sub); break;
                        case EntityKind.Building: Enum<BuildingType>(e.Sub); break;
                        default: Enum<ResourceType>(e.Sub); break;
                    }
                    if (e.Kind == EntityKind.ResourceNode) { e.Amount = r.I32(); }
                    else if (e.Full)
                    {
                        if (e.Kind == EntityKind.Unit)
                        {
                            e.Order = (int)Enum<OrderType>(r.U8()); e.MoveX = r.F32(); e.MoveY = r.F32(); e.TargetId = r.I32();
                            e.Carry = r.U8(); e.CarryType = (int)Enum<ResourceType>(r.U8());
                        }
                        else
                        {
                            e.BuildProgress = r.F32();
                            int q = r.U8();
                            if (q > MaxQueue) throw new FormatException("queue too long");
                            for (int k = 0; k < q; k++) { int u = (int)Enum<UnitType>(r.U8()); e.Queue.Add(new KeyValuePair<int, float>(u, r.F32())); }
                            int rt = r.U8();
                            if (rt != 255) e.ResearchTech = (int)Enum<TechId>(rt);
                            e.ResearchRemaining = r.F32();
                        }
                    }
                    s.Entities.Add(e);
                }
                r.ExpectEnd();
                state = s;
                return true;
            }
            catch (FormatException ex) { error = ex.Message; return false; }
        }

        static T Enum<T>(int v) where T : struct, IConvertible
        {
            if (!System.Enum.IsDefined(typeof(T), v)) throw new FormatException("invalid " + typeof(T).Name);
            return (T)System.Enum.ToObject(typeof(T), v);
        }
    }

}

namespace Rts.Core
{
    using Rts.Core.Net;

    /// <summary>Client-side replica support. A replica never simulates (Tick is a no-op) and rejects local commands;
    /// it only mirrors the fog-filtered state the host sends.</summary>
    public sealed partial class MatchSimulation
    {
        public bool IsReplica { get; private set; }
        public Phase ReplicaPhase { get; private set; }

        public void MarkReplica() { IsReplica = true; }

        public void ApplyReplicatedState(ReplicatedState s)
        {
            if (!IsReplica) throw new InvalidOperationException("not a replica");
            Time = s.Time;
            Result = new MatchResult { Ended = s.Ended, WinnerId = s.Winner };
            ReplicaPhase = s.Phase;

            // The local player's own economy. The opponent's PlayerState stays empty: it is hidden information.
            var m = s.Me;
            if (!Players.TryGetValue(m.Id, out var p)) { p = new PlayerState(m.Id); Players[m.Id] = p; }
            p.Wood = m.Wood; p.Food = m.Food; p.Gold = m.Gold; p.Age = m.Age; p.PopulationUsed = m.PopulationUsed;
            p.Researched.Clear(); p.Researching.Clear(); p.Completed.Clear();
            foreach (TechId t in Enum.GetValues(typeof(TechId)))
            {
                if ((m.ResearchedMask & (1 << (int)t)) != 0) p.Researched.Add(t);
                if ((m.ResearchingMask & (1 << (int)t)) != 0) p.Researching.Add(t);
            }
            for (int i = 0; i < m.Completed.Length; i++) if (m.Completed[i] > 0) p.Completed[(BuildingType)i] = m.Completed[i];

            var seen = new HashSet<int>();
            foreach (var re in s.Entities)
            {
                seen.Add(re.Id);
                if (re.Owner != 0 && !Players.ContainsKey(re.Owner)) Players[re.Owner] = new PlayerState(re.Owner);
                if (!_byId.TryGetValue(re.Id, out var e))
                {
                    e = new Entity { Id = re.Id };
                    Entities.Add(e);
                    _byId[re.Id] = e;
                    if (re.Id >= _nextId) _nextId = re.Id + 1;
                }
                e.OwnerId = re.Owner; e.Kind = re.Kind; e.Alive = true;
                e.Pos = new Vec2(re.X, re.Y); e.Hp = re.Hp; e.MaxHp = re.MaxHp; e.Complete = re.Complete;
                switch (re.Kind)
                {
                    case EntityKind.Unit:
                        e.Unit = (UnitType)re.Sub;
                        if (re.Full)
                        {
                            e.Order = (OrderType)re.Order; e.MoveTarget = new Vec2(re.MoveX, re.MoveY); e.TargetId = re.TargetId;
                            e.Carry = re.Carry; e.CarryType = (ResourceType)re.CarryType;
                        }
                        else { e.Order = OrderType.None; e.Carry = 0; }
                        break;
                    case EntityKind.Building:
                        e.Building = (BuildingType)re.Sub;
                        e.Queue.Clear();
                        e.ResearchTech = null;
                        if (re.Full)
                        {
                            e.BuildProgress = re.BuildProgress;
                            foreach (var q in re.Queue) e.Queue.Add(new ProductionItem { Unit = (UnitType)q.Key, Remaining = q.Value });
                            if (re.ResearchTech >= 0) { e.ResearchTech = (TechId)re.ResearchTech; e.ResearchRemaining = re.ResearchRemaining; }
                        }
                        break;
                    default:
                        e.NodeResource = (ResourceType)re.Sub; e.Amount = re.Amount;
                        break;
                }
            }

            // Units/buildings that are no longer in the message are gone or have left vision.
            for (int i = Entities.Count - 1; i >= 0; i--)
            {
                var e = Entities[i];
                if (e.Kind == EntityKind.ResourceNode || seen.Contains(e.Id)) continue;
                e.Alive = false; _byId.Remove(e.Id); Entities.RemoveAt(i);
            }

            // Resource nodes are map knowledge and persist. A node is dropped when the host reports it empty, or when it
            // lies in an area the player can see (own base / vision) yet the host did not send it (it was depleted).
            var view = VisibilityService.FilterFor(m.Id, Entities);
            for (int i = Entities.Count - 1; i >= 0; i--)
            {
                var e = Entities[i];
                if (e.Kind != EntityKind.ResourceNode) continue;
                bool drop = seen.Contains(e.Id) ? e.Amount <= 0 : StateCodec.NodeKnown(e, view);
                if (!drop) continue;
                e.Alive = false; _byId.Remove(e.Id); Entities.RemoveAt(i);
            }
        }
    }
}
