namespace NexusForever.Aspire.Database.Migrations.WorldDatabase
{
    public sealed class WorldDatabaseImportException : Exception
    {
        public string RelativePath { get; }

        public WorldDatabaseImportException(string relativePath, Exception innerException)
            : base($"Failed to import world database file `{relativePath}`. Its SQL and version record were rolled back.", innerException)
        {
            RelativePath = relativePath;
        }
    }
}
