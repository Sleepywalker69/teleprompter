using System.Text;
using System.Text.RegularExpressions;

namespace Teleprompter.Services;

/// <summary>
/// Decides whether what the reader has spoken (from the mic/outgoing stream)
/// has reached the end of the current script sentence, so the prompter can
/// auto-advance (feature 3). Tolerant of misrecognitions, filler words, and
/// minor word differences — accuracy demands are low because we already know
/// the expected text.
/// </summary>
public static partial class SpeechMatcher
{
    [GeneratedRegex(@"[^\p{L}\p{Nd}\s]", RegexOptions.Compiled)]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex Whitespace();

    private static string[] Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var cleaned = Punctuation().Replace(text.ToLowerInvariant(), " ");
        return Whitespace().Split(cleaned.Trim()).Where(w => w.Length > 0).ToArray();
    }

    /// <summary>
    /// True once every word of <paramref name="expectedSentence"/> has been
    /// matched, in order, somewhere within <paramref name="spokenText"/>.
    /// </summary>
    public static bool IsSentenceComplete(string? spokenText, string? expectedSentence)
    {
        var expected = Normalize(expectedSentence);
        if (expected.Length == 0) return false;

        var spoken = Normalize(spokenText);
        if (spoken.Length == 0) return false;

        // Greedy in-order match: advance through the expected words, scanning
        // the spoken words and accepting fuzzy matches. Extra spoken words are
        // skipped; the sentence is complete when we consume the last expected word.
        int e = 0;
        for (int s = 0; s < spoken.Length && e < expected.Length; s++)
        {
            if (WordsMatch(spoken[s], expected[e]))
                e++;
        }
        return e == expected.Length;
    }

    private static bool WordsMatch(string a, string b)
    {
        if (a == b) return true;
        // Numbers / very short words must match exactly to avoid false advances.
        if (a.Length < 4 || b.Length < 4) return false;
        // Allow a small edit distance for longer words (plurals, ASR slips).
        return LevenshteinAtMost(a, b, 1);
    }

    private static bool LevenshteinAtMost(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return false;

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            int rowMin = cur[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
                rowMin = Math.Min(rowMin, cur[j]);
            }
            if (rowMin > max) return false; // early-out: can't recover under the cap
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length] <= max;
    }
}
