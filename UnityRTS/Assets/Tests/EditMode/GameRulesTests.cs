using System;
using System.IO;
using NUnit.Framework;
using Rts.Core;
using UnityEngine;

namespace Rts.Tests
{
    [Serializable] class CostJson { public int wood, food, gold; }
    [Serializable] class UnitJson { public string id, name, trained_at; public CostJson cost; public int health, damage, population, required_age; public float speed, range, train_time_s; }
    [Serializable] class UnitList { public UnitJson[] items; }
    [Serializable] class BuildingJson { public string id, name; public CostJson cost; public int health, population_provided; public float build_time_s; }
    [Serializable] class BuildingList { public BuildingJson[] items; }
    [Serializable] class TechJson { public string id; public CostJson cost; public float research_time_s; public string researched_at; }
    [Serializable] class TechList { public TechJson[] items; }

    public class GameDataTests
    {
        static string Knowledge(string file) =>
            "{\"items\":" + File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "rag", "knowledge", file))) + "}";

        [Test]
        public void ThreeResourcesSixUnitsFiveBuildingsFiveTechs()
        {
            Assert.AreEqual(3, Enum.GetValues(typeof(ResourceType)).Length);
            Assert.AreEqual(6, GameData.Units.Count);
            Assert.AreEqual(5, GameData.Buildings.Count);
            Assert.AreEqual(5, GameData.Techs.Count);
        }

        [Test]
        public void UnitsMatchKnowledgeBase()
        {
            foreach (var j in JsonUtility.FromJson<UnitList>(Knowledge("units.json")).items)
            {
                Assert.IsTrue(WireNames.TryUnit(j.name, out var type), j.name);
                var d = GameData.Units[type];
                Assert.AreEqual(j.cost.wood, d.Cost.Wood, j.id + " wood");
                Assert.AreEqual(j.cost.food, d.Cost.Food, j.id + " food");
                Assert.AreEqual(j.cost.gold, d.Cost.Gold, j.id + " gold");
                Assert.AreEqual(j.health, d.Hp, j.id + " hp");
                Assert.AreEqual(j.damage, d.Damage, j.id + " damage");
                Assert.AreEqual(j.speed, d.Speed, 1e-4, j.id + " speed");
                Assert.AreEqual(j.range, d.Range, 1e-4, j.id + " range");
                Assert.AreEqual(j.train_time_s, d.TrainTimeS, 1e-4, j.id + " train time");
                Assert.AreEqual(j.population, d.Pop, j.id + " pop");
                Assert.AreEqual(j.required_age, d.RequiredAge, j.id + " age");
                Assert.AreEqual(j.trained_at, d.TrainedAt.ToString(), j.id + " trained_at");
            }
        }

        [Test]
        public void BuildingsMatchKnowledgeBase()
        {
            foreach (var j in JsonUtility.FromJson<BuildingList>(Knowledge("buildings.json")).items)
            {
                Assert.IsTrue(WireNames.TryBuilding(j.name, out var type), j.name);
                var d = GameData.Buildings[type];
                Assert.AreEqual(j.cost.wood, d.Cost.Wood, j.id); Assert.AreEqual(j.cost.food, d.Cost.Food, j.id); Assert.AreEqual(j.cost.gold, d.Cost.Gold, j.id);
                Assert.AreEqual(j.health, d.Hp, j.id + " hp");
                Assert.AreEqual(j.population_provided, d.PopProvided, j.id + " pop");
                Assert.AreEqual(j.build_time_s, d.BuildTimeS, 1e-4, j.id + " build time");
            }
        }

        [Test]
        public void TechnologiesMatchKnowledgeBase()
        {
            foreach (var j in JsonUtility.FromJson<TechList>(Knowledge("technologies.json")).items)
            {
                Assert.IsTrue(WireNames.TryTech(j.id, out var id), j.id);
                var d = GameData.Techs[id];
                Assert.AreEqual(j.cost.wood, d.Cost.Wood, j.id); Assert.AreEqual(j.cost.food, d.Cost.Food, j.id); Assert.AreEqual(j.cost.gold, d.Cost.Gold, j.id);
                Assert.AreEqual(j.research_time_s, d.ResearchTimeS, 1e-4, j.id);
                Assert.AreEqual(j.researched_at, d.ResearchedAt.ToString(), j.id);
            }
        }

        [Test]
        public void EveryTrainableUnitAndResearchIsOfferedByExactlyItsBuilding()
        {
            foreach (var u in GameData.Units.Values)
                CollectionAssert.Contains(GameData.Buildings[u.TrainedAt].Trains, u.Type);
            foreach (var t in GameData.Techs.Values)
                CollectionAssert.Contains(GameData.Buildings[t.ResearchedAt].Researches, t.Id);
        }
    }

    public class EconomyTests
    {
        [Test]
        public void StartingState()
        {
            var sim = T.Match();
            var p = sim.Players[1];
            Assert.AreEqual(new Cost(200, 200, 100), p.Resources);
            Assert.AreEqual(3, p.PopulationUsed);
            Assert.AreEqual(10, p.PopulationCap);                                   // Citadel +10
            Assert.AreEqual(2000f, sim.FindCitadel(1).MaxHp);
        }

        [Test]
        public void PopulationCapHousesAndAgeHardCap()
        {
            var p = new PlayerState(1);
            p.Completed[BuildingType.Citadel] = 1;
            Assert.AreEqual(10, p.PopulationCap);
            p.Completed[BuildingType.House] = 2; Assert.AreEqual(20, p.PopulationCap);
            p.Completed[BuildingType.House] = 6; Assert.AreEqual(40, p.PopulationCap);
            p.Completed[BuildingType.House] = 7; Assert.AreEqual(40, p.PopulationCap, "Age I hard cap");
            p.Age = 2; Assert.AreEqual(45, p.PopulationCap);
            p.Completed[BuildingType.House] = 20; Assert.AreEqual(60, p.PopulationCap, "Age II hard cap");
        }

        [Test]
        public void WorkersGatherAndDepositAtCitadel()
        {
            var sim = T.Match();
            var w = T.First(sim, 1, UnitType.Worker);
            int before = sim.Players[1].Wood;
            Assert.IsTrue(sim.OrderGather(1, new[] { w.Id }, T.Node(sim, ResourceType.Wood, 1).Id).Ok);
            T.Run(sim, 60);
            Assert.Greater(sim.Players[1].Wood, before, "wood must be deposited");
        }

        [Test]
        public void AllThreeResourcesCanBeGathered()
        {
            var sim = T.Match();
            var workers = new System.Collections.Generic.List<Entity>();
            foreach (var e in sim.Entities) if (e.OwnerId == 1 && e.Unit == UnitType.Worker && e.Kind == EntityKind.Unit) workers.Add(e);
            var res = new[] { ResourceType.Wood, ResourceType.Food, ResourceType.Gold };
            for (int i = 0; i < 3; i++) sim.OrderGather(1, new[] { workers[i].Id }, T.Node(sim, res[i], 1).Id);
            var start = sim.Players[1].Resources;
            T.Run(sim, 90);
            Assert.Greater(sim.Players[1].Wood, start.Wood);
            Assert.Greater(sim.Players[1].Food, start.Food);
            Assert.Greater(sim.Players[1].Gold, start.Gold);
        }

        [Test]
        public void SharpenedToolsSpeedsUpWoodGathering()
        {
            int Gather(bool tools)
            {
                var sim = T.Match();
                if (tools) sim.Players[1].Researched.Add(TechId.SharpenedTools);
                var w = T.First(sim, 1, UnitType.Worker);
                sim.OrderGather(1, new[] { w.Id }, T.Node(sim, ResourceType.Wood, 1).Id);
                int before = sim.Players[1].Wood;
                T.Run(sim, 100);
                return sim.Players[1].Wood - before;
            }
            Assert.Greater(Gather(true), Gather(false));
        }

        [Test]
        public void OnlyWorkersGather_AndOnlyOwnUnitsObey()
        {
            var sim = T.Match();
            sim.SpawnUnit(1, UnitType.Spearman, new Vec2(-55, -40));
            var sp = T.First(sim, 1, UnitType.Spearman);
            Assert.IsFalse(sim.OrderGather(1, new[] { sp.Id }, T.Node(sim, ResourceType.Wood, 1).Id).Ok);
            var enemyWorker = T.First(sim, 2, UnitType.Worker);
            Assert.IsFalse(sim.OrderMove(1, new[] { enemyWorker.Id }, new Vec2(0, 0)).Ok, "cannot command another player's unit");
        }
    }

    public class ProductionAndTechTests
    {
        [Test]
        public void TrainWorkerCostsFoodReservesPopulationAndSpawns()
        {
            var sim = T.Match();
            Assert.IsTrue(sim.TryTrain(1, UnitType.Worker, 1).Ok);
            Assert.AreEqual(150, sim.Players[1].Food);
            Assert.AreEqual(4, sim.Players[1].PopulationUsed);
            int before = T.Count(sim, 1, UnitType.Worker);
            T.Run(sim, 11); Assert.AreEqual(before, T.Count(sim, 1, UnitType.Worker));
            T.Run(sim, 2);  Assert.AreEqual(before + 1, T.Count(sim, 1, UnitType.Worker));
            Assert.AreEqual(4, sim.Players[1].PopulationUsed);
        }

        [Test]
        public void TrainingRejections()
        {
            var sim = T.Match();
            Assert.IsFalse(sim.TryTrain(1, UnitType.Spearman, 1).Ok, "needs Barracks");
            Assert.IsFalse(sim.TryTrain(1, UnitType.Worker, 0).Ok);
            Assert.IsFalse(sim.TryTrain(1, UnitType.Worker, 21).Ok);
            Assert.IsFalse(sim.TryTrain(1, UnitType.Worker, 5).Ok, "250 food needed, 200 available");
            T.Give(sim, 1, food: 1000);
            Assert.IsFalse(sim.TryTrain(1, UnitType.Worker, 8).Ok, "population 3+8 > cap 10");
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -60));
            T.Give(sim, 1, food: 1000, gold: 1000);
            var r = sim.TryTrain(1, UnitType.Guardian, 1);
            Assert.IsFalse(r.Ok); StringAssert.Contains("Age 2", r.Reason);
            Assert.AreEqual(3, sim.Players[1].PopulationUsed, "failed commands must not change state");
        }

        [Test]
        public void BuildingMustBeConstructedByWorkerBeforeItProduces()
        {
            var sim = T.Match();
            var site = sim.FindBuildSite(1).Value;
            var w = T.First(sim, 1, UnitType.Worker);
            Assert.IsTrue(sim.TryBuild(1, BuildingType.Barracks, site, w.Id).Ok);
            Assert.AreEqual(50, sim.Players[1].Wood);                              // 200 - 150
            Assert.IsFalse(sim.TryTrain(1, UnitType.Spearman, 1).Ok, "not built yet");
            T.Run(sim, 60);
            Assert.AreEqual(1, sim.Players[1].CountCompleted(BuildingType.Barracks));
            Assert.IsTrue(sim.TryTrain(1, UnitType.Spearman, 1).Ok);
        }

        [Test]
        public void CitadelCannotBeBuiltAndHouseNeedsWood()
        {
            var sim = T.Match();
            Assert.IsFalse(sim.TryBuild(1, BuildingType.Citadel, sim.FindBuildSite(1).Value).Ok);
            sim.Players[1].Wood = 20;
            Assert.IsFalse(sim.TryBuild(1, BuildingType.House, sim.FindBuildSite(1).Value).Ok);
        }

        [Test]
        public void IronAgeNeedsBuildingsThenRaisesAgeAndUnlocksGuardian()
        {
            var sim = T.Match();
            T.Give(sim, 1, wood: 500, food: 1000, gold: 1000);
            Assert.IsFalse(sim.TryResearch(1, TechId.Age2IronAge).Ok);
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            Assert.IsFalse(sim.TryResearch(1, TechId.Age2IronAge).Ok, "needs ArcheryYard or Stable too");
            sim.SpawnBuilding(1, BuildingType.Stable, new Vec2(-80, -70));
            Assert.IsTrue(sim.TryResearch(1, TechId.Age2IronAge).Ok);
            Assert.IsFalse(sim.TryResearch(1, TechId.Age2IronAge).Ok, "already in progress");
            T.Run(sim, 41);
            Assert.AreEqual(2, sim.Players[1].Age);
            Assert.IsTrue(sim.TryTrain(1, UnitType.Guardian, 1).Ok);
            Assert.IsTrue(sim.TryResearch(1, TechId.ForgedBlades).Ok, "Age II tech now available");
        }

        [Test]
        public void AgeTwoTechsAreLockedInAgeOne()
        {
            var sim = T.Match();
            T.Give(sim, 1, food: 1000, gold: 1000);
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            Assert.IsFalse(sim.TryResearch(1, TechId.ForgedBlades).Ok);
            Assert.IsFalse(sim.TryResearch(1, TechId.ReinforcedPlating).Ok);
        }

        [Test]
        public void LongbowAndSharpenedToolsResearchComplete()
        {
            var sim = T.Match();
            T.Give(sim, 1, wood: 1000, food: 500, gold: 500);
            sim.SpawnBuilding(1, BuildingType.ArcheryYard, new Vec2(-40, -70));
            Assert.IsTrue(sim.TryResearch(1, TechId.LongbowTraining).Ok);
            Assert.IsTrue(sim.TryResearch(1, TechId.SharpenedTools).Ok);
            T.Run(sim, 31);
            Assert.IsTrue(sim.Players[1].Has(TechId.LongbowTraining));
            Assert.IsTrue(sim.Players[1].Has(TechId.SharpenedTools));
        }
    }

    public class CombatTests
    {
        static Entity U(UnitType t, int owner = 1)
        {
            return new Entity { Kind = EntityKind.Unit, Unit = t, OwnerId = owner, Hp = GameData.Units[t].Hp, MaxHp = GameData.Units[t].Hp };
        }
        static float Hit(UnitType a, UnitType d, PlayerState ao = null, PlayerState dof = null) => CombatRules.HitDamage(U(a), ao, U(d, 2), dof);

        [Test]
        public void CounterTriangle_SpearmanBeatsRaiderBeatsArcherBeatsSpearman()
        {
            Assert.AreEqual(21f, Hit(UnitType.Spearman, UnitType.Raider));   // 8+14-1
            Assert.AreEqual(9f, Hit(UnitType.Raider, UnitType.Spearman));    // 10-1
            Assert.AreEqual(16f, Hit(UnitType.Raider, UnitType.Archer));     // 10+6-0
            Assert.AreEqual(5f, Hit(UnitType.Archer, UnitType.Raider));      // 7-2
            Assert.AreEqual(9f, Hit(UnitType.Archer, UnitType.Spearman));    // 7+3-1
            Assert.AreEqual(8f, Hit(UnitType.Spearman, UnitType.Archer));    // no bonus, Archer has no armor
        }

        [Test]
        public void GuardianResistsArchersAndBeatsSpearmen_RamIsBuildingKiller()
        {
            Assert.AreEqual(3f, Hit(UnitType.Archer, UnitType.Guardian));    // 7-4
            Assert.AreEqual(12f, Hit(UnitType.Guardian, UnitType.Archer));
            Assert.AreEqual(15f, Hit(UnitType.Guardian, UnitType.Spearman));    // 12+4-1
            var cit = new Entity { Kind = EntityKind.Building, Building = BuildingType.Citadel, OwnerId = 2 };
            Assert.AreEqual(45f, CombatRules.HitDamage(U(UnitType.Ram), null, cit, null));       // 3+42, siege ignores armor
            Assert.AreEqual(2f, CombatRules.HitDamage(U(UnitType.Archer), null, cit, null));     // 7-5
            Assert.AreEqual(2f, Hit(UnitType.Ram, UnitType.Spearman), "Ram is nearly harmless to units");
        }

        [Test]
        public void TechnologiesChangeCombat()
        {
            var p = new PlayerState(1);
            Assert.AreEqual(8f, CombatRules.Effective(UnitType.Spearman, p).Damage);
            p.Researched.Add(TechId.ForgedBlades);
            Assert.AreEqual(9.6f, CombatRules.Effective(UnitType.Spearman, p).Damage, 1e-4);
            Assert.AreEqual(7f, CombatRules.Effective(UnitType.Archer, p).Damage, "Forged Blades is melee only");
            p.Researched.Add(TechId.LongbowTraining);
            Assert.AreEqual(8f, CombatRules.Effective(UnitType.Archer, p).Range);
            Assert.AreEqual(9f, CombatRules.Effective(UnitType.Archer, p).Damage);
            Assert.AreEqual(1, CombatRules.Effective(UnitType.Spearman, p).ArmorPierce);
            p.Researched.Add(TechId.ReinforcedPlating);
            Assert.AreEqual(2, CombatRules.Effective(UnitType.Spearman, p).ArmorPierce);
            Assert.AreEqual(9, CombatRules.Effective(UnitType.Ram, p).ArmorPierce);
            Assert.AreEqual(0, CombatRules.Effective(UnitType.Archer, p).ArmorPierce, "Archers get no plating");
        }

        [Test]
        public void SpearmenDefeatRaidersInASkirmish()
        {
            var sim = T.Match();
            var c = sim.FindCitadel(1).Pos + new Vec2(0, 30);
            for (int i = 0; i < 3; i++)
            {
                sim.SpawnUnit(1, UnitType.Spearman, c + new Vec2(i * 2, 0));
                sim.SpawnUnit(2, UnitType.Raider, c + new Vec2(i * 2, 4));
            }
            T.Run(sim, 40);
            Assert.Greater(T.Count(sim, 1, UnitType.Spearman), T.Count(sim, 2, UnitType.Raider));
        }

        [Test]
        public void DeadUnitsFreePopulationAndAreRemoved()
        {
            var sim = T.Match();
            var c = sim.FindCitadel(1).Pos + new Vec2(0, 30);
            var victim = sim.SpawnUnit(2, UnitType.Archer, c);
            int pop = sim.Players[2].PopulationUsed;
            for (int i = 0; i < 4; i++) sim.SpawnUnit(1, UnitType.Spearman, c + new Vec2(i, 2));
            T.Run(sim, 15);
            Assert.IsFalse(victim.Alive);
            Assert.AreEqual(pop - 1, sim.Players[2].PopulationUsed);
            Assert.IsFalse(sim.Entities.Contains(victim));
            Assert.IsNull(sim.Get(victim.Id));
        }

        [Test]
        public void DestroyingTheCitadelEndsTheMatch()
        {
            var sim = T.Match();
            var enemyCitadel = sim.FindCitadel(2);
            enemyCitadel.Hp = 120;
            var ram = sim.SpawnUnit(1, UnitType.Ram, enemyCitadel.Pos + new Vec2(3, 3));
            Assert.IsTrue(sim.OrderAttack(1, new[] { ram.Id }, enemyCitadel.Id).Ok);
            T.Run(sim, 10);
            Assert.IsTrue(sim.Result.Ended);
            Assert.AreEqual(1, sim.Result.WinnerId);
            Assert.IsFalse(sim.TryTrain(2, UnitType.Worker, 1).Ok, "no commands after the match ends");
        }

        [Test]
        public void GroupMoveUsesFormationAndArrives()
        {
            var sim = T.Match();
            var ids = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 6; i++) ids.Add(sim.SpawnUnit(1, UnitType.Spearman, new Vec2(-60 + i, -20)).Id);
            Assert.IsTrue(sim.OrderMove(1, ids, new Vec2(-30, 0)).Ok);
            T.Run(sim, 30);
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (int id in ids)
            {
                var e = sim.Get(id);
                Assert.Less(Vec2.Distance(e.Pos, new Vec2(-30, 0)), 4f);
                Assert.AreEqual(OrderType.None, e.Order);
                seen.Add(e.Pos.ToString());
            }
            Assert.AreEqual(6, seen.Count, "units must not stack on a single point");
        }

        [Test]
        public void FormationOffsetsAreCenteredAndSpaced()
        {
            foreach (int n in new[] { 1, 2, 5, 9, 16 })
            {
                var o = Formation.Offsets(n, 2f);
                Assert.AreEqual(n, o.Length);
                float sx = 0, sy = 0;
                for (int i = 0; i < n; i++)
                {
                    sx += o[i].X; sy += o[i].Y;
                    for (int j = i + 1; j < n; j++) Assert.GreaterOrEqual(Vec2.Distance(o[i], o[j]), 2f - 1e-4f);
                }
                Assert.AreEqual(0f, sx / n, 1.01f); Assert.AreEqual(0f, sy / n, 1.01f);
            }
        }
    }
}
