using System.Collections.Concurrent;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using NexusForever.Game.Abstract;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Combat;
using NexusForever.Game.Entity;
using NexusForever.Game.Map;
using NexusForever.Game.Quest;
using NexusForever.Game.Static.Combat;
using NexusForever.Game.Static.Entity;
using NexusForever.Game.Static.Quest;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.World.Combat;
using NexusForever.Network.World.Message.Model;
using NexusForever.Shared;
using NLog;

namespace NexusForever.Game.Spell
{
    public static class SpellHandler
    {
        private static readonly ILogger log = LogManager.GetCurrentClassLogger();
        private static readonly ConcurrentDictionary<uint, byte> reportedUnsupportedVitalModifierEffects = new();

        private const uint MaximumQuestId = 0x7FFFu;

        [SpellEffectHandler(SpellEffectType.Damage)]
        public static void HandleEffectDamage(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            if (!target.CanAttack(spell.Caster))
                return;

            // TODO: once spell effect handlers aren't static, this should be injected without the factory
            var factory = LegacyServiceProvider.Provider.GetService<IFactory<IDamageCalculator>>();
            var damageCalculator = factory.Resolve();
            damageCalculator.CalculateDamage(spell.Caster, target, spell, info);

            if (info.DropEffect || info.Damage == null)
                return;

            bool triggerProcs = !spell.Parameters.IsProcTriggered;
            if (triggerProcs && info.Damage.CombatResult == CombatResult.Critical)
                spell.Caster.FireProc(ProcType.CriticalDamage, target);

            target.TakeDamage(spell.Caster, info.Damage, triggerProcs);
        }

        [SpellEffectHandler(SpellEffectType.VitalModifier)]
        public static void HandleEffectVitalModifier(
            ISpell spell,
            IUnitEntity target,
            ISpellTargetEffectInfo info)
        {
            Spell4EffectsEntry entry = info.Entry;
            if (entry.DataBits01 != entry.DataBits02
                || entry.DataBits03 != 0u
                || entry.DataBits04 != 0u
                || entry.DataBits05 != 0u
                || entry.DataBits06 != 0u
                || entry.DataBits07 != 0u
                || entry.DataBits08 != 0u
                || entry.DataBits09 > 1u
                || entry.ParameterType is not { Length: 4 }
                || entry.ParameterType.Any(parameter => parameter != SpellEffectParameterType.None)
                || entry.ParameterValue is not { Length: 4 }
                || entry.ParameterValue.Any(parameter => !float.IsFinite(parameter) || parameter != 0f))
            {
                DropUnsupportedVitalModifierEffect(info, "the row uses an unsupported formula or range shape");
                return;
            }

            var vital = (Vital)entry.DataBits00;
            if (!VitalDefinition.TryGet(vital, out VitalDefinition definition))
            {
                DropUnsupportedVitalModifierEffect(info, $"vital {vital} is not backed by the unit vital policy");
                return;
            }

            int fixedAmountValue = unchecked((int)entry.DataBits01);
            float fixedAmount = fixedAmountValue;
            if (!float.IsFinite(fixedAmount) || (double)fixedAmount != fixedAmountValue)
            {
                DropUnsupportedVitalModifierEffect(info, "the fixed amount cannot be represented exactly by the unit vital API");
                return;
            }

            if (fixedAmount == 0f)
            {
                info.DropEffect = true;
                return;
            }

            float previousValue;
            try
            {
                if (!target.TryGetVitalValue(vital, out previousValue)
                    || !float.IsFinite(previousValue))
                {
                    DropVitalModifierEffect(info, $"vital {vital} is unsupported or unreadable");
                    return;
                }
            }
            catch (Exception exception)
            {
                DropVitalModifierEffect(info, $"vital {vital} could not be read", exception);
                return;
            }

            if (!TryCalculateVitalModifierValue(
                    target,
                    vital,
                    definition,
                    previousValue,
                    fixedAmount,
                    out float expectedValue))
            {
                DropVitalModifierEffect(info, $"vital {vital} has an invalid maximum or result");
                return;
            }

            if (expectedValue == previousValue)
            {
                info.DropEffect = true;
                return;
            }

            CombatLogCastData castData;
            IUnitEntity caster;
            try
            {
                caster = spell.Caster;
                castData = new CombatLogCastData
                {
                    CasterId     = caster.Guid,
                    TargetId     = target.Guid,
                    SpellId      = spell.Parameters.SpellInfo.Entry.Id,
                    CombatResult = CombatResult.Hit
                };
            }
            catch (Exception exception)
            {
                DropVitalModifierEffect(info, "combat-log identity could not be captured", exception);
                return;
            }

            bool modified = false;
            Exception mutationException = null;
            try
            {
                modified = target.TryModifyVital(vital, fixedAmount, caster);
            }
            catch (Exception exception)
            {
                mutationException = exception;
            }

            if (!modified && mutationException == null)
            {
                DropVitalModifierEffect(info, $"vital {vital} mutation reported failure");
                return;
            }

            float currentValue;
            try
            {
                if (!target.TryGetVitalValue(vital, out currentValue)
                    || !float.IsFinite(currentValue)
                    || currentValue != expectedValue)
                {
                    DropVitalModifierEffect(info, $"vital {vital} could not be reconciled", mutationException);
                    return;
                }
            }
            catch (Exception exception)
            {
                DropVitalModifierEffect(info, $"vital {vital} reconciliation failed", exception);
                return;
            }

            if (mutationException != null
                && definition.Stat == Stat.Health
                && expectedValue == 0f)
            {
                DropVitalModifierEffect(
                    info,
                    "Health reached zero while its death transition could not be reconciled",
                    mutationException);
                return;
            }

            float actualAmount = expectedValue - previousValue;

            if (mutationException != null)
                log.Warn(mutationException, $"VitalModifier effect {entry.Id} mutated vital {vital}; the notification failure was reconciled from backing state.");

            info.AddCombatLog(new CombatLogVitalModifier
            {
                Amount         = actualAmount,
                VitalModified  = vital,
                BShowCombatLog = entry.DataBits09 != 0u,
                CastData       = castData
            });
        }

