using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Static.Entity;

namespace NexusForever.Game.Abstract.Entity
{
    public interface ISpellPropertyModifier
    {
        /// <summary>
        /// Logical spell effect which owns this modifier.
        /// </summary>
        SpellEffectIdentity Identity { get; }

        List<IPropertyModifier> Alterations { get; }
        uint Priority { get; }
        Property Property { get; }
        uint StackCount { get; }
    }
}
