using BoardGames.Models;

namespace BoardGames.Tests.Models;

public class MembershipMapTests
{
    [Fact]
    public void Add_DuplicateKey_Throws()
    {
        var map = new MembershipMap<string, int>();
        map.Add("conn-1", 0);

        Assert.Throws<ArgumentException>(() => map.Add("conn-1", 1));
        Assert.Equal(0, map["conn-1"]);
    }

    [Fact]
    public void Remove_ReturnsWhetherRemoved()
    {
        var map = new MembershipMap<string, int> { ["conn-1"] = 0 };

        Assert.True(map.Remove("conn-1"));
        Assert.False(map.Remove("conn-1"));
        Assert.Empty(map);
    }

    [Fact]
    public void ContainsValue_FindsValue()
    {
        var map = new MembershipMap<string, int> { ["conn-1"] = 7, ["conn-2"] = 9 };

        Assert.True(map.ContainsValue(9));
        Assert.False(map.ContainsValue(8));
    }

    [Fact]
    public async Task ConcurrentAddRemoveAndReads_DoNotThrow()
    {
        var map = new MembershipMap<string, int>();
        map.Add("stable", -1);
        var until = DateTime.UtcNow.AddSeconds(1);

        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(() =>
        {
            for (var i = 0; DateTime.UtcNow < until; i++)
            {
                var key = $"w{w}-{i % 50}";
                map[key] = i;
                map.Remove(key);
            }
        }));
        var readers = Enumerable.Range(0, 4).Select(r => Task.Run(() =>
        {
            while (DateTime.UtcNow < until)
            {
                Assert.True(map.ContainsKey("stable"));
                Assert.True(map.ContainsValue(-1));
                _ = map.Keys.FirstOrDefault();
            }
        }));

        await Task.WhenAll(writers.Concat(readers)).WaitAsync(TimeSpan.FromSeconds(10));
    }
}
