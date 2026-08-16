using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Services.Online;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

public sealed class ExternalLoginPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly SessionService _session;
    private readonly AppNavigator _navigator;
    private readonly PersistentSessionService _persistentSession;
    private readonly OAuthService _oauth;
    private readonly ExternalAuthProvider _provider;
    private readonly Label _error;
    private readonly Grid _loginNotice;

    public ExternalLoginPage(
        AppDatabase database,
        SessionService session,
        AppNavigator navigator,
        PersistentSessionService persistentSession,
        OAuthService oauth,
        ExternalAuthProvider provider)
    {
        _database = database;
        _session = session;
        _navigator = navigator;
        _persistentSession = persistentSession;
        _oauth = oauth;
        _provider = provider;
        Title = $"Đăng nhập với {DomainText.ExternalProvider(provider)}";
        BackgroundColor = UiKit.Background;
        _error = UiKit.Caption(string.Empty, UiKit.Danger);
        _error.IsVisible = false;
        _loginNotice = UiKit.LoadingOverlay("Đang đăng nhập");
        _loginNotice.IsVisible = false;
        var login = UiKit.PrimaryButton($"Tiếp tục với {DomainText.ExternalProvider(provider)}");
        login.Clicked += async (_, _) => await LoginAsync(login);

        var content = UiKit.ScrollBody(
            UiKit.LargeTitle($"{DomainText.ExternalProvider(provider)} Account"),
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.Headline("Đăng nhập bằng account đã Bind"),
                    UiKit.Body(
                        $"Ứng dụng sẽ tự lấy account {DomainText.ExternalProvider(provider)} đã đăng nhập trên thiết bị.",
                        UiKit.TextSecondary),
                    _error,
                    login
                }
            }),
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    UiKit.Headline("Xác thực OAuth trực tiếp"),
                    UiKit.Body(
                        "Ứng dụng mở trình duyệt xác thực trực tiếp với Google bằng OAuth Authorization Code + PKCE.",
                        UiKit.TextSecondary)
                }
            }));
        Content = new Grid
        {
            Children = { content, _loginNotice }
        };
    }

    private async Task LoginAsync(Button source)
    {
        source.IsEnabled = false;
        _error.IsVisible = false;
        _loginNotice.IsVisible = true;
        try
        {
            var ticket = await _oauth.AuthenticateAsync(_provider);
            var result = await _database.AuthenticateExternalOAuthAsync(
                _provider,
                ticket,
                OAuthService.CallbackUri);
            if (!result.Succeeded || result.User is null)
            {
                _error.Text = result.Message;
                _error.IsVisible = true;
                return;
            }

            var profile = await _database.GetProfileAsync(result.User.Id);
            _session.Start(result.User, profile);
            await _persistentSession.SaveAsync(result.User.Id);
            _navigator.ShowMain();
        }
        catch (Exception)
        {
            _error.Text = "Không thể đăng nhập. Vui lòng thử lại.";
            _error.IsVisible = true;
        }
        finally
        {
            _loginNotice.IsVisible = false;
            source.IsEnabled = true;
        }
    }

}

public sealed class BindAccountsPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly OAuthService _oauth;

    public BindAccountsPage(
        AppDatabase database,
        SessionService session,
        OAuthService? oauth = null)
        : base(session, "Bind Account")
    {
        _database = database;
        _oauth = oauth ?? new OAuthService(new CloudBackendOptions());
    }

    protected override async Task LoadAsync()
    {
        var links = await _database.GetExternalAccountLinksAsync(CurrentUserId);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.LargeTitle("Bind Account"),
                UiKit.Body(
                    "Kết nối account hiện tại với Google để có thêm lựa chọn đăng nhập.",
                    UiKit.TextSecondary)
            }
        };

        foreach (var provider in new[] { ExternalAuthProvider.Google })
        {
            root.Children.Add(CreateProviderCard(
                provider,
                links.FirstOrDefault(item => item.Provider == provider)));
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private View CreateProviderCard(
        ExternalAuthProvider provider,
        ExternalAccountLink? link)
    {
        var content = new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                UiKit.Headline(DomainText.ExternalProvider(provider)),
                UiKit.StatusBadge(
                    link is null ? "Chưa liên kết" : "Đã liên kết",
                    link is null ? UiKit.Warning : UiKit.Success),
                UiKit.Body(
                    link is null ? "Chưa có account được Bind." : link.Email,
                    UiKit.TextSecondary)
            }
        };

        var bind = UiKit.PrimaryButton(link is null ? "Bind Account" : "Đổi account liên kết");
        bind.Clicked += async (_, _) =>
            await BindProviderAsync(provider, bind);
        content.Children.Add(bind);
        if (link is not null)
        {
            var unbind = UiKit.DestructiveButton("Hủy liên kết");
            unbind.Clicked += async (_, _) =>
            {
                var confirmed = await DisplayAlertAsync(
                    "Hủy liên kết?",
                    $"Bạn sẽ không thể đăng nhập bằng {DomainText.ExternalProvider(provider)} cho đến khi Bind lại.",
                    "Hủy liên kết",
                    "Giữ lại");
                if (!confirmed)
                {
                    return;
                }

                await RunActionAsync(
                    () => _database.UnbindExternalAccountAsync(CurrentUserId, provider),
                    unbind,
                    "Đã hủy liên kết.");
            };
            content.Children.Add(unbind);
        }

        return UiKit.Card(content);
    }

    private async Task BindProviderAsync(
        ExternalAuthProvider provider,
        Button source)
    {
        source.IsEnabled = false;
        try
        {
            var ticket = await _oauth.AuthenticateAsync(provider);
            await _database.BindExternalOAuthAsync(
                CurrentUserId,
                provider,
                ticket,
                OAuthService.CallbackUri);
            await DisplayAlertAsync(
                "Đã liên kết",
                $"Đã xác thực và liên kết account với {DomainText.ExternalProvider(provider)}.",
                "OK");
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Chưa thể liên kết", exception.Message, "Đóng");
        }
        finally
        {
            source.IsEnabled = true;
        }
    }

}
