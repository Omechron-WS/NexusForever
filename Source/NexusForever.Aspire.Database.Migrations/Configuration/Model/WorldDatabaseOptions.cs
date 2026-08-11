namespace NexusForever.Aspire.Database.Migrations.Configuration.Model
{
    public class WorldDatabaseOptions
    {
        public string Path { get; set; }
        public bool Required { get; set; }
        public int LockTimeoutSeconds { get; set; } = 60;
        public int CommandTimeoutSeconds { get; set; } = 600;
    }
}
