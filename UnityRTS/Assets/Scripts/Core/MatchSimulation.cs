using System;
using System.Collections.Generic;

namespace Rts.Core
{
    /// <summary>
    /// Authoritative match logic (pure C#, no Unity types). Player UI commands and approved AI plans
    /// both go through the same TryTrain / TryBuild / TryResearch / Order* methods. With Photon Fusion
    /// (Host Mode) this runs on the host, which is the single state authority; clients send requests.
    /// </summary>
    public sealed partial class MatchSimulation
    {
        public const float MapHalf = 100f;
        public const float BuildingSpacing = 7f;
        public const float EventWindowS = 120f;

        public readonly Dictionary<int, PlayerState> Players = new Dictionary<int, PlayerState>();
        public readonly List<Entity> Entities = new List<Entity>();
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public float Time { get; private set; }
        public MatchResult Result { get; private set; }

        readonly Dictionary<int, Entity> _byId = new Dictionary<int, Entity>();
        readonly Dictionary<int, float> _lastCombat = new Dictionary<int, float>();
        readonly Dictionary<int, float> _lastUnderAttackEvent = new Dictionary<int, float>();
        readonly Dictionary<int, VisibleWorld> _vis = new Dictionary<int, VisibleWorld>();
        int _nextId = 1;
        int _spawnCounter;

        // ------------------------------------------------------------------ setup
        public PlayerState AddPlayer(int id)
        {
            var p = new PlayerState(id) { Wood = GameData.StartingResources.Wood, Food = GameData.StartingResources.Food, Gold = GameData.StartingResources.Gold };
            Players[id] = p;
            return p;
        }

        public Entity Get(int id) => _byId.TryGetValue(id, out var e) && e.Alive ? e : null;

        Entity Add(Entity e)
        {
            e.Id = _nextId++;
            Entities.Add(e);
            _byId[e.Id] = e;
            return e;
        }

        /// <summary>Spawns a unit directly (setup/debug). Counts toward population.</summary>
        public Entity SpawnUnit(int owner, UnitType type, Vec2 pos)
        {
            Players[owner].PopulationUsed += GameData.Units[type].Pop;
            return SpawnUnitRaw(owner, type, pos);
        }

        Entity SpawnUnitRaw(int owner, UnitType type, Vec2 pos)
        {
            var d = GameData.Units[type];
            return Add(new Entity { OwnerId = owner, Kind = EntityKind.Unit, Unit = type, Pos = pos, Hp = d.Hp, MaxHp = d.Hp });
        }

        public Entity SpawnBuilding(int owner, BuildingType type, Vec2 pos, bool complete = true)
        {
            var d = GameData.Buildings[type];
            var e = Add(new Entity { OwnerId = owner, Kind = EntityKind.Building, Building = type, Pos = pos, MaxHp = d.Hp, Complete = complete });
            e.Hp = complete ? d.Hp : d.Hp * 0.1f;
            if (complete) MarkCompleted(owner, type, +1);
            return e;
        }

        public Entity SpawnNode(ResourceType res, Vec2 pos, int amount) =>
            Add(new Entity { OwnerId = 0, Kind = EntityKind.ResourceNode, NodeResource = res, Pos = pos, Amount = amount, Hp = 1, MaxHp = 1 });

        void MarkCompleted(int owner, BuildingType type, int delta)
        {
            var c = Players[owner].Completed;
            c.TryGetValue(type, out int n);
            c[type] = Math.Max(0, n + delta);
        }

        // ------------------------------------------------------------------ queries
        public VisibleWorld GetVisible(int playerId) => VisibilityService.FilterFor(playerId, Entities);

        public Entity FindCitadel(int playerId)
        {
            foreach (var e in Entities)
                if (e.Alive && e.OwnerId == playerId && e.Kind == EntityKind.Building && e.Building == BuildingType.Citadel) return e;
            return null;
        }

