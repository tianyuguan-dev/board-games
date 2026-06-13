using System.Security.Claims;
using BoardGames.Data;
using BoardGames.Dtos;
using BoardGames.Models;
using BoardGames.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IJwtService jwtService,
    IAuthService authService,
    IUserRepository userRepository,
    IGameBalanceRepository balanceRepository,
    AppDbContext dbContext) : ControllerBase
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
        var refreshToken = CreateRefreshToken(user.Id);
        await dbContext.SaveChangesAsync();
        return Ok(new { token, refreshToken = refreshToken.Token, nickname = user.Nickname });
    }

    [HttpPost("guest")]
    public IActionResult GuestLogin()
    {
        // Stateless: no Users row, no GameBalance row, no RefreshToken — guest identity lives in the JWT only.
        // Token is short-lived (4h); guest session is disposable.
        var guestId = Guid.NewGuid().ToString("N");
        var nickname = "Guest_" + guestId.Substring(0, 6);
        var token = jwtService.GenerateGuestJwtToken(guestId, nickname);
        return Ok(new { token, refreshToken = (string?)null, nickname });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequestDto dto)
    {
        var principal = jwtService.GetPrincipalFromExpiredToken(dto.AccessToken);
        if (principal == null)
            return Unauthorized("Invalid access token");

        // Guest tokens have no refresh path; they must re-issue via /auth/guest if expired.
        if (principal.IsGuest())
            return Unauthorized("Guest sessions cannot be refreshed");

        var userId = principal.GetUserIdOrZero();

        var storedToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken
                                       && rt.UserId == userId
                                       && !rt.IsRevoked
                                       && rt.ExpiresAt > DateTime.UtcNow);
        if (storedToken == null)
            return Unauthorized("Invalid refresh token");

        storedToken.IsRevoked = true;

        var user = await userRepository.FindById(userId);
        if (user == null)
            return Unauthorized();

        var newAccessToken = jwtService.GenerateJwtToken(user);
        var newRefreshToken = CreateRefreshToken(user.Id);
        await dbContext.SaveChangesAsync();

        return Ok(new { token = newAccessToken, refreshToken = newRefreshToken.Token });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequestDto dto)
    {
        var token = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == dto.RefreshToken && !rt.IsRevoked);
        if (token != null)
        {
            token.IsRevoked = true;
            await dbContext.SaveChangesAsync();
        }
        return Ok();
    }

    [Authorize]
    [HttpPut("nickname")]
    public async Task<IActionResult> UpdateNickname(UpdateNicknameDto dto)
    {
        if (User.IsGuest()) return Forbid("Not available for guest sessions");
        var userId = User.GetUserIdOrZero();
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
        if (User.IsGuest()) return Forbid("Not available for guest sessions");
        var userId = User.GetUserIdOrZero();
        var success = await authService.ChangePassword(userId, dto.OldPassword, dto.NewPassword);
        if (!success) return BadRequest("Old password is incorrect");
        return Ok(new { message = "Password changed successfully" });
    }

    [Authorize]
    [HttpGet("balances")]
    public async Task<IActionResult> GetBalances()
    {
        if (User.IsGuest()) return Forbid("Not available for guest sessions");
        var userId = User.GetUserIdOrZero();
        var balances = new Dictionary<string, decimal>();
        foreach (var gameType in Enum.GetValues<GameType>())
        {
            var balance = await balanceRepository.GetOrCreate(userId, gameType);
            balances[gameType.ToString()] = balance.Balance;
        }
        return Ok(balances);
    }

    private RefreshToken CreateRefreshToken(int userId)
    {
        var refreshToken = new RefreshToken
        {
            Token = Guid.NewGuid().ToString(),
            UserId = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(15)
        };
        dbContext.RefreshTokens.Add(refreshToken);
        return refreshToken;
    }
}
