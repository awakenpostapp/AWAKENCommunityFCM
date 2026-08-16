using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Services.Online;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

public sealed class LoginPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly SessionService _session;
    private readonly AppNavigator _navigator;
    private readonly RememberedLoginService _rememberedLogin;
    private readonly PersistentSessionService _persistentSession;
    private readonly OAuthService _oauth;
    private readonly Entry _usernameEntry;
    private readonly Entry _passwordEntry;
    private readonly ImageButton _passwordVisibilityButton;
    private readonly Label _errorLabel;
    private readonly Button _loginButton;
    private readonly Grid _loginNotice;
    private bool _isLoggingIn;

    public LoginPage(
        AppDatabase database,
        SessionService session,
        AppNavigator navigator,
        RememberedLoginService rememberedLogin,
        PersistentSessionService persistentSession,
        OAuthService oauth)
    {
        _database = database;
        _session = session;
        _navigator = navigator;
        _rememberedLogin = rememberedLogin;
        _persistentSession = persistentSession;
        _oauth = oauth;
        Title = "Đăng nhập";
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = UiKit.Background;

        _usernameEntry = new Entry
        {
            Placeholder = "Username",
            ReturnType = ReturnType.Next,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };
        _passwordEntry = new Entry
        {
            Placeholder = "Password",
            IsPassword = true,
            ReturnType = ReturnType.Go
        };
        _passwordVisibilityButton = new ImageButton
        {
            Source = "password_eye.svg",
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(8),
            WidthRequest = 48,
            HeightRequest = 44,
            MinimumHeightRequest = 44
        };
        _passwordVisibilityButton.Clicked += (_, _) => TogglePasswordVisibility();
        SemanticProperties.SetDescription(_passwordVisibilityButton, "Hiện mật khẩu");

        var passwordField = new Grid
        {
            ColumnSpacing = 4,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        passwordField.Children.Add(_passwordEntry);
        Grid.SetColumn(_passwordVisibilityButton, 1);
        passwordField.Children.Add(_passwordVisibilityButton);

        _errorLabel = UiKit.Caption(string.Empty, UiKit.Danger);
        _errorLabel.IsVisible = false;
        _loginNotice = UiKit.LoadingOverlay("Đang đăng nhập");
        _loginNotice.IsVisible = false;
        _loginButton = UiKit.PrimaryButton("Đăng nhập", async (_, _) => await LoginAsync());
        _passwordEntry.Completed += async (_, _) => await LoginAsync();

        var registerButton = UiKit.SecondaryButton("Tạo tài khoản Sáng lập & Điều hành");
        registerButton.Clicked += async (_, _) =>
            await Navigation.PushAsync(new FounderRegistrationPage(_database));

        var forgotButton = new Button
        {
            Text = "Quên mật khẩu?",
            BackgroundColor = Colors.Transparent,
            TextColor = UiKit.Primary,
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            Padding = new Thickness(6),
            HorizontalOptions = LayoutOptions.End
        };
        forgotButton.Clicked += async (_, _) =>
            await Navigation.PushAsync(new ForgotPasswordPage(
                _database,
                _rememberedLogin,
                resetUsername =>
                {
                    _usernameEntry.Text = resetUsername;
                    _passwordEntry.Text = string.Empty;
                    _errorLabel.Text = string.Empty;
                    _errorLabel.IsVisible = false;
                    _passwordEntry.Focus();
                }));

        var googleLogin = UiKit.SecondaryButton("Đăng nhập với Google");
        googleLogin.Clicked += async (_, _) => await LoginWithGoogleAsync(googleLogin);
        var orLabel = UiKit.Caption("HOẶC");
        orLabel.HorizontalTextAlignment = TextAlignment.Center;
        var form = UiKit.Card(new VerticalStackLayout
        {
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.LabeledField("USERNAME", _usernameEntry),
                UiKit.LabeledField("PASSWORD", passwordField),
                _errorLabel,
                _loginButton,
                registerButton,
                forgotButton,
                orLabel,
                googleLogin,
            }
        });
        form.Margin = new Thickness(0, -34, 0, 0);

        var hero = new Border
        {
            MinimumHeightRequest = 224,
            BackgroundColor = UiKit.PrimaryDark,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = 26
            },
            Padding = new Thickness(20, 24, 20, 58),
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                        new Image
                        {
                            Source = "awaken_community_fcm_logo_ui.png",
                            HeightRequest = 112,
                            WidthRequest = 90,
                            Aspect = Aspect.AspectFit,
                            HorizontalOptions = LayoutOptions.Center
                        },
                    new Label
                    {
                        Text = "AWAKEN Community FCM",
                        FontFamily = "OpenSansSemibold",
                        FontSize = 20,
                        TextColor = Colors.White,
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineBreakMode = LineBreakMode.NoWrap,
                        LineHeight = 1.12
                    },
                    new Label
                    {
                        Text = "Quản lý đội bóng cộng đồng",
                        FontSize = 14,
                        TextColor = UiKit.TealSoft,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };

        var scrollContent = new VerticalStackLayout
        {
            Padding = new Thickness(16, 18, 16, 10),
            Spacing = 12,
            Children =
            {
                hero,
                form
            }
        };
        var scroll = UiKit.KeyboardAwareScroll(scrollContent);

        var footer = new VerticalStackLayout
        {
            Padding = new Thickness(16, 8, 16, 18),
            Spacing = 2,
            BackgroundColor = UiKit.Background,
            Children =
            {
                new Label
                {
                    Text = $"Phiên bản Release: {AppInfo.Current.VersionString}",
                    FontSize = 11,
                    TextColor = UiKit.TextSecondary,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = "Designed by AWAKEN POST Production",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 11,
                    TextColor = UiKit.PrimaryDark,
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };
        var page = new Grid
        {
            RowSpacing = 0,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        page.Children.Add(scroll);
        page.Children.Add(_loginNotice);
        Grid.SetRow(footer, 1);
        page.Children.Add(footer);
        Content = page;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _passwordEntry.IsPassword = true;
        _passwordVisibilityButton.Source = "password_eye.svg";
        SemanticProperties.SetDescription(_passwordVisibilityButton, "Hiện mật khẩu");
    }

    private void TogglePasswordVisibility()
    {
        _passwordEntry.IsPassword = !_passwordEntry.IsPassword;
        _passwordVisibilityButton.Source = _passwordEntry.IsPassword
            ? "password_eye.svg"
            : "password_eye_off.svg";
        SemanticProperties.SetDescription(
            _passwordVisibilityButton,
            _passwordEntry.IsPassword ? "Hiện mật khẩu" : "Ẩn mật khẩu");
    }

    private async Task LoginAsync()
    {
        if (_isLoggingIn)
        {
            return;
        }

        _isLoggingIn = true;
        _errorLabel.IsVisible = false;
        _loginNotice.IsVisible = true;
        _loginButton.IsEnabled = false;
        _loginButton.Text = "Đang đăng nhập";
        try
        {
            var username = (_usernameEntry.Text ?? string.Empty).Trim();
            var password = _passwordEntry.Text ?? string.Empty;
            var result = await _database.AuthenticateAsync(
                username,
                password);
            if (!result.Succeeded || result.User is null)
            {
                _errorLabel.Text = result.Message;
                _errorLabel.IsVisible = true;
                return;
            }

            var profile = await _database.GetProfileAsync(result.User.Id);
            _session.Start(result.User, profile);
            await _persistentSession.SaveAsync(result.User.Id);
            _rememberedLogin.Forget();
            _navigator.ShowMain();
        }
        catch (Exception)
        {
            _errorLabel.Text = "Không thể mở dữ liệu. Vui lòng thử lại.";
            _errorLabel.IsVisible = true;
        }
        finally
        {
            _isLoggingIn = false;
            _loginNotice.IsVisible = false;
            _loginButton.IsEnabled = true;
            _loginButton.Text = "Đăng nhập";
        }
    }

    private async Task LoginWithGoogleAsync(Button source)
    {
        if (_isLoggingIn)
        {
            return;
        }

        _isLoggingIn = true;
        _errorLabel.IsVisible = false;
        _loginNotice.IsVisible = true;
        source.IsEnabled = false;
        try
        {
            // Start the trusted Google OAuth selector immediately from the
            // login screen; there is no intermediate app page.
            var ticket = await _oauth.AuthenticateAsync(ExternalAuthProvider.Google);
            var result = await _database.AuthenticateExternalOAuthAsync(
                ExternalAuthProvider.Google,
                ticket,
                OAuthService.CallbackUri);
            if (!result.Succeeded || result.User is null)
            {
                _errorLabel.Text = result.Message.Contains("Bind", StringComparison.OrdinalIgnoreCase)
                                   || result.Message.Contains("liên kết", StringComparison.OrdinalIgnoreCase)
                    ? "Tài khoản Google của bạn chưa liên kết với tài khoản"
                    : result.Message;
                _errorLabel.IsVisible = true;
                return;
            }

            var profile = await _database.GetProfileAsync(result.User.Id);
            _session.Start(result.User, profile);
            await _persistentSession.SaveAsync(result.User.Id);
            _navigator.ShowMain();
        }
        catch (Exception)
        {
            _errorLabel.Text = "Không thể kết nối Google. Vui lòng thử lại.";
            _errorLabel.IsVisible = true;
        }
        finally
        {
            _isLoggingIn = false;
            _loginNotice.IsVisible = false;
            source.IsEnabled = true;
        }
    }
}

public sealed class FounderRegistrationPage : ContentPage
{
    private readonly AppDatabase _database;

    public FounderRegistrationPage(AppDatabase database)
    {
        _database = database;
        Title = "Tạo tài khoản Sáng lập & Điều hành";
        BackgroundColor = UiKit.Background;

        var username = new Entry { Placeholder = "Username" };
        var fullName = new Entry { Placeholder = "Tên Sáng lập & Điều hành" };
        var email = new Entry { Placeholder = "Email", Keyboard = Keyboard.Email };
        var password = new Entry { Placeholder = "Mật khẩu" };
        var confirm = new Entry { Placeholder = "Nhập lại mật khẩu" };
        var save = UiKit.PrimaryButton("Tạo tài khoản");
        var createNotice = UiKit.LoadingOverlay("Đang tạo tài khoản");

        save.Clicked += async (_, _) =>
        {
            if (!string.Equals(password.Text, confirm.Text, StringComparison.Ordinal))
            {
                await DisplayAlertAsync(
                    "Chưa thể tạo tài khoản",
                    "Hai mật khẩu không trùng nhau.",
                    "Đóng");
                return;
            }

            save.IsEnabled = false;
            createNotice.IsVisible = true;
            try
            {
                await _database.RegisterFounderAsync(
                    username.Text ?? string.Empty,
                    fullName.Text ?? string.Empty,
                    email.Text ?? string.Empty,
                    password.Text ?? string.Empty);
                await DisplayAlertAsync(
                    "Đã gửi yêu cầu",
                    "Account Sáng lập & Điều hành đã được tạo và đang chờ Admin xác nhận thành lập. Sau khi được duyệt, bạn mới có thể đăng nhập.",
                    "OK");
                await Navigation.PopAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể tạo tài khoản", exception.Message, "Đóng");
            }
            finally
            {
                createNotice.IsVisible = false;
                save.IsEnabled = true;
            }
        };

        var content = UiKit.ScrollBody(
            UiKit.Body(
                "Tài khoản này có toàn quyền vận hành đội bóng, lớp học, thành viên, điểm danh và tài chính.",
                UiKit.TextSecondary),
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.LabeledField("USERNAME", username),
                    UiKit.LabeledField("HỌ VÀ TÊN", fullName),
                    UiKit.LabeledField("EMAIL", email),
                    UiKit.LabeledField("MẬT KHẨU", UiKit.PasswordField(password)),
                    UiKit.LabeledField("XÁC NHẬN MẬT KHẨU", UiKit.PasswordField(confirm)),
                    save
                }
            }));
        Content = new Grid
        {
            Children = { content, createNotice }
        };
    }
}

