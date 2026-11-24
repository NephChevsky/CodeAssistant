namespace CodeAssistant.Lib.Models
{
    public class FeatureRequest
    {
        public string Description { get; set; } = string.Empty;
    }

    public sealed record PlannerQuestion(string Question, IReadOnlyList<string> Choices, bool AllowFreeform);

    /// <summary>User verdict on a submitted plan. <see cref="Feedback"/> is required when not approved.</summary>
    public sealed record PlanReview(bool Approved, string? Feedback = null);

    public sealed record FeaturePlanResult(FeaturePlan Plan, string MarkdownPath, string SessionId);

    public enum PlanningPhase
    {
        Discovery,
        Alignment,
        Design,
        Refinement,
        Completed,
    }
}
