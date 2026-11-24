namespace CodeAssistant.Lib.Models
{
    public class FeatureRequest
    {
        public string Description { get; set; } = string.Empty;

        /// <summary>Repository the planner may read for this request. Falls back to <c>PlannerOptions.RepositoryRoot</c> when null.</summary>
        public string? RepositoryRoot { get; set; }

        /// <summary>Falls back to <c>PlannerOptions.Model</c> when null.</summary>
        public string? Model { get; set; }

        /// <summary>Falls back to <c>PlannerOptions.ReasoningEffort</c> when null.</summary>
        public string? ReasoningEffort { get; set; }
    }

    public sealed record PlannerQuestion(string Question, IReadOnlyList<string> Choices, bool AllowFreeform);

    /// <summary>User verdict on a submitted plan. <see cref="Feedback"/> is required when not approved.</summary>
    public sealed record PlanReview(bool Approved, string? Feedback = null);

    public sealed record FeaturePlanResult(FeaturePlan Plan, string MarkdownPath, string JsonPath, string SessionId);

    public enum PlanningPhase
    {
        Discovery,
        Alignment,
        Design,
        Refinement,
        Completed,
    }
}
