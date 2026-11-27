using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Rts.Core;

namespace Rts.Tests
{
    public class FogOfWarAndSnapshotTests
    {
        static Vec2 NorthOfBase(MatchSimulation sim, float dist) => sim.FindCitadel(1).Pos + new Vec2(0, dist);

        [Test]
        public void EnemyOutsideVisionIsInvisibleAndAbsentFromSnapshot()
        {
            var sim = T.Match();
            sim.SpawnUnit(2, UnitType.Raider, NorthOfBase(sim, 80));          // far from every P1 unit/building
            var view = sim.GetVisible(1);
            Assert.AreEqual(0, view.EnemyContacts.Count);
            Assert.AreEqual(0, view.VisibleEnemyIds.Count);
            var json = sim.BuildSnapshot(1, 1000).ToJson();
            StringAssert.DoesNotContain("Raider", json);
            Assert.AreEqual(0, sim.BuildSnapshot(1, 1000).visible_enemies.Length);
        }

        [Test]
        public void EnemyInsideVisionAppearsWithTypeCountAndCoarseZoneOnly()
        {
            var sim = T.Match();
            for (int i = 0; i < 3; i++) sim.SpawnUnit(2, UnitType.Raider, NorthOfBase(sim, 30) + new Vec2(i, 0));
            sim.SpawnUnit(1, UnitType.Spearman, NorthOfBase(sim, 25));       // forward scout gives vision of the gate
            var snap = sim.BuildSnapshot(1, 1000);
            Assert.AreEqual(1, snap.visible_enemies.Length);
            Assert.AreEqual("Raider", snap.visible_enemies[0].type);
            Assert.AreEqual(3, snap.visible_enemies[0].count);
            Assert.AreEqual("NorthGate", snap.visible_enemies[0].zone);
        }

        [Test]
        public void LeavingVisionRemovesTheEnemyAgain()
        {
            var sim = T.Match();
            var raider = sim.SpawnUnit(2, UnitType.Raider, NorthOfBase(sim, 30));
            sim.SpawnUnit(1, UnitType.Worker, NorthOfBase(sim, 25));
            Assert.AreEqual(1, sim.GetVisible(1).EnemyContacts.Count);
            raider.Pos = NorthOfBase(sim, 90);
            Assert.AreEqual(0, sim.GetVisible(1).EnemyContacts.Count);
        }

        [Test]
        public void SnapshotNeverContainsEnemyEconomyPositionsOrIds()
        {
            var sim = T.Match();
            sim.Players[2].Gold = 123457; sim.Players[2].Wood = 765431; sim.Players[2].Food = 998877;
            sim.SpawnUnit(2, UnitType.Raider, NorthOfBase(sim, 30));
            sim.SpawnUnit(1, UnitType.Spearman, NorthOfBase(sim, 25));
            string json = sim.BuildSnapshot(1, 1000).ToJson();
            foreach (var secret in new[] { "123457", "765431", "998877", "position", "\"x\"", "\"y\"", "owner", "player_id", "match_id", "target" })
                StringAssert.DoesNotContain(secret, json);
            var p2c = sim.FindCitadel(2).Pos;
            StringAssert.DoesNotContain(((int)p2c.X).ToString() + ".", json);
        }

        [Test]
        public void SnapshotTypeHasOnlyAllowlistedFields()
        {
            // If someone adds e.g. enemy_resources to the snapshot, this test fails and forces a security review.
            var top = new HashSet<string>();
            foreach (var f in typeof(AiSafeSnapshot).GetFields(BindingFlags.Public | BindingFlags.Instance)) top.Add(f.Name);
            CollectionAssert.AreEquivalent(new[] { "phase", "resources", "population", "technology", "own_army", "own_buildings", "visible_enemies", "recent_events", "captured_at" }, top);
            foreach (var f in typeof(EnemyContact).GetFields(BindingFlags.Public | BindingFlags.Instance))
                Assert.IsTrue(f.FieldType.IsEnum, $"EnemyContact.{f.Name} must be a coarse enum, not an id/position");
            foreach (var f in typeof(SnapshotUnitGroup).GetFields()) CollectionAssert.Contains(new[] { "type", "count", "zone" }, f.Name);
        }

