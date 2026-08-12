using System.Globalization;
using System.Numerics;
using System.Reflection;
using Moq;
using NexusForever.Game.Static;
using NexusForever.Game.Static.Entity.Movement.Spline;
using NexusForever.Game.Static.Guild;
using NexusForever.Game.Static.Map;
using NexusForever.Game.Static.RBAC;
using NexusForever.Game.Static.Story;
using NexusForever.Network.World.Message.Model.Shared;
using NexusForever.Script.Static;
using NexusForever.WorldServer.Command;
using NexusForever.WorldServer.Command.Context;
using NexusForever.WorldServer.Command.Convert;
using PlayerPath = NexusForever.Game.Static.PlayerPath.Path;

namespace NexusForever.WorldServer.Tests.Command
{
    public sealed class ParameterConverterTests
    {
        [Theory]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        public void IntegerConverters_UseInvariantIntegerStyle(string cultureName)
        {
            using var scope = new CultureScope(cultureName);

            Assert.Equal(byte.MaxValue, Convert<byte>(new ByteParameterConverter(), "255"));
            Assert.Equal(ushort.MaxValue, Convert<ushort>(new UShortParameterConverter(), "65535"));
            Assert.Equal(uint.MaxValue, Convert<uint>(new UIntParameterConverter(), "4294967295"));
            Assert.Equal(int.MinValue, Convert<int>(new IntParameterConverter(), "-2147483648"));
            Assert.Equal(42, Convert<int>(new IntParameterConverter(), "+42"));

            Assert.Throws<OverflowException>(() => Convert<byte>(new ByteParameterConverter(), "256"));
            Assert.Throws<OverflowException>(() => Convert<ushort>(new UShortParameterConverter(), "65536"));
            Assert.Throws<OverflowException>(() => Convert<uint>(new UIntParameterConverter(), "4294967296"));
            Assert.Throws<OverflowException>(() => Convert<uint>(new UIntParameterConverter(), "-1"));
            Assert.Throws<OverflowException>(() => Convert<int>(new IntParameterConverter(), "2147483648"));

            foreach (string value in new[] { "1,000", "0x10", "1e3", "1.0" })
                Assert.Throws<FormatException>(() => Convert<int>(new IntParameterConverter(), value));
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        public void FloatConverter_UsesInvariantDecimalAndExponent(string cultureName)
        {
            using var scope = new CultureScope(cultureName);

            Assert.Equal(1.25f, Convert<float>(new FloatParameterConverter(), "1.25"));
            Assert.Equal(-2f, Convert<float>(new FloatParameterConverter(), "-2"));
            Assert.Equal(300f, Convert<float>(new FloatParameterConverter(), "3e2"));
            Assert.Equal(float.MaxValue, Convert<float>(new FloatParameterConverter(), "3.4028235E+38"));

            Assert.Throws<FormatException>(() => Convert<float>(new FloatParameterConverter(), "1,5"));
            Assert.Throws<FormatException>(() => Convert<float>(new FloatParameterConverter(), "1,000"));
        }

        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("-Infinity")]
        [InlineData("1e50")]
        public void FloatConverter_RejectsNonFiniteValues(string value)
        {
            Assert.Throws<FormatException>(() => Convert<float>(new FloatParameterConverter(), value));
        }

        [Fact]
        public void Vector3Converter_UsesTheSameInvariantFiniteContract()
        {
            using var scope = new CultureScope("de-DE");

            Vector3 value = Convert<Vector3>(
                new Vector3ParameterConverter(),
                "1.25",
                "-2",
                "3e2");

            Assert.Equal(new Vector3(1.25f, -2f, 300f), value);
        }

