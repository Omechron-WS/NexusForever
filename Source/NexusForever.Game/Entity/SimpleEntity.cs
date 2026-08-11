using NexusForever.Database.World.Model;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Network.World.Entity;
using NexusForever.Network.World.Entity.Model;
using NexusForever.Script;
using NLog;

namespace NexusForever.Game.Entity
{
    public class SimpleEntity : UnitEntity, ISimpleEntity
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public override EntityType Type => EntityType.Simple;

        public byte QuestChecklistIdx { get; private set; }

        #region Dependency Injection

        public SimpleEntity(IMovementManager movementManager)
            : base(movementManager)
        {
        }

        #endregion

        public override void Initialise(EntityModel model)
        {
            base.Initialise(model);
            QuestChecklistIdx = model.QuestChecklistIdx;
            scriptCollection = ScriptManager.Instance.InitialiseEntityScripts<ISimpleEntity>(this);
        }

        protected override IEntityModel BuildEntityModel()
        {
            return new SimpleEntityModel
            {
                CreatureId        = CreatureId,
                QuestChecklistIdx = QuestChecklistIdx
            };
        }

        public override void OnActivate(IPlayer activator)
        {
            if (CreatureEntry.DatacubeId != 0u)
                activator.DatacubeManager.AddDatacube((ushort)CreatureEntry.DatacubeId, int.MaxValue);
        }

        public override void OnActivateCast(IPlayer activator)
        {
            base.OnActivateCast(activator);
        }

        /// <inheritdoc />
        public override void OnActivateCast(IPlayer activator, uint clientUniqueId)
        {
            base.OnActivateCast(activator, clientUniqueId);
        }

        /// <inheritdoc />
        public override void OnActivateSuccess(IPlayer activator)
        {
            if (QuestChecklistIdx >= 32)
            {
                base.OnActivateSuccess(activator);
                return;
            }

            uint progress = 1u << QuestChecklistIdx;

            try
            {
                if (CreatureEntry != null && CreatureEntry.DatacubeId != 0u)
                {
                    IDatacube datacube = activator.DatacubeManager.GetDatacube((ushort)CreatureEntry.DatacubeId, DatacubeType.Datacube);
                    if (datacube == null)
                        activator.DatacubeManager.AddDatacube((ushort)CreatureEntry.DatacubeId, progress);
                    else
                    {
                        datacube.Progress |= progress;
                        activator.DatacubeManager.SendDatacube(datacube);
                    }
                }
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to update datacube progress for activated entity {Guid}.");
            }

            try
            {
                if (CreatureEntry != null && CreatureEntry.DatacubeVolumeId != 0u)
                {
                    IDatacube datacube = activator.DatacubeManager.GetDatacube((ushort)CreatureEntry.DatacubeVolumeId, DatacubeType.Journal);
                    if (datacube == null)
                        activator.DatacubeManager.AddDatacubeVolume((ushort)CreatureEntry.DatacubeVolumeId, progress);
                    else
                    {
                        datacube.Progress |= progress;
                        activator.DatacubeManager.SendDatacubeVolume(datacube);
                    }
                }
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to update datacube volume progress for activated entity {Guid}.");
            }

            try
            {
                activator.QuestManager?.ObjectiveUpdate(
                    QuestObjectiveType.ActivateTargetGroupChecklist,
                    CreatureId,
                    QuestChecklistIdx);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to update checklist progress for activated entity {Guid}.");
            }

            base.OnActivateSuccess(activator);
        }
    }
}
