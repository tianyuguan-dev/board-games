using System.Collections.Concurrent;
using System.Reflection;
using BoardGames.Models.Avalon;
using BoardGames.Models.BlackJack;
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

    // These collections are scanned by cross-room lookups without the owning room's lock
    // (see docs/specs/cross-room-lookup-thread-safety.md), so they must stay thread safe.
    [Theory]
    [InlineData(typeof(AvalonRoom), nameof(AvalonRoom.Players))]
    [InlineData(typeof(AvalonRoom), nameof(AvalonRoom.PlayerUserIds))]
    [InlineData(typeof(AvalonRoom), nameof(AvalonRoom.DisconnectedPlayers))]
    [InlineData(typeof(BlackJackRoom), nameof(BlackJackRoom.Players))]
    public void RoomMembershipCollections_AreConcurrent(Type roomType, string property)
    {
        var type = roomType.GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!.PropertyType;

        Assert.True(IsConcurrentDictionary(type), $"{roomType.Name}.{property} is {type.Name}");
    }

    private static bool IsConcurrentDictionary(Type? type)
    {
        for (; type != null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>)) return true;
        }
        return false;
    }
}
