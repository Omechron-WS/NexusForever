using NexusForever.Game.Abstract.Loot;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Loot;
using NexusForever.WorldServer.Network;

namespace NexusForever.WorldServer.Network.Message.Handler.Loot
{
    public class ClientLootItemHandler : IMessageHandler<IWorldSession, ClientLootItem>
    {
        #region Dependency Injection

        private readonly IGlobalLootManager lootManager;

        public ClientLootItemHandler(
            IGlobalLootManager lootManager)
        {
            this.lootManager = lootManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientLootItem message)
        {
            if (message.Request)
                return;

            lootManager.GiveLoot(session.Player, message.OwnerUnitId, message.LootUnitId);
        }
    }
}
