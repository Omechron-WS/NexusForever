using NexusForever.Game.Abstract.CSI;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Network.World.Entity;

namespace NexusForever.Game.Spell
{
    public class SpellParameters : ISpellParameters
    {
        public ICharacterSpell CharacterSpell { get; set; }
        public ISpellInfo SpellInfo { get; set; }
        public ISpellInfo ParentSpellInfo { get; set; }
        public ISpellInfo RootSpellInfo { get; set; }
        public bool UserInitiatedSpellCast { get; set; }
        public bool IsProcTriggered { get; set; }
        public bool IsThresholdChild { get; set; }
        public byte ThresholdValue { get; set; }
        public ISpell ThresholdParent { get; set; }
        public uint CastTimeOverride { get; set; }
        public uint PrimaryTargetId { get; set; }
        public Position Position { get; set; }
        public ushort TaxiNode { get; set; }
        public IClientSideInteraction ClientSideInteraction { get; set; }
    }
}
