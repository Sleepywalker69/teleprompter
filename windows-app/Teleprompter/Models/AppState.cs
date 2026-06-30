using Teleprompter.Services;

namespace Teleprompter.Models;

/// <summary>
/// Single shared state object bound to both the ControlWindow and the
/// PrompterWindow. Replaces the web app's controller→display WebSocket sync
/// with in-process data binding.
/// </summary>
public sealed class AppState : ObservableObject
{
    private string _scriptText = "";
    private int _wordsPerMinute = 150;
    private double _fontSize = 48;
    private int _currentSentenceIndex = 0;
    private bool _isPlaying;
    private bool _autoAdvanceEnabled;
    private bool _mirrorMode;
    private bool _clickThrough;
    private double _overlayHeightFraction = 0.30;
    private string _transcriptionText = "";

    private IReadOnlyList<string> _sentences = Array.Empty<string>();
    private int _wordCount;

    /// <summary>Raw script text. Re-derives sentences, word count and estimate on change.</summary>
    public string ScriptText
    {
        get => _scriptText;
        set
        {
            if (SetField(ref _scriptText, value))
                RecomputeScript();
        }
    }

    /// <summary>Target reading speed in words per minute (used when auto-advance is OFF).</summary>
    public int WordsPerMinute
    {
        get => _wordsPerMinute;
        set
        {
            if (SetField(ref _wordsPerMinute, value))
                OnPropertyChanged(nameof(EstimatedDurationText));
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set => SetField(ref _fontSize, value);
    }

    public IReadOnlyList<string> Sentences
    {
        get => _sentences;
        private set => SetField(ref _sentences, value);
    }

    public int WordCount
    {
        get => _wordCount;
        private set
        {
            if (SetField(ref _wordCount, value))
                OnPropertyChanged(nameof(EstimatedDurationText));
        }
    }

    /// <summary>Which sentence the prompter is currently on (drives highlight + auto-advance).</summary>
    public int CurrentSentenceIndex
    {
        get => _currentSentenceIndex;
        set => SetField(ref _currentSentenceIndex, Math.Clamp(value, 0, Math.Max(0, _sentences.Count - 1)));
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetField(ref _isPlaying, value);
    }

    /// <summary>When true, mic/outgoing speech advances the prompter instead of fixed WPM scrolling.</summary>
    public bool AutoAdvanceEnabled
    {
        get => _autoAdvanceEnabled;
        set => SetField(ref _autoAdvanceEnabled, value);
    }

    public bool MirrorMode
    {
        get => _mirrorMode;
        set => SetField(ref _mirrorMode, value);
    }

    public bool ClickThrough
    {
        get => _clickThrough;
        set => SetField(ref _clickThrough, value);
    }

    /// <summary>Fraction of screen height the overlay occupies at the top (0.15–0.6).</summary>
    public double OverlayHeightFraction
    {
        get => _overlayHeightFraction;
        set => SetField(ref _overlayHeightFraction, Math.Clamp(value, 0.15, 0.6));
    }

    /// <summary>Accumulated transcript of incoming/loopback audio (feature 4). Append-only.</summary>
    public string TranscriptionText
    {
        get => _transcriptionText;
        set => SetField(ref _transcriptionText, value);
    }

    public string EstimatedDurationText =>
        ReadingEstimator.Format(ReadingEstimator.Estimate(WordCount, WordsPerMinute));

    private void RecomputeScript()
    {
        Sentences = SentenceSplitter.Split(_scriptText);
        WordCount = SentenceSplitter.CountWords(_scriptText);
        if (_currentSentenceIndex > _sentences.Count - 1)
            CurrentSentenceIndex = 0;
        OnPropertyChanged(nameof(Sentences));
    }

    public void AppendTranscription(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var separator = string.IsNullOrEmpty(_transcriptionText) ? "" : " ";
        TranscriptionText = _transcriptionText + separator + text.Trim();
    }
}