        public Phase ComputePhase(int playerId)
        {
            if (IsReplica) return ReplicaPhase;   // clients show the phase the host computed
            var p = Players[playerId];
            var vis = GetVisible(playerId);
            bool recentAttack = false;
            foreach (var ev in Events)
                if (ev.PlayerId == playerId && ev.Kind == EventKind.UnderAttack && Time - ev.Time < 10f) { recentAttack = true; break; }
            bool enemyNearBase = false;   // visible enemy military within 45 units of our Citadel
            foreach (int id in vis.VisibleEnemyIds)
            {
                var e = Get(id);
                if (e != null && e.IsMilitaryUnit && Vec2.Distance(e.Pos, vis.Anchor) <= 45f) { enemyNearBase = true; break; }
            }
            if (recentAttack || enemyNearBase) return Phase.UnderAttack;
            if (_lastCombat.TryGetValue(playerId, out float t) && Time - t < 15f) return Phase.Skirmish;
            if (p.Age >= 2) return Phase.LateGame;
            bool military = p.HasBuilding(BuildingType.Barracks) || p.HasBuilding(BuildingType.ArcheryYard) || p.HasBuilding(BuildingType.Stable);
            foreach (var e in vis.OwnEntities) if (e.IsMilitaryUnit) { military = true; break; }
            return military ? Phase.BuildUp : Phase.EarlyEconomy;
        }

        public AiSafeSnapshot BuildSnapshot(int playerId, double epochSeconds)
        {
            var view = GetVisible(playerId);
            var mine = new List<GameEvent>();
            foreach (var ev in Events)
                if (ev.PlayerId == playerId && Time - ev.Time <= EventWindowS) mine.Add(ev);
            return AiSnapshotBuilder.Build(Players[playerId], view, mine, Time, ComputePhase(playerId), epochSeconds);
        }

        // ------------------------------------------------------------------ commands (authoritative)
        CommandResult Guard(int playerId)
        {
            if (IsReplica) return CommandResult.Fail("client replica: commands are sent to the host");
            if (Result.Ended) return CommandResult.Fail("match has ended");
            if (!Players.ContainsKey(playerId)) return CommandResult.Fail("unknown player");
            return CommandResult.Success;
        }

        public CommandResult TryTrain(int playerId, UnitType unit, int count, int buildingId = 0)
        {
            var g = Guard(playerId); if (!g.Ok) return g;
            if (count < 1 || count > 20) return CommandResult.Fail("count must be 1..20");
            var p = Players[playerId];
            var d = GameData.Units[unit];
            if (d.RequiredAge > p.Age) return CommandResult.Fail($"{unit} requires Age {d.RequiredAge}");

            Entity site = null;
            foreach (var e in Entities)
            {
                if (!e.Alive || e.OwnerId != playerId || e.Kind != EntityKind.Building || !e.Complete || e.Building != d.TrainedAt) continue;
                if (buildingId != 0 && e.Id != buildingId) continue;
                if (site == null || e.Queue.Count < site.Queue.Count) site = e;
            }
            if (site == null) return CommandResult.Fail($"requires a completed {d.TrainedAt}");

            var cost = d.Cost * count;
            if (!p.CanAfford(cost)) return CommandResult.Fail($"insufficient resources (need {cost})");
            int pop = d.Pop * count;
            if (p.PopulationUsed + pop > p.PopulationCap) return CommandResult.Fail($"population cap ({p.PopulationUsed}+{pop} > {p.PopulationCap})");

            p.Spend(cost);
            p.PopulationUsed += pop;
            for (int i = 0; i < count; i++) site.Queue.Add(new ProductionItem { Unit = unit, Remaining = d.TrainTimeS });
            return CommandResult.Success;
        }

        public CommandResult TryBuild(int playerId, BuildingType type, Vec2 pos, int builderId = 0)
        {
            var g = Guard(playerId); if (!g.Ok) return g;
            var p = Players[playerId];
            var d = GameData.Buildings[type];
            if (!d.Buildable) return CommandResult.Fail($"{type} cannot be built");
            if (d.RequiredAge > p.Age) return CommandResult.Fail($"{type} requires Age {d.RequiredAge}");
            if (!p.CanAfford(d.Cost)) return CommandResult.Fail($"insufficient resources (need {d.Cost})");
            if (!IsSiteFree(pos)) return CommandResult.Fail("invalid build site");
            p.Spend(d.Cost);
            SpawnBuilding(playerId, type, pos, complete: false);
            if (builderId != 0)
            {
                var w = Get(builderId);
                if (w != null && w.OwnerId == playerId && w.Kind == EntityKind.Unit && w.Unit == UnitType.Worker) OrderMoveOne(w, pos);
            }
            return CommandResult.Success;
        }

