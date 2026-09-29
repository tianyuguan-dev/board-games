using System.Reflection;
using BoardGames.Services.Avalon;
using BoardGames.Services.BlackJack;

namespace BoardGames.Tests.Services;

public class RoomManagerStructureTests
{
    // Room managers are singletons used by every connection. A System.Random field there (instance or static) is shared state
    // that is not thread safe (see docs/specs/room-creation-thread-safety.md); ids must come from RoomIds.
    [Theory]
    [InlineData(typeof(BlackJackRoomManager))]
    [InlineData(typeof(AvalonRoomManager))]
    public void RoomManagers_HaveNoRandomField(Type managerType)
    {
        var randomFields = managerType
            .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => typeof(Random).IsAssignableFrom(f.FieldType))
            .Select(f => f.Name);

        Assert.Empty(randomFields);
    }
}
