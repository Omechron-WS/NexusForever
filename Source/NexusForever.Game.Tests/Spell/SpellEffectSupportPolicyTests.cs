using System.Reflection;
using NexusForever.Game.Abstract.Entity;
using NexusForever.Game.Abstract.Spell;
using NexusForever.Game.Spell;
using NexusForever.Game.Static.Spell;
using NexusForever.GameTable.Model;

namespace NexusForever.Game.Tests.Spell
{
    public sealed class SpellEffectSupportPolicyTests
    {
        private static readonly (uint EffectId, uint SpellId)[] supportedRows =
        [
            (80977u, 34720u),
            (115515u, 48958u),
            (115520u, 48959u),
            (115525u, 48960u),
            (115530u, 48961u),
            (115535u, 48962u),
            (115540u, 48963u),
            (115545u, 48964u),
            (115550u, 48965u)
        ];

        [Theory]
        [MemberData(nameof(SupportedEffectIds))]
        public void IsSupported_ExactBuild16042ShapeIgnoresRowIdentity(
            uint effectId,
            uint spellId)
        {
            Spell4EffectsEntry entry = ExactEntry();
            entry.Id = effectId;
            entry.SpellId = spellId;
            uint observedBaseId = 0u;

            bool supported = SpellEffectSupportPolicy.IsSupported(entry, spell4BaseId =>
            {
                observedBaseId = spell4BaseId;
                return true;
            });

            Assert.True(supported);
            Assert.Equal(20684u, observedBaseId);
        }

        [Fact]
        public void IsSupported_RowAndSpellIdentifiersAreNotPartOfSemanticShape()
        {
            Spell4EffectsEntry entry = ExactEntry();
            entry.Id = uint.MaxValue;
            entry.SpellId = 0u;

            Assert.True(SpellEffectSupportPolicy.IsSupported(entry, _ => true));
        }

        [Theory]
        [InlineData(80978u, 34721u, 20684u, 3u, 0x453B8000u)]
        [InlineData(80979u, 34722u, 20684u, 0u, 0x3ECCCCCDu)]
        [InlineData(80980u, 34766u, 20685u, 3u, 0x457A0000u)]
        [InlineData(80981u, 34765u, 20685u, 3u, 0x453B8000u)]
        public void IsSupported_NeighboringCooldownFamiliesRemainUnsupported(
            uint effectId,
            uint spellId,
            uint spellBaseId,
            uint mode,
            uint operandBits)
        {
            Spell4EffectsEntry entry = ExactEntry();
            entry.Id = effectId;
            entry.SpellId = spellId;
            entry.DataBits01 = spellBaseId;
            entry.DataBits02 = mode;
            entry.DataBits03 = operandBits;

            Assert.False(SpellEffectSupportPolicy.IsSupported(entry, _ => true));
        }

        [Fact]
        public void IsSupported_EveryNonCooldownEffectTypePreservesRegisteredHandlerWithoutPredicateRead()
        {
            SpellEffectType[] effectTypes = Enum.GetValues<SpellEffectType>()
                .Where(effectType => effectType != SpellEffectType.ModifySpellCooldown)
                .Append(unchecked((SpellEffectType)uint.MaxValue))
                .ToArray();

            foreach (SpellEffectType effectType in effectTypes)
            {
                Spell4EffectsEntry entry = ExactEntry();
                entry.EffectType = effectType;

                Assert.True(SpellEffectSupportPolicy.IsSupported(entry, null));
                Assert.True(SpellEffectSupportPolicy.IsSupported(
                    entry,
                    _ => throw new InvalidOperationException("Must not be read.")));
            }
        }

        [Fact]
        public void IsSupported_NullMissingOrThrowingAuthorityFailsClosed()
        {
            Assert.False(SpellEffectSupportPolicy.IsSupported(null, _ => true));
            Assert.False(SpellEffectSupportPolicy.IsSupported(ExactEntry(), null));
            Assert.False(SpellEffectSupportPolicy.IsSupported(ExactEntry(), _ => false));
            Assert.False(SpellEffectSupportPolicy.IsSupported(
                ExactEntry(),
                _ => throw new InvalidOperationException("Test lookup failure.")));
        }

        [Fact]
        public void IsSupported_EveryCommonFieldDeviationFailsBeforeBaseLookup()
        {
            foreach ((string name, Action<Spell4EffectsEntry> mutate) in Deviations())
            {
                Spell4EffectsEntry entry = ExactEntry();
                mutate(entry);
                int authorityReads = 0;

                bool supported = SpellEffectSupportPolicy.IsSupported(entry, _ =>
                {
                    authorityReads++;
                    return true;
                });

                Assert.True(!supported, $"Expected unsupported deviation: {name}.");
                Assert.Equal(0, authorityReads);
            }
        }

