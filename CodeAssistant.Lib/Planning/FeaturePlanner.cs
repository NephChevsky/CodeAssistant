using System.Text;
using CodeAssistant.Lib.Models;
using CodeAssistant.Lib.Rendering;
using CodeAssistant.Lib.Tools;
using LLMLib;
using Microsoft.Extensions.AI;

namespace CodeAssistant.Lib.Planning
{
    /// <summary>
    /// Drives a planning agent through Discovery, Alignment, Design and Refinement in a single LLM session.
    /// A phase ends only when the agent declares it complete (completion tool or submit_plan) and the user agrees.
    /// </summary>
    public sealed class FeaturePlanner : IFeaturePlanner
    {
        private const int MaxSlugLength = 60;
        private const string MarkdownExtension = ".plan.md";
        private const string JsonExtension = ".plan.json";

        private readonly ILLMService _llm;
        private readonly PlannerOptions _options;

        public FeaturePlanner(ILLMService llm, PlannerOptions options)
        {
            _llm = llm;
            _options = options;
        }

        public async Task<FeaturePlanResult> PlanAsync(FeatureRequest request, IPlanningInteraction interaction, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Description);

            RepositorySandbox sandbox = new(request.RepositoryRoot ?? _options.RepositoryRoot, _options.ExcludedDirectories);
            bool multiRepository = request.MultiRepository && sandbox.Root is not null;
            MessageBuffer buffer = new(interaction);
            PlannerInteractionTools interactionTools = new(interaction, sandbox, buffer.Flush, cancellationToken, multiRepository);
            List<AIFunction> tools =
            [
                .. ReadOnlyRepositoryTools.Create(sandbox, _options, (name, summary) =>
                {
                    buffer.Flush();
                    interaction.OnToolInvoked(name, summary);
                }),
                .. interactionTools.CreateFunctions(),
            ];

            await using ILLMSession session = await _llm.CreateSessionAsync(new LLMSessionOptions
            {
                SystemMessage = PlannerPrompts.SystemMessage(sandbox.Root is not null, multiRepository),
                Model = request.Model ?? _options.Model,
                ReasoningEffort = request.ReasoningEffort ?? _options.ReasoningEffort,
                Tools = tools,
            }, cancellationToken);

            await RunPhaseAsync(session, PlanningPhase.Discovery, PlannerPrompts.Discovery(request.Description), interactionTools, interaction, buffer, cancellationToken);
            await RunPhaseAsync(session, PlanningPhase.Alignment, PlannerPrompts.Alignment, interactionTools, interaction, buffer, cancellationToken);

            interaction.OnPhaseChanged(PlanningPhase.Design);
            interactionTools.Phase = PlanningPhase.Design;
            List<FeaturePlan> plans = [.. await AwaitCompletionAsync(session, PlannerPrompts.Design(multiRepository), interactionTools.TakeSubmittedPlans, PlannerPrompts.SubmitReminder, buffer, cancellationToken)];

