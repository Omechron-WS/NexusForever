namespace NexusForever.API.Authentication
{
    /// <summary>
    /// Adds the configured service credential to an outgoing API request.
    /// </summary>
    public sealed class ServiceAuthenticationHandler : DelegatingHandler
    {
        private readonly string credential;

        /// <summary>
        /// Creates a new authentication handler for the supplied credential.
        /// </summary>
        public ServiceAuthenticationHandler(string credential)
        {
            if (!ServiceAuthentication.IsCredentialValid(credential))
            {
                throw new ArgumentException(
                    $"The service API credential must contain between {ServiceAuthentication.MinimumCredentialLength} and {ServiceAuthentication.MaximumCredentialLength} base64url characters.",
                    nameof(credential));
            }

            this.credential = credential;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Remove(ServiceAuthentication.HeaderName);
            if (!request.Headers.TryAddWithoutValidation(ServiceAuthentication.HeaderName, credential))
                throw new InvalidOperationException("The service authentication header could not be added to the API request.");

            return base.SendAsync(request, cancellationToken);
        }
    }
}
