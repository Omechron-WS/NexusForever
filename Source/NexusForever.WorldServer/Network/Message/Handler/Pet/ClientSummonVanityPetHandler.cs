using System.Linq;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.Network;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model;

namespace NexusForever.WorldServer.Network.Message.Handler.Pet
{
    public class ClientSummonVanityPetHandler : IMessageHandler<IWorldSession, ClientSummonVanityPet>
    {
        public void HandleMessage(IWorldSession session, ClientSummonVanityPet summonVanityPet)
        {
            IPlayer player = session.Player;
            uint requestedBaseId = summonVanityPet.Spell4BaseId;
            ICharacterSpell characterSpell = player.SpellManager.GetSpell(requestedBaseId);
            if (characterSpell == null
                || !ReferenceEquals(characterSpell.Owner, player))
                throw new InvalidPacketValueException();

            ISpellBaseInfo baseInfo = characterSpell.BaseInfo;
            if (baseInfo?.Entry?.Id != requestedBaseId)
                throw new InvalidPacketValueException();

            ISpellInfo spellInfo = characterSpell.SpellInfo;
            if (spellInfo?.Entry == null
                || !ReferenceEquals(spellInfo.BaseInfo, baseInfo)
                || spellInfo.Entry.Id == 0u
                || spellInfo.Entry.Spell4BaseIdBaseSpell != requestedBaseId
                || spellInfo.Effects?.Any(effect => effect != null
                    && effect.SpellId == spellInfo.Entry.Id
                    && effect.EffectType == SpellEffectType.SummonVanityPet
                    && effect.DataBits00 != 0u) != true)
                throw new InvalidPacketValueException();

            player.CastSpell(new SpellParameters
            {
                CharacterSpell         = characterSpell,
                SpellInfo              = spellInfo,
                UserInitiatedSpellCast = false
            });
        }
    }
}
