using System.Collections;
using System.IO;
using NUnit.Framework;
using Rts.Core;
using Rts.Core.Net;
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

        [UnityTest]
        public IEnumerator HostRole_ReplicatesFogFilteredStateToAClient()
        {
            Object.Destroy(_go);
            yield return null;
            _go = new GameObject("HostBootstrap");
            _go.SetActive(false);
            var b = _go.AddComponent<GameBootstrap>();
            b.AutoStart = false;
            _go.SetActive(true);
            Assert.IsFalse(b.Started, "AutoStart off: the network launcher decides the role");

            var net = new LoopbackNetwork();
            var host = b.StartHost(net);
            net.Host = host;
            var client = net.Connect(2);
            Assert.AreEqual(NetRole.Host, b.Ctx.Role);
            Assert.IsTrue(b.Ctx.IsAuthority);

            for (int i = 0; i < 12; i++) { yield return new WaitForSeconds(0.1f); net.Pump(); }
            Assert.Greater(host.StatesSent, 0, "SimulationDriver replicates while the host runs");
            Assert.IsTrue(client.HasState);
            Assert.IsNull(client.Replica.FindCitadel(1), "the client never receives the host's hidden base");
            Assert.IsNotNull(client.Replica.FindCitadel(2));
            Assert.IsTrue(b.Views.IsRendered(b.Ctx.Sim.FindCitadel(1).Id));
            _b = b;     // TearDown destroys _go
        }
    }
}