public sealed class ForgotPasswordPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly RememberedLoginService _rememberedLogin;
    private readonly Action<string>? _onPasswordReset;

    public ForgotPasswordPage(
        AppDatabase database,
        RememberedLoginService rememberedLogin,
        Action<string>? onPasswordReset = null)
    {
        _database = database;
        _rememberedLogin = rememberedLogin;
        _onPasswordReset = onPasswordReset;
        Title = "Quên mật khẩu";
        BackgroundColor = UiKit.Background;

        var username = new Entry { Placeholder = "Username" };
        var email = new Entry { Placeholder = "Email", Keyboard = Keyboard.Email };
        var password = new Entry { Placeholder = "Mật khẩu mới" };
        var confirm = new Entry { Placeholder = "Nhập lại mật khẩu" };
        var passwordField = UiKit.PasswordField(password);
        var confirmField = UiKit.PasswordField(confirm);
        var button = UiKit.PrimaryButton("Đặt lại mật khẩu");

        button.Clicked += async (_, _) =>
        {
            if (password.Text != confirm.Text)
            {
                await DisplayAlertAsync("Chưa thể đặt lại", "Hai mật khẩu không trùng nhau.", "Đóng");
                return;
            }

            button.IsEnabled = false;
            try
            {
                var resetUsername = (username.Text ?? string.Empty).Trim();
                await _database.ResetPasswordByEmailAsync(
                    resetUsername,
                    email.Text ?? string.Empty,
                    password.Text ?? string.Empty);
                _rememberedLogin.Forget();
                _onPasswordReset?.Invoke(resetUsername);
                await DisplayAlertAsync(
                    "Đã đặt lại mật khẩu",
                    "Bạn có thể quay lại đăng nhập.",
                    "OK");
                await Navigation.PopAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể đặt lại", exception.Message, "Đóng");
            }
            finally
            {
                button.IsEnabled = true;
            }
        };

        Content = UiKit.ScrollBody(
            UiKit.LargeTitle("Đặt lại mật khẩu"),
            UiKit.Body(
                "Tài khoản cũ lưu trên thiết bị có thể đối chiếu username và email. "
                + "Với tài khoản online, vui lòng liên hệ Admin hoặc Sáng lập & Điều hành để được đặt lại mật khẩu.",
                UiKit.TextSecondary),
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.LabeledField("USERNAME", username),
                    UiKit.LabeledField("EMAIL", email),
                    UiKit.LabeledField("MẬT KHẨU MỚI", passwordField),
                    UiKit.LabeledField("XÁC NHẬN MẬT KHẨU", confirmField),
                    button
                }
            }));
    }
}

