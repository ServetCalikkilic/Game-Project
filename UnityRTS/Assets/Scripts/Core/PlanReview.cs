using System.Collections.Generic;

namespace Rts.Core
{
    public enum ReviewStatus { Pending, Approved, Rejected, Invalid }

    /// <summary>
    /// Human-in-the-loop state machine for one AI plan: Pending -> Approved | Rejected. Only Approve() can touch
    /// the match, and it re-validates against the live state first. Reject() and Invalid plans never reach the
    /// simulation, so they cannot change gameplay state. A review is one-shot (no replay of an old approval).
    /// </summary>
    public sealed class PlanReview
    {
        public ReviewStatus Status { get; private set; }
        public AiActionPlan Plan { get; private set; }
        public List<string> ParseErrors { get; private set; } = new List<string>();
        /// <summary>Validation against the state at proposal time, shown to the player before they decide.</summary>
        public PlanValidation Preview { get; private set; }
        public ExecutionReport Report { get; private set; }

        public static PlanReview FromJson(string json, MatchSimulation sim, int playerId)
        {
            var review = new PlanReview();
            var parsed = ActionPlanParser.Parse(json);
            if (!parsed.Ok) { review.Status = ReviewStatus.Invalid; review.ParseErrors = parsed.Errors; return review; }
            review.Plan = parsed.Plan;
            review.Preview = GameActionValidator.Validate(parsed.Plan, sim.Players[playerId], ValidationContext.From(sim, playerId));
            review.Status = ReviewStatus.Pending;
            return review;
        }

        /// <summary>Network client: the player approved, the JSON is sent to the host, which re-validates and executes.
        /// The local review is closed so it cannot be approved twice.</summary>
        public bool MarkSubmittedToHost()
        {
            if (Status != ReviewStatus.Pending) return false;
            Status = ReviewStatus.Approved;
            return true;
        }

        public bool Reject()
        {
            if (Status != ReviewStatus.Pending) return false;
            Status = ReviewStatus.Rejected;
            return true;
        }

        public ExecutionReport Approve(MatchSimulation sim, int playerId)
        {
            if (Status != ReviewStatus.Pending)
            {
                var refused = new ExecutionReport { Rejected = 1 };
                refused.Lines.Add("plan is not pending (status: " + Status + ")");
                return refused;
            }
            Status = ReviewStatus.Approved;
            Report = ApprovedPlanExecutor.Execute(sim, playerId, Plan);
            return Report;
        }
    }
}
