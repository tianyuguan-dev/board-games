namespace BoardGames.Tests.Services;

public static class ScriptedIds
{
    // An id source for room managers: returns the given ids in order, then repeats the last one. Not thread safe.
    public static Func<int> Of(params int[] ids)
    {
        var i = 0;
        return () => ids[Math.Min(i++, ids.Length - 1)];
    }
}
