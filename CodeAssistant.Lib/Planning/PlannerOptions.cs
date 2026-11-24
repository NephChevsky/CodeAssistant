namespace CodeAssistant.Lib.Planning
{
    public class PlannerOptions
    {
        /// <summary>Root of the repository the planner may read. Nothing outside it is accessible.</summary>
        public string RepositoryRoot { get; set; } = Environment.CurrentDirectory;

        /// <summary>Model requested from the LLM provider. Provider default when null.</summary>
        public string? Model { get; set; }

        /// <summary>Reasoning effort requested from the model (e.g. "low", "medium", "high"). Provider default when null.</summary>
        public string? ReasoningEffort { get; set; }

        /// <summary>Directory where approved plans are written. Defaults to &lt;RepositoryRoot&gt;/plans.</summary>
        public string? PlansOutputDirectory { get; set; }

        /// <summary>Directory names hidden from the planner's tools, at any depth.</summary>
        public IList<string> ExcludedDirectories { get; set; } = ["bin", "obj", ".git", ".vs", "node_modules"];

        public int MaxReadLines { get; set; } = 500;

        public int MaxFileBytes { get; set; } = 1024 * 1024;

        public int MaxSearchResults { get; set; } = 100;

        public int MaxClarifyingQuestions { get; set; } = 5;

        /// <summary>Number of reminders sent when the agent ends a design or revision turn without submitting a plan.</summary>
        public int MaxSubmitRetries { get; set; } = 2;

        public TimeSpan RegexTimeout { get; set; } = TimeSpan.FromSeconds(2);

        internal string GetPlansOutputDirectory()
        {
            return Path.GetFullPath(PlansOutputDirectory ?? Path.Combine(RepositoryRoot, "plans"));
        }
    }
}
