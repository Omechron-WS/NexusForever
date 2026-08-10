namespace NexusForever.API.Configuration.Model
{
    /// <summary>
    /// Configuration for a service API client.
    /// </summary>
    public class APIConfig
    {
        /// <summary>
        /// Gets or sets the API base address.
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// Gets or sets the service authentication credential.
        /// </summary>
        public string Credential { get; set; }
    }
}
