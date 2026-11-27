using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Rts.Core;
using Rts.Core.Net;
using Rts.Game;

namespace Rts.Tests
{
    /// <summary>Host-mode protocol tests over the in-memory transport: same encode/chunk/decode path as the Fusion transport.</summary>
    public class NetFixture
    {
        public MatchSimulation Host;
        public HostSession Session;
        public LoopbackNetwork Net;
        public ClientSession Client;           // player 2

        public NetFixture(bool connect = true)
        {
            Host = T.Match();
            Net = new LoopbackNetwork();
            Session = new HostSession(Host, Net, 1, () => 1700000000.0);
            Net.Host = Session;
            if (connect) { Client = Net.Connect(2); Net.Pump(); }
        }

        /// <summary>Advance the host and deliver replication + commands.</summary>
        public void Run(float seconds, float dt = 0.1f)
        {
            for (float t = 0; t < seconds; t += dt) { Host.Tick(dt); Session.Replicate(dt); Net.Pump(); }
            Net.Pump();
        }

        public List<string> Notices()
        {
            var l = new List<string>();
            while (Client.TryDequeueNotice(out var n)) l.Add(n);
            return l;
        }

        public Entity ReplicaFirst(UnitType u, int owner) =>
            Client.Replica.Entities.FirstOrDefault(e => e.Alive && e.Kind == EntityKind.Unit && e.Unit == u && e.OwnerId == owner);
    }

    public class CommandCodecTests
    {
        static NetCommand RoundTrip(NetCommand c)
        {
            Assert.IsTrue(CommandCodec.TryDecode(CommandCodec.Encode(c), out var d, out var err), err);
            return d;
        }

        [Test]
        public void EveryCommandRoundTrips()
        {
            var t = RoundTrip(NetCommand.Train(UnitType.Spearman, 5, 12));
            Assert.AreEqual((CommandType.Train, UnitType.Spearman, 5, 12), (t.Type, t.Unit, t.Count, t.BuildingId));
            var b = RoundTrip(NetCommand.Build(BuildingType.Barracks, new Vec2(-40.5f, 20.25f), 7));
            Assert.AreEqual((BuildingType.Barracks, -40.5f, 20.25f, 7), (b.Building, b.X, b.Y, b.TargetId));
            Assert.AreEqual(TechId.LongbowTraining, RoundTrip(NetCommand.Research(TechId.LongbowTraining)).Tech);
            var m = RoundTrip(NetCommand.Move(new[] { 3, 4, 5 }, new Vec2(10, -20)));
            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, m.Ids); Assert.AreEqual(-20f, m.Y);
            var a = RoundTrip(NetCommand.Attack(new[] { 1 }, 99)); Assert.AreEqual(99, a.TargetId);
            Assert.AreEqual(CommandType.Gather, RoundTrip(NetCommand.Gather(new[] { 1, 2 }, 5)).Type);
            Assert.AreEqual(CommandType.Stop, RoundTrip(NetCommand.Stop(new[] { 1 })).Type);
            Assert.AreEqual("{\"x\":\"ünï\"}", RoundTrip(NetCommand.Approve("{\"x\":\"ünï\"}")).PlanJson);
            Assert.AreEqual(CommandType.RequestSnapshot, RoundTrip(NetCommand.Snapshot()).Type);
        }

        [Test]
        public void CommandsCarryNoPlayerIdentity()
        {
            foreach (var f in typeof(NetCommand).GetFields())
                StringAssert.DoesNotContain("player", f.Name.ToLowerInvariant());
        }

