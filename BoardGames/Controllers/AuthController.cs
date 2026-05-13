using System.Security.Claims;
using BoardGames.Data;
using BoardGames.Dtos;
using BoardGames.Models;
using BoardGames.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardGames.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IJwtService jwtService, IAuthService authService, IUserRepository userRepository, IGameBalanceRepository balanceRepository) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequestDto registerRequestDto)
    {
        var username = registerRequestDto.Username;
        var password = registerRequestDto.Password;
        if (await authService.UsernameExists(username))
            return BadRequest("Username already exists");
        await authService.CreateUser(username, password);
        return Ok(new { message = "Registered successfully" });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequestDto loginRequestDto)
    {
        var user = await authService.ValidateUser(loginRequestDto.Username, loginRequestDto.Password);
        if (user == null)
        {
            return Unauthorized("Username or password is incorrect");
        }

        user.LastActiveAt = DateTime.UtcNow;
        await userRepository.Update(user);
        var token = jwtService.GenerateJwtToken(user);
        return Ok(new { token, nickname = user.Nickname });
    }

    [Authorize]
    [HttpPut("nickname")]
    public async Task<IActionResult> UpdateNickname(UpdateNicknameDto dto)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await userRepository.FindById(userId);
        if (user == null) return NotFound();
        user.Nickname = dto.Nickname;
        await userRepository.Update(user);
        return Ok(new { nickname = user.Nickname });
    }

    [Authorize]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var success = await authService.ChangePassword(userId, dto.OldPassword, dto.NewPassword);
        if (!success) return BadRequest("Old password is incorrect");
        return Ok(new { message = "Password changed successfully" });
    }

    [Authorize]
    [HttpGet("balances")]
    public async Task<IActionResult> GetBalances()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var balances = new Dictionary<string, int>();
        foreach (var gameType in Enum.GetValues<GameType>())
        {
            var balance = await balanceRepository.GetOrCreate(userId, gameType);
            balances[gameType.ToString()] = balance.Balance;
        }
        return Ok(balances);
    }
}