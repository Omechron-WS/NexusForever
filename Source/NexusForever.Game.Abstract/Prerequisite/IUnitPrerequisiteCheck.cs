using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Static.Prerequisite;

namespace NexusForever.Game.Abstract.Prerequisite
{
    /// <summary>
    /// A prerequisite component whose subject can be any <see cref="IUnitEntity"/>.
    /// </summary>
    public interface IUnitPrerequisiteCheck
    {
        /// <summary>
        /// Returns whether the supplied component data has a supported unit-safe interpretation.
        /// </summary>
        bool CanEvaluate(PrerequisiteComparison comparison, uint value, uint objectId);

        /// <summary>
        /// Attempts to evaluate the component for the supplied unit.
        /// </summary>
        /// <param name="meets">Predicate result when evaluation succeeds.</param>
        /// <returns><see langword="true"/> when the component was evaluated; otherwise, <see langword="false"/>.</returns>
        bool TryMeets(
            IUnitEntity unit,
            PrerequisiteComparison comparison,
            uint value,
            uint objectId,
            out bool meets);
    }
}