        [Test]
        public void SnapshotJsonMatchesTheMcpServerContract()
        {
            var sim = T.Match();
            sim.TryTrain(1, UnitType.Worker, 1);
            T.Run(sim, 2);
            var dict = (Dictionary<string, object>)MiniJson.Parse(sim.BuildSnapshot(1, 1700000000.5).ToJson());
            CollectionAssert.AreEquivalent(new[] { "phase", "captured_at", "resources", "population", "technology", "own_army", "own_buildings", "visible_enemies", "recent_events" }, dict.Keys);
            Assert.AreEqual("EarlyEconomy", dict["phase"]);
            Assert.AreEqual(150, (double)((Dictionary<string, object>)dict["resources"])["food"]);
            Assert.AreEqual(4, (double)((Dictionary<string, object>)dict["population"])["used"]);
        }

        [Test]
        public void SnapshotBuilderRejectsMismatchedPlayerState()
        {
            var sim = T.Match();
            var view = sim.GetVisible(1);
            Assert.Throws<ArgumentException>(() => AiSnapshotBuilder.Build(sim.Players[2], view, new List<GameEvent>(), 0, Phase.EarlyEconomy, 0));
        }

        [Test]
        public void OwnArmyBuildingsTechAndEventsAreReported()
        {
            var sim = T.Match();
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            sim.SpawnUnit(1, UnitType.Spearman, NorthOfBase(sim, 30));
            sim.Players[1].Researched.Add(TechId.SharpenedTools);
            sim.TryTrain(1, UnitType.Spearman, 1);
            T.Run(sim, 20);
            var snap = sim.BuildSnapshot(1, 5);
            Assert.AreEqual(2, new List<SnapshotUnitGroup>(snap.own_army).FindAll(g => g.type == "Spearman").ConvertAll(g => g.count).Sum());
            Assert.IsTrue(Array.Exists(snap.own_buildings, b => b.type == "Barracks" && b.count == 1));
            Assert.IsTrue(Array.Exists(snap.own_buildings, b => b.type == "Citadel" && b.count == 1));
            CollectionAssert.Contains(snap.technology.researched, "tech_sharpened_tools_v1");
            Assert.IsTrue(Array.Exists(snap.recent_events, e => e.kind == "UNIT_TRAINED" && e.unit_type == "Spearman"));
            Assert.AreEqual("BuildUp", snap.phase);
        }

        [Test]
        public void AttackingAHiddenEnemyIsRefused()
        {
            var sim = T.Match();
            var hidden = sim.SpawnUnit(2, UnitType.Archer, new Vec2(0, 0));
            sim.SpawnUnit(1, UnitType.Spearman, new Vec2(-60, -20));
            var s = T.First(sim, 1, UnitType.Spearman);
            var r = sim.OrderAttack(1, new[] { s.Id }, hidden.Id);
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("not visible", r.Reason);
        }

        [Test]
        public void ZonesAreRelativeToTheViewersBase()
        {
            var a = new Vec2(0, 0);
            var none = new List<Entity>();
            Assert.AreEqual(Zone.BaseCenter, ZoneMap.Classify(new Vec2(5, 5), a, none));
            Assert.AreEqual(Zone.NorthGate, ZoneMap.Classify(new Vec2(3, 40), a, none));
            Assert.AreEqual(Zone.SouthGate, ZoneMap.Classify(new Vec2(-3, -40), a, none));
            Assert.AreEqual(Zone.EastGate, ZoneMap.Classify(new Vec2(40, 3), a, none));
            Assert.AreEqual(Zone.WestGate, ZoneMap.Classify(new Vec2(-40, 3), a, none));
            var forest = new List<Entity> { new Entity { Kind = EntityKind.ResourceNode, NodeResource = ResourceType.Wood, Pos = new Vec2(15, 0), Amount = 100 } };
            Assert.AreEqual(Zone.Forest, ZoneMap.Classify(new Vec2(16, 1), a, forest));
        }

