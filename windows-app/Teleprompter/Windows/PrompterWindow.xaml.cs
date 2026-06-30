using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Teleprompter.Models;

namespace Teleprompter.Windows;

/// <summary>
/// Borderless, always-on-top overlay pinned to the top of the screen so the
/// reader can look at the webcam while reading (feature 2). Supports two modes:
///   * continuous WPM scrolling (auto-advance OFF)
///   * sentence follow-mode showing prev/current/next, current highlighted
///     (auto-advance ON, driven by the mic via AppState.CurrentSentenceIndex)
/// </summary>
public partial class PrompterWindow : Window
{
    private readonly AppState _state;
    private readonly Stopwatch _clock = new();
    private double _lastElapsedSeconds;

    private static readonly Brush DimBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    private static readonly Brush ActiveBrush = Brushes.White;

    public PrompterWindow(AppState state)
    {
        InitializeComponent();
        _state = state;
        _state.PropertyChanged += OnStateChanged;

        Loaded += OnLoaded;
        Closing += OnClosing;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionAtTopOfScreen();
        ApplyFontSize();
        ApplyMirror();
        ApplyClickThrough();
        RebuildText();
        ResetScroll(); // start with text just below the visible area
    }

    private void PositionAtTopOfScreen()
    {
        // Span the full working width at the very top of the primary screen.
        var width = SystemParameters.PrimaryScreenWidth;
        var height = SystemParameters.PrimaryScreenHeight;
        Left = 0;
        Top = 0;
        Width = width;
        Height = height * _state.OverlayHeightFraction;
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(AppState.Sentences):
                case nameof(AppState.CurrentSentenceIndex):
                case nameof(AppState.AutoAdvanceEnabled):
                    RebuildText();
                    break;
                case nameof(AppState.FontSize):
                    ApplyFontSize();
                    break;
                case nameof(AppState.MirrorMode):
                    ApplyMirror();
                    break;
                case nameof(AppState.ClickThrough):
                    ApplyClickThrough();
                    break;
                case nameof(AppState.OverlayHeightFraction):
                    PositionAtTopOfScreen();
                    break;
                case nameof(AppState.IsPlaying):
                    if (!_state.IsPlaying) _clock.Stop();
                    else { _lastElapsedSeconds = _clock.Elapsed.TotalSeconds; _clock.Start(); }
                    break;
            }
        });
    }

    private void ApplyFontSize() => PrompterText.FontSize = _state.FontSize;

    private void ApplyMirror() => MirrorTransform.ScaleX = _state.MirrorMode ? -1 : 1;

    // ---- Continuous WPM scrolling ----------------------------------------

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_state.AutoAdvanceEnabled || !_state.IsPlaying) return;

        var now = _clock.Elapsed.TotalSeconds;
        var delta = now - _lastElapsedSeconds;
        _lastElapsedSeconds = now;

        // Same heuristic as the original web app (pixels/sec = words/sec * 12),
        // scaled by font size so larger text scrolls proportionally.
        var pixelsPerSecond = _state.WordsPerMinute / 60.0 * 12.0 * (_state.FontSize / 48.0);
        ScrollTransform.Y -= pixelsPerSecond * delta;
    }

    public void ResetScroll()
    {
        // Start with the text just below the visible area, scrolling up into view.
        ScrollTransform.Y = Height * 0.8;
        _clock.Restart();
        _lastElapsedSeconds = 0;
    }

    // ---- Sentence rendering / highlighting --------------------------------

    private void RebuildText()
    {
        PrompterText.Inlines.Clear();
        var sentences = _state.Sentences;

        if (sentences.Count == 0)
        {
            PrompterText.VerticalAlignment = VerticalAlignment.Center;
            ScrollTransform.Y = 0;
            PrompterText.Inlines.Add(new Run("Waiting for script…") { Foreground = DimBrush });
            return;
        }

        if (_state.AutoAdvanceEnabled)
        {
            // Follow-mode: center prev / current / next, current highlighted.
            PrompterText.VerticalAlignment = VerticalAlignment.Center;
            ScrollTransform.Y = 0;

            int i = _state.CurrentSentenceIndex;
            if (i > 0)
            {
                PrompterText.Inlines.Add(new Run(sentences[i - 1]) { Foreground = DimBrush });
                PrompterText.Inlines.Add(new LineBreak());
                PrompterText.Inlines.Add(new LineBreak());
            }
            PrompterText.Inlines.Add(new Run(sentences[i])
            {
                Foreground = ActiveBrush,
                FontWeight = FontWeights.SemiBold
            });
            if (i < sentences.Count - 1)
            {
                PrompterText.Inlines.Add(new LineBreak());
                PrompterText.Inlines.Add(new LineBreak());
                PrompterText.Inlines.Add(new Run(sentences[i + 1]) { Foreground = DimBrush });
            }
        }
        else
        {
            // Continuous mode: whole script, scrolled by the render loop.
            PrompterText.VerticalAlignment = VerticalAlignment.Top;
            for (int s = 0; s < sentences.Count; s++)
            {
                PrompterText.Inlines.Add(new Run(sentences[s] + " ")
                {
                    Foreground = s == _state.CurrentSentenceIndex ? ActiveBrush : DimBrush
                });
            }
        }
    }

    // ---- Click-through (WS_EX_TRANSPARENT) --------------------------------

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x20;
    private const long WS_EX_LAYERED = 0x80000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private void ApplyClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if (_state.ClickThrough)
            exStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        else
            exStyle &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _state.PropertyChanged -= OnStateChanged;
    }
}
