
using Microsoft.ML.OnnxRuntime;
using MobileI2VConsole.Models;

namespace MobileI2VConsole.Services;

/// <summary>
/// Manages user-imported ONNX models stored in the app's private persistent directory.
/// This app does not download model files from unverified URLs.
/// </summary>
public interface IModelManager
{
    Task<bool> ImportModelAsync(
        string modelName,
        Stream source,
        CancellationToken ct = default);

    Task<bool> DownloadModelsAsync(
        IProgress<ModelStatus>? progress = null,
        CancellationToken ct = default);

    Task<bool> LoadModelAsync(
        string modelName,
        CancellationToken ct = default);

    Task UnloadModelAsync(string modelName);

    bool IsModelDownloaded(string modelName);

    bool IsModelLoaded(string modelName);

    Task<string> GetModelPathAsync(string modelName);

    IReadOnlyList<ModelStatus> GetAllStatus();

    Task ClearCacheAsync();

    bool TryGetSession(
        string modelName,
        out InferenceSession session);
}