        [Test]
        public void PhaseFollowsTheMatch()
        {
            var sim = T.Match();
            Assert.AreEqual(Phase.EarlyEconomy, sim.ComputePhase(1));
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            Assert.AreEqual(Phase.BuildUp, sim.ComputePhase(1));
            sim.Players[1].Age = 2;
            Assert.AreEqual(Phase.LateGame, sim.ComputePhase(1));
            sim.SpawnUnit(2, UnitType.Raider, NorthOfBase(sim, 30));
            sim.SpawnUnit(1, UnitType.Worker, NorthOfBase(sim, 25));
            Assert.AreEqual(Phase.UnderAttack, sim.ComputePhase(1));
        }

        [Test]
        public void UnderAttackEventIsRecordedForTheVictimOnly()
        {
            var sim = T.Match();
            var pos = NorthOfBase(sim, 30);
            sim.SpawnUnit(1, UnitType.Spearman, pos);
            sim.SpawnUnit(2, UnitType.Raider, pos + new Vec2(2, 0));
            T.Run(sim, 5);
            Assert.IsTrue(sim.BuildSnapshot(1, 0).recent_events.Length > 0);
            Assert.IsTrue(Array.Exists(sim.BuildSnapshot(1, 0).recent_events, e => e.kind == "UNDER_ATTACK" && e.zone == "NorthGate"));
        }
    }

    static class IntListExt { public static int Sum(this List<int> l) { int s = 0; foreach (var x in l) s += x; return s; } }

    public class ActionPlanParserTests
    {
        const string Valid = "{\"summary\":\"Counter cavalry\",\"confidence\":0.84,\"evidence\":[\"unit_spearman_v1\"],\"actions\":[" +
            "{\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":4,\"reason\":\"cavalry\"}," +
            "{\"action_type\":\"MOVE_GROUP\",\"target_zone\":\"NorthGate\",\"reason\":\"defend\"}]}";

        [Test]
        public void ValidPlanParses()
        {
            var r = ActionPlanParser.Parse(Valid);
            Assert.IsTrue(r.Ok, string.Join("; ", r.Errors));
            Assert.AreEqual(2, r.Plan.Actions.Count);
            Assert.AreEqual(UnitType.Spearman, r.Plan.Actions[0].Unit);
            Assert.AreEqual(4, r.Plan.Actions[0].Count);
            Assert.AreEqual(Zone.NorthGate, r.Plan.Actions[1].TargetZone);
            Assert.AreEqual(0.84, r.Plan.Confidence, 1e-9);
        }

        static string Act(string body) => "{\"summary\":\"s\",\"confidence\":0.5,\"actions\":[{" + body + "}]}";

