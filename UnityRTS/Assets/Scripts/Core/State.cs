using System;
using System.Collections.Generic;

namespace Rts.Core
{
    /// <summary>Authoritative per-player economy/tech state (owned by the host/simulation).</summary>
    public sealed class PlayerState
    {
        public int PlayerId;
        public int Wood, Food, Gold;
        public int Age = 1;
        public int PopulationUsed;   // living units + queued units
        public HashSet<TechId> Researched = new HashSet<TechId>();
        public HashSet<TechId> Researching = new HashSet<TechId>();
        public Dictionary<BuildingType, int> Completed = new Dictionary<BuildingType, int>();

        public PlayerState(int id) { PlayerId = id; }

        public Cost Resources => new Cost(Wood, Food, Gold);
        public bool CanAfford(Cost c) => c.CoveredBy(Resources);
        public void Spend(Cost c) { Wood -= c.Wood; Food -= c.Food; Gold -= c.Gold; }
        public void Add(ResourceType t, int n)
        {
            if (t == ResourceType.Wood) Wood += n; else if (t == ResourceType.Food) Food += n; else Gold += n;
        }
        public bool Has(TechId t) => Researched.Contains(t);
        public int CountCompleted(BuildingType b) => Completed.TryGetValue(b, out int n) ? n : 0;
        public bool HasBuilding(BuildingType b) => CountCompleted(b) > 0;

        /// <summary>Citadel gives 10, each House +5, capped by the age hard cap (40 / 60).</summary>
        public int PopulationCap
        {
            get
            {
                int raw = (HasBuilding(BuildingType.Citadel) ? GameData.CitadelPop : 0)
                          + CountCompleted(BuildingType.House) * GameData.Buildings[BuildingType.House].PopProvided;
                return Math.Min(raw, GameData.PopHardCap(Age));
            }
        }

        public PlayerState Clone()
        {
            var c = new PlayerState(PlayerId) { Wood = Wood, Food = Food, Gold = Gold, Age = Age, PopulationUsed = PopulationUsed };
            c.Researched = new HashSet<TechId>(Researched);
            c.Researching = new HashSet<TechId>(Researching);
            c.Completed = new Dictionary<BuildingType, int>(Completed);
            return c;
        }
    }

    public sealed class ProductionItem
    {
        public UnitType Unit;
        public float Remaining;
    }

    public enum GatherPhase { ToNode, Gathering, ToDropoff }

    /// <summary>A unit, building or resource node. Plain data; all rules live in MatchSimulation.</summary>
    public sealed class Entity
    {
        public int Id, OwnerId;                 // OwnerId 0 = neutral (resource nodes)
        public EntityKind Kind;
        public UnitType Unit;
        public BuildingType Building;
        public ResourceType NodeResource;
        public Vec2 Pos;
        public float Hp, MaxHp;
        public bool Alive = true;

        // Unit state
        public OrderType Order;
        public Vec2 MoveTarget;
        public int TargetId;
        public float Cooldown;
        public GatherPhase GatherState;
        public ResourceType CarryType;
        public int Carry;
        public float GatherAcc;
        public int GatherNodeId;
        public bool AutoTargeted;               // attack target chosen by auto-acquire, not by the player

        // Building state
        public bool Complete = true;
        public float BuildProgress;             // seconds of construction done
        public List<ProductionItem> Queue = new List<ProductionItem>();
        public TechId? ResearchTech;
        public float ResearchRemaining;

        // Resource node state
        public int Amount;

        public bool IsMilitaryUnit => Kind == EntityKind.Unit && Unit != UnitType.Worker;
        public float Vision => Kind == EntityKind.Unit ? GameData.Units[Unit].Vision
                             : Kind == EntityKind.Building ? GameData.Buildings[Building].Vision : 0f;
    }

    public struct GameEvent
    {
        public float Time;
        public int PlayerId;           // the player this event belongs to (who may know about it)
        public EventKind Kind;
        public Vec2 Where;             // converted to a coarse Zone when a snapshot is built
        public UnitType? Unit;
        public int Count;
    }

    public struct MatchResult
    {
        public bool Ended;
        public int WinnerId;
    }
}
