using System.Numerics;
using NexusForever.Game.Abstract.Combat;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Entity.Movement;
using NexusForever.Game.Abstract.Loot;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Combat;
using NexusForever.Game.Spell;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Combat;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Reputation;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Message.Model;
using NexusForever.Network.World.Message.Static;
using NexusForever.Script.Template;
using NexusForever.Shared.Game;
using NLog;
using CombatStateType = NexusForever.Game.Static.Combat.CombatState;

namespace NexusForever.Game.Entity
{
    public abstract class UnitEntity : WorldEntity, IUnitEntity
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();

        public float HitRadius { get; protected set; } = 1f;

        /// <summary>
        /// Guid of the <see cref="IUnitEntity"/> currently targeted.
        /// </summary>
        public uint? TargetGuid { get; private set; }

        /// <summary>
        /// Determines whether or not this <see cref="IUnitEntity"/> is alive.
        /// </summary>
        public bool IsAlive
        {
            get
            {
                lock (deathStateLock)
                    return Health > 0u && deathState == null;
            }
        }

        protected EntityDeathState? DeathState
        {
            get
            {
                lock (deathStateLock)
                    return deathState;
            }
        }

        private readonly object deathStateLock = new();
        private EntityDeathState? deathState;
        private double corpseTimeoutRemaining;
        private double lootedCorpseCleanupRemaining;

        /// <summary>
        /// Determines whether or not this <see cref="IUnitEntity"/> is in combat.
        /// </summary>
        public bool InCombat => CombatState != CombatStateType.Free;

        /// <summary>
        /// Current combat lifecycle state.
        /// </summary>
        public CombatStateType CombatState { get; private set; }

        private bool reportedInCombat;

        public IThreatManager ThreatManager { get; private set; }

        /// <inheritdoc />
        public bool TryGetVitalValue(Vital vital, out float value)
        {
            value = 0f;
            if (!VitalDefinition.TryGet(vital, out VitalDefinition definition))
                return false;

            if (definition.UsesIntegerStorage)
            {
                value = GetIntegerVitalValue(definition);
                return true;
            }

            value = GetStatFloat(definition.Stat) ?? 0f;
            return float.IsFinite(value);
        }

