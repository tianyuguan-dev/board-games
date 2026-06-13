using BoardGames.Data;
using BoardGames.Services.Avalon;
using BoardGames.Services.BlackJack;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardGames.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Tests that exercise auto-timer fall-through (turn timeout / betting timeout) override this
    // factory's TimerSettings via the FastTimers nested type below. Default factory keeps production timing
    // so non-timer tests stay deterministic.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            var dbName = "IntegrationTests_" + Guid.NewGuid();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(dbName));

            services.AddSignalR(opts => opts.EnableDetailedErrors = true);
        });

        builder.UseEnvironment("Development");
    }
}

// Variant factory with short BlackJack timers (1s) so tests can verify auto-bet / auto-stand paths
// without a 20-second wait. Used by TurnTimerService integration tests.
public class FastTimerWebApplicationFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            var bjSettings = services.SingleOrDefault(d => d.ServiceType == typeof(BlackJackTimerSettings));
            if (bjSettings != null) services.Remove(bjSettings);
            services.AddSingleton(new BlackJackTimerSettings { TurnTimeSeconds = 1, BettingTimeSeconds = 1 });

            var avSettings = services.SingleOrDefault(d => d.ServiceType == typeof(AvalonHubSettings));
            if (avSettings != null) services.Remove(avSettings);
            services.AddSingleton(new AvalonHubSettings { ReconnectGraceSeconds = 1 });
        });
    }
}
