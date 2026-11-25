using System.ComponentModel;
using System.Text;
using CodeAssistant.Lib.Models;
using CodeAssistant.Lib.Planning;
using CodeAssistant.Lib.Validation;
using Microsoft.Extensions.AI;

namespace CodeAssistant.Lib.Tools
{
    /// <summary>Per-run tools letting the agent ask the user questions, declare a phase complete and submit its plan.</summary>
    public sealed class PlannerInteractionTools
    {
        public const string AskUserName = "ask_user";
        public const string CompleteDiscoveryName = "complete_discovery";
        public const string CompleteAlignmentName = "complete_alignment";
        public const string SubmitPlanName = "submit_plan";

        private readonly IPlanningInteraction _interaction;
        private readonly RepositorySandbox _sandbox;
        private readonly Action _flushMessages;
        private readonly CancellationToken _cancellationToken;
        private readonly bool _multiRepository;
        private readonly object _plansLock = new();
        private List<FeaturePlan> _submittedPlans = [];
        private string? _phaseSummary;

        /// <param name="flushMessages">Invoked before a question or a tool event is shown, so pending assistant text is delivered first.</param>
        /// <param name="multiRepository">When true, each submitted plan names its own git repository (repositoryRoot) inside the sandbox.</param>
        public PlannerInteractionTools(IPlanningInteraction interaction, RepositorySandbox sandbox, Action flushMessages, CancellationToken cancellationToken, bool multiRepository = false)
        {
            _interaction = interaction;
            _sandbox = sandbox;
            _flushMessages = flushMessages;
            _cancellationToken = cancellationToken;
            _multiRepository = multiRepository;
        }

        /// <summary>Current phase. Completion tools of other phases are rejected.</summary>
        public PlanningPhase Phase { get; set; } = PlanningPhase.Discovery;

        public static string CompletionToolName(PlanningPhase phase)
        {
            return phase switch
            {
                PlanningPhase.Discovery => CompleteDiscoveryName,
                PlanningPhase.Alignment => CompleteAlignmentName,
                _ => SubmitPlanName,
            };
        }

        public IReadOnlyList<AIFunction> CreateFunctions()
        {
            return
            [
                AIFunctionFactory.Create(AskUserAsync, AskUserName, "Ask the user a clarifying question and wait for the answer.", FeaturePlanJson.Options),
                AIFunctionFactory.Create(CompleteDiscovery, CompleteDiscoveryName, "Declare the discovery phase complete, once you understand everything relevant to the request. The user reviews your findings before alignment starts.", FeaturePlanJson.Options),
                AIFunctionFactory.Create(CompleteAlignment, CompleteAlignmentName, "Declare the alignment phase complete, once every decision that shapes the plan is settled. The user reviews them before design starts.", FeaturePlanJson.Options),
                AIFunctionFactory.Create(SubmitPlan, SubmitPlanName, "Submit the complete feature plan for validation and user review.", FeaturePlanJson.Options),
            ];
        }

        /// <summary>Returns the markdown summary of the last accepted phase completion since the previous call, or null.</summary>
        public string? TakePhaseSummary()
        {
            return Interlocked.Exchange(ref _phaseSummary, null);
        }

        /// <summary>Returns the valid plans submitted since the previous call (one per repository, the last submission winning), or null when there are none.</summary>
        public IReadOnlyList<FeaturePlan>? TakeSubmittedPlans()
        {
            lock (_plansLock)
            {
                if (_submittedPlans.Count == 0)
                {
                    return null;
                }

                List<FeaturePlan> plans = _submittedPlans;
                _submittedPlans = [];
                return plans;
            }
        }

        public async Task<string> AskUserAsync(
            [Description("The question, self-contained and concise.")] string question,
            [Description("Suggested answers, if any.")] string[]? choices = null,
            [Description("Whether the user may type an answer other than the choices.")] bool allowFreeform = true)
        {
            _flushMessages();
            string answer = await _interaction.AskUserAsync(new PlannerQuestion(question, choices ?? [], allowFreeform || choices is not { Length: > 0 }), _cancellationToken);
            return string.IsNullOrWhiteSpace(answer) ? "(no answer, use your best judgement)" : answer;
        }

