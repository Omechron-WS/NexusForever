using NexusForever.Game.Abstract.CSI;
using NexusForever.Network.World.Entity;

namespace NexusForever.Game.Abstract.Spell
{
    public interface ISpellParameters
    {
        ICharacterSpell CharacterSpell { get; set; }
        ISpellInfo SpellInfo { get; set; }
        ISpellInfo ParentSpellInfo { get; set; }
        ISpellInfo RootSpellInfo { get; set; }
        bool UserInitiatedSpellCast { get; set; }

        /// <summary>
        /// Gets or sets whether this spell was dispatched by a proc and must not dispatch further damage procs.
        /// </summary>
        /// <remarks>
        /// Build 16042 contains no-cooldown OnHit procs whose trigger spells deal damage. Preserving this marker
        /// across child spells prevents those damage effects from recursively triggering themselves or other procs.
        /// </remarks>
        bool IsProcTriggered { get; set; }

        /// <summary>
        /// Gets or sets a creature activation cast-time override in milliseconds.
        /// </summary>
        uint CastTimeOverride { get; set; }

        uint PrimaryTargetId { get; set; }
        Position Position { get; set; }
        ushort TaxiNode { get; set; }
        IClientSideInteraction ClientSideInteraction { get; set; }
    }
}
