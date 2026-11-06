using System.Text;
using BoxOfYellow.ConsoleMarkdownRenderer.Spectre;
using CopilotLib;
using LLMLib;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SerilogLib;
using Spectre.Console;

namespace CodeAssistant.TUI
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            string token = PromptForToken();
            AnsiConsole.Clear();

            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Copilot"] = token,
            });
            builder.Logging.ClearProviders();
            builder.UseSerilogLib(writeToConsole: false);
            builder.Services.AddCopilotLib(options =>
            {
                options.EnableBuiltInTools = true;
                options.WorkingDirectory = Environment.CurrentDirectory;
                options.PermissionHandler = PermissionHandler.ApproveAll;
                options.UserInputHandler = UserInputPrompt.HandleAsync;
            });

            using IHost host = builder.Build();
            await using ILLMService llm = host.Services.GetRequiredService<ILLMService>();

            await RunChatAsync(llm);
        }

        private static async Task RunChatAsync(ILLMService llm)
        {
            using CancellationTokenSource exit = new();
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

            AnsiConsole.Write(new FigletText("CodeAssistant").Color(Color.DeepSkyBlue1));
            AnsiConsole.MarkupLine("[grey]Type [bold]exit[/] to quit.[/]");
            AnsiConsole.WriteLine();

            while (!exit.IsCancellationRequested)
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
                    await StreamReplyAsync(llm, input,cancellationToken: exit.Token);
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

            AnsiConsole.MarkupLine("[grey]Goodbye.[/]");
        }

        private static async Task StreamReplyAsync(ILLMService llm, string input, string? sessionId = null, CancellationToken cancellationToken = default)
        {
            AnsiConsole.Write(new Rule("[bold green]Assistant[/]") { Justification = Justify.Left, Style = Style.Parse("green") });
            AnsiConsole.Markup("[grey italic]Thinking...[/]");

            // Text is buffered and rendered as markdown. It is also flushed before any authorization prompt,
            // because Spectre cannot show a prompt while a live display is active.
            StringBuilder pending = new();
            bool thinkingShown = true;
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
                    Console.Out.Write("\r\u001b[2K");
                    thinkingShown = false;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    AnsiConsole.Write(markdown.Render(text.Trim()).Root ?? Text.Empty);
                }
            }

            UserInputPrompt.BeforePrompt = Flush;
            try
            {
                await foreach (string chunk in llm.AskStreamingAsync(input, sessionId: sessionId, cancellationToken: cancellationToken))
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
                UserInputPrompt.BeforePrompt = null;
            }
        }

        private static string PromptForToken()
        {
            return AnsiConsole.Prompt(new TextPrompt<string>("[bold]GitHub token:[/]").Secret().Validate(t => string.IsNullOrWhiteSpace(t) ? ValidationResult.Error("[red]Token required[/]") : ValidationResult.Success())).Trim();
        }
    }
}
