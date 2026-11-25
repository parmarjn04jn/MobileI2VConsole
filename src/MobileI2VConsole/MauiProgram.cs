
using CommunityToolkit.Maui;
using MobileI2VConsole.Converters;
using MobileI2VConsole.Services;
using MobileI2VConsole.ViewModels;
using MobileI2VConsole.Views;
using Microsoft.Extensions.Logging;

namespace MobileI2VConsole;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement(false)
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Core services
        builder.Services.AddSingleton<IFileService, FileService>();
        builder.Services.AddSingleton<IModelManager, ModelManagerService>();
        builder.Services.AddSingleton<IMediaPickerService, MediaPickerService>();
        builder.Services.AddTransient<IInferenceOrchestrator, InferenceOrchestrator>();

#if ANDROID
        builder.Services.AddSingleton<IVideoEncoder, Platforms.Android.AndroidVideoEncoder>();
#else
        builder.Services.AddSingleton<IVideoEncoder, Services.StubVideoEncoder>();
#endif

        // ViewModels
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<GenerationViewModel>();
        builder.Services.AddTransient<ResultViewModel>();

        // Pages
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<GenerationPage>();
        builder.Services.AddTransient<ResultPage>();

        // Converters
        builder.Services.AddSingleton<InverseBoolConverter>();
        builder.Services.AddSingleton<NotNullToBoolConverter>();
        builder.Services.AddSingleton<NullToBoolConverter>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