            interaction.OnPhaseChanged(PlanningPhase.Refinement);
            interactionTools.Phase = PlanningPhase.Refinement;
            string markdown;
            while (true)
            {
                markdown = string.Join("\n\n---\n\n", plans.Select(FeaturePlanMarkdownRenderer.Render));
                PlanReview review = await interaction.ReviewPlanAsync(plans, markdown, cancellationToken);
                if (review.Approved)
                {
                    break;
                }

                IReadOnlyList<FeaturePlan> revised = await AwaitCompletionAsync(session, PlannerPrompts.Revision(GetFeedback(review), multiRepository), interactionTools.TakeSubmittedPlans, PlannerPrompts.SubmitReminder, buffer, cancellationToken);
                foreach (FeaturePlan plan in revised)
                {
                    int index = plans.FindIndex(p => string.Equals(p.RepositoryRoot, plan.RepositoryRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                    if (index >= 0)
                    {
                        plans[index] = plan;
                    }
                    else
                    {
                        plans.Add(plan);
                    }
                }
            }

            List<PlanFile> files = [];
            foreach (FeaturePlan plan in plans)
            {
                (string markdownPath, string jsonPath) = GetPlanPaths(_options.GetPlansOutputDirectory(plan.RepositoryRoot), plan.Title, DateTime.Now);
                Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
                await File.WriteAllTextAsync(markdownPath, FeaturePlanMarkdownRenderer.Render(plan), cancellationToken);
                await File.WriteAllTextAsync(jsonPath, FeaturePlanJson.Serialize(plan), cancellationToken);
                files.Add(new PlanFile(plan, markdownPath, jsonPath));
            }

            interaction.OnPhaseChanged(PlanningPhase.Completed);
            return new FeaturePlanResult(files, session.SessionId);
        }

        /// <summary>Builds unique <c>yyyyMMdd-slug.plan.md</c> / <c>.plan.json</c> paths that are guaranteed to be directly inside <paramref name="outputDirectory"/>.</summary>
        internal static (string MarkdownPath, string JsonPath) GetPlanPaths(string outputDirectory, string title, DateTime date)
        {
            string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
            string baseName = $"{date:yyyyMMdd}-{Slugify(title)}";
            string basePath = Path.Combine(directory, baseName);
            for (int i = 2; File.Exists(basePath + MarkdownExtension) || File.Exists(basePath + JsonExtension); i++)
            {
                basePath = Path.Combine(directory, $"{baseName}-{i}");
            }

            string fullBasePath = Path.GetFullPath(basePath);
            if (!string.Equals(Path.GetDirectoryName(fullBasePath), directory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Plan path '{fullBasePath}' escapes the output directory.");
            }

            return (fullBasePath + MarkdownExtension, fullBasePath + JsonExtension);
        }

        /// <summary>Lowercase ASCII letters and digits separated by single dashes; "plan" when nothing remains.</summary>
        internal static string Slugify(string title)
        {
            StringBuilder slug = new();
            foreach (char c in title.ToLowerInvariant())
            {
                if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                {
                    slug.Append(c);
                }
                else if (slug.Length > 0 && slug[^1] != '-')
                {
                    slug.Append('-');
                }

                if (slug.Length >= MaxSlugLength)
                {
                    break;
                }
            }

            string result = slug.ToString().Trim('-');
            return result.Length == 0 ? "plan" : result;
        }

        /// <summary>Runs a reviewed phase: the agent works until it declares the phase complete, then the user approves or sends it back with feedback.</summary>
        private async Task RunPhaseAsync(ILLMSession session, PlanningPhase phase, string prompt, PlannerInteractionTools interactionTools, IPlanningInteraction interaction, MessageBuffer buffer, CancellationToken cancellationToken)
        {
            interaction.OnPhaseChanged(phase);
            interactionTools.Phase = phase;
            string reminder = PlannerPrompts.CompletionReminder(phase);
            while (true)
            {
                string summary = await AwaitCompletionAsync(session, prompt, interactionTools.TakePhaseSummary, reminder, buffer, cancellationToken);
                PlanReview review = await interaction.ReviewPhaseAsync(phase, summary, cancellationToken);
                if (review.Approved)
                {
                    return;
                }

                prompt = PlannerPrompts.PhaseFeedback(phase, GetFeedback(review));
            }
        }

        /// <summary>Sends <paramref name="prompt"/>, then reminds the agent until it signals completion through a tool (<paramref name="takeResult"/> returns non-null).</summary>
        private async Task<T> AwaitCompletionAsync<T>(ILLMSession session, string prompt, Func<T?> takeResult, string reminder, MessageBuffer buffer, CancellationToken cancellationToken)
            where T : class
        {
            await AskAsync(session, prompt, buffer, cancellationToken);
            for (int reminders = 0; ; reminders++)
            {
                if (takeResult() is { } result)
                {
                    return result;
                }

                if (reminders >= _options.MaxCompletionReminders)
                {
                    throw new InvalidOperationException($"The agent did not complete the phase after {reminders} reminder(s).");
                }

                await AskAsync(session, reminder, buffer, cancellationToken);
            }
        }

        private static string GetFeedback(PlanReview review)
        {
            return string.IsNullOrWhiteSpace(review.Feedback) ? "Improve it." : review.Feedback;
        }

        private static async Task AskAsync(ILLMSession session, string prompt, MessageBuffer buffer, CancellationToken cancellationToken)
        {
            await foreach (string chunk in session.AskStreamingAsync(prompt, cancellationToken: cancellationToken))
            {
                buffer.Append(chunk);
            }

            buffer.Flush();
        }

        /// <summary>Accumulates streamed chunks and delivers them as whole messages.</summary>
        private sealed class MessageBuffer
        {
            private readonly IPlanningInteraction _interaction;
            private readonly StringBuilder _pending = new();
            private readonly Lock _lock = new();

            public MessageBuffer(IPlanningInteraction interaction)
            {
                _interaction = interaction;
            }

            public void Append(string chunk)
            {
                lock (_lock)
                {
                    _pending.Append(chunk);
                }
            }

            public void Flush()
            {
                string text;
                lock (_lock)
                {
                    text = _pending.ToString();
                    _pending.Clear();
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    _interaction.OnAssistantMessage(text.Trim());
                }
            }
        }
    }
}
