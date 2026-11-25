
using Microsoft.Maui.Storage;
using Microsoft.ML.OnnxRuntime;
using MobileI2VConsole.Models;
using System.Collections.Concurrent;

namespace MobileI2VConsole.Services;

/// <summary>
/// Manages locally imported ONNX models for JN AI Studio.
/// No automatic model downloads or unverified URLs are used.
/// </summary>
public class ModelManagerService : IModelManager, IDisposable
{
    private readonly IFileService _fileService;

    private readonly ConcurrentDictionary<string, InferenceSession> _sessions = new();

    private readonly ConcurrentDictionary<string, ModelStatus> _status = new();

    private static readonly string[] ModelNames =
    {
        "vae_encoder.onnx",
        "qwen2_encoder.onnx",
        "mobilei2v_unet.onnx",
        "turbo_vaed.onnx"
    };

    private readonly SemaphoreSlim _importLock = new(1, 1);

    public ModelManagerService(IFileService fileService)
    {
        _fileService = fileService;

        foreach (var name in ModelNames)
        {
            _status[name] = new ModelStatus
            {
                ModelName = name,
                DownloadProgress = 0,
                IsDownloaded = false,
                IsLoaded = false
            };
        }
    }

    private static bool IsSupportedModel(string modelName)
    {
        return ModelNames.Contains(
            modelName,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> ImportModelAsync(
        string modelName,
        Stream source,
        CancellationToken ct = default)
    {
        if (!IsSupportedModel(modelName))
            throw new ArgumentException("Unsupported model filename.");

        if (source == null || !source.CanRead)
            throw new ArgumentException("Invalid model file stream.");

        await _importLock.WaitAsync(ct);

        string? tempPath = null;

        try
        {
            await _fileService.InitializeAsync();

            string directory = _fileService.GetModelsDirectory();

            Directory.CreateDirectory(directory);

            string targetPath = Path.Combine(directory, modelName);

            tempPath = targetPath + ".importing";

            await UnloadModelAsync(modelName);

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            using (var output = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                await source.CopyToAsync(output, 81920, ct);
                await output.FlushAsync(ct);
            }

            ct.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(tempPath);

            if (!fileInfo.Exists || fileInfo.Length < 1024)
                throw new InvalidDataException(
                    "Model file is empty or too small.");

            File.Move(tempPath, targetPath, true);

            _status[modelName] = new ModelStatus
            {
                ModelName = modelName,
                DownloadProgress = 1.0,
                IsDownloaded = true,
                IsLoaded = false
            };

            return true;
        }
        catch
        {
            if (tempPath != null && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { }
            }

            throw;
        }
        finally
        {
            _importLock.Release();
        }
    }

    public async Task<bool> DownloadModelsAsync(
        IProgress<ModelStatus>? progress = null,
        CancellationToken ct = default)
    {
        await _fileService.InitializeAsync();

        string directory = _fileService.GetModelsDirectory();

        Directory.CreateDirectory(directory);

        foreach (string name in ModelNames)
        {
            ct.ThrowIfCancellationRequested();

            string path = Path.Combine(directory, name);

            bool exists = File.Exists(path) &&
                          new FileInfo(path).Length >= 1024;

            var status = new ModelStatus
            {
                ModelName = name,
                DownloadProgress = exists ? 1.0 : 0,
                IsDownloaded = exists,
                IsLoaded = IsModelLoaded(name)
            };

            _status[name] = status;
            progress?.Report(status);
        }

        // This method only checks local models.
        // It does not download files from the internet.
        return ModelNames.All(IsModelDownloaded);
    }

    public async Task<bool> LoadModelAsync(
        string modelName,
        CancellationToken ct = default)
    {
        if (!IsSupportedModel(modelName))
            return false;

        if (_sessions.ContainsKey(modelName))
            return true;

        ct.ThrowIfCancellationRequested();

        string modelPath = await GetModelPathAsync(modelName);

        if (!File.Exists(modelPath))
            return false;

        InferenceSession? session = null;

        try
        {
            var options = new SessionOptions();

            try
            {
                options.AppendExecutionProvider_Nnapi(0);
                session = new InferenceSession(modelPath, options);
            }
            catch
            {
                session?.Dispose();
                options.Dispose();

                options = new SessionOptions();
                session = new InferenceSession(modelPath, options);
            }

            // Check model metadata before accepting the session.
            _ = session.InputMetadata;
            _ = session.OutputMetadata;

            if (!_sessions.TryAdd(modelName, session))
            {
                session.Dispose();
                return _sessions.ContainsKey(modelName);
            }

            session = null;

            _status[modelName] = new ModelStatus
            {
                ModelName = modelName,
                DownloadProgress = 1.0,
                IsDownloaded = true,
                IsLoaded = true
            };

            return true;
        }
        catch (Exception ex)
        {
            session?.Dispose();

            _status[modelName] = new ModelStatus
            {
                ModelName = modelName,
                DownloadProgress = File.Exists(modelPath) ? 1.0 : 0,
                IsDownloaded = File.Exists(modelPath),
                IsLoaded = false,
                ErrorMessage = ex.Message
            };

            return false;
        }
    }

    public Task UnloadModelAsync(string modelName)
    {
        if (_sessions.TryRemove(modelName, out var session))
            session.Dispose();

        if (_status.TryGetValue(modelName, out var oldStatus))
        {
            _status[modelName] = oldStatus with
            {
                IsLoaded = false
            };
        }

        return Task.CompletedTask;
    }

    public bool IsModelDownloaded(string modelName)
    {
        if (!IsSupportedModel(modelName))
            return false;

        string path = Path.Combine(
            _fileService.GetModelsDirectory(),
            modelName);

        return File.Exists(path) &&
               new FileInfo(path).Length >= 1024;
    }

    public bool IsModelLoaded(string modelName)
    {
        return _sessions.ContainsKey(modelName);
    }

    public async Task<string> GetModelPathAsync(string modelName)
    {
        if (!IsSupportedModel(modelName))
            throw new ArgumentException("Unsupported model filename.");

        await _fileService.InitializeAsync();

        return Path.Combine(
            _fileService.GetModelsDirectory(),
            modelName);
    }

    // Compatibility method for older callers and tests.
    public string GetModelPath(string modelName)
    {
        if (!IsSupportedModel(modelName))
            throw new ArgumentException("Unsupported model filename.");

        return Path.Combine(
            _fileService.GetModelsDirectory(),
            modelName);
    }

    public IReadOnlyList<ModelStatus> GetAllStatus()
    {
        var result = new List<ModelStatus>();

        foreach (string name in ModelNames)
        {
            bool downloaded = IsModelDownloaded(name);
            bool loaded = IsModelLoaded(name);

            var status = new ModelStatus
            {
                ModelName = name,
                DownloadProgress = downloaded ? 1.0 : 0,
                IsDownloaded = downloaded,
                IsLoaded = loaded,
                ErrorMessage = _status.TryGetValue(name, out var old)
                    ? old.ErrorMessage
                    : null
            };

            _status[name] = status;
            result.Add(status);
        }

        return result.AsReadOnly();
    }

    public async Task ClearCacheAsync()
    {
        foreach (string name in ModelNames)
            await UnloadModelAsync(name);

        string directory = _fileService.GetModelsDirectory();

        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Continue cleaning other files.
                }
            }
        }

        foreach (string name in ModelNames)
        {
            _status[name] = new ModelStatus
            {
                ModelName = name,
                DownloadProgress = 0,
                IsDownloaded = false,
                IsLoaded = false
            };
        }
    }

    public bool TryGetSession(
        string modelName,
        out InferenceSession session)
    {
        if (_sessions.TryGetValue(modelName, out var found))
        {
            session = found;
            return true;
        }

        session = null!;
        return false;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Dispose();

        _sessions.Clear();
        _importLock.Dispose();
    }
}
