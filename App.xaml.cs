using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Views;
using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;

namespace CommunityFootballClubManager;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly AppDatabase _database;
    private readonly AppNavigator _navigator;
    private readonly SessionService _session;
    private readonly PersistentSessionService _persistentSession;

    public App(
        AppDatabase database,
        AppNavigator navigator,
        SessionService session,
        PersistentSessionService persistentSession)
    {
        InitializeComponent();
        this.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>()
            .UseWindowSoftInputModeAdjust(WindowSoftInputModeAdjust.Resize);
        UserAppTheme = AppTheme.Light;
        _database = database;
        _navigator = navigator;
        _session = session;
        _persistentSession = persistentSession;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new BootstrapPage(
            _database,
            _navigator,
            _session,
            _persistentSession));
        _navigator.Attach(window);
        return window;
    }
}
