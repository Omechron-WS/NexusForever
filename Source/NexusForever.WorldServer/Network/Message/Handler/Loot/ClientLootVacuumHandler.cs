using NexusForever.Game.Abstract.Loot;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Loot;
using NexusForever.WorldServer.Network;

namespace NexusForever.WorldServer.Network.Message.Handler.Loot
{
    public class ClientLootVacuumHandler : IMessageHandler<IWorldSession, ClientLootVacuum>
    {
        #region Dependency Injection

        private readonly IGlobalLootManager lootManager;

        public ClientLootVacuumHandler(
            IGlobalLootManager lootManager)
        {
            this.lootManager = lootManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientLootVacuum message)
        {
            lootManager.GiveAllLootInRange(session.Player);
        }
    }
}
