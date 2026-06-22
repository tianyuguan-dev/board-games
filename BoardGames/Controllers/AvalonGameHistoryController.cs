using BoardGames.Data;
using BoardGames.Dtos.Avalon;
using BoardGames.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardGames.Controllers;

[ApiController]
[Authorize]
[Route("api/avalon/games")]
public class AvalonGameHistoryController(IAvalonGameHistoryRepository repo) : ControllerBase
{
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecent(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        if (limit < 1 || limit > 100) limit = 20;
        if (offset < 0) offset = 0;

        if (User.IsGuest()) return Ok(new List<AvalonGameSummaryDto>());

        DateTime? fromUtc = null, toUtc = null;
        if (!string.IsNullOrEmpty(from) && DateTime.TryParse(from, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var f))
            fromUtc = f;
        if (!string.IsNullOrEmpty(to) && DateTime.TryParse(to, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var t))
            toUtc = t;

        var userId = User.GetUserIdOrZero();
        var games = await repo.GetMyRecentGames(userId, limit, offset, fromUtc, toUtc);
        var dtos = games.Select(g => AvalonGameSummaryDto.From(g, userId)).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDetail(int id)
    {
        if (User.IsGuest()) return NotFound();

        var userId = User.GetUserIdOrZero();
        var game = await repo.GetGameDetail(id, userId);
        if (game == null)
            return NotFound();   // Could be 403 if game exists but user did not participate; we return 404 to avoid leaking game existence
        return Ok(AvalonGameDetailDto.From(game, userId));
    }
}
