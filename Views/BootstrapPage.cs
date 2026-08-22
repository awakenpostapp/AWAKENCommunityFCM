using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

public sealed class BootstrapPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly AppNavigator _navigator;
    private readonly SessionService _session;
    private readonly PersistentSessionService _persistentSession;
    private bool _started;

    public BootstrapPage(
        AppDatabase database,
        AppNavigator navigator,
        SessionService session,
        PersistentSessionService persistentSession)
    {
        _database = database;
        _navigator = navigator;
        _session = session;
        _persistentSession = persistentSession;
        BackgroundColor = Color.FromArgb("#F8FAFC");
        Content = new Grid
        {
            Padding = new Thickness(24),
            Children =
            {
                new VerticalStackLayout
                {
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Spacing = 12,
                    Children =
                    {
                        new Image
                        {
                            Source = "awaken_community_fcm_logo_ui.png",
                            HeightRequest = 198,
                            WidthRequest = 180,
                            Aspect = Aspect.AspectFit,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new ActivityIndicator
                        {
                            IsRunning = true,
                            Color = UiKit.Primary,
                            Margin = new Thickness(0, 12, 0, 0)
                        }
                    }
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            await _database.InitializeAsync();
            var userId = await _persistentSession.LoadUserIdAsync();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var restored = await _database.RestoreSessionAsync(userId);
                if (restored.Succeeded && restored.User is not null)
                {
                    var profile = await _database.GetProfileAsync(restored.User.Id);
                    _session.Start(restored.User, profile);
                    await _persistentSession.SaveAsync(restored.User.Id);
                    _navigator.ShowMain();
                    return;
                }

                _persistentSession.Clear();
            }

            _navigator.ShowLogin(clearPersistentSession: false);
        }
        catch (Exception exception)
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(18),
                Spacing = 12,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "Không thể khởi tạo dữ liệu",
                        TextColor = UiKit.TextPrimary,
                        FontFamily = "OpenSansSemibold",
                        FontSize = 20,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = AsyncContentPage.UserMessage(exception),
                        TextColor = UiKit.TextSecondary,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            };
        }
    }
}