        private static bool TryCalculateVitalModifierValue(
            IUnitEntity target,
            Vital vital,
            VitalDefinition definition,
            float current,
            float amount,
            out float expected)
        {
            expected = 0f;
            double result = (double)current + amount;
            if (definition.MaximumProperty != null)
            {
                float maximum;
                try
                {
                    if (!target.TryGetVitalMaximum(vital, out maximum)
                        || !float.IsFinite(maximum)
                        || maximum < 0f
                        || (definition.UsesIntegerStorage && (double)maximum > uint.MaxValue))
                        return false;
                }
                catch (Exception exception)
                {
                    log.Warn(exception, $"VitalModifier vital {vital} maximum could not be read.");
                    return false;
                }

                result = Math.Clamp(result, 0d, maximum);
            }
            else
                result = Math.Max(result, 0d);

            if (!double.IsFinite(result)
                || (definition.UsesIntegerStorage && result > uint.MaxValue)
                || (!definition.UsesIntegerStorage && result > float.MaxValue))
                return false;

            expected = definition.UsesIntegerStorage
                ? (float)Math.Truncate(result)
                : (float)result;
            return float.IsFinite(expected);
        }

        private static void DropUnsupportedVitalModifierEffect(
            ISpellTargetEffectInfo info,
            string reason)
        {
            info.DropEffect = true;
            if (reportedUnsupportedVitalModifierEffects.TryAdd(info.Entry.Id, 0))
                log.Warn($"VitalModifier effect {info.Entry.Id} failed closed because {reason}.");
        }

        private static void DropVitalModifierEffect(
            ISpellTargetEffectInfo info,
            string reason,
            Exception exception = null)
        {
            info.DropEffect = true;
            if (exception == null)
                log.Warn($"VitalModifier effect {info.Entry.Id} failed closed because {reason}.");
            else
                log.Warn(exception, $"VitalModifier effect {info.Entry.Id} failed closed because {reason}.");
        }

