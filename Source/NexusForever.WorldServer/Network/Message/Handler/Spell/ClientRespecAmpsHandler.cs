using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Abilities;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Abilities;

namespace NexusForever.WorldServer.Network.Message.Handler.Spell
{
    public class ClientRespecAmpsHandler : IMessageHandler<IWorldSession, ClientRespecAmps>
    {
        #region Dependency Injection

        private readonly IGameTableManager gameTableManager;

        public ClientRespecAmpsHandler(
            IGameTableManager gameTableManager)
        {
            this.gameTableManager = gameTableManager;
        }

        #endregion

        public void HandleMessage(IWorldSession session, ClientRespecAmps requestAmpReset)
        {
            // TODO: handle reset cost

            if (requestAmpReset.SpecIndex >= ActionSet.MaxActionSets
                || !HasValidValueShape(requestAmpReset.RespecType, requestAmpReset.Value))
                throw new InvalidPacketValueException();

            switch (requestAmpReset.RespecType)
            {
                case AmpRespecType.Section:
                {
                    EldanAugmentationCategoryEntry category = gameTableManager.EldanAugmentationCategory
                        .GetEntry(requestAmpReset.Value);
                    if (category == null || category.Id != requestAmpReset.Value)
                        throw new InvalidPacketValueException();
                    break;
                }
                case AmpRespecType.Single:
                {
                    EldanAugmentationEntry amp = gameTableManager.EldanAugmentation
                        .GetEntry(requestAmpReset.Value);
                    if (amp == null || amp.Id != requestAmpReset.Value)
                        throw new InvalidPacketValueException();
                    break;
                }
            }

            ISpellManager spellManager = session.Player.SpellManager;
            if (requestAmpReset.SpecIndex != spellManager.ActiveActionSet)
                throw new InvalidPacketValueException();

            IActionSet actionSet = spellManager.GetActionSet(requestAmpReset.SpecIndex);
            if (requestAmpReset.RespecType == AmpRespecType.Single
                && actionSet.GetAmp((ushort)requestAmpReset.Value)?.Entry?.Id != requestAmpReset.Value)
                throw new InvalidPacketValueException();

            actionSet.RemoveAmp(requestAmpReset.RespecType, requestAmpReset.Value);
            session.EnqueueMessageEncrypted(actionSet.BuildServerAmpList());
        }

        private static bool HasValidValueShape(AmpRespecType type, uint value)
        {
            return type switch
            {
                AmpRespecType.Full    => true,
                AmpRespecType.Section => value != 0u,
                AmpRespecType.Single  => value is > 0u and <= ushort.MaxValue,
                _                     => false
            };
        }
    }
}
