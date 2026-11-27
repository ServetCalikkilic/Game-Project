using System;
using System.IO;
using System.Text;
using Rts.Core;
using Rts.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rts.EditorTools
{
    public static class RtsEditorTools
    {
        public const string ScenePath = "Assets/Scenes/RtsDemo.unity";

        [MenuItem("Rts/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Created " + ScenePath + " (press Play; F1 debug, F2 AI Commander).");
        }

        /// <summary>Writes logs/ai_snapshot_sample.json: a mid-game state with 4 enemy Raiders visible at the north gate.
        /// Feed it to the agent: python -m agent.cli "..." --snapshot logs/ai_snapshot_sample.json</summary>
        [MenuItem("Rts/Export Sample AI Snapshot")]
        public static void ExportSampleSnapshot()
        {
            var sim = MapSetup.CreateDemoMatch();
            var c = sim.FindCitadel(1);
            sim.SpawnBuilding(1, BuildingType.Barracks, c.Pos + new Vec2(14, -22));
            sim.SpawnBuilding(1, BuildingType.ArcheryYard, c.Pos + new Vec2(-14, -22));
            for (int i = 0; i < 3; i++) sim.SpawnUnit(1, UnitType.Spearman, c.Pos + new Vec2(i * 2, 4));
            for (int i = 0; i < 3; i++) sim.SpawnUnit(1, UnitType.Archer, c.Pos + new Vec2(i * 2, 28));    // forward post that sees the gate
            for (int i = 0; i < 4; i++) sim.SpawnUnit(2, UnitType.Raider, c.Pos + new Vec2(i * 1.5f, 38));
            sim.Players[1].Wood = 420; sim.Players[1].Food = 180; sim.Players[1].Gold = 250;
            sim.Tick(0.05f);
            var snap = sim.BuildSnapshot(1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
            string path = Path.Combine(GameContext.RepoRoot, "logs", "ai_snapshot_sample.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, snap.ToJson(), new UTF8Encoding(false));
            Debug.Log("Wrote " + path);
        }

        /// <summary>Batch entry point: unity -batchmode -quit -executeMethod Rts.EditorTools.RtsEditorTools.BatchSetup</summary>
        public static void BatchSetup()
        {
            CreateDemoScene();
            ExportSampleSnapshot();
        }
    }
}
