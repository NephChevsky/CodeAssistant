using CodeAssistant.Lib.Models;

namespace CodeAssistant.Lib.Planning
{
    public interface IFeaturePlanner
    {
        /// <summary>Runs the planning workflow until the user approves a plan, then saves it as markdown.</summary>
        Task<FeaturePlanResult> PlanAsync(FeatureRequest request, IPlanningInteraction interaction, CancellationToken cancellationToken = default);
    }
}