        public bool IsSiteFree(Vec2 pos)
        {
            if (Math.Abs(pos.X) > MapHalf - 5 || Math.Abs(pos.Y) > MapHalf - 5) return false;
            foreach (var e in Entities)
            {
                if (!e.Alive) continue;
                float min = e.Kind == EntityKind.Building ? BuildingSpacing : e.Kind == EntityKind.ResourceNode ? 4f : 0f;
                if (min > 0 && Vec2.Distance(e.Pos, pos) < min) return false;
            }
            return true;
        }

        /// <summary>Finds a free spot around the player's Citadel (ring search). Null if none.</summary>
        public Vec2? FindBuildSite(int playerId)
        {
            var c = FindCitadel(playerId);
            if (c == null) return null;
            for (float r = 12f; r <= 45f; r += 4f)
                for (int i = 0; i < 16; i++)
                {
                    double a = i * Math.PI / 8 + r * 0.37;
                    var pos = c.Pos + new Vec2((float)Math.Cos(a) * r, (float)Math.Sin(a) * r);
                    if (IsSiteFree(pos)) return pos;
                }
            return null;
        }

        public CommandResult TryResearch(int playerId, TechId tech)
        {
            var g = Guard(playerId); if (!g.Ok) return g;
            var p = Players[playerId];
            var d = GameData.Techs[tech];
            if (p.Has(tech) || p.Researching.Contains(tech)) return CommandResult.Fail("already researched or in progress");
            if (d.RequiresAge2 && p.Age < 2) return CommandResult.Fail("requires Age II");
            foreach (var b in d.RequiresAll) if (!p.HasBuilding(b)) return CommandResult.Fail($"requires {b}");
            if (d.RequiresAny.Length > 0)
            {
                bool any = false;
                foreach (var b in d.RequiresAny) any |= p.HasBuilding(b);
                if (!any) return CommandResult.Fail("requires " + string.Join(" or ", d.RequiresAny));
            }
            Entity site = null;
            foreach (var e in Entities)
                if (e.Alive && e.OwnerId == playerId && e.Kind == EntityKind.Building && e.Complete && e.Building == d.ResearchedAt && !e.ResearchTech.HasValue) { site = e; break; }
            if (site == null) return CommandResult.Fail($"requires a free {d.ResearchedAt}");
            if (!p.CanAfford(d.Cost)) return CommandResult.Fail($"insufficient resources (need {d.Cost})");
            p.Spend(d.Cost);
            p.Researching.Add(tech);
            site.ResearchTech = tech;
            site.ResearchRemaining = d.ResearchTimeS;
            return CommandResult.Success;
        }

        IEnumerable<Entity> OwnedUnits(int playerId, IEnumerable<int> ids)
        {
            foreach (int id in ids)
            {
                var e = Get(id);
                if (e != null && e.OwnerId == playerId && e.Kind == EntityKind.Unit) yield return e;
            }
        }

        void OrderMoveOne(Entity u, Vec2 target)
        {
            u.Order = OrderType.Move;
            u.MoveTarget = Clamp(target);
            u.TargetId = 0;
        }

        /// <summary>Move a group; units spread into a grid formation around the target.</summary>
        public CommandResult OrderMove(int playerId, IEnumerable<int> ids, Vec2 target)
        {
            var g = Guard(playerId); if (!g.Ok) return g;
            var units = new List<Entity>(OwnedUnits(playerId, ids));
            if (units.Count == 0) return CommandResult.Fail("no controllable units");
            var offsets = Formation.Offsets(units.Count);
            for (int i = 0; i < units.Count; i++) OrderMoveOne(units[i], target + offsets[i]);
            return CommandResult.Success;
        }

        /// <summary>Attack command. Fog of war applies: the target must be currently visible to the commanding player.</summary>
        public CommandResult OrderAttack(int playerId, IEnumerable<int> ids, int targetId)
        {
            var g = Guard(playerId); if (!g.Ok) return g;
            var t = Get(targetId);
            if (t == null || t.Kind == EntityKind.ResourceNode || t.OwnerId == playerId || t.OwnerId == 0) return CommandResult.Fail("invalid target");
            if (!GetVisible(playerId).CanSee(targetId)) return CommandResult.Fail("target not visible");
            int n = 0;
            foreach (var u in OwnedUnits(playerId, ids)) { u.Order = OrderType.Attack; u.TargetId = targetId; u.AutoTargeted = false; n++; }
            return n > 0 ? CommandResult.Success : CommandResult.Fail("no controllable units");
        }

