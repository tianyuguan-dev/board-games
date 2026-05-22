using BoardGames.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BoardGames.Tests.Integration;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
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
