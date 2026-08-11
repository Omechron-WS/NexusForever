using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusForever.Aspire.Database.Migrations.Configuration.Model;
using NexusForever.Aspire.Database.Migrations.Service;
using NexusForever.Database.Auth;
using NexusForever.Database.Character;
using NexusForever.Database.Chat;
using NexusForever.Database.Friendship;
using NexusForever.Database.Group;
using NexusForever.Database.Query;
using NexusForever.Database.World;
using NexusForever.Database.World.Validation;
using NLog.Extensions.Logging;

namespace NexusForever.Aspire.Database.Migrations
{
    internal class Program
    {
        private const string CrimsonIsleQuest5593PreflightArgument = "--preflight-crimson-isle-q5593";

        static async Task<int> Main(string[] args)
        {
            if (args.Length > 0
                && args[0].Equals(CrimsonIsleQuest5593PreflightArgument, StringComparison.Ordinal))
            {
                return RunCrimsonIsleQuest5593Preflight(args);
            }

            string basePath = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

            var builder = new HostBuilder()
                .ConfigureAppConfiguration(cb =>
                {
                    cb.SetBasePath(basePath)
                        .AddJsonFile("AspireMigrations.json", false)
                        .AddEnvironmentVariables();
                })
                .ConfigureLogging(l =>
                {
                    l.ClearProviders();
                    l.AddNLog();
                })
                .ConfigureServices((hb, sc) =>
                {
                    sc.AddOptions<AccountCreationOptions>()
                        .Bind(hb.Configuration.GetSection("AccountCreation"));

                    sc.AddOptions<WorldDatabaseOptions>()
                        .Bind(hb.Configuration.GetSection("WorldDatabase"));

                    sc.AddHostedService<DatabaseMigrationHostedService>();
                    sc.AddHostedService<AccountCreationHostedService>();
                    sc.AddHostedService<WorldDatabaseHostedService>();
                    sc.AddHostedService<FinishHostedService>();

                    sc.AddScoped(sp =>
                    {
                        var options = sp.GetService<DbContextOptions<AuthContext>>();
                        return new AuthContext(options);
                    });
                    sc.AddScoped(sp =>
                    {
                        var options = sp.GetService<DbContextOptions<CharacterContext>>();
                        return new CharacterContext(options);
                    });
                    sc.AddScoped(sp =>
                    {
                        var options = sp.GetService<DbContextOptions<WorldContext>>();
                        return new WorldContext(options);
                    });

                    sc.AddDbContext<AuthContext>(options =>
                    {
                        var connectionString = hb.Configuration.GetConnectionString("authdb");
                        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                    });
                    sc.AddDbContext<CharacterContext>(options =>
                    {
                        var connectionString = hb.Configuration.GetConnectionString("characterdb");
                        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                    });
                    sc.AddDbContext<WorldContext>(options =>
                    {
                        var connectionString = hb.Configuration.GetConnectionString("worlddb");
                        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                    });
                    sc.AddDbContext<GroupContext>(options =>
                    {
                        var connectionString = hb.Configuration.GetConnectionString("groupdb");
                        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                    });
                    sc.AddDbContext<ChatContext>(options =>
                    {
                        var connectionString = hb.Configuration.GetConnectionString("chatdb");
                        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                    });
                    sc.AddDbContext<FriendshipContext>(options =>
                    {
                        var connectionString = hb.Configuration.GetConnectionString("friendshipdb");
                        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                    });
                    sc.AddDbContext<QueryContext>(options =>
                    {
                        var connectionString = hb.Configuration.GetConnectionString("querydb");
                        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
                    });
                });

            IHost host = builder.Build();
            await host.RunAsync();
            return 0;
        }

        private static int RunCrimsonIsleQuest5593Preflight(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine(
                    $"Usage: NexusForever.Aspire.Database.Migrations {CrimsonIsleQuest5593PreflightArgument} <file>");
                return 2;
            }

            string sql;
            try
            {
                sql = File.ReadAllText(args[1]);
            }
            catch (Exception exception) when (exception is ArgumentException
                or IOException
                or NotSupportedException
                or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Unable to read SQL file `{args[1]}`: {exception.Message}");
                return 2;
            }

            CrimsonIsleQuest5593SqlPreflightResult result = CrimsonIsleQuest5593SqlPreflight.Validate(sql);
            Console.WriteLine($"SHA-256: {result.Sha256}");
            Console.WriteLine(
                $"World 870 / area 1236: Mondo={result.MondoSpawnCount}, Mines={result.MineSpawnCount}, "
                + $"Scrabs={result.ScrabSpawnCount}, EligibleScrabs={result.EligibleScrabSpawnCount}");
            Console.WriteLine("Static SQL preflight only; no SQL was executed and no database was accessed.");

            if (result.IsValid)
            {
                Console.WriteLine("Crimson Isle quest 5593 content contract: VALID");
                return 0;
            }

            Console.Error.WriteLine("Crimson Isle quest 5593 content contract: INVALID");
            foreach (string diagnostic in result.Diagnostics)
                Console.Error.WriteLine($"- {diagnostic}");

            return 1;
        }
    }
}
