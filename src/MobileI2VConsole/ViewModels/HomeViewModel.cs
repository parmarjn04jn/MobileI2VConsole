
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using MobileI2VConsole.Models;
using MobileI2VConsole.Services;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace MobileI2VConsole.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly IMediaPickerService _mediaPicker;
    private readonly IFileService _fileService;
    private readonly IModelManager _modelManager;

    private static readonly string[] RequiredModels =
    {
        "vae_encoder.onnx",
        "qwen2_encoder.onnx",
        "mobilei2v_unet.onnx",
        "turbo_vaed.onnx"
    };

    [ObservableProperty]
    private string? selectedImagePath;

    [ObservableProperty]
    private string? promptText;

    [ObservableProperty]
    private ImageSource? imagePreview;

    [ObservableProperty]
    private bool isModelDownloaded;

    [ObservableProperty]
    private bool isDownloading;

    [ObservableProperty]
    private double downloadProgress;

    [ObservableProperty]
    private string downloadStatusText = "Import your four ONNX models";

    [ObservableProperty]
    private string selectedImageRatio = "1:1";

    [ObservableProperty]
    private string selectedVideoRatio = "16:9";

    [ObservableProperty]
    private bool isGenerating;

    public ObservableCollection<PromptTemplate> PromptTemplates { get; } = new();

    public bool IsDownloadNeeded => !IsModelDownloaded;

    public HomeViewModel(
        IMediaPickerService mediaPicker,
        IFileService fileService,
        IModelManager modelManager)
    {
        _mediaPicker = mediaPicker;
        _fileService = fileService;
        _modelManager = modelManager;

        LoadDefaultPromptTemplates();
        _ = CheckAndExtractModelsAsync();
    }

    partial void OnIsModelDownloadedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDownloadNeeded));
    }

    partial void OnSelectedImagePathChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
            ImagePreview = ImageSource.FromFile(value);
        else
            ImagePreview = null;
    }

    private void LoadDefaultPromptTemplates()
    {
        PromptTemplates.Clear();

        PromptTemplates.Add(new PromptTemplate
        {
            Name = "Gentle Motion",
            Prompt = "gentle motion, smooth and subtle movement",
            Icon = "🌊"
        });

        PromptTemplates.Add(new PromptTemplate
        {
            Name = "Cinematic Pan",
            Prompt = "cinematic panning shot, slow camera movement",
            Icon = "🎬"
        });

        PromptTemplates.Add(new PromptTemplate
        {
            Name = "Character Movement",
            Prompt = "natural character movement, blinking and subtle body motion",
            Icon = "🧍"
        });

        PromptTemplates.Add(new PromptTemplate
        {
            Name = "Camera Zoom",
            Prompt = "slow camera zoom in, smooth camera movement",
            Icon = "🔍"
        });
    }

    [RelayCommand]
    private async Task PickImageAsync()
    {
        try
        {
            var result = await _mediaPicker.PickImageAsync();

            if (result != null)
                SelectedImagePath = result.FullPath;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert(
                "Image Error", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private async Task CaptureImageAsync()
    {
        try
        {
            var result = await _mediaPicker.CaptureImageAsync();

            if (result != null)
                SelectedImagePath = result.FullPath;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert(
                "Camera Error", ex.Message, "OK");
        }
    }

    [RelayCommand]
    private void ApplyTemplate(PromptTemplate? template)
    {
        if (template != null)
            PromptText = template.Prompt;
    }

    [RelayCommand]
    private async Task ImportModelsAsync()
    {
        if (IsDownloading)
            return;

        try
        {
            var files = await FilePicker.Default.PickMultipleAsync(
                new PickOptions
                {
                    PickerTitle = "Select JN AI Studio ONNX Models"
                });

            if (files == null)
                return;

            IsDownloading = true;
            DownloadProgress = 0;
            DownloadStatusText = "Importing ONNX models...";

            int imported = 0;

            foreach (var file in files)
            {
                string fileName = Path.GetFileName(file.FileName);

                string? requiredName = RequiredModels.FirstOrDefault(
                    name => string.Equals(
                        name,
                        fileName,
                        StringComparison.OrdinalIgnoreCase));

                if (requiredName == null)
                    continue;

                DownloadStatusText = $"Importing {requiredName}...";

                using Stream stream = await file.OpenReadAsync();

                bool success = await _modelManager.ImportModelAsync(
                    requiredName,
                    stream);

                if (success)
                    imported++;

                DownloadProgress =
                    (double)imported / RequiredModels.Length;
            }

            await RefreshModelStatusAsync();

            DownloadStatusText = IsModelDownloaded
                ? "All four ONNX models imported successfully."
                : $"{imported} model(s) imported. Select the remaining ONNX files.";

            await Shell.Current.DisplayAlert(
                "Model Import",
                DownloadStatusText,
                "OK");
        }
        catch (Exception ex)
        {
            DownloadStatusText = $"Import failed: {ex.Message}";

            await Shell.Current.DisplayAlert(
                "Import Error", ex.Message, "OK");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (IsGenerating)
            return;

        if (string.IsNullOrWhiteSpace(SelectedImagePath) ||
            !File.Exists(SelectedImagePath))
        {
            await Shell.Current.DisplayAlert(
                "No Image",
                "Please select an image first.",
                "OK");
            return;
        }

        await RefreshModelStatusAsync();

        if (!IsModelDownloaded)
        {
            await Shell.Current.DisplayAlert(
                "Models Not Ready",
                "Please import all four ONNX models first.",
                "OK");
            return;
        }

        int width;
        int height;

        switch (SelectedVideoRatio)
        {
            case "9:16":
                width = 720;
                height = 1280;
                break;

            case "16:9":
                width = 1280;
                height = 720;
                break;

            case "1:1":
                width = 1024;
                height = 1024;
                break;

            case "4:5":
                width = 1024;
                height = 1280;
                break;

            default:
                width = 1280;
                height = 720;
                break;
        }

        try
        {
            IsGenerating = true;

            var request = new GenerationRequest
            {
                ImagePath = SelectedImagePath,
                Prompt = PromptText,
                Width = width,
                Height = height,
                FrameCount = 17,
                Fps = 17,
                DiffusionSteps = 2
            };

            string serialized = JsonSerializer.Serialize(request);

            await Shell.Current.GoToAsync(
                "generation",
                new Dictionary<string, object>
                {
                    ["Request"] = serialized
                });
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert(
                "Generation Error", ex.Message, "OK");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    [RelayCommand]
    private async Task CheckModelStatusAsync()
    {
        await RefreshModelStatusAsync();
    }

    [RelayCommand]
    private async Task DownloadModelsAsync()
    {
        // This command checks locally imported files.
        // It does not download models from the internet.
        await RefreshModelStatusAsync();
    }

    private async Task CheckAndExtractModelsAsync()
    {
        await RefreshModelStatusAsync();
    }

    private async Task RefreshModelStatusAsync()
    {
        try
        {
            await _fileService.InitializeAsync();

            await _modelManager.DownloadModelsAsync();

            var statuses = _modelManager.GetAllStatus();

            int readyCount = statuses.Count(s => s.IsDownloaded);

            IsModelDownloaded =
                RequiredModels.All(_modelManager.IsModelDownloaded);

            DownloadProgress =
                (double)readyCount / RequiredModels.Length;

            DownloadStatusText = IsModelDownloaded
                ? "All four ONNX models are ready."
                : $"{readyCount} of 4 ONNX models imported.";
        }
        catch (Exception ex)
        {
            IsModelDownloaded = false;
            DownloadStatusText = $"Model status error: {ex.Message}";
        }
    }
}
