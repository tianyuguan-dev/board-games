using System.Collections.Concurrent;

namespace BoardGames.Services.BlackJack;

public class BlackJackGuestSession : IBlackJackGuestSession
{
    public const decimal StartingBalance = 100m;
    private readonly ConcurrentDictionary<string, decimal> _balances = new();

    public decimal GetOrInit(string connectionId) =>
        _balances.GetOrAdd(connectionId, StartingBalance);

    public decimal ApplyDelta(string connectionId, decimal delta) =>
        _balances.AddOrUpdate(connectionId, StartingBalance + delta, (_, current) => current + delta);

    public void Reset(string connectionId) =>
        _balances[connectionId] = StartingBalance;

    public void Remove(string connectionId) =>
        _balances.TryRemove(connectionId, out _);
}
