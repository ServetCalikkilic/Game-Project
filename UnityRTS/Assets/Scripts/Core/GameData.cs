using System.Collections.Generic;

namespace Rts.Core
{
    /// <summary>
    /// Static game balance. Values mirror rag/knowledge/*.json (the source of truth);
    /// GameDataTests compares them against those files so the two cannot drift silently.
    /// </summary>
    public sealed class UnitDef
    {
        public UnitType Type;
        public Cost Cost;
        public int Hp, Damage, Pop, RequiredAge;
        public DamageType DamageType;
        public int ArmorMelee, ArmorPierce;
        public float Speed, Range, TrainTimeS, Vision;
        public BuildingType TrainedAt;
        public Dictionary<UnitType, int> BonusVsUnit = new Dictionary<UnitType, int>();
        public int BonusVsBuilding;
        public bool IsMilitary => Type != UnitType.Worker;
    }

    public sealed class BuildingDef
    {
        public BuildingType Type;
        public Cost Cost;
        public int Hp, PopProvided, RequiredAge;
        public float BuildTimeS, Vision;
        public UnitType[] Trains = new UnitType[0];
        public TechId[] Researches = new TechId[0];
        public bool Buildable => Type != BuildingType.Citadel;
    }

    public sealed class TechDef
    {
        public TechId Id;
        public Cost Cost;
        public float ResearchTimeS;
        public BuildingType ResearchedAt;
        public bool RequiresAge2;
        public BuildingType[] RequiresAll = new BuildingType[0];   // e.g. Barracks
        public BuildingType[] RequiresAny = new BuildingType[0];   // e.g. ArcheryYard OR Stable
    }

    public static class GameData
    {
        // Gameplay constants that are not part of the knowledge base.
        public const float AttackCooldownS = 1.0f;
        public const int CarryCapacity = 10;
        public const int CitadelPop = 10;
        public const int BuildingArmorMelee = 2;
        public const int BuildingArmorPierce = 5;   // arrows barely scratch buildings
        public const float BuildRange = 5f;          // a friendly Worker this close advances construction
        public const float AggroRange = 6f;
        public const float DropoffRange = 6f;
        public const float GatherRange = 2.5f;
        public static readonly Cost StartingResources = new Cost(wood: 200, food: 200, gold: 100);
        public const int StartingWorkers = 3;

        public static int PopHardCap(int age) => age >= 2 ? 60 : 40;

        public static float GatherRate(ResourceType t) => t == ResourceType.Wood ? 1.0f : t == ResourceType.Food ? 0.8f : 0.7f;

        public static readonly Dictionary<UnitType, UnitDef> Units = BuildUnits();
        public static readonly Dictionary<BuildingType, BuildingDef> Buildings = BuildBuildings();
        public static readonly Dictionary<TechId, TechDef> Techs = BuildTechs();

        static Dictionary<UnitType, UnitDef> BuildUnits()
        {
            var d = new Dictionary<UnitType, UnitDef>();
            void Add(UnitDef u) { u.Vision = u.Type == UnitType.Raider ? 14f : 12f; d[u.Type] = u; }

            Add(new UnitDef { Type = UnitType.Worker, Cost = new Cost(food: 50), Hp = 40, Damage = 3, DamageType = DamageType.Melee,
                ArmorMelee = 0, ArmorPierce = 0, Speed = 3.0f, Range = 1.0f, TrainTimeS = 12, Pop = 1, TrainedAt = BuildingType.Citadel, RequiredAge = 1 });
            Add(new UnitDef { Type = UnitType.Spearman, Cost = new Cost(wood: 25, food: 50), Hp = 70, Damage = 8, DamageType = DamageType.Melee,
                ArmorMelee = 1, ArmorPierce = 1, Speed = 3.2f, Range = 1.5f, TrainTimeS = 16, Pop = 1, TrainedAt = BuildingType.Barracks, RequiredAge = 1,
                BonusVsUnit = { { UnitType.Raider, 14 } } });
            Add(new UnitDef { Type = UnitType.Archer, Cost = new Cost(wood: 45, gold: 25), Hp = 45, Damage = 7, DamageType = DamageType.Pierce,
                ArmorMelee = 0, ArmorPierce = 0, Speed = 3.4f, Range = 7.0f, TrainTimeS = 18, Pop = 1, TrainedAt = BuildingType.ArcheryYard, RequiredAge = 1,
                BonusVsUnit = { { UnitType.Spearman, 3 } } });
            Add(new UnitDef { Type = UnitType.Raider, Cost = new Cost(food: 70, gold: 40), Hp = 100, Damage = 10, DamageType = DamageType.Melee,
                ArmorMelee = 1, ArmorPierce = 2, Speed = 5.2f, Range = 1.5f, TrainTimeS = 22, Pop = 2, TrainedAt = BuildingType.Stable, RequiredAge = 1,
                BonusVsUnit = { { UnitType.Archer, 6 }, { UnitType.Worker, 4 } } });
            Add(new UnitDef { Type = UnitType.Guardian, Cost = new Cost(food: 60, gold: 45), Hp = 140, Damage = 12, DamageType = DamageType.Melee,
                ArmorMelee = 3, ArmorPierce = 4, Speed = 2.6f, Range = 1.5f, TrainTimeS = 24, Pop = 2, TrainedAt = BuildingType.Barracks, RequiredAge = 2,
                BonusVsUnit = { { UnitType.Spearman, 4 } } });
            Add(new UnitDef { Type = UnitType.Ram, Cost = new Cost(wood: 140, gold: 60), Hp = 200, Damage = 3, DamageType = DamageType.Siege,
                ArmorMelee = 2, ArmorPierce = 8, Speed = 2.2f, Range = 1.5f, TrainTimeS = 30, Pop = 3, TrainedAt = BuildingType.Barracks, RequiredAge = 2,
                BonusVsBuilding = 42 });
            return d;
        }

