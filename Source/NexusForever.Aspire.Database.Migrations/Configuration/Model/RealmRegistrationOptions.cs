namespace NexusForever.Aspire.Database.Migrations.Configuration.Model
{
    public sealed class RealmRegistrationOptions
    {
        public byte Id { get; set; } = 1;
        public string Name { get; set; } = "NexusForever";
        public string Host { get; set; } = "127.0.0.1";
        public ushort Port { get; set; } = 24_000;
    }
}
