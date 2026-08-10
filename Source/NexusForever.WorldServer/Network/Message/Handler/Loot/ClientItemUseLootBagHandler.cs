using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.WorldServer.Network;

namespace NexusForever.WorldServer.Network.Message.Handler.Loot
{
    public class ClientItemUseLootBagHandler : IMessageHandler<IWorldSession, ClientItemUseLootBag>
    {
        private const uint LootBagCategoryId = 138u;

        #region Dependency Injection

        private readonly IGlobalLootManager lootManager;

        public ClientItemUseLootBagHandler(
            IGlobalLootManager lootManager)
        {
            this.lootManager = lootManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientItemUseLootBag message)
        {
            IItem item = session.Player.Inventory.GetItem(message.ItemLocation);
            if (item == null)
                return;

            if (item.Guid != message.Guid)
                return;

            if (item.Info.Entry.Item2CategoryId != LootBagCategoryId)
                return;

            if (!lootManager.HasLootTable(item))
                return;

            if (session.Player.Inventory.ItemUse(item))
                lootManager.DropLoot(session.Player, item);
        }
    }
}
