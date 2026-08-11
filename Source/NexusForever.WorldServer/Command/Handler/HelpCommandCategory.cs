using NexusForever.Game.Static.RBAC;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Static;

namespace NexusForever.WorldServer.Command.Handler
{
    [Command(Permission.Help, "Display help information for supplied category or command.", "help", "h", "?")]
    public class HelpCommandCategory : CommandCategory
    {
        /// <summary>
        /// Invoke <see cref="CommandCategory"/> with the supplied <see cref="ICommandContext"/> and <see cref="ParameterQueue"/>.
        /// </summary>
        public override CommandResult Invoke(ICommandContext context, ParameterQueue queue)
        {
            CommandResult result = CanInvoke(context);
            if (result != CommandResult.Ok)
                return result;

            CommandManager.Instance.HandleHelp(context, queue);
            return CommandResult.Ok;
        }
    }
}