        public string CompleteDiscovery(
            [Description("Concise summary of your findings: relevant architecture, patterns and conventions to follow, constraints.")] string summary,
            [Description("Existing files and directories relevant to the request.")] string[]? relevantFiles = null,
            [Description("Open questions that need the user's input during alignment. Empty when there are none.")] string[]? openQuestions = null)
        {
            if (Phase != PlanningPhase.Discovery)
            {
                return RejectWrongPhase(CompleteDiscoveryName);
            }

            relevantFiles ??= [];
            List<string> errors = [];
            if (string.IsNullOrWhiteSpace(summary))
            {
                errors.Add("summary is required.");
            }

            errors.AddRange(relevantFiles
                .Where(f => !FeaturePlanValidator.FileExists(_sandbox, f))
                .Select(f => $"relevant file '{f}' does not exist or is not accessible."));
            if (errors.Count > 0)
            {
                return Reject(CompleteDiscoveryName, errors);
            }

            StringBuilder markdown = new StringBuilder().AppendLine(summary.Trim());
            AppendList(markdown, "Relevant files", relevantFiles.Select(f => $"`{f}`"));
            AppendList(markdown, "Open questions", openQuestions ?? []);
            return Accept(CompleteDiscoveryName, markdown.ToString());
        }

        public string CompleteAlignment(
            [Description("Decisions taken with the user, each with its rationale.")] string[]? decisions = null,
            [Description("Assumptions you made where the user did not decide.")] string[]? assumptions = null)
        {
            if (Phase != PlanningPhase.Alignment)
            {
                return RejectWrongPhase(CompleteAlignmentName);
            }

            decisions ??= [];
            assumptions ??= [];
            if (!decisions.Concat(assumptions).Any(s => !string.IsNullOrWhiteSpace(s)))
            {
                return Reject(CompleteAlignmentName, ["list at least one decision or assumption."]);
            }

            StringBuilder markdown = new();
            AppendList(markdown, "Decisions", decisions);
            AppendList(markdown, "Assumptions", assumptions);
            return Accept(CompleteAlignmentName, markdown.ToString().Trim());
        }

        public string SubmitPlan([Description("The complete plan. In multi-repository mode, one plan per repository, with repositoryRoot set to that repository's absolute path.")] FeaturePlan plan)
        {
            if (Phase is not (PlanningPhase.Design or PlanningPhase.Refinement))
            {
                return RejectWrongPhase(SubmitPlanName);
            }

            if (_sandbox.Root is not null && !_multiRepository)
            {
                plan.RepositoryRoot = _sandbox.Root;
            }

            IReadOnlyList<string> errors = FeaturePlanValidator.Validate(plan, _sandbox, _multiRepository);
            _flushMessages();
            _interaction.OnToolInvoked(SubmitPlanName, errors.Count == 0 ? "plan is valid" : $"{errors.Count} validation error(s)");
            if (errors.Count > 0)
            {
                return $"The plan is invalid. Fix these errors and call {SubmitPlanName} again with the full plan:\n- " + string.Join("\n- ", errors);
            }

            plan.RepositoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.RepositoryRoot));
            lock (_plansLock)
            {
                _submittedPlans.RemoveAll(p => string.Equals(p.RepositoryRoot, plan.RepositoryRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                _submittedPlans.Add(plan);
            }

            return _multiRepository
                ? "Plan accepted. Submit the plans of the other affected repositories, then stop and wait for the user's review."
                : "Plan accepted. Stop here and wait for the user's review.";
        }

        private static void AppendList(StringBuilder markdown, string title, IEnumerable<string> items)
        {
            List<string> values = [.. items.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim())];
            if (values.Count == 0)
            {
                return;
            }

            markdown.AppendLine().AppendLine($"### {title}").AppendLine();
            values.ForEach(v => markdown.AppendLine($"- {v}"));
        }

        private string Accept(string toolName, string markdown)
        {
            _flushMessages();
            _interaction.OnToolInvoked(toolName, "phase complete, waiting for the user");
            Volatile.Write(ref _phaseSummary, markdown);
            return "Recorded. Stop here and wait for the user's review.";
        }

        private string Reject(string toolName, IReadOnlyList<string> errors)
        {
            _flushMessages();
            _interaction.OnToolInvoked(toolName, $"{errors.Count} validation error(s)");
            return $"Rejected. Fix these errors and call {toolName} again:\n- " + string.Join("\n- ", errors);
        }

        private string RejectWrongPhase(string toolName)
        {
            _flushMessages();
            _interaction.OnToolInvoked(toolName, $"rejected, the current phase is {Phase}");
            return $"{toolName} cannot be called now: the current phase is {Phase}. Complete it with {CompletionToolName(Phase)}.";
        }
    }
}
