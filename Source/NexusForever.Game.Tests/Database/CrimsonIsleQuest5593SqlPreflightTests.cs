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

        [Fact]
        public void Validate_MissingRepeatedOrLateScopedDelete_FailsClosed()
        {
            string delete = CreateDeleteStatement();
            string missingSql = CreateValidSql().Replace(delete + "\n", "", StringComparison.Ordinal);
            string repeatedSql = CreateValidSql().Replace(delete, delete + "\n" + delete, StringComparison.Ordinal);
            string lateSql = CreateValidSql()
                .Replace(delete + "\n", "", StringComparison.Ordinal)
                .Replace(
                    "SET @GUID = (SELECT IFNULL(MAX(`id`), 0) FROM `entity`);",
                    "SET @GUID = (SELECT IFNULL(MAX(`id`), 0) FROM `entity`);\n" + delete,
                    StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult missingResult =
                CrimsonIsleQuest5593SqlPreflight.Validate(missingSql);
            CrimsonIsleQuest5593SqlPreflightResult repeatedResult =
                CrimsonIsleQuest5593SqlPreflight.Validate(repeatedSql);
            CrimsonIsleQuest5593SqlPreflightResult lateResult =
                CrimsonIsleQuest5593SqlPreflight.Validate(lateSql);

            Assert.False(missingResult.IsValid);
            Assert.Contains(missingResult.Diagnostics,
                diagnostic => diagnostic.Contains("exactly one scoped", StringComparison.Ordinal));
            Assert.False(repeatedResult.IsValid);
            Assert.Contains(repeatedResult.Diagnostics,
                diagnostic => diagnostic.Contains("found 2", StringComparison.Ordinal));
            Assert.False(lateResult.IsValid);
            Assert.Contains(lateResult.Diagnostics,
                diagnostic => diagnostic.Contains("must precede", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("1236", "1235", "exactly the 20 known")]
        [InlineData("1236", "1236, 1236", "is repeated")]
        [InlineData("1236", "70000", "unsigned 16-bit")]
        public void Validate_AlteredScopedDeleteAreaSet_FailsClosed(
            string oldValue,
            string newValue,
            string expectedDiagnostic)
        {
            string sql = CreateValidSql().Replace(
                $", {oldValue}, 1237",
                $", {newValue}, 1237",
                StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains(expectedDiagnostic, StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData("DELETE FROM `entity` WHERE `world` = @WORLD AND `area` = 1236;")]
        [InlineData("DELETE FROM `entity`;")]
        public void Validate_MalformedOrUnscopedDelete_FailsClosed(string replacement)
        {
            string sql = CreateValidSql().Replace(CreateDeleteStatement(), replacement, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("unsupported DELETE", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("DROP TABLE `entity`;")]
        [InlineData("UPDATE `entity` SET `world` = 0;")]
        [InlineData("START TRANSACTION;")]
        [InlineData("COMMIT;")]
        [InlineData("INSERT INTO `other_table` (`Id`) VALUES")]
        [InlineData("SELECT 1;")]
        public void Validate_UnsupportedExecutableStatement_FailsClosed(string statement)
        {
            string sql = CreateValidSql() + statement + "\n";

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Validate_TrailingStatementInjection_FailsClosed()
        {
            string sql = CreateValidSql().Replace(
                "SET @WORLD = 870;",
                "SET @WORLD = 870; DROP TABLE `entity`;",
                StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("malformed @WORLD", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("--not-a-comment")]
        [InlineData("--\u00A0DROP TABLE `entity`;")]
        [InlineData("# comment")]
        [InlineData("/* comment */")]
        [InlineData("/*!40101 SET @WORLD = 870 */;")]
        [InlineData("SELECT 1; SELECT 2;")]
        public void Validate_UnsupportedCommentOrMultipleStatementSyntax_FailsClosed(string statement)
        {
            string sql = CreateValidSql() + statement + "\n";

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("unsupported SQL statement", StringComparison.Ordinal));
        }

        [Fact]
        public void Validate_InlineCommentSyntax_FailsClosed()
        {
            string sql = CreateValidSql().Replace(
                "SET @WORLD = 870;",
                "SET @WORLD = 870; -- inline comment",
                StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("malformed @WORLD", StringComparison.Ordinal));
        }

        [Fact]
        public void Validate_CaseInsensitiveKeywordsWithCanonicalIdentifiers_RemainSupported()
        {
            string sql = CreateValidSql().Replace(
                CreateDeleteStatement(),
                "delete from `entity` where `world` = @world and `area` in "
                    + "(622, 623, 629, 1217, 1218, 1219, 1225, 1226, 1227, 1236, 1237, 1244, "
                    + "1284, 1320, 1325, 1360, 1361, 1611, 1885, 4674);",
                StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
        }

        [Theory]
        [InlineData("DELETE FROM `entity`", "DELETE FROM `entity")]
        [InlineData("MAX(`id`)", "MAX(`id)")]
        [InlineData("FROM `entity`);", "FROM entity`);")]
        [InlineData("INSERT INTO `entity`", "INSERT INTO `entity")]
        [InlineData("INSERT INTO `entity`", "INSERT INTO `ENTITY`")]
        [InlineData("(`Id`, `Type`", "(``Id``, `Type`")]
        [InlineData("(`Id`, `Type`", "(`Id, `Type`")]
        public void Validate_NonCanonicalOrUnbalancedIdentifiers_FailClosed(
            string oldValue,
            string newValue)
        {
            string sql = CreateValidSql().Replace(oldValue, newValue, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
        }

        [Fact]
        public void Validate_ConsecutiveGuidGenerations_FailClosed()
        {
            const string setGuid = "SET @GUID = (SELECT IFNULL(MAX(`id`), 0) FROM `entity`);";
            string sql = CreateValidSql().Replace(setGuid, setGuid + "\n" + setGuid, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("generation 0 must contain exactly one completed entity INSERT", StringComparison.Ordinal));
        }

        [Fact]
        public void Validate_MultipleEntityInsertsInOneGeneration_FailClosed()
        {
            string extraInsert = string.Join('\n',
            [
                "INSERT INTO `entity` (`Id`, `Type`, `Creature`, `World`, `Area`, `X`, `Y`, `Z`, `RX`, `RY`, `RZ`, `DisplayInfo`, `OutfitInfo`, `Faction1`, `Faction2`) VALUES",
                "    (@GUID+10, 0, 99999, @WORLD, 1236, 1, 2, 3, 0, 0, 0, 0, 0, 1, 1);",
                ""
            ]);
            string sql = CreateValidSql().Replace(
                "INSERT INTO `entity_spline`",
                extraInsert + "INSERT INTO `entity_spline`",
                StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("more than one entity INSERT", StringComparison.Ordinal));
        }

        [Fact]
        public void Validate_DependentInsertBeforeEntity_FailsClosed()
        {
            const string setGuid = "SET @GUID = (SELECT IFNULL(MAX(`id`), 0) FROM `entity`);";
            string earlyStat = string.Join('\n',
            [
                "INSERT INTO `entity_stats` (`Id`, `Stat`, `Value`) VALUES",
                "    (@GUID+1, 99, 1);"
            ]);
            string sql = CreateValidSql().Replace(
                setGuid,
                setGuid + "\n" + earlyStat,
                StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains("must follow the one completed entity INSERT", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("@WORLD, 1236", "871, 1236", "must use the validated @WORLD")]
        [InlineData("@WORLD, 1236", "@WORLD, 1235", "outside the exact Crimson Isle replacement scope")]
        public void Validate_EntityOutsideReplacementScope_FailsClosed(
            string oldValue,
            string newValue,
            string expectedDiagnostic)
        {
            string sql = CreateValidSql().Replace(oldValue, newValue, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains(expectedDiagnostic, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("99999, @WORLD, 1237", "99999, 871, 1237", "must use the validated @WORLD")]
        [InlineData("99999, @WORLD, 1237", "99999, @WORLD, 9999", "outside the exact Crimson Isle replacement scope")]
        public void Validate_UnrelatedEntityOutsideReplacementScope_FailsClosed(
            string oldValue,
            string newValue,
            string expectedDiagnostic)
        {
            string sql = CreateSqlWithUnrelatedEntity().Replace(oldValue, newValue, StringComparison.Ordinal);

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);

            Assert.False(result.IsValid);
            Assert.Contains(result.Diagnostics,
                diagnostic => diagnostic.Contains(expectedDiagnostic, StringComparison.Ordinal));
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
        [InlineData("    (@GUID+5, 0, 420),", "    (@GUID+5, 0, 1E-100),")]
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
                CreateDeleteStatement(),
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
                .Replace("SET @WORLD = 870;\n", "", StringComparison.Ordinal)
                .Replace(CreateDeleteStatement() + "\n", "", StringComparison.Ordinal);

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
                CreateDeleteStatement(),
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

        private static string CreateDeleteStatement()
        {
            return "DELETE FROM `entity` WHERE `world` = @WORLD AND `area` IN "
                + "(622, 623, 629, 1217, 1218, 1219, 1225, 1226, 1227, 1236, 1237, 1244, "
                + "1284, 1320, 1325, 1360, 1361, 1611, 1885, 4674);";
        }

        private static string CreateSqlWithUnrelatedEntity()
        {
            return CreateValidSql().Replace(
                "    (@GUID+9, 0, 24054, @WORLD, 1236, 18, 20, 30, 0, 0, 0, 0, 0, 550, 550);",
                string.Join('\n',
                [
                    "    (@GUID+9, 0, 24054, @WORLD, 1236, 18, 20, 30, 0, 0, 0, 0, 0, 550, 550),",
                    "    (@GUID+10, 0, 99999, @WORLD, 1237, 19, 20, 30, 0, 0, 0, 0, 0, 1, 1);"
                ]),
                StringComparison.Ordinal);
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
