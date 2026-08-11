using NexusForever.Game.Abstract.Entity;

namespace NexusForever.Game.Abstract.Prerequisite
{
    public interface IPrerequisiteManager
    {
        /// <summary>
        /// Checks if <see cref="IPlayer"/> meets supplied prerequisite.
        /// </summary>
        bool Meets(IPlayer player, uint prerequisiteId);

        /// <summary>
        /// Checks if <see cref="IPlayer"/> meets supplied prerequisite.
        /// </summary>
        bool Meets(IPlayer player, uint prerequisiteId, IPrerequisiteParameters parameters);

        /// <summary>
        /// Returns whether every component of the supplied prerequisite has a supported unit-safe interpretation.
        /// </summary>
        bool CanEvaluateForUnit(uint prerequisiteId);

        /// <summary>
        /// Attempts to evaluate a unit-safe prerequisite for the supplied <see cref="IUnitEntity"/>.
        /// </summary>
        /// <param name="meets">Predicate result when evaluation succeeds.</param>
        /// <returns><see langword="true"/> when the complete prerequisite was evaluated; otherwise, <see langword="false"/>.</returns>
        bool TryMeets(IUnitEntity unit, uint prerequisiteId, out bool meets);
    }
}