        /// <inheritdoc />
        public bool TryGetVitalMaximum(Vital vital, out float maximum)
        {
            maximum = 0f;
            if (!VitalDefinition.TryGet(vital, out VitalDefinition definition)
                || definition.MaximumProperty == null)
                return false;

            maximum = GetPropertyValue(definition.MaximumProperty.Value);
            if (!float.IsFinite(maximum) || maximum < 0f)
            {
                maximum = 0f;
                return false;
            }

            if (definition.UsesIntegerStorage && (double)maximum > uint.MaxValue)
            {
                maximum = 0f;
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public bool TryModifyVital(Vital vital, float delta, IUnitEntity source = null)
        {
            if (!float.IsFinite(delta)
                || !VitalDefinition.TryGet(vital, out VitalDefinition definition))
                return false;

            if (definition.Stat == Stat.Health && delta > 0f && !IsAlive)
                return false;

            if (definition.UsesIntegerStorage)
                return TryModifyIntegerVital(definition, delta, source);

            float current = GetStatFloat(definition.Stat) ?? 0f;
            if (!float.IsFinite(current)
                || !TryGetVitalMaximum(vital, out float maximum))
                return false;

            double result = Math.Clamp((double)current + delta, 0d, maximum);
            if (!double.IsFinite(result) || result > float.MaxValue)
                return false;

            float value = (float)result;
            if (value != current)
                SetStat(definition.Stat, value);

            return true;
        }

        private uint GetIntegerVitalValue(VitalDefinition definition)
        {
            return definition.Stat switch
            {
                Stat.Health          => Health,
                Stat.Shield          => Shield,
                Stat.InterruptArmour => InterruptArmor,
                _                    => GetStatInteger(definition.Stat) ?? 0u
            };
        }

        private bool TryModifyIntegerVital(VitalDefinition definition, float delta, IUnitEntity source)
        {
            uint current = GetIntegerVitalValue(definition);
            double result = (double)current + delta;

            if (definition.MaximumProperty != null)
            {
                float maximum = GetPropertyValue(definition.MaximumProperty.Value);
                if (!float.IsFinite(maximum) || maximum < 0f || (double)maximum > uint.MaxValue)
                    return false;

                result = Math.Clamp(result, 0d, maximum);
            }
            else
                result = Math.Max(result, 0d);

            if (!double.IsFinite(result) || result > uint.MaxValue)
                return false;

            uint value;
            try
            {
                value = checked((uint)Math.Truncate(result));
            }
            catch (OverflowException)
            {
                return false;
            }

            if (value == current)
                return true;

            switch (definition.Stat)
            {
                case Stat.Health:
                    if (value > current)
                        ModifyHealth(value - current, DamageType.Heal, source);
                    else
                        ModifyHealth(current - value, DamageType.Physical, source);
                    break;
                case Stat.Shield:
                    Shield = value;
                    break;
                case Stat.InterruptArmour:
                    InterruptArmor = value;
                    break;
                default:
                    SetStat(definition.Stat, value);
                    break;
            }

            return true;
        }

        private const double RegenerationInterval = 0.5d;
        private const int MaximumRegenerationCatchUpTicks = 20;

        /// <summary>
        /// Maximum time an unlooted corpse remains in the world. This intentionally matches the
        /// 30-minute loot expiry rather than Omechron's older 10-minute corpse timeout.
        /// </summary>
        private const double CorpseTimeout = 1800d;

        private const double LootedCorpseCleanupDelay = 5d;

        private double regenerationAccumulator;
        private double healthRegenerationRemainder;
        private double shieldRegenerationRemainder;

        private readonly List<ISpell> pendingSpells = new();
        private readonly Dictionary<ProcType, List<IProcInfo>> procs = new();

        private readonly Dictionary<Property, Dictionary</*spell4Id*/uint, List<ISpellPropertyModifier>>> spellProperties = new();

        #region Dependency Injection

        public UnitEntity(IMovementManager movementManager)
            : base(movementManager)
        {
            ThreatManager = new ThreatManager(this);

            InitialiseHitRadius();
        }

        #endregion

        public override void Dispose()
        {
            ThreatManager.ClearThreatList();
            ClearProcs();
            ClearSpellModifierProperties();

            foreach (ISpell spell in pendingSpells.ToArray())
            {
                spell.Finish();
                spell.Dispose();
            }

            pendingSpells.Clear();
            base.Dispose();
        }

        private void InitialiseHitRadius()
        {
            if (CreatureEntry == null)
                return;

            Creature2ModelInfoEntry modelInfoEntry = GameTableManager.Instance.Creature2ModelInfo.GetEntry(CreatureEntry.Creature2ModelInfoId);
            if (modelInfoEntry != null)
                HitRadius = modelInfoEntry.HitRadius * CreatureEntry.ModelScale;
        }

        public override void Update(double lastTick)
        {
            base.Update(lastTick);

            foreach (ISpell spell in pendingSpells.ToArray())
            {
                spell.Update(lastTick);
                spell.LateUpdate(lastTick);

                if (spell.IsFinished)
                {
                    spell.Dispose();
                    pendingSpells.Remove(spell);
                }
            }

            KeyValuePair<ProcType, IProcInfo>[] procSnapshot = procs
                .SelectMany(pair => pair.Value.Select(proc => new KeyValuePair<ProcType, IProcInfo>(pair.Key, proc)))
                .ToArray();
            foreach (KeyValuePair<ProcType, IProcInfo> entry in procSnapshot)
            {
                if (!IsProcRegistered(entry.Key, entry.Value))
                    continue;

                try
                {
                    entry.Value.Update(lastTick);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to update {entry.Key} proc for entity {Guid}.");

                    if (!TryDetachProc(entry.Key, entry.Value))
                        continue;

                    try
                    {
                        entry.Value.Cancel();
                    }
                    catch (Exception cancelException)
                    {
                        log.Error(cancelException, $"Failed to cancel rejected {entry.Key} proc for entity {Guid}.");
                    }
                }
            }

            ThreatManager.Update(lastTick);
            CombatStateTick();
            UpdateRegeneration(lastTick);
            UpdateDeathLifecycle(lastTick);
        }

        /// <summary>
        /// Remove tracked <see cref="IGridEntity"/> that is no longer in vision range.
        /// </summary>
        public override void RemoveVisible(IGridEntity entity)
        {
            if (entity.Guid == TargetGuid)
                SetTarget((IWorldEntity)null);

            ThreatManager.RemoveHostile(entity.Guid);

            base.RemoveVisible(entity);
        }

        /// <summary>
        /// Add or refresh an owned <see cref="Property"/> modifier.
        /// </summary>
        public bool AddSpellModifierProperty(ISpellPropertyModifier spellModifier)
        {
            ArgumentNullException.ThrowIfNull(spellModifier);

            if (!IsAlive)
                return false;

            if (!spellProperties.TryGetValue(
                spellModifier.Property,
                out Dictionary<uint, List<ISpellPropertyModifier>> spellGroups))
            {
                spellGroups = [];
                spellProperties.Add(spellModifier.Property, spellGroups);
            }

            uint spell4Id = spellModifier.Identity.Spell4Id;
            if (!spellGroups.TryGetValue(spell4Id, out List<ISpellPropertyModifier> owners))
            {
                owners = [];
                spellGroups.Add(spell4Id, owners);
            }

            int previousIndex = owners.FindIndex(owner => owner.Identity == spellModifier.Identity);
            ISpellPropertyModifier previousModifier = null;
            if (previousIndex >= 0)
            {
                previousModifier = owners[previousIndex];
                owners.RemoveAt(previousIndex);
            }

            owners.Add(spellModifier);

            try
            {
                CalculateProperty(spellModifier.Property);
                return true;
            }
            catch
            {
                int appliedIndex = owners.FindLastIndex(owner => ReferenceEquals(owner, spellModifier));
                if (appliedIndex >= 0)
                    owners.RemoveAt(appliedIndex);

                if (previousModifier != null)
                    owners.Insert(previousIndex, previousModifier);

                RemoveEmptySpellPropertyGroups(spellModifier.Property, spell4Id, owners, spellGroups);

                try
                {
                    CalculateProperty(spellModifier.Property);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to restore property {spellModifier.Property} after modifier {spellModifier.Identity} was rejected.");
                }

                throw;
            }
        }

        /// <summary>
        /// Remove one exact owned <see cref="Property"/> modifier.
        /// </summary>
        public bool RemoveSpellModifierProperty(Property property, SpellEffectIdentity identity)
        {
            if (!spellProperties.TryGetValue(
                property,
                out Dictionary<uint, List<ISpellPropertyModifier>> spellGroups)
                || !spellGroups.TryGetValue(identity.Spell4Id, out List<ISpellPropertyModifier> owners))
                return false;

            int index = owners.FindIndex(owner => owner.Identity == identity);
            if (index < 0)
                return false;

            ISpellPropertyModifier removedModifier = owners[index];
            owners.RemoveAt(index);

            try
            {
                CalculateProperty(property);
                RemoveEmptySpellPropertyGroups(property, identity.Spell4Id, owners, spellGroups);
                return true;
            }
            catch
            {
                owners.Insert(index, removedModifier);

                try
                {
                    CalculateProperty(property);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to restore property {property} after modifier {identity} removal failed.");
                }

                throw;
            }
        }

        /// <summary>
        /// Remove all spell-owned <see cref="Property"/> modifiers affecting this <see cref="IUnitEntity"/>.
        /// </summary>
        public void ClearSpellModifierProperties()
        {
            Property[] affectedProperties = spellProperties.Keys.ToArray();
            spellProperties.Clear();

            foreach (Property property in affectedProperties)
            {
                try
                {
                    CalculateProperty(property);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to recalculate property {property} after clearing spell modifiers.");
                }
            }
        }

        private void RemoveEmptySpellPropertyGroups(
            Property property,
            uint spell4Id,
            List<ISpellPropertyModifier> owners,
            Dictionary<uint, List<ISpellPropertyModifier>> spellGroups)
        {
            if (owners.Count == 0)
                spellGroups.Remove(spell4Id);

            if (spellGroups.Count == 0)
                spellProperties.Remove(property);
        }

        /// <summary>
        /// Return all <see cref="IPropertyModifier"/> for this <see cref="IUnitEntity"/>'s <see cref="Property"/>
        /// </summary>
        private IEnumerable<ISpellPropertyModifier> GetSpellPropertyModifiers(Property property)
        {
            if (!spellProperties.TryGetValue(
                property,
                out Dictionary<uint, List<ISpellPropertyModifier>> spellGroups))
                return Enumerable.Empty<ISpellPropertyModifier>();

            // Stack-group semantics are not implemented yet. Preserve the existing one-modifier-per-Spell4
            // calculation while retaining older live owners for independent cleanup.
            return spellGroups.Values
                .Where(owners => owners.Count != 0)
                .Select(owners => owners[^1]);
        }

        protected override void CalculatePropertyValue(IPropertyValue propertyValue)
        {
            base.CalculatePropertyValue(propertyValue);

            // Run through spell adjustments first because they could adjust base properties
            // dataBits01 appears to be some form of Priority or Math Operator
            foreach (ISpellPropertyModifier spellModifier in GetSpellPropertyModifiers(propertyValue.Property)
                .OrderByDescending(s => s.Priority))
            {
                foreach (IPropertyModifier alteration in spellModifier.Alterations)
                {
                    // TODO: Add checks to ensure we're not modifying FlatValue and Percentage in the same effect?
                    switch (alteration.ModType)
                    {
                        case ModType.FlatValue:
                        case ModType.LevelScale:
                            propertyValue.Value += alteration.GetValue(Level);
                            break;
                        case ModType.Percentage:
                            propertyValue.Value *= alteration.GetValue();
                            break;
                    }
                }
            }
        }

        private void UpdateRegeneration(double elapsed)
        {
            if (!double.IsFinite(elapsed) || elapsed <= 0d)
                return;

            double maximumCatchUp = RegenerationInterval * MaximumRegenerationCatchUpTicks;
            regenerationAccumulator = Math.Min(regenerationAccumulator + elapsed, maximumCatchUp);

            int ticks = Math.Min(
                (int)Math.Floor(regenerationAccumulator / RegenerationInterval),
                MaximumRegenerationCatchUpTicks);
            regenerationAccumulator -= ticks * RegenerationInterval;

            for (int i = 0; i < ticks; i++)
                OnRegenerationTick();
        }

        /// <summary>
        /// Applies one fixed 0.5 second unit regeneration tick.
        /// </summary>
        protected virtual void OnRegenerationTick()
        {
            if (!IsAlive || InCombat)
                return;

            RegenerateIntegerVital(
                Vital.Health,
                MaxHealth / 50d,
                Health < MaxHealth ? MaxHealth - Health : 0u,
                ref healthRegenerationRemainder);

            float shieldRegenerationPercentage = GetPropertyValue(Property.ShieldRegenPct);
            double shieldRegeneration = float.IsFinite(shieldRegenerationPercentage)
                ? MaxShieldCapacity * (double)shieldRegenerationPercentage * RegenerationInterval
                : 0d;
            RegenerateIntegerVital(
                Vital.ShieldCapacity,
                shieldRegeneration,
                Shield < MaxShieldCapacity ? MaxShieldCapacity - Shield : 0u,
                ref shieldRegenerationRemainder);
        }

        private void RegenerateIntegerVital(Vital vital, double amount, uint deficit, ref double remainder)
        {
            if (deficit == 0u)
            {
                remainder = 0d;
                return;
            }

            if (!double.IsFinite(amount) || amount <= 0d)
            {
                remainder = 0d;
                return;
            }

            double total = Math.Min(amount + remainder, deficit);
            uint wholeAmount = (uint)Math.Floor(total);
            remainder = total - wholeAmount;
            if (wholeAmount == 0u)
                return;

            if (!TryModifyVital(vital, wholeAmount))
            {
                remainder = 0d;
                return;
            }

            if (wholeAmount == deficit)
                remainder = 0d;
        }

        private void UpdateDeathLifecycle(double elapsed)
        {
            if (this is IPlayer || !double.IsFinite(elapsed) || elapsed <= 0d)
                return;

            EntityDeathState? expiredState = null;
            lock (deathStateLock)
            {
                switch (deathState)
                {
                    case EntityDeathState.Corpse:
                        corpseTimeoutRemaining = Math.Max(0d, corpseTimeoutRemaining - elapsed);
                        if (corpseTimeoutRemaining == 0d)
                            expiredState = EntityDeathState.Corpse;
                        break;
                    case EntityDeathState.CorpseLooted:
                        lootedCorpseCleanupRemaining = Math.Max(0d, lootedCorpseCleanupRemaining - elapsed);
                        if (lootedCorpseCleanupRemaining == 0d)
                            expiredState = EntityDeathState.CorpseLooted;
                        break;
                }
            }

            if (!expiredState.HasValue || Map == null)
                return;

            try
            {
                Map.EnqueueRemove(this);
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to enqueue expired corpse {Guid} for removal.");
                return;
            }

            TryTransitionDeathState(expiredState, EntityDeathState.Dead);
        }

        private bool TryTransitionDeathState(EntityDeathState? expectedState, EntityDeathState newState)
        {
            lock (deathStateLock)
            {
                if (deathState != expectedState)
                    return false;

                deathState = newState;

                switch (newState)
                {
                    case EntityDeathState.Corpse:
                        corpseTimeoutRemaining = CorpseTimeout;
                        lootedCorpseCleanupRemaining = 0d;
                        break;
                    case EntityDeathState.CorpseLooted:
                        corpseTimeoutRemaining = 0d;
                        lootedCorpseCleanupRemaining = LootedCorpseCleanupDelay;
                        break;
                    case EntityDeathState.Dead:
                        corpseTimeoutRemaining = 0d;
                        lootedCorpseCleanupRemaining = 0d;
                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// Clear the death lifecycle after health has been restored by the resurrection system.
        /// </summary>
        protected void ClearDeathState()
        {
            lock (deathStateLock)
            {
                if (deathState == null)
                    return;

                deathState = null;
                corpseTimeoutRemaining = 0d;
                lootedCorpseCleanupRemaining = 0d;
            }

            PublishDeathState();
        }

        /// <summary>
        /// Publish the current alive or dead state to nearby players.
        /// </summary>
        protected virtual void PublishDeathState()
        {
            bool isAlive = IsAlive;
            EnqueueToVisible(new ServerEntityDeathState
            {
                UnitId    = Guid,
                Dead      = !isAlive,
                Reason    = 0, // client does nothing with this value
                RezHealth = isAlive ? Health : 0u
            }, true);
        }

        /// <summary>
        /// Cast a <see cref="ISpell"/> with the supplied spell id and <see cref="ISpellParameters"/>.
        /// </summary>
        public void CastSpell(uint spell4Id, ISpellParameters parameters)
        {
            CastSpellTracked(spell4Id, parameters);
        }

        /// <summary>
        /// Cast a <see cref="ISpell"/> with the supplied spell id and return the created spell, or null if no spell was created.
        /// </summary>
#nullable enable
        public ISpell? CastSpellTracked(uint spell4Id, ISpellParameters parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException();

            Spell4Entry spell4Entry = GameTableManager.Instance.Spell4.GetEntry(spell4Id);
            if (spell4Entry == null)
                throw new ArgumentOutOfRangeException();

            return CastSpellInternal(spell4Entry.Spell4BaseIdBaseSpell, (byte)spell4Entry.TierIndex, parameters);
        }
#nullable disable

        /// <summary>
        /// Cast a <see cref="ISpell"/> with the supplied spell base id, tier and <see cref="ISpellParameters"/>.
        /// </summary>
        public void CastSpell(uint spell4BaseId, byte tier, ISpellParameters parameters)
        {
            CastSpellInternal(spell4BaseId, tier, parameters);
        }

        private ISpell CastSpellInternal(uint spell4BaseId, byte tier, ISpellParameters parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException();

            ISpellBaseInfo spellBaseInfo = GlobalSpellManager.Instance.GetSpellBaseInfo(spell4BaseId);
            if (spellBaseInfo == null)
                throw new ArgumentOutOfRangeException();

            ISpellInfo spellInfo = spellBaseInfo.GetSpellInfo(tier);
            if (spellInfo == null)
                throw new ArgumentOutOfRangeException();

            parameters.SpellInfo = spellInfo;
            return CastSpellInternal(parameters);
        }

        /// <summary>
        /// Cast a <see cref="ISpell"/> with the supplied <see cref="ISpellParameters"/>.
        /// </summary>
        public void CastSpell(ISpellParameters parameters)
        {
            CastSpellInternal(parameters);
        }

        private ISpell CastSpellInternal(ISpellParameters parameters)
        {
            if (!IsAlive)
                return null;

            if (parameters == null)
                throw new ArgumentNullException();

            if (DisableManager.Instance.IsDisabled(DisableType.BaseSpell, parameters.SpellInfo.BaseInfo.Entry.Id))
            {
                if (this is IPlayer player)
                    player.SendSystemMessage($"Unable to cast base spell {parameters.SpellInfo.BaseInfo.Entry.Id} because it is disabled.");
                return null;
            }

            if (DisableManager.Instance.IsDisabled(DisableType.Spell, parameters.SpellInfo.Entry.Id))
            {
                if (this is IPlayer player)
                    player.SendSystemMessage($"Unable to cast spell {parameters.SpellInfo.Entry.Id} because it is disabled.");
                return null;
            }

            ApplyUserInitiatedCastSideEffects(parameters);

            CastMethod castMethod = ResolveCastMethod(parameters);
            ISpell spell = GlobalSpellManager.Instance.NewSpell(castMethod, this, parameters);
            if (spell == null)
                return null;

            return StartAndTrackSpell(spell);
        }

        /// <summary>
        /// Apply side effects which belong to the one user-started root transaction.
        /// </summary>
        internal void ApplyUserInitiatedCastSideEffects(ISpellParameters parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            if (parameters.UserInitiatedSpellCast && !parameters.IsThresholdChild)
                DismountForUserInitiatedCast();
        }

        /// <summary>
        /// Dismount a player for a user-started root cast.
        /// </summary>
        protected virtual void DismountForUserInitiatedCast()
        {
            if (this is IPlayer player)
                player.Dismount();
        }

        /// <summary>
        /// Make a spell discoverable before its start callbacks and tear it down if start throws.
        /// </summary>
        internal ISpell StartAndTrackSpell(ISpell spell)
        {
            ArgumentNullException.ThrowIfNull(spell);

            try
            {
                pendingSpells.Add(spell);
                spell.Cast();
                return spell;
            }
            catch
            {
                int trackedIndex = pendingSpells.FindIndex(candidate => ReferenceEquals(candidate, spell));
                if (trackedIndex >= 0)
                    pendingSpells.RemoveAt(trackedIndex);

                try
                {
                    spell.Finish();
                }
                catch (Exception cleanupException)
                {
                    log.Error(cleanupException, "Failed to finish a spell after its start threw.");
                }

                try
                {
                    spell.Dispose();
                }
                catch (Exception cleanupException)
                {
                    log.Error(cleanupException, "Failed to dispose a spell after its start threw.");
                }

                throw;
            }
        }

        /// <summary>
        /// Resolves the concrete spell implementation, forcing activation spells through the CSI lifecycle.
        /// </summary>
        internal static CastMethod ResolveCastMethod(ISpellParameters parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);
            return parameters.ClientSideInteraction != null
                ? CastMethod.ClientSideInteraction
                : (CastMethod)parameters.SpellInfo.BaseInfo.Entry.CastMethod;
        }

        /// <summary>
        /// Cancel any <see cref="ISpell"/>'s that are interrupted by movement.
        /// </summary>
        public void CancelSpellsOnMove()
        {
            foreach (ISpell spell in pendingSpells)
                if (spell.IsMovingInterrupted() && spell.IsCasting)
                    spell.CancelCast(CastResult.CasterMovement);
        }

        /// <summary>
        /// Cancel an <see cref="ISpell"/> based on its casting id.
        /// </summary>
        /// <param name="castingId">Casting ID of the spell to cancel</param>
        public void CancelSpellCast(uint castingId)
        {
            ISpell spell = pendingSpells.SingleOrDefault(s => s.CastingId == castingId);
            spell?.CancelCast(CastResult.SpellCancelled);
        }

        /// <summary>
        /// Register a proc on this entity, rejecting a duplicate effect entry for the same event type.
        /// </summary>
        public bool ApplyProc(IProcInfo proc)
        {
            ArgumentNullException.ThrowIfNull(proc);

            if (!ReferenceEquals(proc.Owner, this))
                throw new ArgumentException("The proc owner does not match this entity.", nameof(proc));

            if (!procs.TryGetValue(proc.Type, out List<IProcInfo> procList))
            {
                procList = [];
                procs.Add(proc.Type, procList);
            }

            if (procList.Any(existing => existing.EffectId == proc.EffectId))
                return false;

            procList.Add(proc);
            return true;
        }

        /// <summary>
        /// Remove a proc from this entity.
        /// </summary>
        public bool RemoveProc(IProcInfo proc)
        {
            if (proc == null || !procs.TryGetValue(proc.Type, out List<IProcInfo> procList))
                return false;

            int index = procList.FindIndex(candidate => ReferenceEquals(candidate, proc));
            if (index < 0)
                return false;

            IProcInfo removedProc = procList[index];
            procList.RemoveAt(index);
            if (procList.Count == 0)
                procs.Remove(proc.Type);

            removedProc.Cancel();
            return true;
        }

        private bool IsProcRegistered(ProcType type, IProcInfo proc)
        {
            return procs.TryGetValue(type, out List<IProcInfo> procList)
                && procList.Any(candidate => ReferenceEquals(candidate, proc));
        }

        private bool TryDetachProc(ProcType type, IProcInfo proc)
        {
            if (!procs.TryGetValue(type, out List<IProcInfo> procList))
                return false;

            int index = procList.FindIndex(candidate => ReferenceEquals(candidate, proc));
            if (index < 0)
                return false;

            procList.RemoveAt(index);
            if (procList.Count == 0)
                procs.Remove(type);

            return true;
        }

        private void ClearProcs()
        {
            IProcInfo[] activeProcs = procs.Values.SelectMany(list => list).ToArray();
            procs.Clear();

            foreach (IProcInfo proc in activeProcs)
                proc.Cancel();
        }

        /// <summary>
        /// Dispatch a proc event to all matching procs on this entity.
        /// </summary>
        public void FireProc(ProcType type, IUnitEntity primaryTarget = null)
        {
            if (!procs.TryGetValue(type, out List<IProcInfo> procList))
                return;

            foreach (IProcInfo proc in procList.ToArray())
            {
                if (!IsProcRegistered(type, proc))
                    continue;

                try
                {
                    proc.Trigger(primaryTarget);
                }
                catch (Exception exception)
                {
                    log.Error(exception, $"Failed to trigger {type} proc for entity {Guid}.");

                    if (!TryDetachProc(type, proc))
                        continue;

                    try
                    {
                        proc.Cancel();
                    }
                    catch (Exception cancelException)
                    {
                        log.Error(cancelException, $"Failed to cancel rejected {type} proc for entity {Guid}.");
                    }
                }
            }
        }

        /// <summary>
        /// Returns an active <see cref="ISpell"/> that is affecting this <see cref="IUnitEntity"/>
        /// </summary>
        public ISpell GetActiveSpell(Func<ISpell, bool> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);
            return pendingSpells.FirstOrDefault(predicate);
        }

        /// <summary>
        /// Returns an active <see cref="ISpell"/> with the supplied server casting identifier.
        /// </summary>
        public ISpell GetActiveSpell(uint castingId)
        {
            return pendingSpells.FirstOrDefault(spell => spell.CastingId == castingId);
        }

        /// <summary>
        /// Determine if this <see cref="IUnitEntity"/> can attack supplied <see cref="IUnitEntity"/>.
        /// </summary>
        public virtual bool CanAttack(IUnitEntity target)
        {
            if (!IsAlive)
                return false;

            if (!target.IsValidAttackTarget() || !IsValidAttackTarget())
                return false;

            return GetDispositionTo(target.Faction1) < Disposition.Friendly;
        }

        /// <summary>
        /// Returns whether or not this <see cref="IUnitEntity"/> is an attackable target.
        /// </summary>
        public bool IsValidAttackTarget()
        {
            // TODO: Expand on this. There's bound to be flags or states that should prevent an entity from being attacked.
            return (this is IPlayer or INonPlayerEntity);
        }

        /// <summary>
        /// Deal damage to this <see cref="IUnitEntity"/> from the supplied <see cref="IUnitEntity"/>.
        /// </summary>
        public void TakeDamage(IUnitEntity attacker, IDamageDescription damageDescription, bool triggerProcs = true)
        {
            if (!IsAlive || !attacker.IsAlive)
                return;

            if (triggerProcs)
            {
                attacker.FireProc(ProcType.OnHit, this);
                FireProc(ProcType.OnDamageReceived, attacker);
            }

            // TODO: Calculate Threat properly
            ThreatManager.UpdateThreat(attacker, (int)damageDescription.RawDamage);

            Shield -= damageDescription.ShieldAbsorbAmount;
            ModifyHealth(damageDescription.AdjustedDamage, damageDescription.DamageType, attacker);
        }

        /// <summary>
        /// Modify the health of this <see cref="IUnitEntity"/> by the supplied amount.
        /// </summary>
        /// <remarks>
        /// If the <see cref="DamageType"/> is <see cref="DamageType.Heal"/> amount is added to current health otherwise subtracted.
        /// </remarks>
        public virtual void ModifyHealth(uint amount, DamageType type, IUnitEntity source)
        {
            uint previousHealth = Health;
            long newHealth = previousHealth;
            if (type == DamageType.Heal)
                newHealth += amount;
            else
                newHealth -= amount;

            Health = (uint)Math.Clamp(newHealth, 0u, MaxHealth);

            if (previousHealth > 0u && Health == 0u)
                OnDeath();
        }

        protected virtual void OnDeath()
        {
            if (!TryTransitionDeathState(null, EntityDeathState.JustDied))
                return;

            regenerationAccumulator = 0d;
            healthRegenerationRemainder = 0d;
            shieldRegenerationRemainder = 0d;

            try
            {
                ExecuteDeathOperation(PublishDeathState, "publish the death state");

                foreach (ISpell spell in pendingSpells.ToArray())
                {
                    ExecuteDeathOperation(() =>
                    {
                        if (spell.IsCasting)
                            spell.CancelCast(CastResult.CasterCannotBeDead);
                        else
                            spell.Finish();
                    }, "stop a pending spell during death");
                }

                ExecuteDeathOperation(ClearSpellModifierProperties, "clear spell property modifiers during death");
                ExecuteDeathOperation(ClearProcs, "clear procs during death");
                ExecuteDeathOperation(GenerateRewards, "generate death rewards");
            }
            finally
            {
                bool enteredCorpse = TryTransitionDeathState(EntityDeathState.JustDied, EntityDeathState.Corpse);
                ExecuteDeathOperation(ClearThreatsAfterDeath, "clear the death threat list");

                if (enteredCorpse && this is not IPlayer)
                {
                    if (EntityId != 0u)
                        ExecuteDeathOperation(ScheduleRespawnAfterDeath, "schedule the entity respawn");

                    if (Loot.Count == 0)
                        TryTransitionDeathState(EntityDeathState.Corpse, EntityDeathState.CorpseLooted);
                }
            }
        }

        private void ScheduleRespawnAfterDeath()
        {
            if (Map == null)
            {
                log.Error($"Persistent entity {EntityId} has no map available for respawn scheduling.");
                return;
            }

            if (!Map.ScheduleRespawn(this))
                log.Warn($"Map rejected the respawn reservation for persistent entity {EntityId}.");
        }

        private void ExecuteDeathOperation(Action operation, string description)
        {
            try
            {
                operation();
            }
            catch (Exception exception)
            {
                log.Error(exception, $"Failed to {description} for entity {Guid}.");
            }
        }

        /// <summary>
        /// Clear all hostile relationships after the entity has entered its corpse state.
        /// </summary>
        protected virtual void ClearThreatsAfterDeath()
        {
            ThreatManager.ClearThreatList();
        }

        /// <summary>
        /// Grant kill credit and loot to eligible players captured by this entity's threat list.
        /// </summary>
        protected virtual void GenerateRewards()
        {
            foreach (IHostileEntity hostile in ThreatManager.ToArray())
            {
                ExecuteDeathOperation(() =>
                {
                    IUnitEntity entity = GetVisible<IUnitEntity>(hostile.HatedUnitId);
                    if (entity is IPlayer player)
                        RewardKiller(player);
                }, $"generate death rewards for threat participant {hostile.HatedUnitId}");
            }
        }

        /// <inheritdoc />
        protected override void OnLootRemoved(ILootInstance lootInstance)
        {
            base.OnLootRemoved(lootInstance);

            if (this is not IPlayer && Loot.Count == 0)
                TryTransitionDeathState(EntityDeathState.Corpse, EntityDeathState.CorpseLooted);
        }

        protected virtual void RewardKiller(IPlayer player)
        {
            uint creatureId = CreatureId;
            uint entityGuid = Guid;
            ulong characterId = player.CharacterId;
            IReadOnlyList<(string Operation, Exception Exception)> failures = DispatchKillRewards(
                creatureId,
                () =>
                {
                    uint creatureDifficultyId = CreatureEntry?.Creature2DifficultyId ?? 0u;
                    if (creatureDifficultyId == 0u)
                        return null;

                    return GameTableManager.Instance.Creature2Difficulty
                        ?.GetEntry(creatureDifficultyId)
                        ?.Id;
                },
                () => AssetManager.Instance.GetTargetGroupsForCreatureId(creatureId),
                (type, data, progress) => player.QuestManager.ObjectiveUpdate(type, data, progress),
                () => NexusForever.Game.Loot.GlobalLootManager.Instance.DropLoot(player, this));

            foreach ((string operation, Exception exception) in failures)
            {
                log.Error(exception,
                    $"Failed to {operation} for entity {entityGuid} and player {characterId}.");
            }

            // TODO: Reward XP
            // TODO: Handle Achievements
        }

        /// <summary>
        /// Dispatch all kill-credit and loot observers for one rewarded participant.
        /// </summary>
        internal static IReadOnlyList<(string Operation, Exception Exception)> DispatchKillRewards(
            uint creatureId,
            Func<uint?> resolveDifficultyId,
            Func<IEnumerable<uint>> resolveTargetGroupIds,
            Action<QuestObjectiveType, uint, uint> updateObjective,
            Action dropLoot)
        {
            uint? creatureDifficultyId = null;
            uint[] targetGroupIds = [];
            List<(string Operation, Exception Exception)> failures = [];

            Execute("resolve creature difficulty",
                () => creatureDifficultyId = resolveDifficultyId());
            Execute("resolve target groups",
                () => targetGroupIds = resolveTargetGroupIds()?.ToArray() ?? []);

            Execute("publish direct creature kill credit",
                () => updateObjective(QuestObjectiveType.KillCreature, creatureId, 1u));

            if (creatureDifficultyId is uint resolvedDifficultyId)
            {
                Execute("publish creature difficulty kill credit",
                    () => updateObjective(QuestObjectiveType.KillCreature2, resolvedDifficultyId, 1u));
            }

            foreach (uint targetGroupId in targetGroupIds)
            {
                Execute($"publish target-group {targetGroupId} kill credit",
                    () => updateObjective(QuestObjectiveType.KillTargetGroup, targetGroupId, 1u));
                Execute($"publish target-groups {targetGroupId} kill credit",
                    () => updateObjective(QuestObjectiveType.KillTargetGroups, targetGroupId, 1u));
            }

            Execute("drop loot", dropLoot);
            return failures;

            void Execute(string operation, Action action)
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failures.Add((operation, exception));
                }
            }
        }

        /// <summary>
        /// Set target to supplied target guid.
        /// </summary>
        /// <remarks>
        /// A null target will clear the current target.
        /// </remarks>
        public void SetTarget(uint? target, uint threat = 0u)
        {
            SetTarget(target != null ? GetVisible<IWorldEntity>(target.Value) : null, threat);
        }

        /// <summary>
        /// Set target to supplied <see cref="IUnitEntity"/>.
        /// </summary>
        /// <remarks>
        /// A null target will clear the current target.
        /// </remarks>
        public virtual void SetTarget(IWorldEntity target, uint threat = 0u)
        {
            // notify current target they are no longer the target
            if (TargetGuid != null)
                GetVisible<IWorldEntity>(TargetGuid.Value)?.OnUntargeted(this);

            target?.OnTargeted(this);

            EnqueueToVisible(new ServerEntityTargetUnit
            {
                UnitId      = Guid,
                NewTargetId = target?.Guid ?? 0u,
                ThreatLevel = threat
            });

            TargetGuid = target?.Guid;
        }

        /// <summary>
        /// Invoked when a new <see cref="IHostileEntity"/> is added to the threat list.
        /// </summary>
        public virtual void OnThreatAddTarget(IHostileEntity hostile)
        {
            UpdateCombatState();
            scriptCollection?.Invoke<IUnitScript>(s => s.OnThreatAddTarget(hostile));
        }

        /// <summary>
        /// Invoked when an existing <see cref="IHostileEntity"/> is removed from the threat list.
        /// </summary>
        public virtual void OnThreatRemoveTarget(IHostileEntity hostile)
        {
            UpdateCombatState();
            scriptCollection?.Invoke<IUnitScript>(s => s.OnThreatRemoveTarget(hostile));
        }

        /// <summary>
        /// Invoked when an existing <see cref="IHostileEntity"/> is update on the threat list.
        /// </summary>
        public virtual void OnThreatChange(IHostileEntity hostile)
        {
            scriptCollection?.Invoke<IUnitScript>(s => s.OnThreatChange(hostile));
        }

        private void UpdateCombatState()
        {
            if (ThreatManager.IsThreatened)
            {
                CombatState = CombatStateType.Engaged;
                return;
            }

            if (CombatState == CombatStateType.Engaged)
                CombatState = CombatStateType.Exiting;
        }

        /// <summary>
        /// Advance delayed combat exit and publish state changes at tick boundaries.
        /// </summary>
        private void CombatStateTick()
        {
            CombatState = CombatState switch
            {
                CombatStateType.Exiting => CombatStateType.Exited,
                CombatStateType.Exited  => CombatStateType.Free,
                _                       => CombatState
            };

            if (reportedInCombat == InCombat)
                return;

            reportedInCombat = InCombat;
            OnCombatStateChange(InCombat);
        }

        /// <summary>
        /// Publish an entered or exited combat transition.
        /// </summary>
        protected virtual void OnCombatStateChange(bool inCombat)
        {
            Sheathed = !inCombat;
            SetStandState(inCombat ? StandState.Stand : StandState.State0);

            EnqueueToVisible(new ServerUnitEnteredCombat
            {
                UnitId   = Guid,
                InCombat = inCombat
            }, true);

            scriptCollection?.Invoke<IUnitScript>(script => script.OnCombatStateChange(inCombat));
        }
    }
}
