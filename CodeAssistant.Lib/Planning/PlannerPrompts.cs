using CodeAssistant.Lib.Models;
using CodeAssistant.Lib.Tools;

namespace CodeAssistant.Lib.Planning
{
    internal static class PlannerPrompts
    {
        public static string SystemMessage(bool hasRepositoryRoot, bool multiRepository = false)
        {
            string scope = hasRepositoryRoot
                ? "for the repository you are given access to"
                : "for a project on this machine. You can read any file; first locate the project the request is about";
            string paths = hasRepositoryRoot
                ? "Paths are relative to the repository root."
                : $"There is no repository root: always use absolute paths ({ReadOnlyRepositoryTools.ListDirName} without a path lists the drives).";
            string planPaths = hasRepositoryRoot
                ? "Relevant files and task files use paths relative to the repository root"
                : "Set repositoryRoot to the absolute path of the repository the plan applies to; relevant files and task files use paths relative to it";
            if (multiRepository)
            {
                scope = "for the git repositories found in the folder you are given access to (each subfolder containing .git is a repository)";
                paths = "Paths are relative to that folder, so they start with the repository's folder name.";
                planPaths = "Write one plan per repository that needs changes; set each plan's repositoryRoot to the absolute path of that repository's root (the folder containing .git). " +
                    "Relevant files and task files of a plan use paths relative to its own repository root. Mention work that depends on another repository's plan in the tasks' descriptions and dependencies";
            }

            return
                "You are a software planning agent. Your only job is to produce an implementation plan for a feature requested by the user, " +
                $"{scope}. You never implement anything: you have no write access and must not attempt to modify files or run commands.\n\n" +
                "Tools:\n" +
                $"- Explore with {ReadOnlyRepositoryTools.ListDirName}, {ReadOnlyRepositoryTools.FileSearchName}, {ReadOnlyRepositoryTools.GrepSearchName} and {ReadOnlyRepositoryTools.ReadFileName}. {paths}\n" +
                $"- Ask the user with {PlannerInteractionTools.AskUserName} only for decisions that materially change the plan and cannot be found in the code. Offer concrete choices when possible.\n" +
                $"- You decide when a phase is done: declare Discovery complete with {PlannerInteractionTools.CompleteDiscoveryName} and Alignment with {PlannerInteractionTools.CompleteAlignmentName}. " +
                "The user then agrees or sends you back with feedback. Never move on to a later phase on your own.\n" +
                $"- Submit the plan only by calling {PlannerInteractionTools.SubmitPlanName}. Never write the plan as plain text.\n\n" +
                "Plan rules:\n" +
                "- Phases are ordered; each has a goal and concrete tasks.\n" +
                "- Tasks have unique ids (T1.1, T1.2, T2.1...), a precise description, the files they touch, and dependencies (dependsOn / parallelWith) referencing other task ids.\n" +
                "- Every task contributes to at least one acceptance criterion (acceptanceCriterionIds).\n" +
                "- Acceptance criteria have unique ids (AC-01...) and are observable, preferably Given/When/Then.\n" +
                "- Every acceptance criterion has at least one acceptance test with a unique id (AT-01.1...), a kind (unit, integration, manual), how to run it, and the expected result.\n" +
                $"- {planPaths}; files marked modify or reference must exist.\n" +
                "- Keep the plan scoped to the request: list exclusions in outOfScope and open points in furtherConsiderations.\n" +
                $"- If {PlannerInteractionTools.SubmitPlanName} returns validation errors, fix them and call it again with the full plan.";
        }

        public static string Discovery(string featureDescription)
        {
            return "Feature request:\n" + featureDescription + "\n\n" +
                "Phase 1 - Discovery. Explore the repository to understand everything relevant to this request: entry points, existing patterns, " +
                "conventions, similar features, configuration and tests. Do not ask questions and do not write a plan yet. " +
                $"When you understand everything relevant, call {PlannerInteractionTools.CompleteDiscoveryName} with a concise summary of your findings, " +
                "the relevant files, and the open questions you identified.";
        }

        public const string Alignment =
            "Phase 2 - Alignment. For open questions that materially change the plan, ask the user with " +
            $"{PlannerInteractionTools.AskUserName} (one question per call, with suggested choices). " +
            "Do not ask about anything you can find in the code. " +
            $"When every decision is settled, call {PlannerInteractionTools.CompleteAlignmentName} with the decisions and your assumptions. " +
            "If nothing is ambiguous, call it right away with your assumptions.";

        public static string CompletionReminder(PlanningPhase phase)
        {
            string tool = PlannerInteractionTools.CompletionToolName(phase);
            return $"You have not declared the {phase} phase complete. If it is, call {tool} now; otherwise continue the {phase} phase and call {tool} when you are done.";
        }

        public static string PhaseFeedback(PlanningPhase phase, string feedback)
        {
            return $"The user does not agree that the {phase} phase is complete:\n" + feedback + "\n\n" +
                $"Continue the {phase} phase accordingly (explore further or ask questions as needed), " +
                $"then call {PlannerInteractionTools.CompletionToolName(phase)} again with the full updated summary.";
        }

        public static string Design(bool multiRepository)
        {
            return multiRepository
                ? $"Phase 3 - Design. Call {PlannerInteractionTools.SubmitPlanName} once for each repository that needs changes, each with the complete plan of that repository, following the plan rules. " +
                    "Do not print the plans as text."
                : $"Phase 3 - Design. Call {PlannerInteractionTools.SubmitPlanName} with the complete plan, following the plan rules. Do not print the plan as text.";
        }

        public const string SubmitReminder =
            $"You have not submitted a plan. Call {PlannerInteractionTools.SubmitPlanName} now with the complete plan.";

        public static string Revision(string feedback, bool multiRepository)
        {
            string submit = multiRepository
                ? $"call {PlannerInteractionTools.SubmitPlanName} with the full revised plan (not a diff) of each repository whose plan changes; plans you do not resubmit are kept as they are"
                : $"call {PlannerInteractionTools.SubmitPlanName} with the full revised plan (not a diff)";
            return "Phase 4 - Refinement. The user reviewed the plan and requested changes:\n" + feedback + "\n\n" +
                $"Explore further if needed, then {submit}.";
        }
    }
}
