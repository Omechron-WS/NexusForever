using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Prerequisite;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Model.Abilities;

namespace NexusForever.WorldServer.Network.Message.Handler.Spell
{
    public class ClientSetStanceHandler : IMessageHandler<IWorldSession, ClientSetStance>
    {
        private const byte InnateAbilitySlotCount = 3;

        #region Dependency Injection

        private readonly IGameTableManager gameTableManager;
        private readonly IPrerequisiteManager prerequisiteManager;

        public ClientSetStanceHandler(
            IGameTableManager gameTableManager,
            IPrerequisiteManager prerequisiteManager)
        {
            this.gameTableManager    = gameTableManager;
            this.prerequisiteManager = prerequisiteManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientSetStance innateChange)
        {
            byte innateIndex = innateChange.InnateIndex;
            if (innateIndex >= InnateAbilitySlotCount)
                throw new InvalidPacketValueException();

            IPlayer player = session.Player;
            ClassEntry classEntry = gameTableManager.Class?.GetEntry((byte)player.Class);
            if (classEntry?.Spell4IdInnateAbilityActive is not { Length: InnateAbilitySlotCount }
                || classEntry.Spell4IdInnateAbilityPassive is not { Length: InnateAbilitySlotCount }
                || classEntry.PrerequisiteIdInnateAbility is not { Length: InnateAbilitySlotCount }
                || classEntry.Spell4IdInnateAbilityActive[innateIndex] == 0u)
                throw new InvalidPacketValueException();

            uint prerequisiteId = classEntry.PrerequisiteIdInnateAbility[innateIndex];
            if (prerequisiteId != 0u
                && !prerequisiteManager.Meets(player, prerequisiteId))
                throw new InvalidPacketValueException();

            player.InnateIndex = innateIndex;

            session.EnqueueMessageEncrypted(new ServerStanceChanged
            {
                InnateIndex = innateIndex
            });
        }
    }
}
