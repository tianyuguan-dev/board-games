using BoardGames.Data;
using BoardGames.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController(AppDbContext db, IConfiguration config) : ControllerBase
{
    private bool IsAuthorized()
    {
        var header = Request.Headers["X-Admin-Token"].FirstOrDefault();
        var password = config["Admin:Password"];
        return !string.IsNullOrEmpty(password) && header == password;
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers([FromQuery] string? search)
    {
        if (!IsAuthorized()) return Unauthorized();

        IQueryable<User> query = db.Users.OrderByDescending(u => u.LastActiveAt);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u => u.Username.Contains(search) || u.Nickname.Contains(search));

        var users = await query.Select(u => new
        {
            u.Id,
            u.Username,
            u.Nickname,
            u.CreatedAt,
            u.LastActiveAt
        }).ToListAsync();

        return Ok(users);
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] AdminResetPasswordDto dto)
    {
        if (!IsAuthorized()) return Unauthorized();

        var user = await db.Users.FindAsync(dto.UserId);
        if (user == null) return NotFound("User not found");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await db.SaveChangesAsync();

        return Ok(new { message = $"Password reset for {user.Username}" });
    }

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(int id)
    {
        if (!IsAuthorized()) return Unauthorized();

        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();

        var balances = await db.GameBalances
            .Where(b => b.UserId == id)
            .Select(b => new { gameType = b.GameType.ToString(), b.Balance })
            .ToListAsync();

        return Ok(new
        {
            user.Id, user.Username, user.Nickname,
            user.CreatedAt, user.LastActiveAt,
            Balances = balances
        });
    }

    [HttpPut("users/{id}/nickname")]
    public async Task<IActionResult> UpdateNickname(int id, [FromBody] AdminUpdateNicknameDto dto)
    {
        if (!IsAuthorized()) return Unauthorized();

        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.Nickname = dto.Nickname;
        await db.SaveChangesAsync();
        return Ok(new { message = $"Nickname updated to {user.Nickname}" });
    }

    [HttpPut("users/{id}/balance")]
    public async Task<IActionResult> UpdateBalance(int id, [FromBody] AdminUpdateBalanceDto dto)
    {
        if (!IsAuthorized()) return Unauthorized();

        var user = await db.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (!Enum.TryParse<GameType>(dto.GameType, out var gameType))
            return BadRequest("Invalid game type");

        var balance = await db.GameBalances
            .FirstOrDefaultAsync(b => b.UserId == id && b.GameType == gameType);
        if (balance == null)
        {
            balance = new GameBalance { UserId = id, GameType = gameType, Balance = dto.Balance };
            db.GameBalances.Add(balance);
        }
        else
        {
            balance.Balance = dto.Balance;
        }
        await db.SaveChangesAsync();
        return Ok(new { message = $"{gameType} balance set to {dto.Balance}" });
    }
}

public class AdminResetPasswordDto
{
    public int UserId { get; set; }
    public string NewPassword { get; set; } = string.Empty;
}

public class AdminUpdateNicknameDto
{
    public string Nickname { get; set; } = string.Empty;
}

public class AdminUpdateBalanceDto
{
    public string GameType { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}
