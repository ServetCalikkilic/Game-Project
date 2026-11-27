using System.Collections.Generic;
using System.Text;
using Rts.Core;
using UnityEngine;

namespace Rts.Game
{
    /// <summary>IMGUI HUD: resources/population/age/phase, selection info, production/research/build buttons, HP bars.</summary>
    public sealed class RtsHud : MonoBehaviour
    {
        public GameContext Ctx;
        public Camera Cam;
        public SelectionController Selection;
        public RtsCameraController CameraRig;
        public bool ShowHelp = true;

        public static float Scale => Mathf.Max(1f, Screen.height / 800f);

        /// <summary>Registers a panel rectangle (IMGUI space under the HUD scale) so world clicks under it are ignored.</summary>
        public static void Block(Rect r)
        {
            float s = Scale;
            RtsInput.AddBlocker(new Rect(r.x * s, r.y * s, r.width * s, r.height * s));
        }

        void Update()
        {
            var input = RtsInput.Instance;
            if (Ctx == null || input == null || !Ctx.Ready) return;
            if (input.Pressed(KeyCode.Tab) && Ctx.Role == NetRole.Local)
            {
                Ctx.LocalPlayer = Ctx.LocalPlayer == 1 ? 2 : 1;
                Ctx.Selection.Clear();
                Ctx.RefreshVisibility();
                var c = Ctx.Sim.FindCitadel(Ctx.LocalPlayer);
                if (c != null && CameraRig != null) CameraRig.FocusOn(GameContext.ToWorld(c.Pos));
                Ctx.Say("Now controlling player " + Ctx.LocalPlayer + " (hotseat debug view)");
            }
            if (input.Pressed(KeyCode.H)) ShowHelp = !ShowHelp;
        }

        void OnGUI()
        {
            if (Ctx == null || Ctx.Sim == null || Cam == null) return;
            if (!Ctx.Ready) { GUI.Box(new Rect(Screen.width / 2f - 170, Screen.height / 2f - 20, 340, 40), ""); GUI.Label(new Rect(Screen.width / 2f - 150, Screen.height / 2f - 10, 320, 24), string.IsNullOrEmpty(Ctx.NetStatus) ? "Waiting for the host state..." : Ctx.NetStatus); return; }
            bool repaint = Event.current.type == EventType.Repaint;
            float s = Scale;
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = Screen.height / s;

            DrawTopBar(W, repaint);
            if (repaint) DrawBars(s);
            DrawSelectionPanel(H, repaint);
            DrawToast(W, H);
            DrawDragRect(s);
            if (ShowHelp) DrawHelp(W, repaint);
            DrawResult(W, H);
            GUI.matrix = oldMatrix;
        }

        void DrawTopBar(float W, bool repaint)
        {
            var r = new Rect(0, 0, W, 26);
            GUI.Box(r, "");
            var p = Ctx.Me;
            string age = p.Age >= 2 ? "Age II" : "Age I";
            GUI.Label(new Rect(8, 3, W - 16, 22),
                $"{(Ctx.Role == NetRole.Local ? "" : Ctx.NetStatus + "   |   ")}Player {Ctx.LocalPlayer}   Wood {p.Wood}   Food {p.Food}   Gold {p.Gold}   |   Pop {p.PopulationUsed}/{p.PopulationCap}   |   {age}   |   Phase: {Ctx.Sim.ComputePhase(Ctx.LocalPlayer)}   |   t={Ctx.Sim.Time:0}s   |   F1 Debug   F2 AI Commander   Tab switch player   H help");
            if (repaint) Block(r);
        }

        void DrawBars(float s)
        {
            foreach (var e in Ctx.Sim.Entities)
            {
                if (!e.Alive || e.Kind == EntityKind.ResourceNode) continue;
                bool mine = e.OwnerId == Ctx.LocalPlayer;
                if (!mine && !Ctx.LocalVisible.CanSee(e.Id)) continue;
                bool selected = Ctx.Selection.Contains(e.Id);
                if (!selected && e.Hp >= e.MaxHp - 0.01f) continue;
                var sp = Cam.WorldToScreenPoint(new Vector3(e.Pos.X, e.Kind == EntityKind.Building ? 6.5f : 2.3f, e.Pos.Y));
                if (sp.z <= 0) continue;
                float w = e.Kind == EntityKind.Building ? 46 : 26;
                var rect = new Rect(sp.x / s - w / 2, (Screen.height - sp.y) / s, w, 4);
                GUI.color = Color.black; GUI.DrawTexture(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), Texture2D.whiteTexture);
                GUI.color = Color.Lerp(Color.red, Color.green, Mathf.Clamp01(e.Hp / e.MaxHp));
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(e.Hp / e.MaxHp), rect.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        void DrawDragRect(float s)
        {
            if (Selection == null || !Selection.Dragging) return;
            var r = Selection.DragRectGui;
            var rect = new Rect(r.x / s, r.y / s, r.width / s, r.height / s);
            GUI.color = new Color(0.4f, 1f, 0.4f, 0.25f); GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void DrawToast(float W, float H)
        {
            string t = Ctx.Toast;
            if (string.IsNullOrEmpty(t)) return;
            GUI.Box(new Rect(W / 2 - 220, H - 215, 440, 26), t);
        }

        void DrawHelp(float W, bool repaint)
        {
            var r = new Rect(W - 330, 32, 322, 150);
            GUI.Box(r, "Controls");
            GUI.Label(new Rect(r.x + 8, r.y + 20, r.width - 12, r.height - 22),
                "Left click / drag: select (Shift adds)\nRight click: move / attack (visible enemy) / gather (workers on a node)\nWASD or arrows: pan   Wheel: zoom\nS: stop   Esc: clear selection\nSelect Citadel/Barracks/...: train & research\nSelect Workers: build & gather buttons\nShift+click Train: x5");
            if (repaint) Block(r);
        }

        void DrawResult(float W, float H)
        {
            if (!Ctx.Sim.Result.Ended) return;
            var r = new Rect(W / 2 - 160, H / 2 - 40, 320, 80);
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x + 20, r.y + 28, 300, 30), Ctx.Sim.Result.WinnerId == Ctx.LocalPlayer ? "VICTORY - enemy Citadel destroyed" : "DEFEAT - your Citadel was destroyed");
        }