        private static (string Name, Action<Spell4EffectsEntry> Mutate)[] Deviations()
        {
            return
            [
                ("target flags", entry => entry.TargetFlags = (uint)SpellEffectTargetFlags.Target),
                ("damage type", entry => entry.DamageType = (DamageType)1u),
                ("delay", entry => entry.DelayTime = 1u),
                ("tick", entry => entry.TickTime = 1u),
                ("duration", entry => entry.DurationTime = 1u),
                ("flags", entry => entry.Flags = 1u),
                ("selector", entry => entry.DataBits00 = 1u),
                ("base", entry => entry.DataBits01 = 20685u),
                ("mode", entry => entry.DataBits02 = 3u),
                ("operand negative zero", entry => entry.DataBits03 = 0x80000000u),
                ("operand NaN", entry => entry.DataBits03 = 0x7FC00000u),
                ("data 4", entry => entry.DataBits04 = 1u),
                ("data 5", entry => entry.DataBits05 = 1u),
                ("data 6", entry => entry.DataBits06 = 1u),
                ("data 7", entry => entry.DataBits07 = 1u),
                ("data 8", entry => entry.DataBits08 = 1u),
                ("data 9", entry => entry.DataBits09 = 1u),
                ("cost type 0", entry => entry.InnateCostPerTickType0 = 1u),
                ("cost type 1", entry => entry.InnateCostPerTickType1 = 1u),
                ("cost 0", entry => entry.InnateCostPerTick0 = 1u),
                ("cost 1", entry => entry.InnateCostPerTick1 = 1u),
                ("comparison", entry => entry.EmmComparison = 1u),
                ("comparison value", entry => entry.EmmValue = 1u),
                ("threat next float", entry => entry.ThreatMultiplier = BitConverter.UInt32BitsToSingle(0x3F800001u)),
                ("threat NaN", entry => entry.ThreatMultiplier = float.NaN),
                ("effect group", entry => entry.Spell4EffectGroupListId = 1u),
                ("caster apply", entry => entry.PrerequisiteIdCasterApply = 1u),
                ("target apply", entry => entry.PrerequisiteIdTargetApply = 1u),
                ("caster persistence", entry => entry.PrerequisiteIdCasterPersistence = 1u),
                ("target persistence", entry => entry.PrerequisiteIdTargetPersistence = 1u),
                ("target suspend", entry => entry.PrerequisiteIdTargetSuspend = 1u),
                ("parameter type null", entry => entry.ParameterType = null),
                ("parameter type length", entry => entry.ParameterType = new SpellEffectParameterType[3]),
                ("parameter type", entry => entry.ParameterType[0] = SpellEffectParameterType.Brutality),
                ("parameter value null", entry => entry.ParameterValue = null),
                ("parameter value length", entry => entry.ParameterValue = new float[3]),
                ("parameter negative zero", entry => entry.ParameterValue[0] = BitConverter.UInt32BitsToSingle(0x80000000u)),
                ("parameter NaN", entry => entry.ParameterValue[0] = float.NaN),
                ("phase", entry => entry.PhaseFlags = 1u),
                ("order", entry => entry.OrderIndex = 1u)
            ];
        }

        [Fact]
        public void GlobalManager_RowLookupGatesAfterRegistrationAndEnumLookupRemainsAvailable()
        {
            var manager = new GlobalSpellManager(_ => true);
            SpellEffectDelegate handler = (_, _, _) => { };
            Dictionary<SpellEffectType, SpellEffectDelegate> handlers = Handlers(manager);
            handlers.Add(SpellEffectType.ModifySpellCooldown, handler);

            Assert.Same(handler, manager.GetEffectHandler(SpellEffectType.ModifySpellCooldown));
            Assert.Same(handler, manager.GetEffectHandler(ExactEntry()));
            foreach ((string name, Action<Spell4EffectsEntry> mutate) in Deviations())
            {
                Spell4EffectsEntry unsupported = ExactEntry();
                mutate(unsupported);
                Assert.True(
                    manager.GetEffectHandler(unsupported) == null,
                    $"Expected row lookup to reject deviation: {name}.");
            }
            Assert.Null(manager.GetEffectHandler((Spell4EffectsEntry)null));
        }

        [Fact]
        public void GlobalManager_RowLookupDoesNotReadAuthorityWithoutRegisteredHandler()
        {
            int authorityReads = 0;
            var manager = new GlobalSpellManager(_ =>
            {
                authorityReads++;
                return true;
            });

            Assert.Null(manager.GetEffectHandler(ExactEntry()));
            Assert.Equal(0, authorityReads);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GlobalManager_MissingOrThrowingBaseAuthorityRejectsOnlyRowLookup(
            bool throwFromAuthority)
        {
            var manager = new GlobalSpellManager(_ => throwFromAuthority
                ? throw new InvalidOperationException("Test lookup failure.")
                : false);
            SpellEffectDelegate handler = (_, _, _) => { };
            Handlers(manager).Add(SpellEffectType.ModifySpellCooldown, handler);

            Assert.Same(handler, manager.GetEffectHandler(SpellEffectType.ModifySpellCooldown));
            Assert.Null(manager.GetEffectHandler(ExactEntry()));
        }

        public static IEnumerable<object[]> SupportedEffectIds =>
            supportedRows.Select(row => new object[] { row.EffectId, row.SpellId });

        internal static Spell4EffectsEntry ExactEntry()
        {
            return new Spell4EffectsEntry
            {
                Id             = 80977u,
                SpellId        = 34720u,
                TargetFlags    = (uint)SpellEffectTargetFlags.Caster,
                EffectType     = SpellEffectType.ModifySpellCooldown,
                DataBits01     = 20684u,
                ThreatMultiplier = 1f,
                ParameterType  = new SpellEffectParameterType[4],
                ParameterValue = new float[4],
                PhaseFlags     = uint.MaxValue
            };
        }

        private static Dictionary<SpellEffectType, SpellEffectDelegate> Handlers(
            GlobalSpellManager manager)
        {
            FieldInfo field = typeof(GlobalSpellManager).GetField(
                "spellEffectDelegates",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return Assert.IsType<Dictionary<SpellEffectType, SpellEffectDelegate>>(
                field.GetValue(manager));
        }
    }
}
