using NexusForever.Database.World.Validation;

namespace NexusForever.Game.Tests.Database
{
    public class CrimsonIsleQuest5593SqlPreflightTests
    {
        [Fact]
        public void Validate_MinimumSyntheticContract_ReturnsResolvedCountsAndStableHash()
        {
            string sql = CreateValidSql();

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);
            CrimsonIsleQuest5593SqlPreflightResult repeatedResult = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(1, result.MondoSpawnCount);
            Assert.Equal(3, result.MineSpawnCount);
            Assert.Equal(5, result.ScrabSpawnCount);
            Assert.Equal(4, result.EligibleScrabSpawnCount);
            Assert.Matches("^[0-9a-f]{64}$", result.Sha256);
            Assert.Equal(result.Sha256, repeatedResult.Sha256);
            Assert.Empty(result.Diagnostics);
        }

        [Theory]
        [InlineData("(@GUID+1, 0, 24187,", "(@GUID+1, 0, 24186,", "24187")]
        [InlineData("(@GUID+4, 0, 24251,", "(@GUID+4, 0, 24250,", "three normal mine")]
        [InlineData("(@GUID+8, 0, 24054,", "(@GUID+8, 0, 24053,", "four normal, unsplined Scrab")]
        public void Validate_InsufficientRequiredPopulation_FailsClosed(
            string oldValue,
            string newValue,
            string expectedDiagnostic)
        {
            string sql = CreateValidSql().Replace(oldValue, newValue, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains(expectedDiagnostic, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Validate_WrongOrRepeatedWorldAssignment_FailsClosed()
        {
            string wrongWorldSql = CreateValidSql()
                .Replace("SET @WORLD = 870;", "SET @WORLD = 871;", StringComparison.Ordinal);
            string oversizedWorldSql = CreateValidSql()
                .Replace("SET @WORLD = 870;", "SET @WORLD = 70000;", StringComparison.Ordinal);
            string repeatedWorldSql = "SET @WORLD = 870;\n" + CreateValidSql();

            CrimsonIsleQuest5593SqlPreflightResult wrongWorldResult =
                CrimsonIsleQuest5593SqlPreflight.Validate(wrongWorldSql);
            CrimsonIsleQuest5593SqlPreflightResult oversizedWorldResult =
                CrimsonIsleQuest5593SqlPreflight.Validate(oversizedWorldSql);
            CrimsonIsleQuest5593SqlPreflightResult repeatedWorldResult =
                CrimsonIsleQuest5593SqlPreflight.Validate(repeatedWorldSql);

            Assert.False(wrongWorldResult.IsValid);
            Assert.Contains(wrongWorldResult.Diagnostics,
                diagnostic => diagnostic.Contains("Expected @WORLD 870", StringComparison.Ordinal));
            Assert.False(oversizedWorldResult.IsValid);
            Assert.Contains(oversizedWorldResult.Diagnostics,
                diagnostic => diagnostic.Contains("unsigned 16-bit range", StringComparison.Ordinal));
            Assert.False(repeatedWorldResult.IsValid);
            Assert.Contains(repeatedWorldResult.Diagnostics,
                diagnostic => diagnostic.Contains("exactly one @WORLD", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(
            "(@GUID+1, 0, 24187, @WORLD, 1236, 10, 20, 30, 0, 0, 0, 0, 0, 170, 170),",
            "(@GUID+1, 1, 24187, @WORLD, 1236, 10, 20, 30, 0, 0, 0, 0, 0, 170, 170),",
            "entity type")]
        [InlineData(
            "(@GUID+2, 0, 24251, @WORLD, 1236, 11, 20, 30, 0, 0, 0, 0, 0, 865, 865),",
            "(@GUID+2, 0, 24251, @WORLD, 1236, 11, 20, 30, 0, 0, 0, 0, 0, 550, 550),",
            "factions")]
        [InlineData(
            "(@GUID+5, 0, 24054, @WORLD, 1236, 14, 20, 30, 0, 0, 0, 0, 0, 550, 550),",
            "(@GUID+5, 0, 24054, @WORLD, 1236, NaN, 20, 30, 0, 0, 0, 0, 0, 550, 550),",
            "finite float-compatible")]
        public void Validate_UnsupportedRelevantEntityShape_FailsClosed(
            string oldRow,
            string newRow,
            string expectedDiagnostic)
        {
            string sql = CreateValidSql().Replace(oldRow, newRow, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains(expectedDiagnostic, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Validate_SplineAndEventRowsExcludeScrabsFromEligibility()
        {
            string sql = CreateValidSql() + string.Join('\n',
            [
                "INSERT INTO `entity_event` (`Id`, `EventId`, `Phase`) VALUES",
                "    (@GUID+8, 42, 0);",
                ""
            ]);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Equal(5, result.ScrabSpawnCount);
            Assert.Equal(3, result.EligibleScrabSpawnCount);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("four normal, unsplined Scrab", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(
            "(@GUID+9, 6700, 1, 2.5, 0, 0, 0);",
            "(@GUID+9, 6700.5, 1, 2.5, 0, 0, 0);",
            "`SplineId`")]
        [InlineData(
            "(@GUID+9, 6700, 1, 2.5, 0, 0, 0);",
            "(@GUID+9, 6700, 1, Infinity, 0, 0, 0);",
            "`Speed`")]
        [InlineData(
            "(@GUID+9, 6700, 1, 2.5, 0, 0, 0);",
            "(@GUID+9, 6700, 1.5, 2.5, 0, 0, 0);",
            "`Mode`")]
        public void Validate_MalformedSplineFields_FailClosed(
            string oldRow,
            string newRow,
            string expectedColumn)
        {
            string sql = CreateValidSql().Replace(oldRow, newRow, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains(expectedColumn, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("(@GUID+9, 42.5, 0);", "`EventId`")]
        [InlineData("(@GUID+9, 42, -1);", "`Phase`")]
        public void Validate_MalformedEventFields_FailClosed(string eventRow, string expectedColumn)
        {
            string sql = CreateValidSql() + CreateEventInsert(eventRow);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Equal(4, result.EligibleScrabSpawnCount);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains(expectedColumn, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("(@GUID+9, 42, 1);")]
        [InlineData("(@GUID+9, 42, 0);")]
        public void Validate_MultipleEventsForOneEntity_FailClosed(string repeatedEventRow)
        {
            string sql = CreateValidSql() + CreateEventInsert(
                "(@GUID+9, 42, 0),",
                repeatedEventRow);

            CrimsonIsleQuest5593SqlPreflightResult result =
                CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Equal(4, result.EligibleScrabSpawnCount);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("more than one event row", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("(@GUID+9, 0, 420),", "(@GUID+9, 0, NaN),", "`Value`")]
        [InlineData("(@GUID+9, 10, 3);", "(@GUID+9, 10.5, 3);", "`Stat`")]
        public void Validate_InvalidUnrelatedStatFields_FailClosed(
            string oldRow,
            string newRow,
            string expectedColumn)
        {
            string sql = CreateValidSql().Replace(oldRow, newRow, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Equal(4, result.EligibleScrabSpawnCount);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains(expectedColumn, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("    (@GUID+5, 0, 420),\n", "")]
        [InlineData("    (@GUID+5, 0, 420),", "    (@GUID+5, 0, 0),")]
        [InlineData("    (@GUID+5, 0, 420),", "    (@GUID+5, 0, NaN),")]
        [InlineData("    (@GUID+5, 10, 3),", "    (@GUID+5, 10, 1E400),")]
        public void Validate_MissingNonPositiveOrNonFiniteScrabStat_FailsClosed(
            string oldValue,
            string newValue)
        {
            string sql = CreateValidSql().Replace(oldValue, newValue, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Equal(3, result.EligibleScrabSpawnCount);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("positive finite stat", StringComparison.Ordinal));
        }

        [Fact]
        public void Validate_ReusedRelativeOffsetsAcrossGuidGenerationsRemainIsolated()
        {
            string firstGeneration = string.Join('\n',
            [
                "SET @WORLD = 870;",
                "SET @GUID = (SELECT IFNULL(MAX(`id`), 0) FROM `entity`);",
                "INSERT INTO `entity` (`Id`, `Type`, `Creature`, `World`, `Area`, `X`, `Y`, `Z`, `RX`, `RY`, `RZ`, `DisplayInfo`, `OutfitInfo`, `Faction1`, `Faction2`) VALUES",
                "    (@GUID+1, 0, 99999, @WORLD, 1237, 1, 2, 3, 0, 0, 0, 0, 0, 1, 1),",
                "    (@GUID+9, 0, 99999, @WORLD, 1237, 1, 2, 3, 0, 0, 0, 0, 0, 1, 1);",
                "INSERT INTO `entity_spline` (`Id`, `SplineId`, `Mode`, `Speed`, `FX`, `FY`, `FZ`) VALUES",
                "    (@GUID+9, 1, 1, 1, 0, 0, 0);",
                "INSERT INTO `entity_event` (`Id`, `EventId`, `Phase`) VALUES",
                "    (@GUID+1, 42, 0);",
                ""
            ]);
            string secondGeneration = CreateValidSql()
                .Replace("SET @WORLD = 870;\n", "", StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result =
                CrimsonIsleQuest5593SqlPreflight.Validate(firstGeneration + secondGeneration);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Equal(4, result.EligibleScrabSpawnCount);
        }

        [Theory]
        [InlineData(
            "INSERT INTO `entity` (`Id`, `Type`, `Creature`, `World`, `Area`, `X`, `Y`, `Z`, `RX`, `RY`, `RZ`, `DisplayInfo`, `OutfitInfo`, `Faction1`, `Faction2`) VALUES",
            "INSERT INTO `entity` (`Id`, `Type`, `Creature`, `World`, `Area`, `X`, `Y`, `Z`, `RX`, `RY`, `RZ`, `DisplayInfo`, `OutfitInfo`, `Faction1`) VALUES",
            "missing required column")]
        [InlineData(
            "(@GUID+1, 0, 24187, @WORLD, 1236, 10, 20, 30, 0, 0, 0, 0, 0, 170, 170),",
            "(@GUID+1, 0, 24187, @WORLD, 1236, 10, 20, 30, 0, 0, 0, 0, 0, 170),",
            "values for")]
        [InlineData(
            "@GUID+1, 0, 24187",
            "1, 0, 24187",
            "relative entity identity")]
        public void Validate_MalformedTargetGrammar_FailsClosed(
            string oldValue,
            string newValue,
            string expectedDiagnostic)
        {
            string sql = CreateValidSql().Replace(oldValue, newValue, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains(expectedDiagnostic, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Validate_DuplicateAndOrphanTargetRows_FailClosed()
        {
            string duplicateSql = CreateValidSql().Replace(
                "(@GUID+2, 0, 24251,",
                "(@GUID+1, 0, 24251,",
                StringComparison.Ordinal);
            string orphanSql = CreateValidSql() + string.Join('\n',
            [
                "INSERT INTO `entity_stats` (`Id`, `Stat`, `Value`) VALUES",
                "    (@GUID+99, 0, 1);",
                ""
            ]);

            CrimsonIsleQuest5593SqlPreflightResult duplicateResult =
                CrimsonIsleQuest5593SqlPreflight.Validate(duplicateSql);
            CrimsonIsleQuest5593SqlPreflightResult orphanResult =
                CrimsonIsleQuest5593SqlPreflight.Validate(orphanSql);

            Assert.False(duplicateResult.IsValid);
            Assert.Contains(duplicateResult.Diagnostics,
                diagnostic => diagnostic.Contains("duplicate entity identity", StringComparison.Ordinal));
            Assert.False(orphanResult.IsValid);
            Assert.Contains(orphanResult.Diagnostics,
                diagnostic => diagnostic.Contains("Orphan stat", StringComparison.Ordinal));
        }

        [Fact]
        public void Validate_TargetRowsBeforeGuidGeneration_FailClosed()
        {
            string sql = CreateValidSql().Replace(
                "SET @GUID = (SELECT IFNULL(MAX(`id`), 0) FROM `entity`);\n",
                "",
                StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("before a valid @GUID generation", StringComparison.Ordinal));
        }

        private static string CreateValidSql()
        {
            return string.Join('\n',
            [
                "-- synthetic Crimson Isle quest 5593 content contract",
                "SET @WORLD = 870;",
                "DELETE FROM `entity` WHERE `world` = @WORLD AND `area` = 1236;",
                "SET @GUID = (SELECT IFNULL(MAX(`id`), 0) FROM `entity`);",
                "INSERT INTO `entity` (`Id`, `Type`, `Creature`, `World`, `Area`, `X`, `Y`, `Z`, `RX`, `RY`, `RZ`, `DisplayInfo`, `OutfitInfo`, `Faction1`, `Faction2`) VALUES",
                "    (@GUID+1, 0, 24187, @WORLD, 1236, 10, 20, 30, 0, 0, 0, 0, 0, 170, 170),",
                "    (@GUID+2, 0, 24251, @WORLD, 1236, 11, 20, 30, 0, 0, 0, 0, 0, 865, 865),",
                "    (@GUID+3, 0, 24251, @WORLD, 1236, 12, 20, 30, 0, 0, 0, 0, 0, 865, 865),",
                "    (@GUID+4, 0, 24251, @WORLD, 1236, 13, 20, 30, 0, 0, 0, 0, 0, 865, 865),",
                "    (@GUID+5, 0, 24054, @WORLD, 1236, 14, 20, 30, 0, 0, 0, 0, 0, 550, 550),",
                "    (@GUID+6, 0, 24054, @WORLD, 1236, 15, 20, 30, 0, 0, 0, 0, 0, 550, 550),",
                "    (@GUID+7, 0, 24054, @WORLD, 1236, 16, 20, 30, 0, 0, 0, 0, 0, 550, 550),",
                "    (@GUID+8, 0, 24054, @WORLD, 1236, 17, 20, 30, 0, 0, 0, 0, 0, 550, 550),",
                "    (@GUID+9, 0, 24054, @WORLD, 1236, 18, 20, 30, 0, 0, 0, 0, 0, 550, 550);",
                "INSERT INTO `entity_spline` (`Id`, `SplineId`, `Mode`, `Speed`, `FX`, `FY`, `FZ`) VALUES",
                "    (@GUID+9, 6700, 1, 2.5, 0, 0, 0);",
                "INSERT INTO `entity_stats` (`Id`, `Stat`, `Value`) VALUES",
                "    (@GUID+5, 0, 420),",
                "    (@GUID+5, 10, 3),",
                "    (@GUID+6, 0, 420),",
                "    (@GUID+6, 10, 3),",
                "    (@GUID+7, 0, 420),",
                "    (@GUID+7, 10, 3),",
                "    (@GUID+8, 0, 420),",
                "    (@GUID+8, 10, 3),",
                "    (@GUID+9, 0, 420),",
                "    (@GUID+9, 10, 3);",
                ""
            ]);
        }

        private static string CreateEventInsert(params string[] rows)
        {
            return string.Join('\n',
            [
                "INSERT INTO `entity_event` (`Id`, `EventId`, `Phase`) VALUES",
                .. rows,
                ""
            ]);
        }
    }
}
