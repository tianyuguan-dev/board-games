using BoardGames.Controllers;
using BoardGames.Data;
using BoardGames.Dtos;
using BoardGames.Models;
using BoardGames.Services;
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
}
