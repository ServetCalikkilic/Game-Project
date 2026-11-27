using System.IO;
using Fusion;
using Rts.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Rts.Net.EditorTools
{
    /// <summary>Creates the networked prefab and the network demo scene. Available once the Fusion SDK is imported (RTS_FUSION).</summary>
    public static class FusionSceneTools
    {
        public const string PrefabPath = "Assets/Prefabs/RtsNetObject.prefab";
        public const string ScenePath = "Assets/Scenes/RtsNetwork.unity";

        [MenuItem("Rts/Network/Create Net Prefab and Scene")]
        public static void CreateNetAssets()
        {
            Directory.CreateDirectory("Assets/Prefabs");
            Directory.CreateDirectory("Assets/Scenes");

            var go = new GameObject("RtsNetObject");
            go.AddComponent<NetworkObject>();
            go.AddComponent<RtsNetObject>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            // Registers the prefab in Fusion's prefab table (menu exists in the Fusion SDK; harmless if the path differs).
            EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var root = new GameObject("GameBootstrap");
            var bootstrap = root.AddComponent<GameBootstrap>();
            bootstrap.AutoStart = false;
            var launcher = root.AddComponent<FusionLauncher>();
            launcher.Bootstrap = bootstrap;
            launcher.NetObjectPrefab = prefab.GetComponent<NetworkObject>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene("Assets/Scenes/RtsDemo.unity", true),
            };
            Debug.Log("Created " + PrefabPath + " and " + ScenePath + ". Set your Photon AppId in Window > Fusion > Realtime Settings, then press Play.");
        }
    }
}
