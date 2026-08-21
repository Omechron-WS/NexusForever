using NexusForever.Game.Abstract.Guild;
using NexusForever.Game.Static.Guild;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Guild;

namespace NexusForever.WorldServer.Network.Message.Handler.Guild
{
    public class ClientGuildOperationHandler : IMessageHandler<IWorldSession, ClientGuildOperation>
    {
        #region Dependency Injection

        private readonly IGlobalGuildManager globalGuildManager;

        public ClientGuildOperationHandler(
            IGlobalGuildManager globalGuildManager)
        {
            this.globalGuildManager = globalGuildManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientGuildOperation clientGuildOperation)
        {
            if (IsRankOperation(clientGuildOperation.Operation) && clientGuildOperation.Rank > 9u)
                throw new InvalidPacketValueException();

            globalGuildManager.HandleGuildOperation(session.Player, clientGuildOperation);
        }

        private static bool IsRankOperation(GuildOperation operation)
        {
            return operation is GuildOperation.RankAdd
                or GuildOperation.RankDelete
                or GuildOperation.RankPermissions
                or GuildOperation.RankRename;
        }
    }
}
