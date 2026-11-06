using CopilotLib;
using Spectre.Console;

namespace CodeAssistant.TUI
{
    public static class UserInputPrompt
    {
        /// <summary>Invoked before a prompt so pending assistant output can be rendered first.</summary>
        public static Action? BeforePrompt { get; set; }

        public static Task<UserInputAnswer> HandleAsync(UserInputQuestion request)
        {
            BeforePrompt?.Invoke();

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(new Text(request.Question))
                .Header("[bold yellow]Question[/]")
                .BorderColor(Color.Yellow)
                .RoundedBorder());

            List<string> choices = [.. request.Choices];
            if (choices.Count > 0)
            {
                const string other = "Other (type an answer)";
                if (request.AllowFreeform)
                {
                    choices.Add(other);
                }

                string selected = AnsiConsole.Prompt(new SelectionPrompt<string>().AddChoices(choices));
                if (selected != other)
                {
                    return Task.FromResult(new UserInputAnswer(selected, false));
                }
            }

            string answer = AnsiConsole.Prompt(new TextPrompt<string>("[bold cyan]Answer>[/]").AllowEmpty());
            return Task.FromResult(new UserInputAnswer(answer, true));
        }
    }
}