        [TestCase("\"action_type\":\"LAUNCH_NUKE\",\"reason\":\"r\"", "unknown action type")]
        [TestCase("\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":-1,\"reason\":\"r\"", "negative count")]
        [TestCase("\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":21,\"reason\":\"r\"", "count too large")]
        [TestCase("\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":2.5,\"reason\":\"r\"", "fractional count")]
        [TestCase("\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Dragon\",\"count\":1,\"reason\":\"r\"", "unknown unit")]
        [TestCase("\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"3\",\"count\":1,\"reason\":\"r\"", "numeric enum string")]
        [TestCase("\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"spearman\",\"count\":1,\"reason\":\"r\"", "wrong case")]
        [TestCase("\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"reason\":\"r\"", "missing count")]
        [TestCase("\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":1,\"tech_id\":\"tech_age2_iron_age_v1\",\"reason\":\"r\"", "forbidden field")]
        [TestCase("\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":1,\"player_id\":\"2\",\"reason\":\"r\"", "unknown field")]
        [TestCase("\"action_type\":\"BUILD\",\"building_type\":\"Citadel\",\"reason\":\"r\"", "citadel not buildable")]
        [TestCase("\"action_type\":\"RESEARCH\",\"tech_id\":\"tech_nuke_v1\",\"reason\":\"r\"", "unknown tech")]
        [TestCase("\"action_type\":\"MOVE_GROUP\",\"target_zone\":\"EnemyBase\",\"reason\":\"r\"", "unknown zone")]
        [TestCase("\"action_type\":\"MOVE_GROUP\",\"target_zone\":\"NorthGate\"", "missing reason")]
        public void InvalidActionsAreRejected(string body, string why)
        {
            var r = ActionPlanParser.Parse(Act(body));
            Assert.IsFalse(r.Ok, why);
            Assert.IsNull(r.Plan, why);
            Assert.IsNotEmpty(r.Errors, why);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[]")]
        [TestCase("{\"summary\":\"s\",\"confidence\":2,\"actions\":[]}")]
        [TestCase("{\"summary\":\"s\",\"confidence\":0.5}")]
        [TestCase("{\"summary\":\"s\",\"confidence\":0.5,\"actions\":[],\"debug\":true}")]
        [TestCase("{\"summary\":\"s\",\"confidence\":0.5,\"actions\":[]} trailing")]
        [TestCase("{\"summary\":\"s\",\"summary\":\"t\",\"confidence\":0.5,\"actions\":[]}")]
        [TestCase("{\"summary\":\"s\",\"confidence\":0.5,\"actions\":[],\"evidence\":[\"../../etc/passwd\"]}")]
        public void InvalidEnvelopesAreRejected(string json)
        {
            Assert.IsFalse(ActionPlanParser.Parse(json).Ok, json);
        }

        [Test]
        public void MoreThanEightActionsRejected()
        {
            string one = "{\"action_type\":\"MOVE_GROUP\",\"target_zone\":\"NorthGate\",\"reason\":\"r\"}";
            string json = "{\"summary\":\"s\",\"confidence\":0.5,\"actions\":[" + string.Join(",", new[] { one, one, one, one, one, one, one, one, one }) + "]}";
            Assert.IsFalse(ActionPlanParser.Parse(json).Ok);
        }

        [Test]
        public void NullAndHugeInputsDoNotThrow()
        {
            Assert.IsFalse(ActionPlanParser.Parse(null).Ok);
            Assert.IsFalse(ActionPlanParser.Parse(new string('a', 70000)).Ok);
            Assert.IsFalse(ActionPlanParser.Parse(new string('[', 100)).Ok);
        }
    }

    public class GameActionValidatorTests
    {
        static PlanValidation Check(MatchSimulation sim, params AiAction[] actions) =>
            GameActionValidator.Validate(T.Plan(actions), sim.Players[1], ValidationContext.From(sim, 1));

        [Test]
        public void ValidPlanPasses()
        {
            var sim = T.Match();
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            sim.SpawnUnit(1, UnitType.Spearman, new Vec2(-55, -40));
            var v = Check(sim, T.Train(UnitType.Spearman, 3), T.Move(Zone.NorthGate));
            Assert.IsTrue(v.AllValid, string.Join(";", v.Checks.ConvertAll(c => c.Reason)));
            Assert.AreEqual(new Cost(wood: 75, food: 150), v.TotalCost);
        }

        [Test]
        public void ReasonsForRejection()
        {
            var sim = T.Match();
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            T.Give(sim, 1, food: 1000, gold: 1000, wood: 1000);
            var bad = new AiAction { Type = ActionType.TrainUnit, Unit = UnitType.Spearman, Count = -4, Reason = "x" };
            var cases = new[]
            {
                (bad, "invalid count"),
                (new AiAction { Type = ActionType.TrainUnit, Unit = UnitType.Spearman, Count = 21, Reason = "x" }, "invalid count"),
                (new AiAction { Type = ActionType.TrainUnit, Unit = (UnitType)99, Count = 1, Reason = "x" }, "unknown unit"),
                (new AiAction { Type = (ActionType)42, Reason = "x" }, "unknown action"),
                (T.Train(UnitType.Archer, 1), "required building missing"),
                (T.Train(UnitType.Guardian, 1), "requires Age 2"),
                (T.Train(UnitType.Worker, 8), "population cap"),
                (T.Build(BuildingType.Citadel), "cannot be built"),
                (T.Research(TechId.ForgedBlades), "requires Age II"),
                (T.Research(TechId.Age2IronAge), "required building missing"),
                (T.Move(Zone.NorthGate), "no military"),
            };
            foreach (var (action, expected) in cases)
            {
                var v = Check(sim, action);
                Assert.IsFalse(v.AllValid, expected);
                StringAssert.Contains(expected, v.Checks[0].Reason);
            }
        }

