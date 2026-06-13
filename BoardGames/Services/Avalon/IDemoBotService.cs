using BoardGames.Hubs.Avalon;
using BoardGames.Models.Avalon;
using Microsoft.AspNetCore.SignalR;

namespace BoardGames.Services.Avalon;

public interface IDemoBotService
{
    // Called after every phase-changing event in a demo room. Schedules the next bot actions
    // with a small delay so the guest sees state changes in a natural pace.
    Task TriggerNextBotActions(AvalonRoom room, IHubContext<AvalonHub> hubContext);
}