        [SpellEffectHandler(SpellEffectType.Proc)]
        public static void HandleEffectProc(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            var proc = new ProcInfo(target, info.Entry);
            if (target.ApplyProc(proc))
                spell.TrackProc(target, proc);
        }

        [SpellEffectHandler(SpellEffectType.QuestAdvanceObjective)]
        public static void HandleEffectQuestAdvanceObjective(
            ISpell spell,
            IUnitEntity target,
            ISpellTargetEffectInfo info)
        {
            if (info?.Entry == null)
                return;

            if (target is not IPlayer player || player.QuestManager == null)
            {
                info.DropEffect = true;
                return;
            }

            uint questId = info.Entry.DataBits00;
            uint objectiveIndex = info.Entry.DataBits01;
            uint progress = info.Entry.DataBits02;
            if (questId is 0u or > MaximumQuestId
                || objectiveIndex >= byte.MaxValue
                || progress == 0u)
            {
                info.DropEffect = true;
                return;
            }

            try
            {
                if (!player.QuestManager.TryObjectiveUpdate(
                        (ushort)questId,
                        (byte)objectiveIndex,
                        progress,
                        out var objective))
                {
                    info.DropEffect = true;
                    return;
                }

                var interaction = spell?.Parameters?.ClientSideInteraction;
                if (interaction?.ActivateUnit != null
                    && objective.ObjectiveInfo.Type == QuestObjectiveType.ActivateEntity
                    && objective.IsTarget(interaction.ActivateUnit.CreatureId))
                {
                    // Immediate effects run before CompleteSuccess and can exclude their exact
                    // objective from the generic activation event. Delayed effects arrive after
                    // the terminal claim and are deliberately rejected by the interaction.
                    interaction.TrySuppressActivateEntityObjective(objective.ObjectiveInfo.Id);
                }
            }
            catch (QuestException)
            {
                // The effect can legitimately target a player without the referenced active objective.
                info.DropEffect = true;
            }
            catch (ArgumentException)
            {
                // Invalid static quest references fail this effect without aborting the owning spell.
                info.DropEffect = true;
            }
        }

        [SpellEffectHandler(SpellEffectType.Heal)]
        public static void HandleEffectHeal(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            if (target.CanAttack(spell.Caster))
                return;

            var factory = LegacyServiceProvider.Provider.GetService<IFactory<IDamageCalculator>>();
            var damageCalculator = factory.Resolve();
            uint healing = damageCalculator.CalculateBaseAmount(spell.Caster, target, info);

            info.AddDamage(new SpellTargetInfo.SpellTargetEffectInfo.DamageDescription
            {
                DamageType     = DamageType.Heal,
                RawDamage      = healing,
                AdjustedDamage = healing,
                CombatResult   = CombatResult.Hit
            });
            target.ModifyHealth(healing, DamageType.Heal, spell.Caster);
        }

        [SpellEffectHandler(SpellEffectType.HealShields)]
        public static void HandleEffectHealShields(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            if (target.CanAttack(spell.Caster))
                return;

            var factory = LegacyServiceProvider.Provider.GetService<IFactory<IDamageCalculator>>();
            var damageCalculator = factory.Resolve();
            uint healing = damageCalculator.CalculateBaseAmount(spell.Caster, target, info);

            info.AddDamage(new SpellTargetInfo.SpellTargetEffectInfo.DamageDescription
            {
                DamageType     = DamageType.HealShields,
                RawDamage      = healing,
                AdjustedDamage = healing,
                CombatResult   = CombatResult.Hit
            });
            target.Shield = (uint)Math.Min((ulong)target.Shield + healing, target.MaxShieldCapacity);
        }

        [SpellEffectHandler(SpellEffectType.Resurrect)]
        public static void HandleEffectResurrect(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            if (target is not IPlayer player)
                return;

            player.ResurrectionManager.ResurrectRequest(spell.Caster.Guid);
        }

        [SpellEffectHandler(SpellEffectType.Proxy)]
        public static void HandleEffectProxy(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            target.CastSpell(info.Entry.DataBits00, new SpellParameters
            {
                ParentSpellInfo        = spell.Parameters.SpellInfo,
                RootSpellInfo          = spell.Parameters.RootSpellInfo,
                UserInitiatedSpellCast = false,
                IsProcTriggered        = spell.Parameters.IsProcTriggered
            });
        }

