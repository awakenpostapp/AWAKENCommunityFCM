using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Services.Online;
using CommunityFootballClubManager.Ui;
using CommunityFootballClubManager.Views;
using Microsoft.Extensions.DependencyInjection;
using CommunityFootballClubManager.Platforms.Android;

namespace CommunityFootballClubManager.UiHarness;

public partial class HarnessApp : Application
{
    private readonly IServiceProvider _services;
    public HarnessApp(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        UserAppTheme = AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var menu = new VerticalStackLayout { Padding = 24, Spacing = 14 };
        menu.Children.Add(UiKit.Title("UI QA · Dữ liệu mẫu"));
        menu.Children.Add(UiKit.Caption("App riêng, HTTP bị chặn. Không kết nối production."));
        var window = new Window(new ContentPage { BackgroundColor = UiKit.Background, Content = new ScrollView { Content = menu } });
        foreach (var role in new[] { UserRole.Founder, UserRole.CoFounder, UserRole.Manager, UserRole.Coach, UserRole.Trainee })
            menu.Children.Add(UiKit.PrimaryButton(role.ToString(), async (_, _) => await OpenRole(window, role)));
        menu.Children.Add(UiKit.PrimaryButton("Coach · Điểm danh", async (_, _) => await OpenRole(window, UserRole.Coach, "attendance")));
        menu.Children.Add(UiKit.PrimaryButton("Thành tích trống", async (_, _) => await OpenRole(window, UserRole.Trainee, "empty")));
        menu.Children.Add(UiKit.PrimaryButton("Thành tích lỗi", async (_, _) => await OpenRole(window, UserRole.Trainee, "error")));
        _services.GetRequiredService<AppNavigator>().Attach(window);
        return window;
    }

    private async Task OpenRole(Window window, UserRole role, string mode = "normal")
    {
        var fixture = _services.GetRequiredService<FixtureBackend>();
        fixture.Reset(role, mode);
        var session = _services.GetRequiredService<SessionService>();
        var state = _services.GetRequiredService<OnlineDataState>();
        session.Start(state.CurrentUser!, state.CurrentProfile!);
        await _services.GetRequiredService<CloudTokenStore>().SaveAsync(new CloudTokenResponse
        {
            AccessToken = "ui-fixture-not-a-real-token", RefreshToken = "fixture.refresh",
            SessionId = "fixture", ExpiresIn = 86400
        }, state.CurrentUser!.Id, state.TenantId);
        var tabs = new RoleTabbedPage(_services, session);
        window.Page = tabs;
        if (mode == "attendance")
        {
            var db = _services.GetRequiredService<AppDatabase>();
            var row = (await db.GetClassesAsync(state.CurrentUser.Id)).First();
            tabs.CurrentPage = tabs.Children[2];
            await tabs.CurrentPage.Navigation.PushAsync(new AttendancePage(db, session, row, DateTime.Today));
        }
        else if (role == UserRole.Trainee) tabs.CurrentPage = tabs.Children[3];
    }
}

public static class HarnessProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder().UseMauiApp<HarnessApp>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("NunitoSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("NunitoSans-Bold.ttf", "OpenSansSemibold");
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
        builder.Services.AddSingleton(new CloudBackendOptions { BaseAddress = new Uri("https://ui-fixture.invalid/") });
        builder.Services.AddSingleton<CloudTokenStore>();
        builder.Services.AddSingleton<FixtureBackend>();
        builder.Services.AddSingleton(sp => new CloudApiClient(new HttpClient(sp.GetRequiredService<FixtureBackend>()),
            sp.GetRequiredService<CloudBackendOptions>(), sp.GetRequiredService<CloudTokenStore>()));
        return builder.Build();
    }
}
