using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Rts.EditorTools
{
    /// <summary>
    /// Keeps the RTS_FUSION scripting define in sync with the Photon Fusion SDK: defined when the Fusion.Runtime assembly is
    /// present (so the Rts.Net.Fusion assembly compiles), removed when it is not (so the project still builds without the SDK).
    /// </summary>
    [InitializeOnLoad]
    public static class FusionDefineSymbol
    {
        public const string Symbol = "RTS_FUSION";

        static FusionDefineSymbol()
        {
            if (Apply(IsFusionInstalled())) Debug.Log("[Rts] Updated scripting define " + Symbol + " for the Photon Fusion SDK.");
        }

        public static bool IsFusionInstalled() =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Fusion.Runtime");

        /// <summary>Returns true if the defines changed (a recompile follows).</summary>
        public static bool Apply(bool enabled)
        {
            bool changed = false;
            foreach (var target in new[] { NamedBuildTarget.Standalone })
            {
                var defines = new List<string>(PlayerSettings.GetScriptingDefineSymbols(target).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                bool has = defines.Contains(Symbol);
                if (enabled == has) continue;
                if (enabled) defines.Add(Symbol); else defines.Remove(Symbol);
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
                changed = true;
            }
            return changed;
        }

        [MenuItem("Rts/Network/Fusion SDK Status")]
        public static void ReportStatus()
        {
            Debug.Log(IsFusionInstalled()
                ? "[Rts] Photon Fusion SDK detected. Network scripts are enabled (define " + Symbol + ")."
                : "[Rts] Photon Fusion SDK NOT found. Import the Fusion 2 .unitypackage from the Photon dashboard (see docs/unity_setup.md). The game still runs offline.");
        }
    }
}
