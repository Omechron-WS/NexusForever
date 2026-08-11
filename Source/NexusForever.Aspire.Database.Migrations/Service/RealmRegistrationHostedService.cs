using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusForever.Aspire.Database.Migrations.Configuration.Model;
using NexusForever.Database.Auth;
using NexusForever.Database.Auth.Model;

namespace NexusForever.Aspire.Database.Migrations.Service
{
    public sealed class RealmRegistrationHostedService : IHostedService
    {
        private readonly ILogger<RealmRegistrationHostedService> _log;
        private readonly RealmRegistrationOptions _options;
        private readonly AuthContext _context;

        public RealmRegistrationHostedService(
            ILogger<RealmRegistrationHostedService> log,
            IOptions<RealmRegistrationOptions> options,
            AuthContext context)
        {
            _log = log;
            _options = options.Value;
            _context = context;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            RealmRegistration.Validate(_options);

            ServerModel realm = await _context.Server.SingleOrDefaultAsync(
                server => server.Id == _options.Id,
                cancellationToken);
            if (realm == null)
            {
                throw new InvalidOperationException(
                    $"Seeded realm `{_options.Id}` was not found after the auth database migrations.");
            }

            if (!RealmRegistration.Apply(_options, realm))
            {
                _log.LogInformation("Realm {RealmId} already advertises {Host}:{Port}; no update required.", realm.Id, realm.Host, realm.Port);
                return;
            }

            await _context.SaveChangesAsync(cancellationToken);
            _log.LogInformation(
                "Realm {RealmId} ({RealmName}) now advertises {Host}:{Port}.",
                realm.Id,
                realm.Name,
                realm.Host,
                realm.Port);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
