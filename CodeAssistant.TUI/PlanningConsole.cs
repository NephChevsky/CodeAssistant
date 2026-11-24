using BoxOfYellow.ConsoleMarkdownRenderer.Spectre;
using CodeAssistant.Lib.Models;
using CodeAssistant.Lib.Planning;
using Spectre.Console;

namespace CodeAssistant.TUI
{
    /// <summary>Console front-end for the feature planner.</summary>
    internal sealed class PlanningConsole : IPlanningInteraction
    {
        private const string Approve = "Approve";
        private const string RequestChanges = "Request changes";

        private readonly MarkdownRenderer _markdown = new();
        private readonly Lock _lock = new();
        private bool _thinkingShown;

        public static async Task RunAsync(IFeaturePlanner planner, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string description = AnsiConsole.Prompt(new TextPrompt<string>("[bold cyan]Feature>[/]").PromptStyle("white").AllowEmpty()).Trim();
                if (description.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (description.Length == 0)
                {
                    continue;
                }

                PlanningConsole console = new();
                try
                {
                    FeaturePlanResult result = await planner.PlanAsync(new FeatureRequest { Description = description }, console, cancellationToken);
                    AnsiConsole.MarkupLine($"[green]Plan saved to[/] {Markup.Escape(result.MarkdownPath)}");
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    console.ClearThinking();
                    AnsiConsole.Write(new Panel(new Markup($"[red]{Markup.Escape(ex.Message)}[/]"))
                        .Header("[red]Error[/]")
                        .BorderColor(Color.Red)
                        .RoundedBorder());
                }

                AnsiConsole.WriteLine();
            }
        }

        public void OnPhaseChanged(PlanningPhase phase)
        {
            lock (_lock)
            {
                ClearThinking();
                AnsiConsole.Write(new Rule($"[bold deepskyblue1]{phase}[/]").LeftJustified());
                if (phase is PlanningPhase.Discovery or PlanningPhase.Alignment or PlanningPhase.Design)
                {
                    ShowThinking();
                }
            }
        }

        public void OnAssistantMessage(string markdown)
        {
            lock (_lock)
            {
                ClearThinking();
                string rendered = Program.RenderToString(_markdown.Render(markdown).Root ?? Text.Empty, AnsiConsole.Profile.Width - Program.AssistantPrefixWidth);
                AnsiConsole.Markup("[bold green]Assistant>[/] ");
                Console.Out.Write(rendered.TrimEnd() + Environment.NewLine);
                ShowThinking();
            }
        }

        public void OnToolInvoked(string toolName, string summary)
        {
            lock (_lock)
            {
                ClearThinking();
                AnsiConsole.MarkupLine($"[grey]  > {Markup.Escape(toolName)} {Markup.Escape(summary)}[/]");
                ShowThinking();
            }
        }

        public Task<string> AskUserAsync(PlannerQuestion question, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                ClearThinking();
                string answer = UserInputPrompt.Ask(question);
                ShowThinking();
                return Task.FromResult(answer);
            }
        }

        public Task<PlanReview> ReviewPlanAsync(FeaturePlan plan, string markdown, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                ClearThinking();
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Panel(_markdown.Render(markdown).Root ?? Text.Empty)
                    .Header("[bold]Proposed plan[/]")
                    .BorderColor(Color.DeepSkyBlue1)
                    .RoundedBorder());

                string choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .Title("[bold]Review the plan:[/]")
                    .AddChoices(Approve, RequestChanges));
                if (choice == Approve)
                {
                    return Task.FromResult(new PlanReview(true));
                }

                string feedback = AnsiConsole.Prompt(new TextPrompt<string>("[bold cyan]Changes>[/]")
                    .Validate(f => string.IsNullOrWhiteSpace(f) ? ValidationResult.Error("[red]Describe the changes you want[/]") : ValidationResult.Success()));
                ShowThinking();
                return Task.FromResult(new PlanReview(false, feedback.Trim()));
            }
        }

        private void ShowThinking()
        {
            if (!_thinkingShown)
            {
                AnsiConsole.Markup("[grey italic]Thinking...[/]");
                _thinkingShown = true;
            }
        }

        private void ClearThinking()
        {
            if (_thinkingShown)
            {
                Console.Out.Write("\r\u001b[2K");
                _thinkingShown = false;
            }
        }
    }
}