        [Test]
        public void MalformedCommandsAreRejectedNotThrown()
        {
            var bad = new List<byte[]>
            {
                null, new byte[0], new byte[] { 0 }, new byte[] { 99 }, new byte[] { 1 },
                new byte[] { 1, 99, 3, 0, 0, 0, 0 },                                   // unknown unit
                new byte[] { 1, 1, 0, 0, 0, 0, 0 },                                     // count 0
                new byte[] { 1, 1, 21, 0, 0, 0, 0 },                                    // count 21
                new byte[] { 1, 1, 3, 0, 0, 0, 0, 7 },                                  // trailing byte
                new byte[] { 4, 0, 0, 0, 0, 0, 0, 0, 0, 0 },                            // move with zero ids
                CommandCodec.Encode(NetCommand.Research(TechId.ForgedBlades)).Concat(new byte[] { 1 }).ToArray(),
            };
            foreach (var b in bad)
            {
                Assert.IsFalse(CommandCodec.TryDecode(b, out var c, out var err), BitConverter.ToString(b ?? new byte[0]));
                Assert.IsNull(c); Assert.IsNotEmpty(err);
            }
            // out-of-range coordinate, NaN, too many ids
            var big = CommandCodec.Encode(NetCommand.Move(new[] { 1 }, new Vec2(5000, 0)));
            Assert.IsFalse(CommandCodec.TryDecode(big, out _, out _));
            var nan = CommandCodec.Encode(NetCommand.Move(new[] { 1 }, new Vec2(float.NaN, 0)));
            Assert.IsFalse(CommandCodec.TryDecode(nan, out _, out _));
            var many = NetCommand.Stop(Enumerable.Range(0, 200));
            Assert.AreEqual(CommandCodec.MaxIds, many.Ids.Count, "builder caps the id list");
        }

