using System.Text;
using CodeAssistant.Lib.Models;

namespace CodeAssistant.Lib.Rendering
{
    public static class FeaturePlanMarkdownRenderer
    {
        public static string Render(FeaturePlan plan)
        {
            StringBuilder md = new();
            md.AppendLine($"# Plan: {plan.Title}").AppendLine();
            md.AppendLine("## TL;DR").AppendLine().AppendLine(plan.Summary).AppendLine();

            AppendList(md, "Findings", plan.Findings);

            md.AppendLine("## Phases & tasks").AppendLine();
            foreach (PlanPhase phase in plan.Phases)
            {
                md.AppendLine($"### {phase.Name}").AppendLine();
                if (!string.IsNullOrWhiteSpace(phase.Goal))
                {
                    md.AppendLine(phase.Goal).AppendLine();
                }

                md.AppendLine("| Id | Status | Task | Files | Depends on | Parallel with | Criteria |");
                md.AppendLine("|----|--------|------|-------|------------|---------------|----------|");
                foreach (PlanTask task in phase.Tasks)
                {
                    md.AppendLine($"| {Cell(task.Id)} | {(task.Status == PlanTaskStatus.Done ? "[x]" : "[ ]")} | **{Cell(task.Title)}**<br>{Cell(task.Description)} | {Cell(string.Join(", ", task.Files.Select(f => $"`{f}`")))} | {Cell(Join(task.DependsOn))} | {Cell(Join(task.ParallelWith))} | {Cell(Join(task.AcceptanceCriterionIds))} |");
                }

                md.AppendLine();
            }

            md.AppendLine("## Acceptance criteria").AppendLine();
            foreach (AcceptanceCriterion criterion in plan.AcceptanceCriteria)
            {
                md.AppendLine($"### {criterion.Id}").AppendLine().AppendLine(criterion.Description).AppendLine();
                md.AppendLine("| Test | Kind | Description | How to run | Expected result |");
                md.AppendLine("|------|------|-------------|------------|-----------------|");
                foreach (AcceptanceTest test in criterion.Tests)
                {
                    md.AppendLine($"| {Cell(test.Id)} | {test.Kind} | {Cell(test.Description)} | {Cell(test.HowToRun)} | {Cell(test.ExpectedResult)} |");
                }

                md.AppendLine();
            }

            if (plan.RelevantFiles.Count > 0)
            {
                md.AppendLine("## Relevant files").AppendLine();
                foreach (RelevantFile file in plan.RelevantFiles)
                {
                    md.AppendLine($"- `{file.Path}` ({file.Change.ToString().ToLowerInvariant()}) — {file.Purpose}");
                }

                md.AppendLine();
            }

            AppendList(md, "Decisions", plan.Decisions);
            AppendList(md, "Out of scope", plan.OutOfScope);
            AppendList(md, "Further considerations", plan.FurtherConsiderations, numbered: true);

            return md.ToString().TrimEnd() + Environment.NewLine;
        }

        private static void AppendList(StringBuilder md, string title, List<string> items, bool numbered = false)
        {
            if (items.Count == 0)
            {
                return;
            }

            md.AppendLine($"## {title}").AppendLine();
            for (int i = 0; i < items.Count; i++)
            {
                md.AppendLine(numbered ? $"{i + 1}. {items[i]}" : $"- {items[i]}");
            }

            md.AppendLine();
        }

        private static string Join(List<string> ids)
        {
            return ids.Count == 0 ? "—" : string.Join(", ", ids);
        }

        private static string Cell(string value)
        {
            return value.Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>");
        }
    }
}
