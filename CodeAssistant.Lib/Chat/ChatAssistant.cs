using CodeAssistant.Lib.Planning;
using CodeAssistant.Lib.Tools;
using Microsoft.Extensions.AI;

namespace CodeAssistant.Lib.Chat
{
    /// <summary>System messages and tools of the free chat modes, which can read the whole file system but not modify it.</summary>
    public static class ChatAssistant
    {
        private const string ProductionSystemMessage =
            "You are running in a production environment. You may freely consult and read things (files, configuration, logs, data, web pages), " +
            "but you must never modify anything (create, edit, delete or move files, run commands with side effects, change configuration or data) " +
            "without first asking the user for explicit permission and receiving it.";

        private const string FileAccessSystemMessage =
            $"You can read any file on this machine with the {ReadOnlyRepositoryTools.ListDirName}, {ReadOnlyRepositoryTools.ReadFileName}, {ReadOnlyRepositoryTools.GrepSearchName} " +
            $"and {ReadOnlyRepositoryTools.FileSearchName} tools, always using absolute paths ({ReadOnlyRepositoryTools.ListDirName} without a path lists the drives). " +
            "You have no tool to modify files or run commands.";

        public static string SystemMessage(bool productionMode)
        {
            return productionMode ? ProductionSystemMessage + "\n\n" + FileAccessSystemMessage : FileAccessSystemMessage;
        }

        /// <summary>Read-only tools over the whole file system.</summary>
        public static IReadOnlyList<AIFunction> CreateTools(Action<string, string>? onInvoked = null)
        {
            return ReadOnlyRepositoryTools.Create(new RepositorySandbox(null, []), new PlannerOptions(), onInvoked);
        }
    }
}
