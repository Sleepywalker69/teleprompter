namespace Teleprompter.Services;

/// <summary>
/// Estimates how long it takes to read a script aloud. Mirrors the formula the
/// original web app used (controller.js: <c>(wordCount / wpm) * 60</c>).
/// </summary>
public static class ReadingEstimator
{
    public static TimeSpan Estimate(int wordCount, int wordsPerMinute)
    {
        if (wordCount <= 0 || wordsPerMinute <= 0)
            return TimeSpan.Zero;

        var seconds = wordCount / (double)wordsPerMinute * 60.0;
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Formats a duration as M:SS (or H:MM:SS when an hour or longer).</summary>
    public static string Format(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        return $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}";
    }
}