        [SpellEffectHandler(SpellEffectType.Disguise)]
        public static void HandleEffectDisguise(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            Creature2Entry creature2 = GameTableManager.Instance.Creature2.GetEntry(info.Entry.DataBits02);
            if (creature2 == null)
                return;

            Creature2DisplayGroupEntryEntry displayGroupEntry = GameTableManager.Instance.Creature2DisplayGroupEntry.Entries.FirstOrDefault(d => d.Creature2DisplayGroupId == creature2.Creature2DisplayGroupId);
            if (displayGroupEntry == null)
                return;

            target.DisplayInfo = displayGroupEntry.Creature2DisplayInfoId;
        }

        [SpellEffectHandler(SpellEffectType.SummonMount)]
        public static void HandleEffectSummonMount(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            // TODO: handle NPC mounting?
            if (target is not IPlayer player)
                return;

            if (!player.CanMount())
                return;

            // TODO: needs to be replaced once spell effect handlers aren't static
            var factory = LegacyServiceProvider.Provider.GetService<IEntityFactory>();

            var mount = factory.CreateEntity<IMountEntity>();
            mount.Initialise(player, spell.Parameters.SpellInfo.Entry.Id, info.Entry.DataBits00, info.Entry.DataBits01, info.Entry.DataBits04);
            mount.EnqueuePassengerAdd(player, VehicleSeatType.Pilot, 0);

            // usually for hover boards
            /*if (info.Entry.DataBits04 > 0u)
            {
                mount.SetAppearance(new ItemVisual
                {
                    Slot      = ItemSlot.Mount,
                    DisplayId = (ushort)info.Entry.DataBits04
                });
            }*/

            var position = new MapPosition
            {
                Position = player.Position
            };

            if (player.Map.CanEnter(mount, position))
                player.Map.EnqueueAdd(mount, position);

            // FIXME: also cast 52539,Riding License - Riding Skill 1 - SWC - Tier 1,34464
            // FIXME: also cast 80530,Mount Sprint  - Tier 2,36122

            player.CastSpell(52539, new SpellParameters());
            player.CastSpell(80530, new SpellParameters());
        }

        [SpellEffectHandler(SpellEffectType.Teleport)]
        public static void HandleEffectTeleport(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            WorldLocation2Entry locationEntry = GameTableManager.Instance.WorldLocation2.GetEntry(info.Entry.DataBits00);
            if (locationEntry == null)
                return;

            if (target is IPlayer player)
                if (player.CanTeleport())
                    player.TeleportTo((ushort)locationEntry.WorldId, locationEntry.Position0, locationEntry.Position1, locationEntry.Position2);
        }

        [SpellEffectHandler(SpellEffectType.FullScreenEffect)]
        public static void HandleFullScreenEffect(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            // TODO/FIXME: Add duration into the queue so that the spell will automatically finish at the correct time. This is a workaround for Full Screen Effects.
            //events.EnqueueEvent(new Event.SpellEvent(info.Entry.DurationTime / 1000d, () => { status = SpellStatus.Finished; SendSpellFinish(); }));
        }

        [SpellEffectHandler(SpellEffectType.RapidTransport)]
        public static void HandleEffectRapidTransport(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            TaxiNodeEntry taxiNode = GameTableManager.Instance.TaxiNode.GetEntry(spell.Parameters.TaxiNode);
            if (taxiNode == null)
                return;

            WorldLocation2Entry worldLocation = GameTableManager.Instance.WorldLocation2.GetEntry(taxiNode.WorldLocation2Id);
            if (worldLocation == null)
                return;

            if (target is not IPlayer player)
                return;

            if (!player.CanTeleport())
                return;

            var rotation = new Quaternion(worldLocation.Facing0, worldLocation.Facing0, worldLocation.Facing2, worldLocation.Facing3);
            player.Rotation = rotation.ToEuler();
            player.TeleportTo((ushort)worldLocation.WorldId, worldLocation.Position0, worldLocation.Position1, worldLocation.Position2);
        }

