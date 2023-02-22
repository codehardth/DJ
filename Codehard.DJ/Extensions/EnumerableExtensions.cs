namespace Codehard.DJ.Extensions;

internal static class EnumerableExtensions
{
    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, Random? random = null)
    {
        var buffer = source.ToList();

        if (!buffer.Any())
        {
            yield break;
        }

        random ??= new Random();

        for (var idx = 0; idx < buffer.Count; idx++)
        {
            var tempIdx = random.Next(idx, buffer.Count);

            yield return buffer[tempIdx];

            buffer[tempIdx] = buffer[idx];
        }
    }
}