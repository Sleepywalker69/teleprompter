using System.Text.RegularExpressions;

namespace Teleprompter.Services;

/// <summary>
/// Splits a raw script into an ordered list of sentences. Used both for the
/// reading-time estimate (feature 1) and for the auto-advance follow-mode
/// (feature 3), where each sentence is a discrete advance step.
/// </summary>
public static partial class SentenceSplitter
{
    // Split after . ! ? (and the ellipsis ...) when followed by whitespace.
    // Keeps the terminating punctuation attached to the sentence.
    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.Compiled)]
    private static partial Regex SentenceBoundary();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex Whitespace();

    public static IReadOnlyList<string> Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var result = new List<string>();
        // Split on blank-line paragraph breaks first so a missing terminal
        // period (common in scripts) still produces separate sentences.
        foreach (var paragraph in text.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var raw in SentenceBoundary().Split(paragraph))
            {
                var sentence = raw.Trim();
                if (sentence.Length > 0)
                    result.Add(sentence);
            }
        }
        return result;
    }

    public static int CountWords(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return Whitespace().Split(text.Trim()).Count(w => w.Length > 0);
    }
}