        static Dictionary<BuildingType, BuildingDef> BuildBuildings()
        {
            var d = new Dictionary<BuildingType, BuildingDef>();
            void Add(BuildingDef b) { b.Vision = b.Type == BuildingType.Citadel ? 18f : 14f; d[b.Type] = b; }

            Add(new BuildingDef { Type = BuildingType.Citadel, Cost = Cost.Zero, Hp = 2000, PopProvided = CitadelPop, RequiredAge = 1, BuildTimeS = 0,
                Trains = new[] { UnitType.Worker }, Researches = new[] { TechId.Age2IronAge, TechId.SharpenedTools } });
            Add(new BuildingDef { Type = BuildingType.House, Cost = new Cost(wood: 30), Hp = 300, PopProvided = 5, RequiredAge = 1, BuildTimeS = 15 });
            Add(new BuildingDef { Type = BuildingType.Barracks, Cost = new Cost(wood: 150), Hp = 800, RequiredAge = 1, BuildTimeS = 35,
                Trains = new[] { UnitType.Spearman, UnitType.Guardian, UnitType.Ram }, Researches = new[] { TechId.ForgedBlades, TechId.ReinforcedPlating } });
            Add(new BuildingDef { Type = BuildingType.ArcheryYard, Cost = new Cost(wood: 175), Hp = 700, RequiredAge = 1, BuildTimeS = 35,
                Trains = new[] { UnitType.Archer }, Researches = new[] { TechId.LongbowTraining } });
            Add(new BuildingDef { Type = BuildingType.Stable, Cost = new Cost(wood: 175, gold: 50), Hp = 700, RequiredAge = 1, BuildTimeS = 40,
                Trains = new[] { UnitType.Raider } });
            return d;
        }

        static Dictionary<TechId, TechDef> BuildTechs()
        {
            var d = new Dictionary<TechId, TechDef>();
            void Add(TechDef t) { d[t.Id] = t; }
            Add(new TechDef { Id = TechId.Age2IronAge, Cost = new Cost(food: 300, gold: 150), ResearchTimeS = 40, ResearchedAt = BuildingType.Citadel,
                RequiresAll = new[] { BuildingType.Barracks }, RequiresAny = new[] { BuildingType.ArcheryYard, BuildingType.Stable } });
            Add(new TechDef { Id = TechId.SharpenedTools, Cost = new Cost(wood: 100, food: 75), ResearchTimeS = 25, ResearchedAt = BuildingType.Citadel });
            Add(new TechDef { Id = TechId.LongbowTraining, Cost = new Cost(wood: 175, gold: 75), ResearchTimeS = 30, ResearchedAt = BuildingType.ArcheryYard });
            Add(new TechDef { Id = TechId.ForgedBlades, Cost = new Cost(food: 150, gold: 100), ResearchTimeS = 35, ResearchedAt = BuildingType.Barracks, RequiresAge2 = true });
            Add(new TechDef { Id = TechId.ReinforcedPlating, Cost = new Cost(food: 100, gold: 120), ResearchTimeS = 35, ResearchedAt = BuildingType.Barracks, RequiresAge2 = true });
            return d;
        }
    }
}
