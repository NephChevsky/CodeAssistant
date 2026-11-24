using CodeAssistant.Lib.Models;
using CodeAssistant.Lib.Tools;

namespace CodeAssistant.Lib.Validation
{
    /// <summary>Checks a <see cref="FeaturePlan"/> for completeness and consistency. Errors are phrased so the agent can fix them.</summary>
    public static class FeaturePlanValidator
    {
        /// <param name="sandbox">When provided, modify/reference files must exist inside it.</param>
        public static IReadOnlyList<string> Validate(FeaturePlan plan, RepositorySandbox? sandbox = null)
        {
            List<string> errors = [];

            if (string.IsNullOrWhiteSpace(plan.Title))
            {
                errors.Add("title is required.");
            }

            if (string.IsNullOrWhiteSpace(plan.Summary))
            {
                errors.Add("summary is required.");
            }

            List<PlanTask> tasks = [.. plan.Phases.SelectMany(p => p.Tasks)];
            if (tasks.Count == 0)
            {
                errors.Add("the plan must contain at least one task.");
            }

            foreach (PlanPhase phase in plan.Phases.Where(p => string.IsNullOrWhiteSpace(p.Name)))
            {
                errors.Add("every phase needs a name.");
            }

            HashSet<string> taskIds = CheckIds("task", tasks.Select(t => t.Id), errors);
            HashSet<string> criterionIds = CheckIds("acceptance criterion", plan.AcceptanceCriteria.Select(c => c.Id), errors);
            CheckIds("acceptance test", plan.AcceptanceCriteria.SelectMany(c => c.Tests).Select(t => t.Id), errors);

            foreach (PlanTask task in tasks)
            {
                string label = string.IsNullOrWhiteSpace(task.Id) ? $"task '{task.Title}'" : $"task {task.Id}";
                if (string.IsNullOrWhiteSpace(task.Title) || string.IsNullOrWhiteSpace(task.Description))
                {
                    errors.Add($"{label} needs a title and a description.");
                }

                CheckReferences(label, "dependsOn", task.Id, task.DependsOn, taskIds, errors);
                CheckReferences(label, "parallelWith", task.Id, task.ParallelWith, taskIds, errors);

                if (task.AcceptanceCriterionIds.Count == 0)
                {
                    errors.Add($"{label} must reference at least one acceptance criterion in acceptanceCriterionIds.");
                }

                foreach (string id in task.AcceptanceCriterionIds.Where(id => !criterionIds.Contains(id)))
                {
                    errors.Add($"{label} references unknown acceptance criterion '{id}'.");
                }
            }

            foreach (AcceptanceCriterion criterion in plan.AcceptanceCriteria)
            {
                string label = string.IsNullOrWhiteSpace(criterion.Id) ? "an acceptance criterion" : $"acceptance criterion {criterion.Id}";
                if (string.IsNullOrWhiteSpace(criterion.Description))
                {
                    errors.Add($"{label} needs a description.");
                }

                if (criterion.Tests.Count == 0)
                {
                    errors.Add($"{label} must have at least one acceptance test.");
                }

                foreach (AcceptanceTest test in criterion.Tests.Where(t => string.IsNullOrWhiteSpace(t.Description) || string.IsNullOrWhiteSpace(t.HowToRun) || string.IsNullOrWhiteSpace(t.ExpectedResult)))
                {
                    errors.Add($"acceptance test {test.Id} of {label} needs a description, howToRun and expectedResult.");
                }
            }

            if (sandbox is not null && GetRepositoryRoot(plan, sandbox, errors) is { } root)
            {
                foreach (RelevantFile file in plan.RelevantFiles.Where(f => f.Change is FileChangeKind.Modify or FileChangeKind.Reference))
                {
                    if (Path.IsPathRooted(file.Path))
                    {
                        errors.Add($"relevant file '{file.Path}' must be relative to the repository root.");
                    }
                    else if (!FileExists(sandbox, Path.Combine(root, file.Path)))
                    {
                        errors.Add($"relevant file '{file.Path}' is marked {file.Change.ToString().ToLowerInvariant()} but does not exist (use a path relative to the repository root, or mark it create).");
                    }
                }
            }

            return errors;
        }

        /// <summary>The sandbox root when there is one, otherwise the plan's repositoryRoot when it is an accessible directory.</summary>
        private static string? GetRepositoryRoot(FeaturePlan plan, RepositorySandbox sandbox, List<string> errors)
        {
            if (sandbox.Root is not null)
            {
                return sandbox.Root;
            }

            if (!string.IsNullOrWhiteSpace(plan.RepositoryRoot) && Path.IsPathFullyQualified(plan.RepositoryRoot))
            {
                try
                {
                    string fullPath = sandbox.Resolve(plan.RepositoryRoot);
                    if (Directory.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                }
            }

            errors.Add($"repositoryRoot '{plan.RepositoryRoot}' must be the absolute path of an existing directory: the root of the repository the plan applies to.");
            return null;
        }

        private static HashSet<string> CheckIds(string kind, IEnumerable<string> ids, List<string> errors)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    errors.Add($"every {kind} needs an id.");
                }
                else if (!seen.Add(id))
                {
                    errors.Add($"{kind} id '{id}' is used more than once.");
                }
            }

            return seen;
        }

        private static void CheckReferences(string label, string field, string ownId, List<string> references, HashSet<string> taskIds, List<string> errors)
        {
            foreach (string id in references)
            {
                if (string.Equals(id, ownId, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{label} cannot reference itself in {field}.");
                }
                else if (!taskIds.Contains(id))
                {
                    errors.Add($"{label} references unknown task '{id}' in {field}.");
                }
            }
        }

        private static bool FileExists(RepositorySandbox sandbox, string path)
        {
            try
            {
                string fullPath = sandbox.Resolve(path);
                return File.Exists(fullPath) || Directory.Exists(fullPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }
    }
}
