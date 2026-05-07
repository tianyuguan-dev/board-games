using BoardGames.Data;
using BoardGames.Models;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Tests.Data;

public class UserRepositoryTests
{
    private AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Add_SavesUserToDatabase()
    {
        var db = CreateInMemoryDb();
        var repo = new UserRepository(db);

        var user = new User { Username = "terry", PasswordHash = "hash123" };
        var result = await repo.Add(user);

        Assert.Equal("terry", result.Username);
        Assert.True(result.Id > 0);
    }

    [Fact]
    public async Task FindByUsername_ReturnsUser_WhenExists()
    {
        var db = CreateInMemoryDb();
        db.Users.Add(new User { Username = "terry", PasswordHash = "hash123" });
        await db.SaveChangesAsync();

        var repo = new UserRepository(db);
        var result = await repo.FindByUsername("terry");

        Assert.NotNull(result);
        Assert.Equal("terry", result.Username);
    }

    [Fact]
    public async Task FindByUsername_ReturnsNull_WhenNotExists()
    {
        var db = CreateInMemoryDb();
        var repo = new UserRepository(db);

        var result = await repo.FindByUsername("nobody");

        Assert.Null(result);
    }
}
