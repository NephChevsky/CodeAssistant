using CodeAssistant.Lib.Models;
using Spectre.Console;

namespace CodeAssistant.TUI
{
    public static class UserInputPrompt
    {
        public static string Ask(PlannerQuestion request)
        {
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
                    return selected;
                }
            }

            return AnsiConsole.Prompt(new TextPrompt<string>("[bold cyan]Answer>[/]").AllowEmpty());
        }
    }
}