        public CommandResult OrderGather(int playerId, IEnumerable<int> ids, int nodeId)
        {
            var g = Guard(playerId); if (!g.Ok) return g;
            var node = Get(nodeId);
            if (node == null || node.Kind != EntityKind.ResourceNode) return CommandResult.Fail("invalid resource node");
            int n = 0;
            foreach (var u in OwnedUnits(playerId, ids))
            {
                if (u.Unit != UnitType.Worker) continue;
                if (u.Carry > 0 && u.CarryType != node.NodeResource) u.Carry = 0;
                u.CarryType = node.NodeResource;
                u.Order = OrderType.Gather; u.GatherNodeId = nodeId; u.GatherState = u.Carry >= GameData.CarryCapacity ? GatherPhase.ToDropoff : GatherPhase.ToNode;
                n++;
            }
            return n > 0 ? CommandResult.Success : CommandResult.Fail("only Workers can gather");
        }

        public void OrderStop(int playerId, IEnumerable<int> ids)
        {
            if (IsReplica) return;
            foreach (var u in OwnedUnits(playerId, ids)) { u.Order = OrderType.None; u.TargetId = 0; }
        }

        static Vec2 Clamp(Vec2 p) => new Vec2(Math.Max(-MapHalf, Math.Min(MapHalf, p.X)), Math.Max(-MapHalf, Math.Min(MapHalf, p.Y)));

        // ------------------------------------------------------------------ simulation step
        public void Tick(float dt)
        {
            if (IsReplica || Result.Ended) return;   // replicas never simulate; they only apply host state
            Time += dt;
            _vis.Clear();
            foreach (int id in Players.Keys) _vis[id] = VisibilityService.FilterFor(id, Entities);

            int count = Entities.Count;   // entities spawned this tick start acting next tick
            for (int i = 0; i < count; i++)
            {
                var e = Entities[i];
                if (!e.Alive) continue;
                if (e.Kind == EntityKind.Building) UpdateBuilding(e, dt);
                else if (e.Kind == EntityKind.Unit) UpdateUnit(e, dt);
            }
            Entities.RemoveAll(e => !e.Alive && e.Kind != EntityKind.ResourceNode || e.Kind == EntityKind.ResourceNode && e.Amount <= 0);
            foreach (var key in new List<int>(_byId.Keys)) if (!_byId[key].Alive || (_byId[key].Kind == EntityKind.ResourceNode && _byId[key].Amount <= 0)) _byId.Remove(key);
            if (Events.Count > 300) Events.RemoveRange(0, Events.Count - 300);
        }

        void Log(int playerId, EventKind kind, Vec2 where, UnitType? unit = null, int count = 1) =>
            Events.Add(new GameEvent { Time = Time, PlayerId = playerId, Kind = kind, Where = where, Unit = unit, Count = count });

        void UpdateBuilding(Entity b, float dt)
        {
            var owner = Players[b.OwnerId];
            var def = GameData.Buildings[b.Building];
            if (!b.Complete)
            {
                bool workerNear = false;
                foreach (var u in Entities)
                    if (u.Alive && u.OwnerId == b.OwnerId && u.Kind == EntityKind.Unit && u.Unit == UnitType.Worker && Vec2.Distance(u.Pos, b.Pos) <= GameData.BuildRange) { workerNear = true; break; }
                if (!workerNear) return;
                b.BuildProgress += dt;
                b.Hp = Math.Min(b.MaxHp, b.Hp + b.MaxHp * 0.9f * dt / def.BuildTimeS);
                if (b.BuildProgress >= def.BuildTimeS)
                {
                    b.Complete = true;
                    MarkCompleted(b.OwnerId, b.Building, +1);
                    Log(b.OwnerId, EventKind.BuildingCompleted, b.Pos);
                }
                return;
            }
            if (b.Queue.Count > 0)
            {
                var item = b.Queue[0];
                item.Remaining -= dt;
                if (item.Remaining <= 0)
                {
                    b.Queue.RemoveAt(0);
                    double a = _spawnCounter++ * 0.9;
                    var pos = b.Pos + new Vec2((float)Math.Cos(a) * 5f, (float)Math.Sin(a) * 5f - 1f);
                    SpawnUnitRaw(b.OwnerId, item.Unit, Clamp(pos));   // population was reserved when queued
                    Log(b.OwnerId, EventKind.UnitTrained, b.Pos, item.Unit);
                }
            }
            if (b.ResearchTech.HasValue)
            {
                b.ResearchRemaining -= dt;
                if (b.ResearchRemaining <= 0)
                {
                    var t = b.ResearchTech.Value;
                    owner.Researching.Remove(t);
                    owner.Researched.Add(t);
                    if (t == TechId.Age2IronAge) owner.Age = 2;
                    b.ResearchTech = null;
                    Log(b.OwnerId, EventKind.TechResearched, b.Pos);
                }
            }
        }