        [Test]
        public void RandomBytesNeverThrow()
        {
            var rnd = new Random(1234);
            for (int i = 0; i < 5000; i++)
            {
                var b = new byte[rnd.Next(0, 64)];
                rnd.NextBytes(b);
                if (b.Length > 0 && i % 2 == 0) b[0] = (byte)rnd.Next(1, 10);
                Assert.DoesNotThrow(() => CommandCodec.TryDecode(b, out _, out _));
                Assert.DoesNotThrow(() => StateCodec.TryDecode(b, out _, out _));
            }
        }
    }

    public class ChunkingTests
    {
        [Test]
        public void SplitAndReassembleIncludingReorderAndDuplicates()
        {
            var rnd = new Random(7);
            foreach (int size in new[] { 0, 1, 399, 400, 401, 1234, 9000 })
            {
                var payload = new byte[size]; rnd.NextBytes(payload);
                var chunks = Chunker.Split(payload, 42);
                Assert.IsTrue(chunks.All(c => c.Length <= Chunker.DefaultMaxChunk + Chunker.HeaderSize));
                var asm = new ChunkAssembler();
                byte[] result = null;
                foreach (var c in chunks.OrderBy(_ => rnd.Next()).SelectMany(c => new[] { c, c }))   // shuffled, each delivered twice
                {
                    var r = asm.Add(c);
                    if (r != null) { Assert.IsNull(result, "must complete only once"); result = r; }
                }
                Assert.IsNotNull(result, "size " + size);
                CollectionAssert.AreEqual(payload, result);
            }
        }

        [Test]
        public void MalformedChunksAreIgnoredAndMemoryIsBounded()
        {
            var asm = new ChunkAssembler();
            Assert.IsNull(asm.Add(null));
            Assert.IsNull(asm.Add(new byte[] { 1, 0, 0 }));
            Assert.IsNull(asm.Add(new byte[] { 1, 0, 5, 3, 9 }));    // index >= count
            Assert.IsNull(asm.Add(new byte[] { 1, 0, 0, 0 }));       // count 0
            for (int id = 0; id < 100; id++) asm.Add(new byte[] { (byte)id, 0, 0, 2, 1 });   // 100 never-completing messages
            // still works afterwards
            var one = Chunker.Split(new byte[] { 5, 6, 7 }, 500)[0];
            CollectionAssert.AreEqual(new byte[] { 5, 6, 7 }, asm.Add(one));
        }
    }

    public class ReplicationTests
    {
        [Test]
        public void StateRoundTripsExactly()
        {
            var sim = T.Match();
            sim.SpawnBuilding(1, BuildingType.Barracks, new Vec2(-40, -70));
            sim.TryTrain(1, UnitType.Spearman, 2);
            sim.Players[1].Researched.Add(TechId.SharpenedTools);
            var bytes = StateCodec.Encode(StateCodec.Capture(sim, 1));
            Assert.IsTrue(StateCodec.TryDecode(bytes, out var decoded, out var err), err);
            CollectionAssert.AreEqual(bytes, StateCodec.Encode(decoded));
            Assert.AreEqual(1, decoded.Me.Id);
            Assert.AreEqual(sim.Players[1].Wood, decoded.Me.Wood);
        }

        [Test]
        public void TruncatedOrCorruptStateIsRejected()
        {
            var bytes = StateCodec.Encode(StateCodec.Capture(T.Match(), 1));
            for (int cut = 0; cut < bytes.Length; cut += Math.Max(1, bytes.Length / 40))
                Assert.IsFalse(StateCodec.TryDecode(bytes.Take(cut).ToArray(), out _, out _), "cut at " + cut);
            var longer = bytes.Concat(new byte[] { 0 }).ToArray();
            Assert.IsFalse(StateCodec.TryDecode(longer, out _, out _));
            var wrongVersion = (byte[])bytes.Clone(); wrongVersion[0] = 99;
            Assert.IsFalse(StateCodec.TryDecode(wrongVersion, out _, out _));
        }

        [Test]
        public void ClientReceivesOwnWorldAndNothingHidden()
        {
            var f = new NetFixture();
            f.Host.Players[1].Gold = 123457; f.Host.Players[1].Wood = 765431;
            f.Run(1);
            var r = f.Client.Replica;
            Assert.IsTrue(f.Client.HasState);
            Assert.IsTrue(r.IsReplica);
            Assert.IsNotNull(r.FindCitadel(2));
            Assert.IsNull(r.FindCitadel(1), "the enemy Citadel is in the fog and must not be replicated");
            Assert.IsFalse(r.Entities.Any(e => e.OwnerId == 1), "no enemy entity is visible at game start");
            Assert.IsTrue(!r.Players.ContainsKey(1) || (r.Players[1].Gold == 0 && r.Players[1].Wood == 0), "enemy economy is never replicated");
            Assert.AreEqual(f.Host.Players[2].Wood, r.Players[2].Wood);
            Assert.AreEqual(f.Host.Players[2].PopulationUsed, r.Players[2].PopulationUsed);
            Assert.AreEqual(Phase.EarlyEconomy, r.ComputePhase(2));
        }

        [Test]
        public void EnemyAppearsOnlyWhileVisibleAndNeverExposesDetail()
        {
            var f = new NetFixture();
            var c2 = f.Host.FindCitadel(2).Pos;
            var raider = f.Host.SpawnUnit(1, UnitType.Raider, c2 + new Vec2(0, 12));       // inside P2 Citadel vision (18)
            raider.Order = OrderType.Move; raider.MoveTarget = c2 + new Vec2(0, 10);       // host-only intent
            f.Run(1);
            var seen = f.ReplicaFirst(UnitType.Raider, 1);
            Assert.IsNotNull(seen, "visible enemy is replicated");
            Assert.AreEqual(OrderType.None, seen.Order, "enemy orders are not replicated");
            Assert.AreEqual(0, seen.Carry);
            raider.Pos = c2 + new Vec2(0, 90); raider.Order = OrderType.None;
            f.Run(1);
            Assert.IsNull(f.ReplicaFirst(UnitType.Raider, 1), "enemy that left vision is removed from the replica");
        }

        [Test]
        public void ReplicaMatchesHostForOwnEntities()
        {
            var f = new NetFixture();
            var w = f.Host.Entities.First(e => e.OwnerId == 2 && e.Unit == UnitType.Worker && e.Kind == EntityKind.Unit);
            f.Host.OrderGather(2, new[] { w.Id }, T.Node(f.Host, ResourceType.Wood, 2).Id);
            f.Run(20);
            var rw = f.Client.Replica.Get(w.Id);
            Assert.IsNotNull(rw);
            Assert.AreEqual(w.Pos.X, rw.Pos.X, 3f);          // within one replication interval of movement
            Assert.AreEqual(f.Host.Players[2].Wood, f.Client.Replica.Players[2].Wood, 30);
            Assert.AreEqual(f.Host.Time, f.Client.Replica.Time, 0.5f);
        }

        [Test]
        public void ReplicaNeverSimulatesOrAcceptsLocalCommands()
        {
            var f = new NetFixture();
            var r = f.Client.Replica;
            float t = r.Time; var cit = r.FindCitadel(2); int food = r.Players[2].Food;
            r.Tick(5f);
            Assert.AreEqual(t, r.Time, "Tick must not advance a replica");
            Assert.IsFalse(r.TryTrain(2, UnitType.Worker, 1).Ok);
            Assert.IsFalse(r.TryBuild(2, BuildingType.House, new Vec2(0, 0)).Ok);
            Assert.IsFalse(r.TryResearch(2, TechId.SharpenedTools).Ok);
            Assert.IsFalse(r.OrderMove(2, new[] { 1 }, default).Ok);
            Assert.AreEqual(food, r.Players[2].Food);
            Assert.Throws<InvalidOperationException>(() => new MatchSimulation().ApplyReplicatedState(new ReplicatedState()));
            Assert.Throws<ArgumentException>(() => { var rep = new MatchSimulation(); rep.MarkReplica(); new HostAuthority(rep); });
        }

        [Test]
        public void DepletedNodesDisappearFromTheReplica()
        {
            var f = new NetFixture();
            var node = T.Node(f.Host, ResourceType.Wood, 2);
            Assert.IsNotNull(f.Client.Replica.Get(node.Id), "own-base node is known");
            node.Amount = 0;
            f.Run(1);
            Assert.IsNull(f.Client.Replica.Get(node.Id));
        }

        [Test]
        public void ClientIgnoresStateForAnotherPlayer()
        {
            var f = new NetFixture();
            int before = f.Client.StatesApplied;
            var other = StateCodec.Encode(StateCodec.Capture(f.Host, 1));      // player 1's view
            var payload = new byte[other.Length + 1]; payload[0] = (byte)HostMessage.State; Array.Copy(other, 0, payload, 1, other.Length);
            foreach (var chunk in Chunker.Split(payload, 9)) f.Client.ReceiveFromHost(chunk);
            Assert.AreEqual(before, f.Client.StatesApplied);
            Assert.AreEqual(1, f.Client.MalformedMessages);
        }
    }

    public class HostAuthorityTests
    {
        [Test]
        public void ClientTrainRunsThroughTheAuthoritativeCommandAndReplicatesBack()
        {
            var f = new NetFixture();
            f.Host.SpawnBuilding(2, BuildingType.Barracks, f.Host.FindCitadel(2).Pos + new Vec2(-14, 22));
            f.Run(0.5f);
            int food = f.Host.Players[2].Food;
            f.Client.Send(NetCommand.Train(UnitType.Spearman, 3));
            f.Net.Pump();
            Assert.AreEqual(food - 150, f.Host.Players[2].Food, "host state changed through TryTrain");
            f.Run(1);
            Assert.AreEqual(food - 150, f.Client.Replica.Players[2].Food, "and was replicated back");
            f.Run(55);   // 3 spearmen x 16 s
            Assert.AreEqual(3, f.Client.Replica.Entities.Count(e => e.OwnerId == 2 && e.Unit == UnitType.Spearman && e.Kind == EntityKind.Unit));
        }

        [Test]
        public void RejectedCommandsChangeNothingAndTheClientIsTold()
        {
            var f = new NetFixture();
            f.Run(0.5f);
            string before = f.Host.Players[2].Resources.ToString() + f.Host.Entities.Count;
            f.Client.Send(NetCommand.Train(UnitType.Spearman, 1));                 // no Barracks
            f.Client.Send(NetCommand.Train(UnitType.Worker, 20));                  // too expensive
            f.Client.Send(NetCommand.Research(TechId.ForgedBlades));               // needs Age II
            f.Net.Pump();
            Assert.AreEqual(before, f.Host.Players[2].Resources.ToString() + f.Host.Entities.Count);
            var n = f.Notices();
            Assert.AreEqual(3, n.Count, string.Join("|", n));
            StringAssert.Contains("Barracks", n[0]);
        }

        [Test]
        public void ClientCannotControlTheOpponentsUnits()
        {
            var f = new NetFixture();
            var enemy = f.Host.Entities.First(e => e.OwnerId == 1 && e.Kind == EntityKind.Unit);
            var pos = enemy.Pos;
            f.Client.Send(NetCommand.Move(new[] { enemy.Id }, new Vec2(0, 0)));
            f.Client.Send(NetCommand.Stop(new[] { enemy.Id }));
            f.Net.Pump();
            f.Run(2);
            Assert.AreEqual(pos.X, enemy.Pos.X); Assert.AreEqual(pos.Y, enemy.Pos.Y);
            Assert.AreNotEqual(OrderType.Move, enemy.Order);
            StringAssert.Contains("no controllable", f.Notices()[0]);
        }

        [Test]
        public void AttackOnAHiddenEnemyIsRefusedByTheHost()
        {
            var f = new NetFixture();
            var mine = f.Host.SpawnUnit(2, UnitType.Spearman, f.Host.FindCitadel(2).Pos + new Vec2(3, 3));
            var hidden = f.Host.FindCitadel(1);
            f.Client.Send(NetCommand.Attack(new[] { mine.Id }, hidden.Id));
            f.Net.Pump();
            Assert.AreNotEqual(OrderType.Attack, mine.Order);
            StringAssert.Contains("not visible", f.Notices().Last());
        }

        [Test]
        public void BuildFarFromTheCitadelIsRejected_NearIsAcceptedAndReplicated()
        {
            var f = new NetFixture();
            var cit = f.Host.FindCitadel(2).Pos;
            var worker = f.Host.Entities.First(e => e.OwnerId == 2 && e.Kind == EntityKind.Unit);
            f.Client.Send(NetCommand.Build(BuildingType.House, new Vec2(-90, -90), worker.Id));
            f.Net.Pump();
            Assert.IsFalse(f.Host.Entities.Any(e => e.OwnerId == 2 && e.Building == BuildingType.House && e.Kind == EntityKind.Building));
            StringAssert.Contains("too far", f.Notices().Last());
            var site = f.Client.Replica.FindBuildSite(2).Value;                  // the client picks a site on its replica
            f.Client.Send(NetCommand.Build(BuildingType.House, site, worker.Id));
            f.Run(1);
            Assert.IsTrue(f.Host.Entities.Any(e => e.OwnerId == 2 && e.Building == BuildingType.House && e.Kind == EntityKind.Building && !e.Complete));
            Assert.IsTrue(f.Client.Replica.Entities.Any(e => e.OwnerId == 2 && e.Building == BuildingType.House && e.Kind == EntityKind.Building && !e.Complete));
        }

        [Test]
        public void ApprovedAiPlanUsesTheSameValidatorOnTheHost()
        {
            var f = new NetFixture();
            f.Host.SpawnBuilding(2, BuildingType.Barracks, f.Host.FindCitadel(2).Pos + new Vec2(-14, 22));
            f.Run(0.5f);
            const string plan = "{\"summary\":\"s\",\"confidence\":0.8,\"actions\":[{\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":3,\"reason\":\"r\"},{\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Guardian\",\"count\":1,\"reason\":\"r\"}]}";
            f.Client.Send(NetCommand.Approve(plan));
            f.Net.Pump();
            var n = f.Notices().Last();
            StringAssert.Contains("1 executed, 1 rejected", n);
            StringAssert.Contains("requires Age 2", n);
            Assert.AreEqual(3, f.Host.Entities.Where(e => e.OwnerId == 2).SelectMany(e => e.Queue).Count(q => q.Unit == UnitType.Spearman));
        }

        [Test]
        public void InvalidOrStalePlansNeverChangeHostState()
        {
            var f = new NetFixture();
            f.Run(0.5f);
            string before = f.Host.Players[2].Resources.ToString() + f.Host.Entities.Count;
            f.Client.Send(NetCommand.Approve("{\"summary\":\"s\",\"confidence\":0.8,\"actions\":[{\"action_type\":\"NUKE\",\"reason\":\"r\"}]}"));
            f.Client.Send(NetCommand.Approve("not json"));
            f.Client.Send(NetCommand.Approve("{\"summary\":\"s\",\"confidence\":0.8,\"actions\":[{\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":99,\"reason\":\"r\"}]}"));
            f.Net.Pump();
            Assert.AreEqual(before, f.Host.Players[2].Resources.ToString() + f.Host.Entities.Count);
            Assert.AreEqual(3, f.Notices().Count(x => x.Contains("plan rejected")));
        }

        [Test]
        public void SnapshotRequestReturnsTheHostsFogFilteredSnapshotForThatPlayer()
        {
            var f = new NetFixture();
            f.Host.Players[1].Gold = 123457;
            var c2 = f.Host.FindCitadel(2).Pos;
            f.Host.SpawnUnit(1, UnitType.Raider, c2 + new Vec2(0, 14));
            f.Host.SpawnUnit(1, UnitType.Archer, c2 + new Vec2(0, 95));            // hidden
            string json = null;
            f.Client.RequestSnapshot(j => json = j);
            f.Net.Pump();
            Assert.IsNotNull(json);
            var d = (Dictionary<string, object>)MiniJson.Parse(json);
            Assert.AreEqual(1700000000.0, (double)d["captured_at"]);
            var enemies = (List<object>)d["visible_enemies"];
            Assert.AreEqual(1, enemies.Count);
            StringAssert.DoesNotContain("123457", json);
            StringAssert.DoesNotContain("Archer", json);
        }

        [Test]
        public void CommandsFromUnknownSendersAreIgnoredAndFloodsAreRateLimited()
        {
            var f = new NetFixture();
            f.Session.OnCommand(7, CommandCodec.Encode(NetCommand.Research(TechId.SharpenedTools)));      // not a joined player
            f.Session.OnCommand(1, CommandCodec.Encode(NetCommand.Research(TechId.SharpenedTools)));      // the host's own id is not a remote
            Assert.IsEmpty(f.Host.Players[1].Researching);
            f.Session.Authority.MaxCommandsPerSecond = 5;
            for (int i = 0; i < 40; i++) f.Client.Send(NetCommand.Stop(new[] { 1 }));
            f.Net.Pump();
            Assert.IsTrue(f.Session.Authority.Log.Any(l => l.Contains("rate limit")));
        }

        [Test]
        public void GarbageFromAClientDoesNotBreakTheHost()
        {
            var f = new NetFixture();
            var rnd = new Random(3);
            for (int i = 0; i < 300; i++)
            {
                var b = new byte[rnd.Next(0, 40)]; rnd.NextBytes(b);
                Assert.DoesNotThrow(() => f.Session.OnCommand(2, b));
            }
            f.Run(1);
            Assert.IsTrue(f.Client.HasState);
        }
    }

    public class NetworkedMatchTests
    {
        [Test]
        public void HostAndClientBothSeeCombatAndTheWinner()
        {
            var f = new NetFixture();
            var c1 = f.Host.FindCitadel(1);
            c1.Hp = 100;
            // player 2 (the client) sends its Ram to the enemy Citadel; fog forces it to get close first
            var ram = f.Host.SpawnUnit(2, UnitType.Ram, c1.Pos + new Vec2(3, 3));
            f.Run(0.5f);
            Assert.IsNotNull(f.Client.Replica.FindCitadel(1), "enemy Citadel is visible once the Ram is next to it");
            f.Client.Send(NetCommand.Attack(new[] { ram.Id }, c1.Id));
            f.Run(5);
            Assert.IsTrue(f.Host.Result.Ended);
            Assert.AreEqual(2, f.Host.Result.WinnerId);
            Assert.IsTrue(f.Client.Replica.Result.Ended, "the client replica shows the match result");
            Assert.AreEqual(2, f.Client.Replica.Result.WinnerId);
            Assert.IsFalse(f.Host.TryTrain(1, UnitType.Worker, 1).Ok);
        }

        [Test]
        public void ClientEconomyLoopWorksOverTheNetwork()
        {
            var f = new NetFixture();
            var workers = f.Client.Replica.Entities.Where(e => e.OwnerId == 2 && e.Kind == EntityKind.Unit && e.Unit == UnitType.Worker).Select(e => e.Id).ToList();
            var node = f.Client.Replica.Entities.First(e => e.Kind == EntityKind.ResourceNode && e.NodeResource == ResourceType.Gold && Vec2.Distance(e.Pos, f.Host.FindCitadel(2).Pos) < 40);
            int gold = f.Client.Replica.Players[2].Gold;
            f.Client.Send(NetCommand.Gather(workers, node.Id));
            f.Run(60);
            Assert.Greater(f.Client.Replica.Players[2].Gold, gold);
            Assert.AreEqual(f.Host.Players[2].Gold, f.Client.Replica.Players[2].Gold, 10);
        }

        [Test]
        public void TwoClientsInOneHostAreIsolatedFromEachOther()
        {
            // Host plays as player 1, client as player 2; each only sees its own side until units meet.
            var f = new NetFixture();
            f.Run(2);
            Assert.IsFalse(f.Client.Replica.Entities.Any(e => e.OwnerId == 1));
            var c1 = f.Host.FindCitadel(1).Pos;
            f.Host.SpawnUnit(2, UnitType.Spearman, c1 + new Vec2(0, 10));
            f.Run(1);
            Assert.IsTrue(f.Client.Replica.Entities.Any(e => e.OwnerId == 1), "after contact the client sees what its units see");
        }

        [Test]
        public void ReplicationIsCompactAndChunked()
        {
            var f = new NetFixture();
            f.Run(3);
            var bytes = StateCodec.Encode(StateCodec.Capture(f.Host, 2));
            Assert.Less(bytes.Length, 4000, "a fresh match state must stay small");
            Assert.Greater(f.Net.HostToClientMessages, 0);
            Assert.GreaterOrEqual(f.Client.StatesApplied, 10);
        }
    }

    public class CommandSinkTests
    {
        const string TrainPlan = "{\"summary\":\"s\",\"confidence\":0.8,\"actions\":[{\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":2,\"reason\":\"r\"}]}";

        [Test]
        public void ClientSinkSendsIntentsAndTheHostDecides()
        {
            var f = new NetFixture();
            f.Host.SpawnBuilding(2, BuildingType.Barracks, f.Host.FindCitadel(2).Pos + new Vec2(-14, 22));
            f.Run(0.5f);
            ICommandSink sink = new ClientCommandSink(f.Client);
            int food = f.Host.Players[2].Food;
            Assert.IsTrue(sink.Train(UnitType.Spearman, 2, 0).Ok, "the client only reports 'sent'");
            Assert.AreEqual(food, f.Host.Players[2].Food, "nothing changes until the host processes it");
            f.Net.Pump();
            Assert.AreEqual(food - 100, f.Host.Players[2].Food);
            sink.Stop(new[] { 1 });
            sink.Gather(new[] { 99999 }, 1);
            f.Net.Pump();
            Assert.IsNotEmpty(f.Notices());
        }

        [Test]
        public void ClientApprovalSendsTheReviewedJsonOnceAndTheHostRevalidates()
        {
            var f = new NetFixture();
            f.Host.SpawnBuilding(2, BuildingType.Barracks, f.Host.FindCitadel(2).Pos + new Vec2(-14, 22));
            f.Run(0.5f);
            var sink = new ClientCommandSink(f.Client);
            var review = PlanReview.FromJson(TrainPlan, f.Client.Replica, 2);
            Assert.IsTrue(review.Preview.AllValid, "the client previews against its replica");
            var report = sink.ApprovePlan(review, TrainPlan);
            Assert.AreEqual(ReviewStatus.Approved, review.Status);
            Assert.AreEqual(0, report.Executed, "execution happens on the host, not here");
            f.Net.Pump();
            Assert.AreEqual(2, f.Host.Entities.Where(e => e.OwnerId == 2).SelectMany(e => e.Queue).Count());
            Assert.AreEqual(1, sink.ApprovePlan(review, TrainPlan).Rejected, "a review cannot be submitted twice");
            f.Net.Pump();
            Assert.AreEqual(2, f.Host.Entities.Where(e => e.OwnerId == 2).SelectMany(e => e.Queue).Count());
        }

        [Test]
        public void RejectedReviewSendsNothing()
        {
            var f = new NetFixture();
            f.Host.SpawnBuilding(2, BuildingType.Barracks, f.Host.FindCitadel(2).Pos + new Vec2(-14, 22));
            f.Run(0.5f);
            var review = PlanReview.FromJson(TrainPlan, f.Client.Replica, 2);
            string before = f.Host.Players[2].Resources.ToString();
            Assert.IsTrue(review.Reject());
            Assert.AreEqual(1, new ClientCommandSink(f.Client).ApprovePlan(review, TrainPlan).Rejected);
            f.Net.Pump();
            Assert.AreEqual(before, f.Host.Players[2].Resources.ToString());
        }

        [Test]
        public void LocalSinkUsesTheSameAuthoritativeMethodsAndFollowsTheLocalPlayer()
        {
            var sim = T.Match();
            int player = 1;
            ICommandSink sink = new LocalCommandSink(sim, () => player);
            Assert.IsTrue(sink.Train(UnitType.Worker, 1, 0).Ok);
            Assert.AreEqual(150, sim.Players[1].Food);
            player = 2;
            Assert.IsTrue(sink.Train(UnitType.Worker, 1, 0).Ok);
            Assert.AreEqual(150, sim.Players[2].Food);
            Assert.AreEqual(150, sim.Players[1].Food);
            string json = null;
            sink.RequestSnapshot(j => json = j);
            Assert.IsNotNull(json);
            StringAssert.Contains("\"phase\"", json);
        }
    }
}
