namespace NexusForever.Aspire.Database.Migrations.WorldDatabase
{
    public sealed class WorldDatabasePackageValidationException : Exception
    {
        public IReadOnlyList<string> Diagnostics { get; }

        public WorldDatabasePackageValidationException(IEnumerable<string> diagnostics)
            : this(Materialize(diagnostics))
        {
        }

        private WorldDatabasePackageValidationException(IReadOnlyList<string> diagnostics)
            : base(CreateMessage(diagnostics))
        {
            Diagnostics = diagnostics;
        }

        private static IReadOnlyList<string> Materialize(IEnumerable<string> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);
            return diagnostics.ToArray();
        }

        private static string CreateMessage(IReadOnlyList<string> diagnostics)
        {
            return "World database package validation failed:" + Environment.NewLine
                + string.Join(Environment.NewLine, diagnostics.Select(diagnostic => $"- {diagnostic}"));
        }
    }
}
