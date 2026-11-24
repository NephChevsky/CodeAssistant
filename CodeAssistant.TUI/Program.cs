using System.Text;
using BoxOfYellow.ConsoleMarkdownRenderer.Spectre;
using CodeAssistant.Lib;
using CodeAssistant.Lib.Planning;
using CodeAssistant.Lib.Tools;
using CopilotLib;
using LLMLib;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SerilogLib;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace CodeAssistant.TUI
{
    public static class Program
    {
        private const string ProductionMode = "Production mode";
        private const string FullControlMode = "Full control";
        private const string PlanFeatureMode = "Plan feature";
        internal const int AssistantPrefixWidth = 11;
        private const string SonnetModel = "claude-sonnet-5.5";
        private const string OpusModel = "claude-opus-5.5";

        private const string ProductionSystemMessage =
            "You are running in a production environment. You may freely consult and read things (files, configuration, logs, data, web pages), " +
            "but you must never modify anything (create, edit, delete or move files, run commands with side effects, change configuration or data) " +
            "without first asking the user for explicit permission and receiving it.";

        private const string FileAccessSystemMessage =
            "You can read any file on this machine with the list_dir, read_file, grep_search and file_search tools, always using absolute paths " +
            "(list_dir without a path lists the drives). You have no tool to modify files or run commands.";

        public static async Task Main(string[] args)
        {
            string token = PromptForToken();
            string mode = PromptForMode();
            string model = PromptForModel();
            string reasoningEffort = PromptForReasoningEffort(model);
            AnsiConsole.Clear();

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Copilot"] = token,
            });
            builder.Logging.ClearProviders();
            builder.UseSerilogLib(writeToConsole: false);
            builder.Services.AddCopilotLib();
            builder.Services.AddCodeAssistantLib(options =>
            {
                options.RepositoryRoot = null;
                options.Model = model;
                options.ReasoningEffort = reasoningEffort;
            });

            using IHost host = builder.Build();
            await using ILLMService llm = host.Services.GetRequiredService<ILLMService>();

            using CancellationTokenSource exit = new();
            RegisterShutdown(llm, exit);

            AnsiConsole.Write(new FigletText("CodeAssistant").Color(Color.DeepSkyBlue1));
            AnsiConsole.MarkupLine("[grey]Type [bold]exit[/] to quit.[/]");
            AnsiConsole.WriteLine();

            if (mode == PlanFeatureMode)
            {
                await PlanningConsole.RunAsync(host.Services.GetRequiredService<IFeaturePlanner>(), exit.Token);
            }
            else
            {
                IReadOnlyList<AIFunction> tools = ReadOnlyRepositoryTools.Create(new RepositorySandbox(null, []), host.Services.GetRequiredService<PlannerOptions>());
                await RunChatAsync(llm, mode == ProductionMode, model, reasoningEffort, tools, exit.Token);
            }

            AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
        }

        private static void RegisterShutdown(ILLMService llm, CancellationTokenSource exit)
        {
            int shuttingDown = 0;
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                if (Interlocked.Exchange(ref shuttingDown, 1) == 1)
                {
                    return;
                }

                exit.Cancel();
                AnsiConsole.MarkupLine("[grey]Shutting down...[/]");

                // Prompts block the main thread, so clean up here before exiting.
                try
                {
                    llm.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception)
                {
                    // Exiting anyway.
                }

                Log.CloseAndFlush();
                Environment.Exit(0);
            };
        }

        private static async Task RunChatAsync(ILLMService llm, bool productionMode, string model, string reasoningEffort, IReadOnlyList<AIFunction> tools, CancellationToken cancellationToken)
        {
            string systemMessage = productionMode ? ProductionSystemMessage + "\n\n" + FileAccessSystemMessage : FileAccessSystemMessage;
            await using ILLMSession session = await llm.CreateSessionAsync(
                new LLMSessionOptions { SystemMessage = systemMessage, Model = model, ReasoningEffort = reasoningEffort, Tools = tools },
                cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                string input;
                try
                {
                    input = AnsiConsole.Prompt(new TextPrompt<string>("[bold cyan]You>[/]").PromptStyle("white").AllowEmpty());
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                try
                {
                    await StreamReplyAsync(session, input, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AnsiConsole.Write(new Panel(new Markup($"[red]{Markup.Escape(ex.Message)}[/]"))
                        .Header("[red]Error[/]")
                        .BorderColor(Color.Red)
                        .RoundedBorder());
                }

                AnsiConsole.WriteLine();
            }
        }

        private static async Task StreamReplyAsync(ILLMSession session, string input, CancellationToken cancellationToken = default)
        {
            AnsiConsole.Markup("[bold green]Assistant>[/] ");
            AnsiConsole.Markup("[grey italic]Thinking...[/]");

            // Text is buffered and rendered as markdown once the reply is complete.
            StringBuilder pending = new();
            bool thinkingShown = true;
            bool prefixShown = false;
            MarkdownRenderer markdown = new();

            void Flush()
            {
                string text;
                lock (pending)
                {
                    text = pending.ToString();
                    pending.Clear();
                }

                if (thinkingShown)
                {
                    // Erase the whole "Assistant> Thinking..." line; the prefix is redrawn with the message.
                    Console.Out.Write("\r\u001b[2K");
                    thinkingShown = false;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    if (!prefixShown)
                    {
                        AnsiConsole.MarkupLine("[bold green]Assistant>[/]");
                        prefixShown = true;
                    }

                    return;
                }

                // Rendered slightly narrower so the first line fits after the prefix; wrapped lines start at column 0.
                string rendered = RenderToString(markdown.Render(text.Trim()).Root ?? Text.Empty, AnsiConsole.Profile.Width - AssistantPrefixWidth);
                if (!prefixShown)
                {
                    AnsiConsole.Markup("[bold green]Assistant>[/] ");
                    prefixShown = true;
                }

                Console.Out.Write(rendered.TrimEnd() + Environment.NewLine);
            }

            try
            {
                await foreach (string chunk in session.AskStreamingAsync(input, cancellationToken: cancellationToken))
                {
                    lock (pending)
                    {
                        pending.Append(chunk);
                    }
                }

                Flush();
            }
            finally
            {
                // On failure the "Assistant> Thinking..." line is still displayed; clear it so the error starts on a clean line.
                if (thinkingShown)
                {
                    Console.Out.Write("\r\u001b[2K");
                    thinkingShown = false;
                }
            }
        }

        internal static string RenderToString(IRenderable renderable, int width)
        {
            StringWriter writer = new();
            IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                ColorSystem = Enum.Parse<ColorSystemSupport>(AnsiConsole.Profile.Capabilities.ColorSystem.ToString()),
                Out = new AnsiConsoleOutput(writer),
            });
            console.Profile.Width = Math.Max(20, width);
            console.Write(renderable);
            return writer.ToString();
        }

        private static string PromptForMode()
        {
            return AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("[bold]Select a mode:[/]")
                .AddChoices(ProductionMode, FullControlMode, PlanFeatureMode));
        }

        private static string PromptForModel()
        {
            return AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("[bold]Select a model:[/]")
                .AddChoices(SonnetModel, OpusModel));
        }

        private static string PromptForReasoningEffort(string model)
        {
            string[] efforts = ["low", "medium", "high"];
            string preselected = model == OpusModel ? "high" : "medium";
            // The cursor starts on the first choice, so the preselected one is listed first.
            return AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("[bold]Select a reasoning effort:[/]")
                .AddChoices(efforts.OrderBy(e => e != preselected)));
        }

        private static string PromptForToken()
        {
            return AnsiConsole.Prompt(new TextPrompt<string>("[bold]GitHub token:[/]").Secret().Validate(t => string.IsNullOrWhiteSpace(t) ? ValidationResult.Error("[red]Token required[/]") : ValidationResult.Success())).Trim();
        }
    }
}
