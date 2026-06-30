using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Whisper.net.Ggml;

namespace Teleprompter.Services;

/// <summary>
/// Locates (and on first run, downloads) the offline speech models:
///   * Whisper ggml model for incoming/loopback transcription (feature 4)
///   * Vosk small English model for the mic follow-mode (feature 3)
/// Everything lands under %LOCALAPPDATA%\Teleprompter\models so nothing large
/// is committed to the repo.
/// </summary>
public sealed class ModelManager
{
    // Whisper large-v3-turbo: best accuracy with near-real-time latency on the
    // RTX 2080. Swap to a smaller GgmlType if running CPU-only.
    public const GgmlType WhisperModel = GgmlType.LargeV3Turbo;

    private const string VoskModelName = "vosk-model-small-en-us-0.15";
    private const string VoskModelUrl =
        "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip";

    public string ModelsDirectory { get; }

    public ModelManager()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        ModelsDirectory = Path.Combine(localAppData, "Teleprompter", "models");
        Directory.CreateDirectory(ModelsDirectory);
    }

    public string WhisperModelPath =>
        Path.Combine(ModelsDirectory, $"ggml-{WhisperModel.ToString().ToLowerInvariant()}.bin");

    public string VoskModelDirectory => Path.Combine(ModelsDirectory, VoskModelName);

    /// <summary>Downloads the Whisper ggml model if it is not already present.</summary>
    public async Task EnsureWhisperModelAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (File.Exists(WhisperModelPath))
            return;

        progress?.Report($"Downloading Whisper model ({WhisperModel})… this happens once.");
        using var modelStream = await WhisperGgmlDownloader.Default
            .GetGgmlModelAsync(WhisperModel, cancellationToken: ct);

        var tempPath = WhisperModelPath + ".part";
        await using (var fileStream = File.Create(tempPath))
            await modelStream.CopyToAsync(fileStream, ct);
        File.Move(tempPath, WhisperModelPath, overwrite: true);
        progress?.Report("Whisper model ready.");
    }

    /// <summary>Downloads and extracts the Vosk model directory if not present.</summary>
    public async Task EnsureVoskModelAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Vosk needs a directory containing the model files.
        if (Directory.Exists(VoskModelDirectory) &&
            File.Exists(Path.Combine(VoskModelDirectory, "am", "final.mdl")))
            return;

        progress?.Report("Downloading Vosk model… this happens once.");
        var zipPath = Path.Combine(ModelsDirectory, VoskModelName + ".zip");

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        await using (var response = await http.GetStreamAsync(VoskModelUrl, ct))
        await using (var file = File.Create(zipPath))
            await response.CopyToAsync(file, ct);

        progress?.Report("Extracting Vosk model…");
        // The zip already contains a top-level folder named like the model.
        ZipFile.ExtractToDirectory(zipPath, ModelsDirectory, overwriteFiles: true);
        File.Delete(zipPath);
        progress?.Report("Vosk model ready.");
    }
}