        [Theory]
        [InlineData("NaN", "2", "3")]
        [InlineData("1", "Infinity", "3")]
        [InlineData("1", "2", "1e50")]
        public void Vector3Converter_RejectsANonFiniteComponent(string x, string y, string z)
        {
            Assert.Throws<FormatException>(() => Convert<Vector3>(
                new Vector3ParameterConverter(),
                x,
                y,
                z));
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        public void TimeSpanConverter_UsesInvariantForms(string cultureName)
        {
            using var scope = new CultureScope(cultureName);

            Assert.Equal(
                new TimeSpan(1, 2, 3, 4),
                Convert<TimeSpan>(new TimeSpanParameterConverter(), "1.02:03:04"));
            Assert.Equal(
                TimeSpan.FromMilliseconds(500),
                Convert<TimeSpan>(new TimeSpanParameterConverter(), "00:00:00.5"));
            Assert.Equal(
                -TimeSpan.FromHours(2),
                Convert<TimeSpan>(new TimeSpanParameterConverter(), "-02:00:00"));
            Assert.Equal(
                TimeSpan.MaxValue,
                Convert<TimeSpan>(new TimeSpanParameterConverter(), "10675199.02:48:05.4775807"));

            Assert.Throws<FormatException>(() => Convert<TimeSpan>(
                new TimeSpanParameterConverter(),
                "00:00:00,5"));
        }

        [Fact]
        public void EnumConverter_PreservesNamedAndOpenNumericValuesButRejectsComposites()
        {
            Assert.Equal(Role.Player, Convert<Role>(new EnumParameterConverter<Role>(), "Player"));
            Assert.Equal((Role)99, Convert<Role>(new EnumParameterConverter<Role>(), "99"));

            Assert.Throws<FormatException>(() => Convert<Role>(
                new EnumParameterConverter<Role>(),
                "Player,GameMaster"));
        }

        [Fact]
        public void DefinedEnumConverter_RejectsUndefinedNamesAndNumbers()
        {
            Assert.Equal(BroadcastTier.High, Convert<BroadcastTier>(
                new DefinedEnumParameterConverter<BroadcastTier>(),
                "High"));
            Assert.Equal(BroadcastTier.Medium, Convert<BroadcastTier>(
                new DefinedEnumParameterConverter<BroadcastTier>(),
                "1"));

            Assert.Throws<ArgumentException>(() => Convert<BroadcastTier>(
                new DefinedEnumParameterConverter<BroadcastTier>(),
                "Unknown"));
            Assert.Throws<FormatException>(() => Convert<BroadcastTier>(
                new DefinedEnumParameterConverter<BroadcastTier>(),
                "99"));
            Assert.Throws<FormatException>(() => Convert<BroadcastTier>(
                new DefinedEnumParameterConverter<BroadcastTier>(),
                "High,Medium"));
        }

        [Fact]
        public void CommandEnumConverters_UseClosedPolicyOnlyForAuditedTypesAndNeverFlags()
        {
            HashSet<Type> closedTypes =
            [
                typeof(DisableType),
                typeof(WorldRemovalReason),
                typeof(PlayerPath),
                typeof(CommunicatorOverlay),
                typeof(CommunicatorPortraitPlacement),
                typeof(CommunicatorBackground),
                typeof(ReloadType),
                typeof(BroadcastTier),
                typeof(SplineMode),
                typeof(GuildType)
            ];

            Type openConverter = typeof(EnumParameterConverter<>);
            Type closedConverter = typeof(DefinedEnumParameterConverter<>);
            List<Type> converters = typeof(CommandManager).Assembly
                .GetTypes()
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                .SelectMany(method => method.GetParameters())
                .Select(parameter => parameter.GetCustomAttribute<ParameterAttribute>()?.Converter)
                .Where(type => type?.IsGenericType == true
                    && (type.GetGenericTypeDefinition() == openConverter
                        || type.GetGenericTypeDefinition() == closedConverter))
                .ToList();

            Assert.NotEmpty(converters);
            foreach (Type converter in converters)
            {
                Type enumType = converter.GetGenericArguments()[0];
                Assert.False(enumType.IsDefined(typeof(FlagsAttribute), inherit: false));
                Assert.Equal(
                    closedTypes.Contains(enumType) ? closedConverter : openConverter,
                    converter.GetGenericTypeDefinition());
            }
        }

        private static T Convert<T>(IParameterConvert converter, params string[] values)
        {
            var queue = new ParameterQueue(values);
            return (T)converter.Convert(Mock.Of<ICommandContext>(), queue);
        }

        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo previousCulture;
            private readonly CultureInfo previousUiCulture;

            public CultureScope(string cultureName)
            {
                previousCulture = CultureInfo.CurrentCulture;
                previousUiCulture = CultureInfo.CurrentUICulture;

                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }
    }
}
