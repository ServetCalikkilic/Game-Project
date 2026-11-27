using System;
using System.IO;
using System.Text;
using Rts.Core;
using UnityEngine;

namespace Rts.Game
{
    /// <summary>
    /// "AI Commander" window (F2): exports the AiSafeSnapshot for the Python gateway, loads a proposed ActionPlan JSON,
    /// shows per-action validation, and lets the player APPROVE or REJECT. Only APPROVE touches the simulation, and it
    /// re-validates first (PlanReview -> GameActionValidator -> ApprovedPlanExecutor).
    /// Dev loop:  F2 > Export snapshot  ->  python -m agent.cli "question" --snapshot logs/ai_snapshot.json --plan-out logs/ai_plan.json
    ///            F2 > Load plan file  ->  Approve / Reject.
    /// </summary>
    public sealed class AiPlanPanel : MonoBehaviour
    {
        public GameContext Ctx;
        public bool Visible;
        public PlanReview Review { get; private set; }

        string _planJson = "";
        string _reviewedJson = "";   // exactly what the player reviewed; this is what APPROVE submits
        string _status = "";
        string _log = "";
        Vector2 _scroll;

        public static string SnapshotPath => Path.Combine(GameContext.RepoRoot, "logs", "ai_snapshot.json");
        public static string PlanPath => Path.Combine(GameContext.RepoRoot, "logs", "ai_plan.json");

        public const string SampleValidPlan =
            "{\"summary\":\"Counter the visible cavalry with Spearmen.\",\"confidence\":0.84,\"evidence\":[\"unit_spearman_v1\"],\"actions\":[" +
            "{\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":3,\"reason\":\"Visible enemy force is cavalry-heavy.\"}," +
            "{\"action_type\":\"MOVE_GROUP\",\"target_zone\":\"NorthGate\",\"reason\":\"Concentrate anti-cavalry units at the threatened area.\"}]}";
        public const string SampleInvalidPlan =
            "{\"summary\":\"Bad plan\",\"confidence\":0.9,\"actions\":[{\"action_type\":\"TRAIN_UNIT\",\"unit_type\":\"Spearman\",\"count\":-4,\"reason\":\"negative count\"}," +
            "{\"action_type\":\"LAUNCH_NUKE\",\"reason\":\"unknown action\"}]}";

        void Update()
        {
            var input = RtsInput.Instance;
            if (input != null && input.Pressed(KeyCode.F2)) Visible = !Visible;
            if (input != null && input.Pressed(KeyCode.F9) && Ctx != null && Ctx.Ready) ExportSnapshot();
        }

        /// <summary>Asks the authoritative side for this player's AiSafeSnapshot and writes it to logs/ai_snapshot.json.
        /// Local/Host: immediate. Client: the host builds it (it knows phase and events) and the file is written when it arrives.</summary>
        public string ExportSnapshot()
        {
            string written = null;
            try
            {
                Ctx.Commands.RequestSnapshot(json => written = WriteSnapshot(json));
                if (written == null && Ctx.Role == NetRole.Client) _status = "Snapshot requested from the host...";
            }
            catch (Exception ex) { _status = "Export failed: " + ex.Message; }
            return written;
        }

        string WriteSnapshot(string json)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath));
                File.WriteAllText(SnapshotPath, json, new UTF8Encoding(false));
                _status = $"Snapshot saved: {SnapshotPath} ({json.Length} bytes)";
                return SnapshotPath;
            }
            catch (Exception ex) { _status = "Export failed: " + ex.Message; return null; }
        }

        public void LoadPlanText(string json)
        {
            _planJson = json;
            _reviewedJson = json;
            Review = PlanReview.FromJson(json, Ctx.Sim, Ctx.LocalPlayer);
            _log = "";
        }

        void OnGUI()
        {
            if (!Visible || Ctx == null || !Ctx.Ready) return;
            float s = RtsHud.Scale;
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = Screen.height / s;
            var r = new Rect(W - 480, 190, 470, Mathf.Min(520, H - 380));
            GUI.Box(r, "AI Strategic Commander (proposals only: you approve, the game validates)");
            if (Event.current.type == EventType.Repaint) RtsHud.Block(r);

            GUILayout.BeginArea(new Rect(r.x + 8, r.y + 22, r.width - 16, r.height - 28));
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Export snapshot (F9)")) ExportSnapshot();
            if (GUILayout.Button("Load plan file")) LoadFile();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Paste valid sample")) LoadPlanText(SampleValidPlan);
            if (GUILayout.Button("Paste invalid sample")) LoadPlanText(SampleInvalidPlan);
            GUILayout.EndHorizontal();
            string edited = GUILayout.TextArea(_planJson, GUILayout.MinHeight(70));
            if (edited != _planJson) _planJson = edited;
            if (GUILayout.Button("Validate pasted JSON")) LoadPlanText(_planJson);

            DrawReview();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = oldMatrix;
        }

        void LoadFile()
        {
            try { LoadPlanText(File.ReadAllText(PlanPath)); }
            catch (Exception ex) { _status = "Could not read " + PlanPath + ": " + ex.Message; }
        }

        void DrawReview()
        {
            if (Review == null) return;
            if (Review.Status == ReviewStatus.Invalid)
            {
                GUILayout.Label("Plan REJECTED by schema/parser (never reaches the game):");
                foreach (var e in Review.ParseErrors) GUILayout.Label(" - " + e);
                return;
            }
            var plan = Review.Plan;
            GUILayout.Label($"{plan.Summary}\nConfidence {plan.Confidence:0.00}" + (string.IsNullOrEmpty(plan.Limitations) ? "" : "\nLimitations: " + plan.Limitations)
                            + (plan.Evidence.Count > 0 ? "\nEvidence: " + string.Join(", ", plan.Evidence) : ""));
            foreach (var e in Review.Preview.PlanErrors) GUILayout.Label("PLAN ERROR: " + e);
            foreach (var c in Review.Preview.Checks)
                GUILayout.Label($"[{(c.Ok ? "OK" : "X")}] {c.Action}  {(c.Ok ? "cost " + c.Cost : "- " + c.Reason)}\n      why: {c.Action.Reason}");
            GUILayout.Label("Total cost of valid actions: " + Review.Preview.TotalCost);

            if (Review.Status == ReviewStatus.Pending)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = Review.Preview.PlanErrors.Count == 0;
                if (GUILayout.Button("APPROVE", GUILayout.Height(30))) Approve();
                GUI.enabled = true;
                if (GUILayout.Button("REJECT", GUILayout.Height(30))) { Review.Reject(); _log = "Rejected by the player. No game state was changed."; Ctx.Say("AI plan rejected"); }
                GUILayout.EndHorizontal();
                GUILayout.Label("Approve re-validates against the current game state; invalid actions are skipped.");
            }
            else GUILayout.Label("Review status: " + Review.Status);
            if (!string.IsNullOrEmpty(_log)) GUILayout.Label(_log);
        }

        void Approve()
        {
            var report = Ctx.Commands.ApprovePlan(Review, _reviewedJson);
            _log = (Ctx.Role == NetRole.Client ? "Submitted. " : "Approved. ") + "Executed " + report.Executed + ", rejected " + report.Rejected + ":\n" + string.Join("\n", report.Lines);
            Ctx.Say($"AI plan approved: {report.Executed} executed, {report.Rejected} rejected");
        }
    }
}
