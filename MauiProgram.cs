using Microsoft.Extensions.Logging;
using CommunityFootballClubManager.Platforms.Android;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Services.Online;
using Microsoft.Extensions.DependencyInjection;

namespace CommunityFootballClubManager;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<PasswordService>();
        builder.Services.AddSingleton<RememberedLoginService>();
        builder.Services.AddSingleton<PersistentSessionService>();
        builder.Services.AddSingleton<SessionService>();
        builder.Services.AddSingleton<OAuthService>();
        builder.Services.AddSingleton<OnlineDataState>();
        builder.Services.AddSingleton<AppDatabase>();
        builder.Services.AddSingleton<MediaService>();
        builder.Services.AddSingleton<QrCodeService>();
        builder.Services.AddSingleton<IImageSaveService, AndroidImageSaveService>();
        builder.Services.AddSingleton<IPlayerCardPngService, AndroidPlayerCardPngService>();
        builder.Services.AddSingleton<IReceiptPdfService, AndroidReceiptPdfService>();
        builder.Services.AddSingleton<AppNavigator>();

        // Public endpoint only. Authentication secrets remain in platform
        // SecureStorage and Cloudflare secrets, never in this configuration.
        builder.Services.AddSingleton(new CloudBackendOptions());
        builder.Services.AddSingleton<CloudTokenStore>();
        builder.Services.AddSingleton(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<CloudBackendOptions>();
            var tokens = serviceProvider.GetRequiredService<CloudTokenStore>();
            var httpClient = new HttpClient
            {
                BaseAddress = options.ApiBaseAddress,
                Timeout = options.RequestTimeout
            };
            return new CloudApiClient(httpClient, options, tokens);
        });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