        bool StepToward(Entity u, Vec2 target, float dt)
        {
            float speed = GameData.Units[u.Unit].Speed * dt;
            var d = target - u.Pos;
            float dist = d.Length;
            if (dist <= speed || dist < 0.001f) { u.Pos = target; return true; }
            u.Pos = Clamp(u.Pos + d * (speed / dist));
            return false;
        }

        void UpdateUnit(Entity u, float dt)
        {
            var owner = Players[u.OwnerId];
            var vis = _vis[u.OwnerId];
            u.Cooldown = Math.Max(0f, u.Cooldown - dt);

            switch (u.Order)
            {
                case OrderType.None:
                    if (u.Unit != UnitType.Worker) AutoAcquire(u, vis);
                    break;
                case OrderType.Move:
                    if (StepToward(u, u.MoveTarget, dt)) u.Order = OrderType.None;
                    break;
                case OrderType.Attack:
                    UpdateAttack(u, owner, vis, dt);
                    break;
                case OrderType.Gather:
                    UpdateGather(u, owner, dt);
                    break;
            }
        }

        void AutoAcquire(Entity u, VisibleWorld vis)
        {
            Entity best = null; float bestD = GameData.AggroRange; bool bestIsUnit = false;
            foreach (int id in vis.VisibleEnemyIds)
            {
                var e = Get(id);
                if (e == null) continue;
                float d = Vec2.Distance(u.Pos, e.Pos);
                bool isUnit = e.Kind == EntityKind.Unit;
                if (d > GameData.AggroRange) continue;
                if (best == null || (isUnit && !bestIsUnit) || (isUnit == bestIsUnit && d < bestD)) { best = e; bestD = d; bestIsUnit = isUnit; }
            }
            if (best != null) { u.Order = OrderType.Attack; u.TargetId = best.Id; u.AutoTargeted = true; }
        }

        void UpdateAttack(Entity u, PlayerState owner, VisibleWorld vis, float dt)
        {
            var t = Get(u.TargetId);
            if (t == null || t.OwnerId == u.OwnerId || !vis.CanSee(t.Id)) { u.Order = OrderType.None; u.TargetId = 0; return; }
            float dist = Vec2.Distance(u.Pos, t.Pos);
            if (u.AutoTargeted && dist > GameData.AggroRange * 2f) { u.Order = OrderType.None; u.TargetId = 0; return; }
            var stats = CombatRules.Effective(u.Unit, owner);
            float reach = stats.Range + (t.Kind == EntityKind.Building ? 3f : 0f);
            if (dist <= reach)
            {
                if (u.Cooldown <= 0f) { Hit(u, owner, t); u.Cooldown = GameData.AttackCooldownS; }
            }
            else StepToward(u, t.Pos, dt);
        }

        void Hit(Entity attacker, PlayerState attackerOwner, Entity target)
        {
            var targetOwner = Players[target.OwnerId];
            target.Hp -= CombatRules.HitDamage(attacker, attackerOwner, target, targetOwner);
            _lastCombat[attacker.OwnerId] = Time;
            _lastCombat[target.OwnerId] = Time;
            if (!_lastUnderAttackEvent.TryGetValue(target.OwnerId, out float last) || Time - last >= 5f)
            {
                _lastUnderAttackEvent[target.OwnerId] = Time;
                Log(target.OwnerId, EventKind.UnderAttack, target.Pos);
            }
            if (target.Hp <= 0) Kill(target);
        }

