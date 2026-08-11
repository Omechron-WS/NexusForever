using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NexusForever.Database.World.Validation
{
    /// <summary>
    /// Performs a deterministic, static validation of the bounded world SQL contract required by
    /// Crimson Isle quest 5593. The validator never executes SQL or accesses a database.
    /// </summary>
    public static class CrimsonIsleQuest5593SqlPreflight
    {
        private const uint RequiredWorldId = 870u;
        private const uint RequiredAreaId = 1_236u;
        private const uint MondoCreatureId = 24_187u;
        private const uint MineCreatureId = 24_251u;
        private const uint ScrabCreatureId = 24_054u;

        private static readonly Regex SetWorldPattern = new(
            @"^\s*SET\s+@WORLD\s*=\s*(?<world>[0-9]+)\s*;\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex SetWorldPrefixPattern = new(
            @"^\s*SET\s+@WORLD\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex SetGuidPattern = new(
            @"^\s*SET\s+@GUID\s*=\s*\(\s*SELECT\s+IFNULL\s*\(\s*MAX\s*\(\s*`?id`?\s*\)\s*,\s*0\s*\)\s+FROM\s+`?entity`?\s*\)\s*;\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex SetGuidPrefixPattern = new(
            @"^\s*SET\s+@GUID\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex InsertPattern = new(
            @"^\s*INSERT\s+INTO\s+`?(?<table>[A-Za-z_][A-Za-z0-9_]*)`?\s*\((?<columns>[^)]*)\)\s+VALUES\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex InsertTablePattern = new(
            @"^\s*INSERT\s+INTO\s+(?:`?[A-Za-z_][A-Za-z0-9_]*`?\s*\.\s*)?`?(?<table>[A-Za-z_][A-Za-z0-9_]*)`?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex TargetTableReferencePattern = new(
            @"\b(entity|entity_stats|entity_spline|entity_event)\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex TuplePattern = new(
            @"^\s*\((?<values>.*)\)\s*(?<terminator>[,;])\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex RelativeIdPattern = new(
            @"^@GUID\s*\+\s*(?<offset>[0-9]+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly HashSet<string> TargetTables = new(StringComparer.OrdinalIgnoreCase)
        {
            "entity",
            "entity_stats",
            "entity_spline",
            "entity_event"
        };

        /// <summary>
        /// Validates SQL text against the Crimson Isle quest 5593 world-content contract.
        /// </summary>
        /// <param name="sql">SQL text in the bounded generated-dump grammar.</param>
        /// <returns>A result containing the content hash, resolved counts and diagnostics.</returns>
        public static CrimsonIsleQuest5593SqlPreflightResult Validate(string sql)
        {
            ArgumentNullException.ThrowIfNull(sql);

            string sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
            var parser = new Parser();
            parser.Parse(sql);
            return parser.CreateResult(sha256);
        }

        private sealed class Parser
        {
            private readonly List<uint> worldAssignments = [];
            private readonly Dictionary<RelativeEntityId, ParsedEntity> entities = [];
            private readonly Dictionary<EntityStatId, double> stats = [];
            private readonly HashSet<RelativeEntityId> splineEntities = [];
            private readonly HashSet<RelativeEntityId> eventEntities = [];
            private readonly List<string> diagnostics = [];

            private int generation = -1;
            private uint? currentWorld;
            private TargetInsert currentInsert;

            public void Parse(string sql)
            {
                using var reader = new StringReader(sql);
                int lineNumber = 0;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    string statement = RemoveLineComment(line).Trim();
                    if (statement.Length == 0)
                        continue;

                    if (currentInsert != null)
                    {
                        Match tupleMatch = TuplePattern.Match(statement);
                        if (tupleMatch.Success)
                        {
                            ParseTuple(currentInsert, tupleMatch.Groups["values"].Value, lineNumber);
                            if (tupleMatch.Groups["terminator"].ValueSpan[0] == ';')
                                CompleteCurrentInsert();
                            continue;
                        }

                        diagnostics.Add($"Line {lineNumber}: expected a tuple for `{currentInsert.Table}` before the next statement.");
                        currentInsert = null;
                    }

                    Match worldMatch = SetWorldPattern.Match(statement);
                    if (worldMatch.Success)
                    {
                        if (!uint.TryParse(worldMatch.Groups["world"].Value, NumberStyles.None,
                                CultureInfo.InvariantCulture, out uint world))
                        {
                            diagnostics.Add($"Line {lineNumber}: @WORLD is outside the supported unsigned integer range.");
                            currentWorld = null;
                            continue;
                        }

                        worldAssignments.Add(world);
                        if (world > ushort.MaxValue)
                        {
                            diagnostics.Add(
                                $"Line {lineNumber}: @WORLD is outside the supported unsigned 16-bit range.");
                            currentWorld = null;
                        }
                        else
                        {
                            currentWorld = world;
                        }
                        continue;
                    }

                    if (SetWorldPrefixPattern.IsMatch(statement))
                    {
                        diagnostics.Add($"Line {lineNumber}: malformed @WORLD assignment.");
                        currentWorld = null;
                        continue;
                    }

                    if (SetGuidPattern.IsMatch(statement))
                    {
                        generation++;
                        continue;
                    }

                    if (SetGuidPrefixPattern.IsMatch(statement))
                    {
                        diagnostics.Add($"Line {lineNumber}: malformed @GUID generation statement.");
                        generation = -1;
                        continue;
                    }

                    Match insertMatch = InsertPattern.Match(statement);
                    if (insertMatch.Success)
                    {
                        string table = insertMatch.Groups["table"].Value;
                        if (TargetTables.Contains(table))
                        {
                            currentInsert = CreateTargetInsert(
                                table,
                                insertMatch.Groups["columns"].Value,
                                lineNumber);
                        }

                        continue;
                    }

                    Match insertTableMatch = InsertTablePattern.Match(statement);
                    if (insertTableMatch.Success && TargetTables.Contains(insertTableMatch.Groups["table"].Value))
                    {
                        diagnostics.Add($"Line {lineNumber}: malformed `{insertTableMatch.Groups["table"].Value}` INSERT header.");
                        continue;
                    }

                    if (statement.StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)
                        && TargetTableReferencePattern.IsMatch(statement))
                    {
                        diagnostics.Add($"Line {lineNumber}: unsupported target-table INSERT syntax.");
                    }
                }

                if (currentInsert != null)
                {
                    diagnostics.Add($"Line {currentInsert.LineNumber}: `{currentInsert.Table}` INSERT is not terminated.");
                    currentInsert = null;
                }

                ValidateOrphans();
            }

            public CrimsonIsleQuest5593SqlPreflightResult CreateResult(string sha256)
            {
                ValidateContract(
                    out int mondoSpawnCount,
                    out int mineSpawnCount,
                    out int scrabSpawnCount,
                    out int eligibleScrabSpawnCount);

                return new CrimsonIsleQuest5593SqlPreflightResult(
                    sha256,
                    mondoSpawnCount,
                    mineSpawnCount,
                    scrabSpawnCount,
                    eligibleScrabSpawnCount,
                    new ReadOnlyCollection<string>(diagnostics.ToArray()));
            }

            private static string RemoveLineComment(string line)
            {
                int commentIndex = line.IndexOf("--", StringComparison.Ordinal);
                return commentIndex < 0 ? line : line[..commentIndex];
            }

            private TargetInsert CreateTargetInsert(string table, string columnList, int lineNumber)
            {
                string[] columns = columnList
                    .Split(',', StringSplitOptions.TrimEntries);
                var columnIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                bool isValid = columns.Length > 0;

                for (int index = 0; index < columns.Length; index++)
                {
                    string column = columns[index].Trim().Trim('`');
                    if (column.Length == 0)
                    {
                        diagnostics.Add($"Line {lineNumber}: `{table}` INSERT contains an empty column name.");
                        isValid = false;
                        continue;
                    }

                    if (!columnIndexes.TryAdd(column, index))
                    {
                        diagnostics.Add($"Line {lineNumber}: `{table}` INSERT repeats column `{column}`.");
                        isValid = false;
                    }
                }

                foreach (string requiredColumn in GetRequiredColumns(table))
                {
                    if (columnIndexes.ContainsKey(requiredColumn))
                        continue;

                    diagnostics.Add($"Line {lineNumber}: `{table}` INSERT is missing required column `{requiredColumn}`.");
                    isValid = false;
                }

                HashSet<string> allowedColumns = GetAllowedColumns(table);
                foreach (string column in columnIndexes.Keys)
                {
                    if (allowedColumns.Contains(column))
                        continue;

                    diagnostics.Add($"Line {lineNumber}: `{table}` INSERT contains unsupported column `{column}`.");
                    isValid = false;
                }

                return new TargetInsert(table, lineNumber, columns.Length, columnIndexes, isValid);
            }

            private static IReadOnlyList<string> GetRequiredColumns(string table)
            {
                return table.ToLowerInvariant() switch
                {
                    "entity" =>
                    [
                        "Id", "Type", "Creature", "World", "Area", "X", "Y", "Z", "RX", "RY", "RZ",
                        "DisplayInfo", "OutfitInfo", "Faction1", "Faction2"
                    ],
                    "entity_stats" => ["Id", "Stat", "Value"],
                    "entity_spline" => ["Id", "SplineId", "Mode", "Speed", "FX", "FY", "FZ"],
                    "entity_event" => ["Id", "EventId", "Phase"],
                    _ => []
                };
            }

            private static HashSet<string> GetAllowedColumns(string table)
            {
                var columns = new HashSet<string>(GetRequiredColumns(table), StringComparer.OrdinalIgnoreCase);
                if (table.Equals("entity", StringComparison.OrdinalIgnoreCase))
                    columns.Add("ActivePropId");
                return columns;
            }

            private void ParseTuple(TargetInsert insert, string valueList, int lineNumber)
            {
                insert.RowCount++;
                string[] values = valueList.Split(',', StringSplitOptions.TrimEntries);
                if (values.Length != insert.ColumnCount)
                {
                    diagnostics.Add(
                        $"Line {lineNumber}: `{insert.Table}` tuple has {values.Length} values for {insert.ColumnCount} columns.");
                    return;
                }

                if (!insert.IsValid)
                    return;

                switch (insert.Table.ToLowerInvariant())
                {
                    case "entity":
                        ParseEntity(insert, values, lineNumber);
                        break;
                    case "entity_stats":
                        ParseEntityStat(insert, values, lineNumber);
                        break;
                    case "entity_spline":
                        ParseEntitySpline(insert, values, lineNumber);
                        break;
                    case "entity_event":
                        ParseEntityEvent(insert, values, lineNumber);
                        break;
                }
            }

            private void ParseEntity(TargetInsert insert, IReadOnlyList<string> values, int lineNumber)
            {
                bool idIsValid = TryParseRelativeId(
                    GetValue(insert, values, "Id"), lineNumber, out RelativeEntityId id);
                bool typeIsValid = TryParseByte(
                    GetValue(insert, values, "Type"), insert.Table, "Type", lineNumber, out byte type);
                bool creatureIsValid = TryParseUInt32(
                    GetValue(insert, values, "Creature"), insert.Table, "Creature", lineNumber, out uint creature);
                bool worldIsValid = TryParseWorld(
                    GetValue(insert, values, "World"), lineNumber, out uint world);
                bool areaIsValid = TryParseUInt16(
                    GetValue(insert, values, "Area"), insert.Table, "Area", lineNumber, out ushort area);
                bool xIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "X"), insert.Table, "X", lineNumber, out double x);
                bool yIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "Y"), insert.Table, "Y", lineNumber, out double y);
                bool zIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "Z"), insert.Table, "Z", lineNumber, out double z);
                bool rxIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "RX"), insert.Table, "RX", lineNumber, out _);
                bool ryIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "RY"), insert.Table, "RY", lineNumber, out _);
                bool rzIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "RZ"), insert.Table, "RZ", lineNumber, out _);
                bool displayInfoIsValid = TryParseUInt32(
                    GetValue(insert, values, "DisplayInfo"), insert.Table, "DisplayInfo", lineNumber, out _);
                bool outfitInfoIsValid = TryParseUInt16(
                    GetValue(insert, values, "OutfitInfo"), insert.Table, "OutfitInfo", lineNumber, out _);
                bool faction1IsValid = TryParseUInt16(
                    GetValue(insert, values, "Faction1"), insert.Table, "Faction1", lineNumber, out ushort faction1);
                bool faction2IsValid = TryParseUInt16(
                    GetValue(insert, values, "Faction2"), insert.Table, "Faction2", lineNumber, out ushort faction2);
                bool activePropIdIsValid = !insert.Columns.ContainsKey("ActivePropId")
                    || TryParseUInt64(
                        GetValue(insert, values, "ActivePropId"), insert.Table, "ActivePropId", lineNumber, out _);

                if (!idIsValid || !typeIsValid || !creatureIsValid || !worldIsValid || !areaIsValid
                    || !xIsValid || !yIsValid || !zIsValid || !rxIsValid || !ryIsValid || !rzIsValid
                    || !displayInfoIsValid || !outfitInfoIsValid || !faction1IsValid || !faction2IsValid
                    || !activePropIdIsValid)
                {
                    return;
                }

                var entity = new ParsedEntity(id, type, creature, world, area, x, y, z, faction1, faction2, lineNumber);
                if (!entities.TryAdd(id, entity))
                    diagnostics.Add($"Line {lineNumber}: duplicate entity identity {id}.");
            }

            private void ParseEntityStat(TargetInsert insert, IReadOnlyList<string> values, int lineNumber)
            {
                bool idIsValid = TryParseRelativeId(
                    GetValue(insert, values, "Id"), lineNumber, out RelativeEntityId id);
                bool statIsValid = TryParseByte(
                    GetValue(insert, values, "Stat"), insert.Table, "Stat", lineNumber, out byte stat);
                bool valueIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "Value"), insert.Table, "Value", lineNumber, out double value);
                if (!idIsValid || !statIsValid || !valueIsValid)
                {
                    return;
                }

                var statId = new EntityStatId(id, stat);
                if (!stats.TryAdd(statId, value))
                    diagnostics.Add($"Line {lineNumber}: duplicate stat {stat} for entity identity {id}.");
            }

            private void ParseEntitySpline(TargetInsert insert, IReadOnlyList<string> values, int lineNumber)
            {
                bool idIsValid = TryParseRelativeId(
                    GetValue(insert, values, "Id"), lineNumber, out RelativeEntityId id);
                bool splineIdIsValid = TryParseUInt16(
                    GetValue(insert, values, "SplineId"), insert.Table, "SplineId", lineNumber, out _);
                bool modeIsValid = TryParseByte(
                    GetValue(insert, values, "Mode"), insert.Table, "Mode", lineNumber, out _);
                bool speedIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "Speed"), insert.Table, "Speed", lineNumber, out _);
                bool fxIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "FX"), insert.Table, "FX", lineNumber, out _);
                bool fyIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "FY"), insert.Table, "FY", lineNumber, out _);
                bool fzIsValid = TryParseFiniteFloat(
                    GetValue(insert, values, "FZ"), insert.Table, "FZ", lineNumber, out _);
                if (!idIsValid || !splineIdIsValid || !modeIsValid || !speedIsValid
                    || !fxIsValid || !fyIsValid || !fzIsValid)
                {
                    return;
                }

                if (!splineEntities.Add(id))
                    diagnostics.Add($"Line {lineNumber}: duplicate spline for entity identity {id}.");
            }

            private void ParseEntityEvent(TargetInsert insert, IReadOnlyList<string> values, int lineNumber)
            {
                bool idIsValid = TryParseRelativeId(
                    GetValue(insert, values, "Id"), lineNumber, out RelativeEntityId id);
                bool eventIdIsValid = TryParseUInt32(
                    GetValue(insert, values, "EventId"), insert.Table, "EventId", lineNumber, out uint eventId);
                bool phaseIsValid = TryParseUInt32(
                    GetValue(insert, values, "Phase"), insert.Table, "Phase", lineNumber, out uint phase);
                if (!idIsValid || !eventIdIsValid || !phaseIsValid)
                    return;

                if (!eventEntities.Add(id))
                {
                    diagnostics.Add(
                        $"Line {lineNumber}: entity identity {id} has more than one event row; "
                        + $"the repeated row contains event {eventId}, phase {phase}.");
                }
            }

            private static string GetValue(TargetInsert insert, IReadOnlyList<string> values, string column)
            {
                return values[insert.Columns[column]];
            }

            private bool TryParseRelativeId(string value, int lineNumber, out RelativeEntityId id)
            {
                id = default;
                if (generation < 0)
                {
                    diagnostics.Add($"Line {lineNumber}: relative entity identity is used before a valid @GUID generation.");
                    return false;
                }

                Match match = RelativeIdPattern.Match(value);
                if (!match.Success
                    || !uint.TryParse(match.Groups["offset"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out uint offset)
                    || offset == 0u)
                {
                    diagnostics.Add($"Line {lineNumber}: unsupported relative entity identity `{value}`.");
                    return false;
                }

                id = new RelativeEntityId(generation, offset);
                return true;
            }

            private bool TryParseWorld(string value, int lineNumber, out uint world)
            {
                if (value.Equals("@WORLD", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentWorld.HasValue && currentWorld.Value <= ushort.MaxValue)
                    {
                        world = currentWorld.Value;
                        return true;
                    }

                    diagnostics.Add(currentWorld.HasValue
                        ? $"Line {lineNumber}: @WORLD is outside the supported unsigned 16-bit range."
                        : $"Line {lineNumber}: @WORLD is used before a valid assignment.");
                    world = 0u;
                    return false;
                }

                bool isValid = TryParseUInt16(value, "entity", "World", lineNumber, out ushort parsed);
                world = parsed;
                return isValid;
            }

            private bool TryParseByte(
                string value,
                string table,
                string column,
                int lineNumber,
                out byte parsed)
            {
                if (byte.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
                    return true;

                diagnostics.Add(
                    $"Line {lineNumber}: `{table}` column `{column}` is not an unsigned 8-bit integer.");
                return false;
            }

            private bool TryParseUInt16(
                string value,
                string table,
                string column,
                int lineNumber,
                out ushort parsed)
            {
                if (ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
                    return true;

                diagnostics.Add(
                    $"Line {lineNumber}: `{table}` column `{column}` is not an unsigned 16-bit integer.");
                return false;
            }

            private bool TryParseUInt32(
                string value,
                string table,
                string column,
                int lineNumber,
                out uint parsed)
            {
                if (uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
                    return true;

                diagnostics.Add($"Line {lineNumber}: `{table}` column `{column}` is not an unsigned integer.");
                return false;
            }

            private bool TryParseUInt64(
                string value,
                string table,
                string column,
                int lineNumber,
                out ulong parsed)
            {
                if (ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed))
                    return true;

                diagnostics.Add(
                    $"Line {lineNumber}: `{table}` column `{column}` is not an unsigned 64-bit integer.");
                return false;
            }

            private bool TryParseFiniteFloat(
                string value,
                string table,
                string column,
                int lineNumber,
                out double parsed)
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                    && IsFiniteFloat(parsed))
                {
                    return true;
                }

                diagnostics.Add(
                    $"Line {lineNumber}: `{table}` column `{column}` is not a finite float-compatible value.");
                return false;
            }

            private void CompleteCurrentInsert()
            {
                if (currentInsert.RowCount == 0)
                {
                    diagnostics.Add(
                        $"Line {currentInsert.LineNumber}: `{currentInsert.Table}` INSERT contains no tuples.");
                }

                currentInsert = null;
            }

            private void ValidateOrphans()
            {
                foreach (EntityStatId statId in stats.Keys
                             .OrderBy(key => key.EntityId.Generation)
                             .ThenBy(key => key.EntityId.Offset)
                             .ThenBy(key => key.Stat))
                {
                    if (!entities.ContainsKey(statId.EntityId))
                        diagnostics.Add($"Orphan stat {statId.Stat} for entity identity {statId.EntityId}.");
                }

                foreach (RelativeEntityId id in splineEntities
                             .OrderBy(key => key.Generation)
                             .ThenBy(key => key.Offset))
                {
                    if (!entities.ContainsKey(id))
                        diagnostics.Add($"Orphan spline for entity identity {id}.");
                }

                foreach (RelativeEntityId id in eventEntities
                             .OrderBy(key => key.Generation)
                             .ThenBy(key => key.Offset))
                {
                    if (!entities.ContainsKey(id))
                        diagnostics.Add($"Orphan event for entity identity {id}.");
                }
            }

            private void ValidateContract(
                out int mondoSpawnCount,
                out int mineSpawnCount,
                out int scrabSpawnCount,
                out int eligibleScrabSpawnCount)
            {
                if (worldAssignments.Count != 1)
                {
                    diagnostics.Add(
                        $"Expected exactly one @WORLD assignment, but found {worldAssignments.Count}.");
                }
                else if (worldAssignments[0] != RequiredWorldId)
                {
                    diagnostics.Add(
                        $"Expected @WORLD {RequiredWorldId}, but found {worldAssignments[0]}.");
                }

                List<ParsedEntity> relevantEntities = entities.Values
                    .Where(entity => entity.World == RequiredWorldId && entity.Area == RequiredAreaId)
                    .Where(entity => entity.Creature is MondoCreatureId or MineCreatureId or ScrabCreatureId)
                    .OrderBy(entity => entity.Id.Generation)
                    .ThenBy(entity => entity.Id.Offset)
                    .ToList();

                mondoSpawnCount = relevantEntities.Count(entity => entity.Creature == MondoCreatureId);
                mineSpawnCount = relevantEntities.Count(entity => entity.Creature == MineCreatureId);
                scrabSpawnCount = relevantEntities.Count(entity => entity.Creature == ScrabCreatureId);

                foreach (ParsedEntity entity in relevantEntities)
                    ValidateRelevantEntityShape(entity);

                int eligibleMondoSpawnCount = relevantEntities.Count(entity =>
                    entity.Creature == MondoCreatureId
                    && IsNormalEntity(entity)
                    && HasExpectedShape(entity, 0u, 170u));
                int eligibleMineSpawnCount = relevantEntities.Count(entity =>
                    entity.Creature == MineCreatureId
                    && IsNormalEntity(entity)
                    && HasExpectedShape(entity, 0u, 865u));

                eligibleScrabSpawnCount = 0;
                foreach (ParsedEntity scrab in relevantEntities.Where(entity => entity.Creature == ScrabCreatureId))
                {
                    if (!IsNormalEntity(scrab)
                        || splineEntities.Contains(scrab.Id)
                        || !HasExpectedShape(scrab, 0u, 550u))
                    {
                        continue;
                    }

                    bool hasHealth = HasPositiveFiniteStat(scrab, 0u);
                    bool hasLevel = HasPositiveFiniteStat(scrab, 10u);
                    if (hasHealth && hasLevel)
                        eligibleScrabSpawnCount++;
                }

                if (eligibleMondoSpawnCount < 1)
                    diagnostics.Add("Expected at least one normal quest giver/receiver spawn for creature 24187 in world 870, area 1236.");
                if (eligibleMineSpawnCount < 3)
                    diagnostics.Add("Expected at least three normal mine spawns for creature 24251 in world 870, area 1236.");
                if (eligibleScrabSpawnCount < 4)
                    diagnostics.Add("Expected at least four normal, unsplined Scrab spawns with positive health and level stats for creature 24054 in world 870, area 1236.");
            }

            private void ValidateRelevantEntityShape(ParsedEntity entity)
            {
                (uint expectedType, uint expectedFaction) = entity.Creature switch
                {
                    MondoCreatureId => (0u, 170u),
                    MineCreatureId => (0u, 865u),
                    ScrabCreatureId => (0u, 550u),
                    _ => throw new InvalidOperationException()
                };

                if (entity.Type != expectedType)
                {
                    diagnostics.Add(
                        $"Line {entity.LineNumber}: creature {entity.Creature} has unsupported entity type {entity.Type}; expected {expectedType}.");
                }

                if (entity.Faction1 != expectedFaction || entity.Faction2 != expectedFaction)
                {
                    diagnostics.Add(
                        $"Line {entity.LineNumber}: creature {entity.Creature} has factions {entity.Faction1}/{entity.Faction2}; expected {expectedFaction}/{expectedFaction}.");
                }

                if (!HasFiniteFloatCoordinates(entity))
                {
                    diagnostics.Add(
                        $"Line {entity.LineNumber}: creature {entity.Creature} has non-finite or out-of-range coordinates.");
                }
            }

            private bool HasPositiveFiniteStat(ParsedEntity entity, uint stat)
            {
                if (stats.TryGetValue(new EntityStatId(entity.Id, stat), out double value)
                    && double.IsFinite(value)
                    && value > 0d
                    && value <= float.MaxValue)
                {
                    return true;
                }

                diagnostics.Add(
                    $"Line {entity.LineNumber}: eligible Scrab {entity.Id} requires a positive finite stat {stat} value.");
                return false;
            }

            private bool IsNormalEntity(ParsedEntity entity)
            {
                return !eventEntities.Contains(entity.Id) && HasFiniteFloatCoordinates(entity);
            }

            private static bool HasExpectedShape(ParsedEntity entity, uint type, uint faction)
            {
                return entity.Type == type
                    && entity.Faction1 == faction
                    && entity.Faction2 == faction;
            }

            private static bool HasFiniteFloatCoordinates(ParsedEntity entity)
            {
                return IsFiniteFloat(entity.X)
                    && IsFiniteFloat(entity.Y)
                    && IsFiniteFloat(entity.Z);
            }

            private static bool IsFiniteFloat(double value)
            {
                return double.IsFinite(value) && value >= -float.MaxValue && value <= float.MaxValue;
            }
        }

        private sealed class TargetInsert
        {
            public string Table { get; }
            public int LineNumber { get; }
            public int ColumnCount { get; }
            public IReadOnlyDictionary<string, int> Columns { get; }
            public bool IsValid { get; }
            public int RowCount { get; set; }

            public TargetInsert(
                string table,
                int lineNumber,
                int columnCount,
                IReadOnlyDictionary<string, int> columns,
                bool isValid)
            {
                Table = table;
                LineNumber = lineNumber;
                ColumnCount = columnCount;
                Columns = columns;
                IsValid = isValid;
            }
        }

        private readonly record struct RelativeEntityId(int Generation, uint Offset)
        {
            public override string ToString()
            {
                return $"generation {Generation}, offset {Offset}";
            }
        }

        private readonly record struct EntityStatId(RelativeEntityId EntityId, uint Stat);

        private sealed record ParsedEntity(
            RelativeEntityId Id,
            uint Type,
            uint Creature,
            uint World,
            uint Area,
            double X,
            double Y,
            double Z,
            uint Faction1,
            uint Faction2,
            int LineNumber);
    }

    /// <summary>
    /// Represents the deterministic outcome of a Crimson Isle quest 5593 static SQL preflight.
    /// </summary>
    public sealed class CrimsonIsleQuest5593SqlPreflightResult
    {
        /// <summary>
        /// Gets a value indicating whether the SQL satisfies the complete static content contract.
        /// </summary>
        public bool IsValid => Diagnostics.Count == 0;

        /// <summary>
        /// Gets the lowercase SHA-256 hash of the UTF-8 SQL text that was validated.
        /// </summary>
        public string Sha256 { get; }

        /// <summary>
        /// Gets the number of creature 24187 rows in world 870, area 1236.
        /// </summary>
        public int MondoSpawnCount { get; }

        /// <summary>
        /// Gets the number of creature 24251 rows in world 870, area 1236.
        /// </summary>
        public int MineSpawnCount { get; }

        /// <summary>
        /// Gets the number of creature 24054 rows in world 870, area 1236.
        /// </summary>
        public int ScrabSpawnCount { get; }

        /// <summary>
        /// Gets the number of normal, unsplined Scrab rows with valid health and level stats.
        /// </summary>
        public int EligibleScrabSpawnCount { get; }

        /// <summary>
        /// Gets deterministic validation diagnostics. An empty collection indicates success.
        /// </summary>
        public IReadOnlyList<string> Diagnostics { get; }

        internal CrimsonIsleQuest5593SqlPreflightResult(
            string sha256,
            int mondoSpawnCount,
            int mineSpawnCount,
            int scrabSpawnCount,
            int eligibleScrabSpawnCount,
            IReadOnlyList<string> diagnostics)
        {
            Sha256 = sha256;
            MondoSpawnCount = mondoSpawnCount;
            MineSpawnCount = mineSpawnCount;
            ScrabSpawnCount = scrabSpawnCount;
            EligibleScrabSpawnCount = eligibleScrabSpawnCount;
            Diagnostics = diagnostics;
        }
    }
}
