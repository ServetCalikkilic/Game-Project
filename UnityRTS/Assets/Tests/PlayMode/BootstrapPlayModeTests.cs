using System.Collections;
using System.IO;
using NUnit.Framework;
using Rts.Core;
using Rts.Game;
using UnityEngine;
using UnityEngine.TestTools;

namespace Rts.Tests
{
    public class BootstrapPlayModeTests
    {
        GameObject _go;
        GameBootstrap _b;

        [SetUp]
        public void Setup()
        {
            _go = new GameObject("Bootstrap");
            _b = _go.AddComponent<GameBootstrap>();      // Awake builds the whole match + UI components
        }

        [TearDown]
        public void Teardown() { Object.Destroy(_go); }

        [UnityTest]
        public IEnumerator WorldIsBuiltAndOwnBaseIsRendered()
        {
            yield return null; yield return null;
            Assert.IsNotNull(_b.Ctx);
            Assert.Greater(_b.Views.ViewCount, 0);
            Assert.IsTrue(_b.Views.IsRendered(_b.Ctx.Sim.FindCitadel(1).Id));
        }

        [UnityTest]
        public IEnumerator FogOfWar_EnemyBaseIsNotRenderedUntilSeen()
        {
            yield return null; yield return null;
            var enemyCitadel = _b.Ctx.Sim.FindCitadel(2);
            Assert.IsFalse(_b.Views.IsRendered(enemyCitadel.Id), "hidden enemy must not be rendered");
            var c = _b.Ctx.Sim.FindCitadel(1);
            var scout = _b.Ctx.Sim.SpawnUnit(2, UnitType.Raider, c.Pos + new Vec2(0, 14));   // inside Citadel vision (18)
            yield return null; yield return null;
            Assert.IsTrue(_b.Views.IsRendered(scout.Id), "visible enemy is rendered");
            scout.Pos = c.Pos + new Vec2(0, 95);
            yield return null; yield return null;
            Assert.IsFalse(_b.Views.IsRendered(scout.Id), "enemy that left vision disappears again");
        }

        [UnityTest]
        public IEnumerator SimulationAdvancesWithFrames()
        {
            _b.Ctx.TimeScale = 8f;
            float t0 = _b.Ctx.Sim.Time;
            yield return new WaitForSeconds(0.5f);
            Assert.Greater(_b.Ctx.Sim.Time, t0);
        }

        [UnityTest]
        public IEnumerator AiPanel_ExportSnapshot_ApproveAndReject()
        {
            yield return null;
            string path = _b.AiPanel.ExportSnapshot();
            Assert.IsTrue(File.Exists(path));
            var parsed = (System.Collections.Generic.Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(path));
            Assert.AreEqual("EarlyEconomy", parsed["phase"]);

            var sim = _b.Ctx.Sim;
            sim.SpawnBuilding(1, BuildingType.Barracks, sim.FindCitadel(1).Pos + new Vec2(14, -22));
            // reject: no change
            _b.AiPanel.LoadPlanText(AiPlanPanel.SampleValidPlan);
            int food = sim.Players[1].Food;
            Assert.IsTrue(_b.AiPanel.Review.Reject());
            Assert.AreEqual(food, sim.Players[1].Food);
            // approve: executes the train action (MOVE_GROUP is rejected: no military units yet)
            _b.AiPanel.LoadPlanText(AiPlanPanel.SampleValidPlan);
            var report = _b.AiPanel.Review.Approve(sim, 1);
            Assert.AreEqual(1, report.Executed, string.Join("; ", report.Lines));
            Assert.AreEqual(food - 150, sim.Players[1].Food);
            // invalid plan never reaches the game
            _b.AiPanel.LoadPlanText(AiPlanPanel.SampleInvalidPlan);
            Assert.AreEqual(ReviewStatus.Invalid, _b.AiPanel.Review.Status);
        }
    }
}
