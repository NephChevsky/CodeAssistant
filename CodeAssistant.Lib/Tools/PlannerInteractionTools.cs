using System.ComponentModel;
using CodeAssistant.Lib.Models;
using CodeAssistant.Lib.Planning;
using CodeAssistant.Lib.Validation;
using Microsoft.Extensions.AI;

namespace CodeAssistant.Lib.Tools
{
    /// <summary>Per-run tools letting the agent ask the user questions and submit its plan.</summary>
    public sealed class PlannerInteractionTools
    {
        public const string AskUserName = "ask_user";
        public const string SubmitPlanName = "submit_plan";

        private readonly IPlanningInteraction _interaction;
        private readonly RepositorySandbox _sandbox;
        private readonly int _maxQuestions;
        private readonly Action _beforeQuestion;
        private readonly CancellationToken _cancellationToken;
        private int _questionsAsked;
        private FeaturePlan? _submittedPlan;

        /// <param name="beforeQuestion">Invoked before a question is shown, so pending assistant text can be delivered first.</param>
        public PlannerInteractionTools(IPlanningInteraction interaction, RepositorySandbox sandbox, int maxQuestions, Action beforeQuestion, CancellationToken cancellationToken)
        {
            _interaction = interaction;
            _sandbox = sandbox;
            _maxQuestions = maxQuestions;
            _beforeQuestion = beforeQuestion;
            _cancellationToken = cancellationToken;
        }

        public IReadOnlyList<AIFunction> CreateFunctions()
        {
            return
            [
                AIFunctionFactory.Create(AskUserAsync, AskUserName, "Ask the user a clarifying question and wait for the answer.", FeaturePlanJson.Options),
                AIFunctionFactory.Create(SubmitPlan, SubmitPlanName, "Submit the complete feature plan for validation and user review.", FeaturePlanJson.Options),
            ];
        }

        /// <summary>Returns the last valid plan submitted since the previous call, or null.</summary>
        public FeaturePlan? TakeSubmittedPlan()
        {
            return Interlocked.Exchange(ref _submittedPlan, null);
        }

        public async Task<string> AskUserAsync(
            [Description("The question, self-contained and concise.")] string question,
            [Description("Suggested answers, if any.")] string[]? choices = null,
            [Description("Whether the user may type an answer other than the choices.")] bool allowFreeform = true)
        {
            if (Interlocked.Increment(ref _questionsAsked) > _maxQuestions)
            {
                return "Question limit reached. Proceed with your best assumptions and record them as decisions.";
            }

            _beforeQuestion();
            string answer = await _interaction.AskUserAsync(new PlannerQuestion(question, choices ?? [], allowFreeform || choices is not { Length: > 0 }), _cancellationToken);
            return string.IsNullOrWhiteSpace(answer) ? "(no answer, use your best judgement)" : answer;
        }

        public string SubmitPlan([Description("The complete plan.")] FeaturePlan plan)
        {
            if (_sandbox.Root is not null)
            {
                plan.RepositoryRoot = _sandbox.Root;
            }

            IReadOnlyList<string> errors = FeaturePlanValidator.Validate(plan, _sandbox);
            _interaction.OnToolInvoked(SubmitPlanName, errors.Count == 0 ? "plan is valid" : $"{errors.Count} validation error(s)");
            if (errors.Count > 0)
            {
                return $"The plan is invalid. Fix these errors and call {SubmitPlanName} again with the full plan:\n- " + string.Join("\n- ", errors);
            }

            Volatile.Write(ref _submittedPlan, plan);
            return "Plan accepted. Stop here and wait for the user's review.";
        }
    }
}