        void Kill(Entity e)
        {
            if (!e.Alive) return;
            e.Alive = false;
            var owner = Players[e.OwnerId];
            if (e.Kind == EntityKind.Unit)
            {
                owner.PopulationUsed = Math.Max(0, owner.PopulationUsed - GameData.Units[e.Unit].Pop);
                Log(e.OwnerId, EventKind.UnitLost, e.Pos, e.Unit);
                return;
            }
            Log(e.OwnerId, EventKind.BuildingLost, e.Pos);
            if (e.Complete) MarkCompleted(e.OwnerId, e.Building, -1);
            foreach (var q in e.Queue) owner.PopulationUsed = Math.Max(0, owner.PopulationUsed - GameData.Units[q.Unit].Pop);
            e.Queue.Clear();
            if (e.ResearchTech.HasValue) { owner.Researching.Remove(e.ResearchTech.Value); e.ResearchTech = null; }
            if (e.Building == BuildingType.Citadel && !Result.Ended)
            {
                int winner = 0;
                foreach (int id in Players.Keys) if (id != e.OwnerId) { winner = id; break; }
                Result = new MatchResult { Ended = true, WinnerId = winner };
            }
        }

        Entity FindNearestNode(ResourceType res, Vec2 from)
        {
            Entity best = null;
            foreach (var n in Entities)
                if (n.Alive && n.Kind == EntityKind.ResourceNode && n.NodeResource == res && n.Amount > 0 &&
                    (best == null || Vec2.Distance(n.Pos, from) < Vec2.Distance(best.Pos, from))) best = n;
            return best;
        }

        Entity FindDropoff(int owner, Vec2 from)
        {
            Entity best = null;
            foreach (var b in Entities)
                if (b.Alive && b.OwnerId == owner && b.Kind == EntityKind.Building && b.Complete && b.Building == BuildingType.Citadel &&
                    (best == null || Vec2.Distance(b.Pos, from) < Vec2.Distance(best.Pos, from))) best = b;
            return best;
        }

        void UpdateGather(Entity u, PlayerState owner, float dt)
        {
            var node = Get(u.GatherNodeId);
            bool nodeOk = node != null && node.Kind == EntityKind.ResourceNode && node.Amount > 0;
            if (!nodeOk && u.GatherState != GatherPhase.ToDropoff)
            {
                // Node depleted: carry what we have home, or look for another node of the same kind.
                var next = FindNearestNode(u.CarryType, u.Pos);
                if (u.Carry > 0) u.GatherState = GatherPhase.ToDropoff;
                else if (next != null) { u.GatherNodeId = next.Id; u.GatherState = GatherPhase.ToNode; return; }
                else { u.Order = OrderType.None; return; }
            }
            switch (u.GatherState)
            {
                case GatherPhase.ToNode:
                    if (Vec2.Distance(u.Pos, node.Pos) <= GameData.GatherRange) { u.GatherState = GatherPhase.Gathering; u.GatherAcc = 0; u.CarryType = node.NodeResource; }
                    else StepToward(u, node.Pos, dt);
                    break;
                case GatherPhase.Gathering:
                {
                    float mult = (node.NodeResource != ResourceType.Gold && owner.Has(TechId.SharpenedTools)) ? 1.2f : 1f;
                    u.GatherAcc += GameData.GatherRate(node.NodeResource) * mult * dt;
                    while (u.GatherAcc >= 1f && u.Carry < GameData.CarryCapacity && node.Amount > 0) { u.Carry++; node.Amount--; u.GatherAcc -= 1f; }
                    if (u.Carry >= GameData.CarryCapacity || node.Amount <= 0) u.GatherState = GatherPhase.ToDropoff;
                    break;
                }
                case GatherPhase.ToDropoff:
                {
                    var drop = FindDropoff(u.OwnerId, u.Pos);
                    if (drop == null) { u.Order = OrderType.None; return; }
                    if (Vec2.Distance(u.Pos, drop.Pos) <= GameData.DropoffRange)
                    {
                        owner.Add(u.CarryType, u.Carry);
                        u.Carry = 0;
                        u.GatherState = GatherPhase.ToNode;
                        if (!nodeOk) { var next = FindNearestNode(u.CarryType, u.Pos); if (next != null) u.GatherNodeId = next.Id; else u.Order = OrderType.None; }
                    }
                    else StepToward(u, drop.Pos, dt);
                    break;
                }
            }
        }
    }
}