        [Test]
        public void CostsAndPopulationAccumulateAcrossActions()
        {
            var sim = T.Match();
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            // Start: 200 food. 2x Spearman (4 per...) -> first 3 cost 150 food, second 3 cost 150 more -> must fail.
            var v = Check(sim, T.Train(UnitType.Spearman, 3), T.Train(UnitType.Spearman, 3));
            Assert.IsTrue(v.Checks[0].Ok);
            Assert.IsFalse(v.Checks[1].Ok);
            StringAssert.Contains("insufficient", v.Checks[1].Reason);
            Assert.AreEqual(1, v.ValidCount);
        }

        [Test]
        public void BuildingQueuedInSamePlanDoesNotSatisfyPrerequisites()
        {
            var sim = T.Match();
            var v = Check(sim, T.Build(BuildingType.Barracks), T.Train(UnitType.Spearman, 1));
            Assert.IsTrue(v.Checks[0].Ok);
            Assert.IsFalse(v.Checks[1].Ok, "Barracks is not completed yet");
        }

        [Test]
        public void ResearchBusyBuildingAndDuplicates()
        {
            var sim = T.Match();
            T.Give(sim, 1, wood: 1000, food: 1000, gold: 1000);
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            sim.SpawnBuilding(1, BuildingType.Stable, new Vec2(-80, -70));
            var v = Check(sim, T.Research(TechId.SharpenedTools), T.Research(TechId.SharpenedTools), T.Research(TechId.Age2IronAge));
            Assert.IsTrue(v.Checks[0].Ok);
            Assert.IsFalse(v.Checks[1].Ok);
            Assert.IsFalse(v.Checks[2].Ok, "Citadel is busy researching Sharpened Tools");
            StringAssert.Contains("no free Citadel", v.Checks[2].Reason);
        }

        [Test]
        public void PlanLevelChecks()
        {
            var sim = T.Match();
            var many = new List<AiAction>();
            for (int i = 0; i < 9; i++) many.Add(T.Build(BuildingType.House));
            var plan = T.Plan(many.ToArray());
            Assert.IsNotEmpty(GameActionValidator.Validate(plan, sim.Players[1], ValidationContext.From(sim, 1)).PlanErrors);
            plan = T.Plan(); plan.Confidence = 1.5;
            Assert.IsNotEmpty(GameActionValidator.Validate(plan, sim.Players[1], ValidationContext.From(sim, 1)).PlanErrors);
            Assert.IsNotEmpty(GameActionValidator.Validate(null, sim.Players[1], ValidationContext.From(sim, 1)).PlanErrors);
            Assert.IsTrue(GameActionValidator.Validate(T.Plan(), sim.Players[1], ValidationContext.From(sim, 1)).AllValid, "an information-only plan is valid");
        }

        [Test]
        public void ValidationDoesNotMutateRealState()
        {
            var sim = T.Match();
            var before = sim.Players[1].Resources;
            Check(sim, T.Build(BuildingType.House), T.Research(TechId.SharpenedTools));
            Assert.AreEqual(before, sim.Players[1].Resources);
            Assert.IsEmpty(sim.Players[1].Researching);
        }
    }

    public class HumanInTheLoopExecutionTests
    {
        static string State(MatchSimulation sim)
        {
            int queued = 0;
            foreach (var e in sim.Entities) queued += e.Queue.Count;
            return $"{sim.Players[1].Resources}|pop{sim.Players[1].PopulationUsed}|q{queued}|n{sim.Entities.Count}|res{sim.Players[1].Researching.Count}";
        }

        const string TrainPlan = "{\"summary\":\"s\",\"confidence\":0.9,\"actions\":[{\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":3,\"reason\":\"r\"}]}";

