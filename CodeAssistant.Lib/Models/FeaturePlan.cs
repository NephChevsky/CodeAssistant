using System.ComponentModel;

namespace CodeAssistant.Lib.Models
{
    [Description("A complete implementation plan for a feature.")]
    public class FeaturePlan
    {
        [Description("Short title of the feature (2-10 words).")]
        public string Title { get; set; } = string.Empty;

        [Description("TL;DR: what will be done, how, and why.")]
        public string Summary { get; set; } = string.Empty;

        [Description("Key facts discovered in the codebase that drive the plan.")]
        public List<string> Findings { get; set; } = [];

        [Description("Ordered implementation phases, each containing tasks.")]
        public List<PlanPhase> Phases { get; set; } = [];

        [Description("Acceptance criteria; each must have at least one acceptance test.")]
        public List<AcceptanceCriterion> AcceptanceCriteria { get; set; } = [];

        [Description("Files that will be modified, created or used as reference, relative to the repository root.")]
        public List<RelevantFile> RelevantFiles { get; set; } = [];

        [Description("Decisions and assumptions made while planning.")]
        public List<string> Decisions { get; set; } = [];

        [Description("What is explicitly excluded from this feature.")]
        public List<string> OutOfScope { get; set; } = [];

        [Description("Open questions or optional improvements for the user to consider.")]
        public List<string> FurtherConsiderations { get; set; } = [];
    }

    public class PlanPhase
    {
        [Description("Phase name.")]
        public string Name { get; set; } = string.Empty;

        [Description("What this phase achieves.")]
        public string Goal { get; set; } = string.Empty;

        [Description("Tasks of this phase.")]
        public List<PlanTask> Tasks { get; set; } = [];
    }

    public class PlanTask
    {
        [Description("Unique task id, e.g. T1.1.")]
        public string Id { get; set; } = string.Empty;

        [Description("Short task title.")]
        public string Title { get; set; } = string.Empty;

        [Description("What to do, precisely enough for a developer to implement it.")]
        public string Description { get; set; } = string.Empty;

        [Description("Progress status of the task; new plans use 'todo'.")]
        public PlanTaskStatus Status { get; set; } = PlanTaskStatus.Todo;

        [Description("Files touched by this task, relative to the repository root.")]
        public List<string> Files { get; set; } = [];

        [Description("Ids of tasks that must be completed first.")]
        public List<string> DependsOn { get; set; } = [];

        [Description("Ids of tasks that can be done in parallel with this one.")]
        public List<string> ParallelWith { get; set; } = [];

        [Description("Ids of the acceptance criteria this task contributes to.")]
        public List<string> AcceptanceCriterionIds { get; set; } = [];
    }

    public enum PlanTaskStatus
    {
        Todo,
        Done,
    }

    public class AcceptanceCriterion
    {
        [Description("Unique criterion id, e.g. AC-01.")]
        public string Id { get; set; } = string.Empty;

        [Description("Observable criterion, preferably Given/When/Then.")]
        public string Description { get; set; } = string.Empty;

        [Description("Tests proving the criterion is met (at least one).")]
        public List<AcceptanceTest> Tests { get; set; } = [];
    }

    public class AcceptanceTest
    {
        [Description("Unique test id, e.g. AT-01.1.")]
        public string Id { get; set; } = string.Empty;

        [Description("Kind of test.")]
        public AcceptanceTestKind Kind { get; set; }

        [Description("What the test does.")]
        public string Description { get; set; } = string.Empty;

        [Description("Command or manual steps to run the test.")]
        public string HowToRun { get; set; } = string.Empty;

        [Description("Expected outcome when the criterion is met.")]
        public string ExpectedResult { get; set; } = string.Empty;
    }

    public enum AcceptanceTestKind
    {
        Unit,
        Integration,
        Manual,
    }

    public class RelevantFile
    {
        [Description("File path relative to the repository root.")]
        public string Path { get; set; } = string.Empty;

        [Description("How the file is involved.")]
        public FileChangeKind Change { get; set; }

        [Description("Why the file is relevant.")]
        public string Purpose { get; set; } = string.Empty;
    }

    public enum FileChangeKind
    {
        Modify,
        Create,
        Reference,
    }
}