        [SpellEffectHandler(SpellEffectType.LearnDyeColor)]
        public static void HandleEffectLearnDyeColor(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            if (target is not IPlayer player)
                return;

            player.Account.GenericUnlockManager.Unlock((ushort)info.Entry.DataBits00);
        }

        [SpellEffectHandler(SpellEffectType.UnlockMount)]
        public static void HandleEffectUnlockMount(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            if (target is not IPlayer player)
                return;

            Spell4Entry spell4Entry = GameTableManager.Instance.Spell4.GetEntry(info.Entry.DataBits00);
            player.SpellManager.AddSpell(spell4Entry.Spell4BaseIdBaseSpell);

            player.Session.EnqueueMessageEncrypted(new ServerUnlockMount
            {
                Spell4Id = info.Entry.DataBits00
            });
        }

        [SpellEffectHandler(SpellEffectType.UnlockPetFlair)]
        public static void HandleEffectUnlockPetFlair(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            if (target is not IPlayer player)
                return;

            player.PetCustomisationManager.UnlockFlair((ushort)info.Entry.DataBits00);
        }

        [SpellEffectHandler(SpellEffectType.UnlockVanityPet)]
        public static void HandleEffectUnlockVanityPet(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            if (target is not IPlayer player)
                return;

            Spell4Entry spell4Entry = GameTableManager.Instance.Spell4.GetEntry(info.Entry.DataBits00);
            player.SpellManager.AddSpell(spell4Entry.Spell4BaseIdBaseSpell);

            player.Session.EnqueueMessageEncrypted(new ServerUnlockMount
            {
                Spell4Id = info.Entry.DataBits00
            });
        }

        [SpellEffectHandler(SpellEffectType.SummonVanityPet)]
        public static void HandleEffectSummonVanityPet(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            if (target is not IPlayer player)
                return;

            // enqueue removal of existing vanity pet if summoned
            if (player.VanityPetGuid != null)
            {
                IPetEntity oldVanityPet = player.GetVisible<IPetEntity>(player.VanityPetGuid.Value);
                oldVanityPet?.RemoveFromMap();
                player.VanityPetGuid = 0u;
            }

            // TODO: needs to be replaced once spell effect handlers aren't static
            var factory = LegacyServiceProvider.Provider.GetService<IEntityFactory>();

            var pet = factory.CreateEntity<IPetEntity>();
            pet.Initialise(player, info.Entry.DataBits00);

            var position = new MapPosition
            {
                Position = player.Position
            };

            if (player.Map.CanEnter(pet, position))
                player.Map.EnqueueAdd(pet, position);
        }

        [SpellEffectHandler(SpellEffectType.TitleGrant)]
        public static void HandleEffectTitleGrant(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            if (target is not IPlayer player)
                return;

            player.TitleManager.AddTitle((ushort)info.Entry.DataBits00);
        }

        [SpellEffectHandler(SpellEffectType.Fluff)]
        public static void HandleEffectFluff(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
        }

        [SpellEffectHandler(SpellEffectType.UnitPropertyModifier)]
        public static void HandleEffectPropertyModifier(ISpell spell, IUnitEntity target, ISpellTargetEffectInfo info)
        {
            // TODO: I suppose these could be cached somewhere instead of generating them every single effect?
            SpellPropertyModifier modifier =
                new SpellPropertyModifier(
                    new SpellEffectIdentity(
                        spell.CastingId,
                        spell.Parameters.SpellInfo.Entry.Id,
                        info.Entry.Id),
                    (Property)info.Entry.DataBits00,
                    info.Entry.DataBits01, 
                    BitConverter.UInt32BitsToSingle(info.Entry.DataBits02), 
                    BitConverter.UInt32BitsToSingle(info.Entry.DataBits03), 
                    BitConverter.UInt32BitsToSingle(info.Entry.DataBits04));
            if (!spell.ApplyPropertyModifier(target, modifier))
                info.DropEffect = true;
        }
    }
}