public sealed class ForcedPasswordChangePage : ContentPage
{
    public ForcedPasswordChangePage(
        AppDatabase database,
        SessionService session,
        AppNavigator navigator,
        RememberedLoginService rememberedLogin)
    {
        Title = "Đổi mật khẩu";
        NavigationPage.SetHasBackButton(this, false);
        BackgroundColor = UiKit.Background;

        var current = new Entry { Placeholder = "Mật khẩu tạm hiện tại" };
        var password = new Entry { Placeholder = "Mật khẩu mới" };
        var confirm = new Entry { Placeholder = "Nhập lại mật khẩu mới" };
        var currentField = UiKit.PasswordField(current);
        var passwordField = UiKit.PasswordField(password);
        var confirmField = UiKit.PasswordField(confirm);
        var save = UiKit.PrimaryButton("Đổi mật khẩu và tiếp tục");
        save.Clicked += async (_, _) =>
        {
            if (password.Text != confirm.Text)
            {
                await DisplayAlertAsync("Chưa thể đổi", "Hai mật khẩu mới không trùng nhau.", "Đóng");
                return;
            }

            save.IsEnabled = false;
            try
            {
                await database.ChangePasswordAsync(
                    session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                    current.Text ?? string.Empty,
                    password.Text ?? string.Empty);
                rememberedLogin.Forget();
                session.MarkPasswordChanged();
                navigator.ShowMain();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể đổi", exception.Message, "Đóng");
            }
            finally
            {
                save.IsEnabled = true;
            }
        };

        var logout = UiKit.SecondaryButton(
            "Đăng xuất",
            async (_, _) => await navigator.LogoutAsync());
        Content = UiKit.ScrollBody(
            UiKit.Body(
                "Đây là lần đăng nhập đầu tiên bằng mật khẩu tạm. Hãy đổi mật khẩu trước khi tiếp tục.",
                UiKit.TextSecondary),
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.LabeledField("MẬT KHẨU TẠM", currentField),
                    UiKit.LabeledField("MẬT KHẨU MỚI", passwordField),
                    UiKit.LabeledField("XÁC NHẬN", confirmField),
                    save,
                    logout
                }
            }));
    }
}
