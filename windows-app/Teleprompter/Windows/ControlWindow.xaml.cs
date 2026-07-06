using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Teleprompter.Models;
using Teleprompter.Services;

namespace Teleprompter.Windows;

public partial class ControlWindow : Window
{
    private readonly AppState _state = new();
    private readonly ModelManager _models = new();

    private PrompterWindow? _prompter;
    private MicTranscriber? _mic;
    private LoopbackTranscriber? _loopback;

    // Latch so the mic advances the prompter at most once per spoken utterance.
    private bool _micCanAdvance = true;

    private const string SampleScript =
        "Welcome to the teleprompter.\n\n" +
        "Type or paste your script here. The estimated read time updates as you type.\n\n" +
        "Open the prompter to pin this text to the top of your screen, right by your webcam.";

    public ControlWindow()
    {
        InitializeComponent();
        _state.ScriptText = SampleScript;
        DataContext = _state;
        Closing += (_, _) => Cleanup();
    }

    private void SetStatus(string message) => StatusText.Text = message;

    // --- Status severity (added for the design pass) -----------------------
    // The single-arg SetStatus above is left intact: it is handed to
    // Progress<string> as an Action<string> for model-download progress.
    // This overload additionally themes the status bar per severity.
    private enum StatusLevel { Info, Busy, Live, Error }

    private void SetStatus(string message, StatusLevel level)
    {
        StatusText.Text = message;
        ApplyStatusLevel(level);
    }

    private void ApplyStatusLevel(StatusLevel level)
    {
        (string bg, string border, string dot, bool busy) = level switch
        {
            StatusLevel.Busy  => ("Status.Progress.Bg", "Status.Progress.Border", "AccentBrush", true),
            StatusLevel.Live  => ("Status.Live.Bg", "Status.Live.Border", "AccentBrush", false),
            StatusLevel.Error => ("Status.Error.Bg", "Status.Error.Border", "DangerBrush", false),
            _                 => ("Status.Info.Bg", "Status.Info.Border", "TextTertiaryBrush", false),
        };
        StatusBar.Background = (Brush)FindResource(bg);
        StatusBar.BorderBrush = (Brush)FindResource(border);
        StatusDot.Fill = (Brush)FindResource(dot);
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private PrompterWindow EnsurePrompter()
    {
        if (_prompter is null)
        {
            _prompter = new PrompterWindow(_state);
            _prompter.Closed += (_, _) =>
            {
                _prompter = null;
                _state.IsPlaying = false;
                PlayPauseButton.Content = "Play";
            };
            _prompter.Show();
        }
        return _prompter;
    }

    // ---- Playback ---------------------------------------------------------

    private void OnOpenPrompter(object sender, RoutedEventArgs e) => EnsurePrompter();

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        var prompter = EnsurePrompter();
        if (_state.IsPlaying)
        {
            _state.IsPlaying = false;
            PlayPauseButton.Content = "Play";
        }
        else
        {
            _ = prompter; // ensure overlay is open; scroll resumes from current position
            _state.IsPlaying = true;
            PlayPauseButton.Content = "Pause";
        }
    }

    private void OnRestart(object sender, RoutedEventArgs e)
    {
        _state.CurrentSentenceIndex = 0;
        _prompter?.ResetScroll();
    }

    private void OnPrev(object sender, RoutedEventArgs e)
        => _state.CurrentSentenceIndex = Math.Max(0, _state.CurrentSentenceIndex - 1);

    private void OnNext(object sender, RoutedEventArgs e)
        => _state.CurrentSentenceIndex = _state.CurrentSentenceIndex + 1;

    // ---- Auto-advance (mic / outgoing voice) ------------------------------

    private async void OnAutoAdvanceChanged(object sender, RoutedEventArgs e)
    {
        if (_state.AutoAdvanceEnabled)
        {
            try
            {
                SetStatus("Preparing mic follow-mode…", StatusLevel.Busy);
                await _models.EnsureVoskModelAsync(new Progress<string>(SetStatus));

                _mic ??= CreateMic();
                _micCanAdvance = true;
                _mic.Start();
                EnsurePrompter();
                SetStatus("Listening to your mic — read aloud to advance.", StatusLevel.Live);
            }
            catch (Exception ex)
            {
                _state.AutoAdvanceEnabled = false; // revert toggle
                SetStatus($"Could not start follow-mode: {ex.Message}", StatusLevel.Error);
            }
        }
        else
        {
            _mic?.Stop();
            SetStatus("Follow-mode off.", StatusLevel.Info);
        }
    }

    private MicTranscriber CreateMic()
    {
        var mic = new MicTranscriber(_models.VoskModelDirectory);
        mic.TextUpdated += OnMicTextUpdated;
        return mic;
    }

    private void OnMicTextUpdated(string text, bool isFinal)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_state.AutoAdvanceEnabled) return;
            var sentences = _state.Sentences;
            if (sentences.Count == 0) return;

            // End of an utterance: re-open the latch for the next sentence.
            if (isFinal) { _micCanAdvance = true; return; }
            if (!_micCanAdvance) return;

            var idx = _state.CurrentSentenceIndex;
            if (idx >= sentences.Count) return;

            if (SpeechMatcher.IsSentenceComplete(text, sentences[idx]))
            {
                _micCanAdvance = false;
                if (idx < sentences.Count - 1)
                    _state.CurrentSentenceIndex = idx + 1;
            }
        });
    }

    // ---- Incoming-audio transcription (loopback) --------------------------

    private async void OnToggleListen(object sender, RoutedEventArgs e)
    {
        if (_loopback is { IsRunning: true })
        {
            _loopback.Stop();
            ListenButton.Content = "Start listening";
            SetStatus("Stopped listening to incoming audio.", StatusLevel.Info);
            return;
        }

        try
        {
            SetStatus("Preparing transcription model…", StatusLevel.Busy);
            await _models.EnsureWhisperModelAsync(new Progress<string>(SetStatus));

            if (_loopback is null)
            {
                _loopback = new LoopbackTranscriber(_models.WhisperModelPath);
                _loopback.TranscriptionProduced += text =>
                    Dispatcher.BeginInvoke(() => _state.AppendTranscription(text));
                _loopback.Status += msg =>
                    Dispatcher.BeginInvoke(() => SetStatus(msg));
            }

            _loopback.Start();
            ListenButton.Content = "Stop listening";
            SetStatus("Capturing incoming audio.", StatusLevel.Live);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not start transcription: {ex.Message}", StatusLevel.Error);
        }
    }

    private void OnCopyAll(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_state.TranscriptionText))
        {
            Clipboard.SetText(_state.TranscriptionText);
            SetStatus("Transcript copied to clipboard.", StatusLevel.Info);
        }
    }

    private void OnClearTranscript(object sender, RoutedEventArgs e)
        => _state.TranscriptionText = "";

    private void OnTranscriptTextChanged(object sender, TextChangedEventArgs e)
    {
        // Keep the newest text in view; the user can still scroll up and select.
        TranscriptBox.ScrollToEnd();
    }

    // ---- Cleanup ----------------------------------------------------------

    private void Cleanup()
    {
        _mic?.Dispose();
        _loopback?.Dispose();
        _prompter?.Close();
    }
}
