using System.Net;
using System.Net.Sockets;
using NexusForever.Aspire.Database.Migrations.Configuration.Model;
using NexusForever.Database.Auth.Model;

namespace NexusForever.Aspire.Database.Migrations.Service
{
    public static class RealmRegistration
    {
        public static bool Apply(RealmRegistrationOptions options, ServerModel realm)
        {
            Validate(options);
            ArgumentNullException.ThrowIfNull(realm);

            if (realm.Id != options.Id)
            {
                throw new InvalidOperationException(
                    $"Loaded realm id `{realm.Id}` does not match configured realm id `{options.Id}`.");
            }

            bool changed = false;
            if (!string.Equals(realm.Name, options.Name, StringComparison.Ordinal))
            {
                realm.Name = options.Name;
                changed = true;
            }

            if (!string.Equals(realm.Host, options.Host, StringComparison.Ordinal))
            {
                realm.Host = options.Host;
                changed = true;
            }

            if (realm.Port != options.Port)
            {
                realm.Port = options.Port;
                changed = true;
            }

            return changed;
        }

        public static void Validate(RealmRegistrationOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            if (options.Id == 0)
                throw new InvalidDataException("Realm:Id must be between 1 and 255.");
            if (string.IsNullOrWhiteSpace(options.Name) || options.Name.Length > 64)
                throw new InvalidDataException("Realm:Name must contain between 1 and 64 characters.");
            if (string.IsNullOrWhiteSpace(options.Host) || options.Host.Length > 64)
                throw new InvalidDataException("Realm:Host must contain between 1 and 64 characters.");
            if (!IPAddress.TryParse(options.Host, out IPAddress address)
                || address.AddressFamily != AddressFamily.InterNetwork
                || !address.ToString().Equals(options.Host, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Realm:Host must be a canonical IPv4 address advertised to the game client.");
            }

            byte firstOctet = address.GetAddressBytes()[0];
            if (firstOctet == 0
                || address.Equals(IPAddress.Any)
                || address.Equals(IPAddress.None)
                || address.Equals(IPAddress.Broadcast)
                || firstOctet >= 224)
            {
                throw new InvalidDataException("Realm:Host must be a usable unicast IPv4 address, not an unspecified, multicast, reserved, or broadcast address.");
            }

            if (options.Port == 0)
                throw new InvalidDataException("Realm:Port must be between 1 and 65535.");
        }
    }
}
