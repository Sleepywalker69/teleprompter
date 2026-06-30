using System.Text.Json;
using NAudio.Wave;
using Vosk;

namespace Teleprompter.Services;

/// <summary>
/// Captures the microphone (the reader's OUTGOING voice) and runs Vosk
/// streaming recognition on it. Used purely to drive teleprompter auto-advance
/// (feature 3); this audio is never shown in the transcription box.
/// </summary>
public sealed class MicTranscriber : IDisposable
{
    private readonly string _modelDirectory;
    private Model? _model;
    private VoskRecognizer? _recognizer;
    private WaveInEvent? _waveIn;
    private readonly object _lock = new();

    /// <summary>
    /// Raised on the UI thread is NOT guaranteed — marshal to the dispatcher in
    /// the handler. <c>text</c> is the cumulative recognition for the current
    /// utterance; <c>isFinal</c> marks the end of an utterance (recognizer reset).
    /// </summary>
    public event Action<string, bool>? TextUpdated;

    public bool IsRunning { get; private set; }

    public MicTranscriber(string voskModelDirectory)
    {
        _modelDirectory = voskModelDirectory;
        Vosk.Vosk.SetLogLevel(-1); // silence Vosk's stdout chatter
    }

    public void Start(int deviceNumber = 0)
    {
        lock (_lock)
        {
            if (IsRunning) return;

            _model ??= new Model(_modelDirectory);
            _recognizer = new VoskRecognizer(_model, 16000.0f);
            _recognizer.SetWords(true);

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            IsRunning = true;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var rec = _recognizer;
        if (rec is null) return;

        if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
        {
            var text = ExtractField(rec.Result(), "text");
            TextUpdated?.Invoke(text, true);
        }
        else
        {
            var partial = ExtractField(rec.PartialResult(), "partial");
            TextUpdated?.Invoke(partial, false);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            if (_waveIn is not null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.StopRecording();
                _waveIn.Dispose();
                _waveIn = null;
            }
            _recognizer?.Dispose();
            _recognizer = null;
            IsRunning = false;
        }
    }

    private static string ExtractField(string json, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var value)
                ? value.GetString() ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    public void Dispose()
    {
        Stop();
        _model?.Dispose();
        _model = null;
    }
}
