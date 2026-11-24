using System.Text;
using CodeAssistant.Lib.Models;
using CodeAssistant.Lib.Rendering;
using CodeAssistant.Lib.Tools;
using LLMLib;
using Microsoft.Extensions.AI;

namespace CodeAssistant.Lib.Planning
{
    /// <summary>Drives a planning agent through Discovery, Alignment, Design and Refinement in a single LLM session.</summary>
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

            RepositorySandbox sandbox = new(_options.RepositoryRoot, _options.ExcludedDirectories);
            MessageBuffer buffer = new(interaction);
            PlannerInteractionTools interactionTools = new(interaction, sandbox, _options.MaxClarifyingQuestions, buffer.Flush, cancellationToken);
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
                SystemMessage = PlannerPrompts.SystemMessage(sandbox.Root is not null),
                Model = _options.Model,
                ReasoningEffort = _options.ReasoningEffort,
                Tools = tools,
            }, cancellationToken);

            interaction.OnPhaseChanged(PlanningPhase.Discovery);
            await AskAsync(session, PlannerPrompts.Discovery(request.Description), buffer, cancellationToken);

            interaction.OnPhaseChanged(PlanningPhase.Alignment);
            await AskAsync(session, PlannerPrompts.Alignment(_options.MaxClarifyingQuestions), buffer, cancellationToken);

            interaction.OnPhaseChanged(PlanningPhase.Design);
            FeaturePlan plan = await RequestPlanAsync(session, PlannerPrompts.Design, interactionTools, buffer, cancellationToken);

            interaction.OnPhaseChanged(PlanningPhase.Refinement);
            string markdown;
            while (true)
            {
                markdown = FeaturePlanMarkdownRenderer.Render(plan);
                PlanReview review = await interaction.ReviewPlanAsync(plan, markdown, cancellationToken);
                if (review.Approved)
                {
                    break;
                }

                string feedback = string.IsNullOrWhiteSpace(review.Feedback) ? "Improve the plan." : review.Feedback;
                plan = await RequestPlanAsync(session, PlannerPrompts.Revision(feedback), interactionTools, buffer, cancellationToken);
            }

            (string markdownPath, string jsonPath) = GetPlanPaths(_options.GetPlansOutputDirectory(plan.RepositoryRoot), plan.Title, DateTime.Now);
            Directory.CreateDirectory(Path.GetDirectoryName(markdownPath)!);
            await File.WriteAllTextAsync(markdownPath, markdown, cancellationToken);
            await File.WriteAllTextAsync(jsonPath, FeaturePlanJson.Serialize(plan), cancellationToken);

            interaction.OnPhaseChanged(PlanningPhase.Completed);
            return new FeaturePlanResult(plan, markdownPath, jsonPath, session.SessionId);
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

        private async Task<FeaturePlan> RequestPlanAsync(ILLMSession session, string prompt, PlannerInteractionTools interactionTools, MessageBuffer buffer, CancellationToken cancellationToken)
        {
            await AskAsync(session, prompt, buffer, cancellationToken);
            for (int reminders = 0; ; reminders++)
            {
                if (interactionTools.TakeSubmittedPlan() is { } plan)
                {
                    return plan;
                }

                if (reminders >= _options.MaxSubmitRetries)
                {
                    throw new InvalidOperationException($"The agent did not submit a valid plan after {reminders} reminder(s).");
                }

                await AskAsync(session, PlannerPrompts.SubmitReminder, buffer, cancellationToken);
            }
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
