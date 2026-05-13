using System.Security.Claims;
using BoardGames.Controllers;
using BoardGames.Data;
using BoardGames.Dtos;
using BoardGames.Models;
using BoardGames.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BoardGames.Tests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _mockAuthService;
    private readonly Mock<IJwtService> _mockJwtService;
    private readonly Mock<IUserRepository> _mockUserRepository;
    private readonly Mock<IGameBalanceRepository> _mockBalanceRepository;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockJwtService = new Mock<IJwtService>();
        _mockUserRepository = new Mock<IUserRepository>();
        _mockBalanceRepository = new Mock<IGameBalanceRepository>();
        _controller = new AuthController(_mockJwtService.Object, _mockAuthService.Object, _mockUserRepository.Object, _mockBalanceRepository.Object);
    }

    private void SetupAuthenticatedUser(int userId = 1)
    {
        var claims = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
        }, "test"));
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = claims }
        };
    }

    [Fact]
    public async Task Register_ReturnsOk_WhenUsernameAvailable()
    {
        _mockAuthService.Setup(s => s.UsernameExists("terry")).ReturnsAsync(false);
        _mockAuthService.Setup(s => s.CreateUser("terry", "123456"))
            .ReturnsAsync(new User { Id = 1, Username = "terry" });

        var result = await _controller.Register(new RegisterRequestDto
            { Username = "terry", Password = "123456" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Register_ReturnsBadRequest_WhenUsernameExists()
    {
        _mockAuthService.Setup(s => s.UsernameExists("terry")).ReturnsAsync(true);

        var result = await _controller.Register(new RegisterRequestDto
            { Username = "terry", Password = "123456" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Login_ReturnsOkWithToken_WhenCredentialsValid()
    {
        var user = new User { Id = 1, Username = "terry" };
        _mockAuthService.Setup(s => s.ValidateUser("terry", "123456"))
            .ReturnsAsync(user);
        _mockJwtService.Setup(s => s.GenerateJwtToken(user))
            .Returns("fake-jwt-token");

        var result = await _controller.Login(new LoginRequestDto
            { Username = "terry", Password = "123456" });

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("fake-jwt-token", okResult.Value!.ToString());
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenCredentialsInvalid()
    {
        _mockAuthService.Setup(s => s.ValidateUser("terry", "wrong"))
            .ReturnsAsync((User?)null);

        var result = await _controller.Login(new LoginRequestDto
            { Username = "terry", Password = "wrong" });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task UpdateNickname_ReturnsOk_WhenUserExists()
    {
        SetupAuthenticatedUser();
        var user = new User { Id = 1, Username = "terry", Nickname = "old" };
        _mockUserRepository.Setup(r => r.FindById(1)).ReturnsAsync(user);

        var result = await _controller.UpdateNickname(new UpdateNicknameDto { Nickname = "newname" });

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("newname", okResult.Value!.ToString());
    }

    [Fact]
    public async Task UpdateNickname_ReturnsNotFound_WhenUserMissing()
    {
        SetupAuthenticatedUser(99);
        _mockUserRepository.Setup(r => r.FindById(99)).ReturnsAsync((User?)null);

        var result = await _controller.UpdateNickname(new UpdateNicknameDto { Nickname = "newname" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ChangePassword_ReturnsOk_WhenSuccessful()
    {
        SetupAuthenticatedUser();
        _mockAuthService.Setup(s => s.ChangePassword(1, "oldpass", "newpass")).ReturnsAsync(true);

        var result = await _controller.ChangePassword(new ChangePasswordDto
            { OldPassword = "oldpass", NewPassword = "newpass" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ChangePassword_ReturnsBadRequest_WhenOldPasswordWrong()
    {
        SetupAuthenticatedUser();
        _mockAuthService.Setup(s => s.ChangePassword(1, "wrong", "newpass")).ReturnsAsync(false);

        var result = await _controller.ChangePassword(new ChangePasswordDto
            { OldPassword = "wrong", NewPassword = "newpass" });

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetBalances_ReturnsAllGameBalances()
    {
        SetupAuthenticatedUser();
        _mockBalanceRepository.Setup(r => r.GetOrCreate(1, GameType.BlackJack))
            .ReturnsAsync(new GameBalance { Balance = 500 });
        _mockBalanceRepository.Setup(r => r.GetOrCreate(1, GameType.Avalon))
            .ReturnsAsync(new GameBalance { Balance = 1000 });

        var result = await _controller.GetBalances();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var balances = Assert.IsType<Dictionary<string, int>>(okResult.Value);
        Assert.Equal(500, balances["BlackJack"]);
        Assert.Equal(1000, balances["Avalon"]);
    }
}