        [Test]
        public void ApproveOnceRejectOnce_RejectionChangesNothing()
        {
            var sim = T.Match();
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            string before = State(sim);

            var rejected = PlanReview.FromJson(TrainPlan, sim, 1);
            Assert.AreEqual(ReviewStatus.Pending, rejected.Status);
            Assert.IsTrue(rejected.Preview.AllValid);
            Assert.IsTrue(rejected.Reject());
            Assert.AreEqual(before, State(sim), "REJECT must cause no gameplay change");
            Assert.AreEqual(0, rejected.Approve(sim, 1).Executed, "a rejected review cannot be approved later");
            Assert.AreEqual(before, State(sim));

            var approved = PlanReview.FromJson(TrainPlan, sim, 1);
            var report = approved.Approve(sim, 1);
            Assert.AreEqual(1, report.Executed);
            Assert.AreNotEqual(before, State(sim));
            Assert.AreEqual(0, approved.Approve(sim, 1).Executed, "approval is one-shot (no replay)");
        }

        [Test]
        public void InvalidJsonNeverReachesTheSimulation()
        {
            var sim = T.Match();
            string before = State(sim);
            var review = PlanReview.FromJson("{\"summary\":\"s\",\"confidence\":0.9,\"actions\":[{\"action_type\":\"NUKE\",\"reason\":\"r\"}]}", sim, 1);
            Assert.AreEqual(ReviewStatus.Invalid, review.Status);
            Assert.IsNotEmpty(review.ParseErrors);
            Assert.AreEqual(0, review.Approve(sim, 1).Executed);
            Assert.AreEqual(before, State(sim));
        }

        [Test]
        public void ApprovedPlanExecutesThroughTheAuthoritativeCommands()
        {
            var sim = T.Match();
            T.Give(sim, 1, wood: 300, food: 500);
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            var s = sim.SpawnUnit(1, UnitType.Spearman, new Vec2(-55, -40));
            var plan = T.Plan(T.Train(UnitType.Spearman, 3), T.Build(BuildingType.House), T.Research(TechId.SharpenedTools), T.Move(Zone.NorthGate));
            var report = ApprovedPlanExecutor.Execute(sim, 1, plan);
            Assert.AreEqual(4, report.Executed, string.Join("; ", report.Lines));
            Assert.AreEqual(0, report.Rejected);
            Assert.AreEqual(OrderType.Move, s.Order);
            Assert.IsTrue(sim.Players[1].Researching.Contains(TechId.SharpenedTools));
            Assert.AreEqual(1, sim.Entities.FindAll(e => e.OwnerId == 1 && e.Building == BuildingType.House && e.Kind == EntityKind.Building).Count);
        }

        [Test]
        public void StalePlanIsRevalidatedAndRejectedAtExecution()
        {
            var sim = T.Match();
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            var plan = T.Plan(T.Train(UnitType.Spearman, 3));
            Assert.IsTrue(GameActionValidator.Validate(plan, sim.Players[1], ValidationContext.From(sim, 1)).AllValid, "valid when proposed");
            sim.Players[1].Food = 10;                                // player spent the food one second before approving
            string before = State(sim);
            var report = ApprovedPlanExecutor.Execute(sim, 1, plan);
            Assert.AreEqual(0, report.Executed);
            Assert.AreEqual(1, report.Rejected);
            StringAssert.Contains("insufficient", report.Lines[0]);
            Assert.AreEqual(before, State(sim), "a rejected action must leave state untouched");
        }

        [Test]
        public void PartialPlanExecutesOnlyValidActions()
        {
            var sim = T.Match();
            var plan = T.Plan(T.Build(BuildingType.House), T.Train(UnitType.Guardian, 1));
            var report = ApprovedPlanExecutor.Execute(sim, 1, plan);
            Assert.AreEqual(1, report.Executed);
            Assert.AreEqual(1, report.Rejected);
            Assert.AreEqual(0, T.Count(sim, 1, UnitType.Guardian));
        }

        [Test]
        public void ExecutionAfterMatchEndIsRefused()
        {
            var sim = T.Match();
            sim.FindCitadel(2).Hp = 1;
            var ram = sim.SpawnUnit(1, UnitType.Ram, sim.FindCitadel(2).Pos + new Vec2(2, 2));
            sim.OrderAttack(1, new[] { ram.Id }, sim.FindCitadel(2).Id);
            T.Run(sim, 5);
            Assert.IsTrue(sim.Result.Ended);
            var report = ApprovedPlanExecutor.Execute(sim, 2, T.Plan(T.Build(BuildingType.House)));
            Assert.AreEqual(0, report.Executed);
        }
    }
}
