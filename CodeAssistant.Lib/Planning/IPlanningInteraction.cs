using CodeAssistant.Lib.Models;

namespace CodeAssistant.Lib.Planning
{
    /// <summary>Host-side callbacks used by <see cref="IFeaturePlanner"/> to display progress and talk to the user.</summary>
    public interface IPlanningInteraction
    {
        void OnPhaseChanged(PlanningPhase phase);

        /// <summary>A complete assistant message (markdown), delivered at the end of a turn or before a question.</summary>
        void OnAssistantMessage(string markdown);

        /// <summary>The LLM session reported usage: cost of the current turn so far and of the whole planning session, in US dollars (AI credits / 100).</summary>
        void OnUsage(decimal responseCost, decimal sessionCost);

        void OnToolInvoked(string toolName, string summary);

        Task<string> AskUserAsync(PlannerQuestion question, CancellationToken cancellationToken);

        /// <summary>The agent declared <paramref name="phase"/> (Discovery or Alignment) complete; the user approves moving on or sends it back with feedback.</summary>
        Task<PlanReview> ReviewPhaseAsync(PlanningPhase phase, string markdown, CancellationToken cancellationToken);

        /// <summary>The agent submitted its plan(s), one per repository (a single plan unless planning several repositories); <paramref name="markdown"/> renders them all.</summary>
        Task<PlanReview> ReviewPlanAsync(IReadOnlyList<FeaturePlan> plans, string markdown, CancellationToken cancellationToken);
    }
}
