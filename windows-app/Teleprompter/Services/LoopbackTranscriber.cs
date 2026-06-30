using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;

namespace Teleprompter.Services;

/// <summary>
/// Captures the INCOMING audio you hear (WASAPI loopback of the default render
/// device) and transcribes it with Whisper.net (GPU-accelerated via the CUDA
/// runtime on the RTX 2080). Emits recognized text for the selectable
/// transcription box (feature 4). The microphone is NOT captured here.
/// </summary>
public sealed class LoopbackTranscriber : IDisposable
{
    private const int TargetSampleRate = 16000;
    private static readonly TimeSpan ChunkInterval = TimeSpan.FromSeconds(5);
    // Below this peak amplitude a chunk is treated as silence and skipped, which
    // avoids Whisper "hallucinating" phantom words during quiet passages.
    private const float SilenceThreshold = 0.01f;

    private readonly string _modelPath;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    private WasapiLoopbackCapture? _capture;
    private WaveFormat? _captureFormat;
    private readonly List<byte> _buffer = new();
    private readonly object _bufferLock = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;

    /// <summary>Raised when a chunk produces text. Marshal to the UI thread in the handler.</summary>
    public event Action<string>? TranscriptionProduced;

    /// <summary>Raised with a human-readable status (errors, GPU/CPU info).</summary>
    public event Action<string>? Status;

    public bool IsRunning { get; private set; }

    public LoopbackTranscriber(string whisperModelPath)
    {
        _modelPath = whisperModelPath;
    }

    public void Start()
    {
        if (IsRunning) return;

        _factory ??= WhisperFactory.FromPath(_modelPath);
        _processor ??= _factory.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        _capture = new WasapiLoopbackCapture(); // default render device
        _captureFormat = _capture.WaveFormat;
        _capture.DataAvailable += OnDataAvailable;

        lock (_bufferLock) _buffer.Clear();

        _cts = new CancellationTokenSource();
        _capture.StartRecording();
        _worker = Task.Run(() => ChunkLoopAsync(_cts.Token));
        IsRunning = true;
        Status?.Invoke($"Listening to: {WasapiDeviceName()}");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_bufferLock)
            _buffer.AddRange(e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());
    }

    private async Task ChunkLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(ChunkInterval, ct);

                byte[] chunk;
                lock (_bufferLock)
                {
                    if (_buffer.Count == 0) continue;
                    chunk = _buffer.ToArray();
                    _buffer.Clear();
                }

                var samples = Resample(chunk, _captureFormat!);
                if (samples.Length < TargetSampleRate / 2) continue;     // < 0.5s, skip
                if (PeakAmplitude(samples) < SilenceThreshold) continue;  // silence, skip

                if (_processor is null) continue;
                await foreach (var segment in _processor.ProcessAsync(samples, ct))
                {
                    var text = segment.Text?.Trim();
                    if (!string.IsNullOrEmpty(text))
                        TranscriptionProduced?.Invoke(text);
                }
            }
        }
        catch (OperationCanceledException) { /* normal stop */ }
        catch (Exception ex)
        {
            Status?.Invoke($"Transcription error: {ex.Message}");
        }
    }

    /// <summary>Converts captured bytes (device format) to 16 kHz mono float samples for Whisper.</summary>
    private static float[] Resample(byte[] bytes, WaveFormat sourceFormat)
    {
        using var ms = new MemoryStream(bytes);
        using var raw = new RawSourceWaveStream(ms, sourceFormat);
        ISampleProvider provider = raw.ToSampleProvider();

        if (provider.WaveFormat.Channels > 1)
            provider = provider.ToMono();
        if (provider.WaveFormat.SampleRate != TargetSampleRate)
            provider = new WdlResamplingSampleProvider(provider, TargetSampleRate);

        var output = new List<float>(bytes.Length / 4);
        var buffer = new float[TargetSampleRate];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
            output.AddRange(buffer.Take(read));
        return output.ToArray();
    }

    private static float PeakAmplitude(float[] samples)
    {
        float peak = 0f;
        foreach (var s in samples)
        {
            var abs = Math.Abs(s);
            if (abs > peak) peak = abs;
        }
        return peak;
    }

    private static string WasapiDeviceName()
    {
        try
        {
            using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.Role.Multimedia);
            return device.FriendlyName;
        }
        catch
        {
            return "default output device";
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        try { _cts?.Cancel(); } catch { /* ignore */ }

        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            try { _capture.StopRecording(); } catch { /* ignore */ }
            _capture.Dispose();
            _capture = null;
        }

        try { _worker?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
        _processor?.Dispose();
        _processor = null;
        _factory?.Dispose();
        _factory = null;
    }
}
