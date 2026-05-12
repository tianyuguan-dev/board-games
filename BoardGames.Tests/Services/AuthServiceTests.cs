using BoardGames.Data;
using BoardGames.Models;
using BoardGames.Services;
using Moq;

namespace BoardGames.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _mockRepo;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _mockRepo = new Mock<IUserRepository>();
        _authService = new AuthService(_mockRepo.Object);
    }

    [Fact]
    public async Task UsernameExists_ReturnsTrue_WhenUserFound()
    {
        _mockRepo.Setup(r => r.FindByUsername("terry"))
            .ReturnsAsync(new User { Id = 1, Username = "terry" });

        var result = await _authService.UsernameExists("terry");

        Assert.True(result);
    }

    [Fact]
    public async Task UsernameExists_ReturnsFalse_WhenUserNotFound()
    {
        _mockRepo.Setup(r => r.FindByUsername("nobody"))
            .ReturnsAsync((User?)null);

        var result = await _authService.UsernameExists("nobody");

        Assert.False(result);
    }

    [Fact]
    public async Task CreateUser_CallsRepoAdd_WithHashedPassword()
    {
        _mockRepo.Setup(r => r.Add(It.IsAny<User>()))
            .ReturnsAsync((User u) => u);

        var user = await _authService.CreateUser("terry", "123456");

        Assert.Equal("terry", user.Username);
        Assert.NotEqual("123456", user.PasswordHash);
        _mockRepo.Verify(r => r.Add(It.IsAny<User>()), Times.Once);
    }

    [Fact]
    public async Task ValidateUser_ReturnsUser_WhenPasswordCorrect()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("123456");
        _mockRepo.Setup(r => r.FindByUsername("terry"))
            .ReturnsAsync(new User { Id = 1, Username = "terry", PasswordHash = hash });

        var result = await _authService.ValidateUser("terry", "123456");

        Assert.NotNull(result);
        Assert.Equal("terry", result.Username);
    }

    [Fact]
    public async Task ValidateUser_ReturnsNull_WhenPasswordWrong()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("123456");
        _mockRepo.Setup(r => r.FindByUsername("terry"))
            .ReturnsAsync(new User { Id = 1, Username = "terry", PasswordHash = hash });

        var result = await _authService.ValidateUser("terry", "wrongpassword");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateUser_ReturnsNull_WhenUserNotFound()
    {
        _mockRepo.Setup(r => r.FindByUsername("nobody"))
            .ReturnsAsync((User?)null);

        var result = await _authService.ValidateUser("nobody", "123456");

        Assert.Null(result);
    }

    [Fact]
    public async Task ChangePassword_ReturnsTrue_WhenOldPasswordCorrect()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("oldpass");
        var user = new User { Id = 1, Username = "terry", PasswordHash = hash };
        _mockRepo.Setup(r => r.FindById(1)).ReturnsAsync(user);

        var result = await _authService.ChangePassword(1, "oldpass", "newpass");

        Assert.True(result);
        Assert.True(BCrypt.Net.BCrypt.Verify("newpass", user.PasswordHash));
        _mockRepo.Verify(r => r.Update(user), Times.Once);
    }

    [Fact]
    public async Task ChangePassword_ReturnsFalse_WhenOldPasswordWrong()
    {
        var hash = BCrypt.Net.BCrypt.HashPassword("oldpass");
        _mockRepo.Setup(r => r.FindById(1))
            .ReturnsAsync(new User { Id = 1, Username = "terry", PasswordHash = hash });

        var result = await _authService.ChangePassword(1, "wrongpass", "newpass");

        Assert.False(result);
        _mockRepo.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ChangePassword_ReturnsFalse_WhenUserNotFound()
    {
        _mockRepo.Setup(r => r.FindById(99)).ReturnsAsync((User?)null);

        var result = await _authService.ChangePassword(99, "oldpass", "newpass");

        Assert.False(result);
    }
}
