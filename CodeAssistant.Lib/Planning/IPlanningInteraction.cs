using CodeAssistant.Lib.Models;

namespace CodeAssistant.Lib.Planning
{
    /// <summary>Host-side callbacks used by <see cref="IFeaturePlanner"/> to display progress and talk to the user.</summary>
    public interface IPlanningInteraction
    {
        void OnPhaseChanged(PlanningPhase phase);

        /// <summary>A complete assistant message (markdown), delivered at the end of a turn or before a question.</summary>
        void OnAssistantMessage(string markdown);

        void OnToolInvoked(string toolName, string summary);

        Task<string> AskUserAsync(PlannerQuestion question, CancellationToken cancellationToken);

        Task<PlanReview> ReviewPlanAsync(FeaturePlan plan, string markdown, CancellationToken cancellationToken);
    }
}
