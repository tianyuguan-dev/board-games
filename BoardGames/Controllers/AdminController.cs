using BoardGames.Data;
using BoardGames.Dtos.Avalon;
using BoardGames.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BoardGames.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdminController(
    AppDbContext db,
    IConfiguration config,
    IAvalonGameHistoryRepository avalonHistoryRepo) : ControllerBase
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

    [HttpGet("users/{id}/avalon-history")]
    public async Task<IActionResult> GetUserAvalonHistory(int id, [FromQuery] int limit = 20, [FromQuery] int offset = 0, [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        if (!IsAuthorized()) return Unauthorized();
        if (limit < 1 || limit > 100) limit = 20;
        if (offset < 0) offset = 0;

        DateTime? fromUtc = null, toUtc = null;
        if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var f))
            fromUtc = f;
        if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var t))
            toUtc = t;

        var games = await avalonHistoryRepo.GetMyRecentGames(id, limit, offset, fromUtc, toUtc);
        var dtos = games.Select(g => AvalonGameSummaryDto.From(g, id)).ToList();
        return Ok(dtos);
    }

    [HttpGet("avalon-games/{gameId}")]
    public async Task<IActionResult> GetAvalonGameDetail(int gameId, [FromQuery] int perspectiveUserId = 0)
    {
        if (!IsAuthorized()) return Unauthorized();

        var game = await avalonHistoryRepo.GetGameDetailById(gameId);
        if (game == null) return NotFound();
        return Ok(AvalonGameDetailDto.From(game, perspectiveUserId));
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