        // ------------------------------------------------------------ selection / commands
        void DrawSelectionPanel(float H, bool repaint)
        {
            var r = new Rect(0, H - 180, 520, 180);
            GUI.Box(r, "");
            if (repaint) Block(r);
            GUILayout.BeginArea(new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 10));

            var units = new SortedDictionary<string, int>(); Entity building = null; Entity single = null; var workers = new List<Entity>();
            foreach (int id in Ctx.Selection)
            {
                var e = Ctx.Sim.Get(id); if (e == null) continue;
                single = e;
                string key = e.Kind == EntityKind.Unit ? e.Unit.ToString() : e.Building.ToString();
                units[key] = units.TryGetValue(key, out int n) ? n + 1 : 1;
                if (e.Kind == EntityKind.Building && building == null) building = e;
                if (e.Kind == EntityKind.Unit && e.Unit == UnitType.Worker) workers.Add(e);
            }
            if (units.Count == 0) { GUILayout.Label("Nothing selected. Click or drag to select your units/buildings."); GUILayout.EndArea(); return; }

            var sb = new StringBuilder("Selected: ");
            foreach (var kv in units) sb.Append(kv.Value).Append("x ").Append(kv.Key).Append("  ");
            if (Ctx.Selection.Count == 1 && single != null)
            {
                sb.Append($"HP {single.Hp:0}/{single.MaxHp:0}");
                if (single.Kind == EntityKind.Building && !single.Complete) sb.Append($"  building {single.BuildProgress / Mathf.Max(0.1f, GameData.Buildings[single.Building].BuildTimeS) * 100:0}%");
                if (single.Kind == EntityKind.Unit && single.Carry > 0) sb.Append($"  carrying {single.Carry} {single.CarryType}");
            }
            GUILayout.Label(sb.ToString());

            if (building != null && building.Complete) BuildingButtons(building);
            if (workers.Count > 0) WorkerButtons(workers);
            GUILayout.EndArea();
        }

        void BuildingButtons(Entity b)
        {
            var def = GameData.Buildings[b.Building];
            var me = Ctx.Me;
            if (b.Queue.Count > 0) GUILayout.Label($"Queue: {b.Queue.Count} unit(s), next {b.Queue[0].Unit} in {b.Queue[0].Remaining:0}s");
            if (b.ResearchTech.HasValue) GUILayout.Label($"Researching {b.ResearchTech.Value} ({b.ResearchRemaining:0}s)");
            if (def.Trains.Length > 0)
            {
                GUILayout.BeginHorizontal();
                foreach (var u in def.Trains)
                {
                    var ud = GameData.Units[u];
                    GUI.enabled = ud.RequiredAge <= me.Age && me.CanAfford(ud.Cost);
                    if (GUILayout.Button($"Train {u}\n{ud.Cost}", GUILayout.Height(40)))
                    {
                        int n = RtsInput.Instance != null && RtsInput.Instance.ShiftHeld ? 5 : 1;
                        Ctx.Say(Ctx.Commands.Train(u, n, b.Id), "Training " + n + "x " + u);
                    }
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
            if (def.Researches.Length > 0)
            {
                GUILayout.BeginHorizontal();
                foreach (var t in def.Researches)
                {
                    var td = GameData.Techs[t];
                    bool done = me.Has(t) || me.Researching.Contains(t);
                    GUI.enabled = !done && me.CanAfford(td.Cost) && (!td.RequiresAge2 || me.Age >= 2);
                    if (GUILayout.Button($"{(done ? "[done] " : "")}{t}\n{td.Cost}", GUILayout.Height(40)))
                        Ctx.Say(Ctx.Commands.Research(t), "Researching " + t);
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
        }

        void WorkerButtons(List<Entity> workers)
        {
            var me = Ctx.Me;
            GUILayout.BeginHorizontal();
            foreach (var bt in new[] { BuildingType.House, BuildingType.Barracks, BuildingType.ArcheryYard, BuildingType.Stable })
            {
                var bd = GameData.Buildings[bt];
                GUI.enabled = me.CanAfford(bd.Cost);
                if (GUILayout.Button($"Build {bt}\n{bd.Cost}", GUILayout.Height(40)))
                {
                    var site = Ctx.Sim.FindBuildSite(Ctx.LocalPlayer);
                    if (!site.HasValue) Ctx.Say("No free build site near the Citadel");
                    else Ctx.Say(Ctx.Commands.Build(bt, site.Value, workers[0].Id), "Building " + bt);
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var res in new[] { ResourceType.Wood, ResourceType.Food, ResourceType.Gold })
                if (GUILayout.Button("Gather " + res))
                {
                    var c = Ctx.Sim.FindCitadel(Ctx.LocalPlayer);
                    Entity best = null;
                    foreach (var n in Ctx.Sim.Entities)
                        if (n.Alive && n.Kind == EntityKind.ResourceNode && n.NodeResource == res && c != null && (best == null || Vec2.Distance(n.Pos, c.Pos) < Vec2.Distance(best.Pos, c.Pos))) best = n;
                    if (best != null) Ctx.Say(Ctx.Commands.Gather(Ctx.Selection, best.Id), "Gathering " + res);
                }
            GUILayout.EndHorizontal();
        }
    }
}
