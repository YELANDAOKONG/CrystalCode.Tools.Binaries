using System.Text.RegularExpressions;

using CrystalCode.Tools.Binaries.Reading;

namespace CrystalCode.Tools.Binaries.Tests.Reading;

public sealed class DifferenceRangesTests
{
    [Fact]
    public async Task MatchesAnAlignedReferenceAcrossWindowsAndUnequalLengths()
    {
        var random = new Random(42);
        for (var sample = 0; sample < 100; sample++)
        {
            var firstBytes = new byte[random.Next(0, 100)];
            var secondBytes = new byte[random.Next(0, 100)];
            random.NextBytes(firstBytes);
            random.NextBytes(secondBytes);
            for (var index = 0; index < Math.Min(firstBytes.Length, secondBytes.Length); index++)
            {
                if (random.Next(2) == 0)
                {
                    secondBytes[index] = firstBytes[index];
                }
            }

            var size = Math.Max(firstBytes.Length, secondBytes.Length);
            var offset = random.Next(size + 1);
            var length = random.Next(1, 101);
            var end = Math.Min(size, offset + length);
            var expected = new List<string>();
            var start = -1;
            for (var position = offset; position <= end; position++)
            {
                var different = position < end && (position >= firstBytes.Length || position >= secondBytes.Length
                    || firstBytes[position] != secondBytes[position]);
                if (different && start < 0)
                {
                    start = position;
                }
                else if (!different && start >= 0)
                {
                    expected.Add($"offset {start}: {position - start} differing bytes");
                    start = -1;
                }
            }

            using var first = new MemoryStream(firstBytes);
            using var second = new MemoryStream(secondBytes);
            var result = await DifferenceRanges.ReadAsync(first, second, offset, length, 100, default);
            var actual = result.Split('\n').Where(line => line.StartsWith("offset ", StringComparison.Ordinal)).ToArray();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public async Task PaginationVisitsEveryRangeExactlyOnce()
    {
        using var first = new MemoryStream(new byte[100]);
        var bytes = new byte[105];
        for (var index = 1; index < 100; index += 2)
        {
            bytes[index] = 1;
        }

        using var second = new MemoryStream(bytes);
        var offsets = new List<long>();
        long offset = 0;
        while (true)
        {
            var result = await DifferenceRanges.ReadAsync(first, second, offset, null, 3, default);
            offsets.AddRange(Regex.Matches(result, @"(?m)^offset (\d+):").Select(match => long.Parse(match.Groups[1].Value)));
            var next = Regex.Match(result, @"continue with offset (\d+)");
            if (!next.Success)
            {
                break;
            }

            var nextOffset = long.Parse(next.Groups[1].Value);
            Assert.True(nextOffset > offset);
            offset = nextOffset;
        }

        Assert.Equal(Enumerable.Range(0, 50).Select(index => 2L * index + 1), offsets);
    }
}
