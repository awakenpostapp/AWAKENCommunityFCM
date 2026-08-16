using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

public sealed class AdminManagementPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly SessionService _session;
    private readonly AppNavigator _navigator;
    private bool _loading;

    public AdminManagementPage(
        AppDatabase database,
        SessionService session,
        AppNavigator navigator)
    {
        _database = database;
        _session = session;
        _navigator = navigator;
        Title = "Quản trị hệ thống";
        BackgroundColor = UiKit.Background;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loading)
        {
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _loading = true;
        try
        {
            var adminId = _session.CurrentUser?.Id
                          ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ.");
            var founders = await _database.GetFounderAccountsAsync(adminId);
            var root = new VerticalStackLayout
            {
                Padding = UiKit.PagePadding,
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.LargeTitle("Quản trị hệ thống"),
                    UiKit.StatusBadge("Admin · Quản lý account Sáng lập & Điều hành", UiKit.Primary),
                    UiKit.Body(
                        "Admin không có quyền vận hành đội bóng. Các chức năng lớp học, thành viên, điểm danh và tài chính chỉ thuộc account Sáng lập & Điều hành.",
                        UiKit.TextSecondary)
                }
            };

            var password = UiKit.SecondaryButton("Đổi mật khẩu Admin");
            password.Clicked += async (_, _) =>
                await Navigation.PushAsync(new AdminPasswordPage(_database, _session));
            root.Children.Add(password);

            var create = UiKit.PrimaryButton("Tạo account Sáng lập & Điều hành");
            create.Clicked += async (_, _) =>
                await Navigation.PushAsync(new AdminFounderEditorPage(_database, _session));
            root.Children.Add(create);

            AddFounderSummary(
                root,
                "Đang chờ đợi xác nhận",
                founders.Where(IsPendingFounder).ToList(),
                "Không có account đang chờ",
                "Các account Founder mới đăng ký sẽ xuất hiện ở đây.");
            AddFounderSummary(
                root,
                "Đang bị vô hiệu hóa",
                founders.Where(IsDisabledFounder).ToList(),
                "Không có account bị vô hiệu hóa",
                "Account ở nhóm này không thể đăng nhập.");
            AddFounderSummary(
                root,
                "Đang hoạt động",
                founders.Where(IsApprovedFounder).ToList(),
                "Chưa có account đang hoạt động",
                "Xác nhận một Founder để bắt đầu vận hành đội bóng.");

            var logoutNotice = UiKit.LoadingOverlay("Đang đăng xuất");
            logoutNotice.IsVisible = false;
            var logout = UiKit.DestructiveButton("Đăng xuất");
            logout.Clicked += async (_, _) =>
            {
                var confirmed = await DisplayAlertAsync(
                    "Đăng xuất?",
                    "Bạn sẽ cần đăng nhập lại để mở khu vực quản trị.",
                    "Đăng xuất",
                    "Hủy");
                if (confirmed)
                {
                    logout.IsEnabled = false;
                    logoutNotice.IsVisible = true;
                    await _navigator.LogoutAsync();
                }
            };
            root.Children.Add(logout);
            var content = UiKit.KeyboardAwareScroll(root);
            Content = new Grid
            {
                Children = { content, logoutNotice }
            };
        }
        catch (Exception exception)
        {
            Content = UiKit.ScrollBody(
                UiKit.EmptyState("Không thể tải khu vực quản trị", exception.Message));
        }
        finally
        {
            _loading = false;
        }
    }

    private static bool IsPendingFounder(MemberRow founder) =>
        string.Equals(founder.FounderApprovalStatus, "pending", StringComparison.OrdinalIgnoreCase)
        || (founder.FounderApprovalStatus is null && !founder.Account.IsActive);

    private static bool IsDisabledFounder(MemberRow founder) =>
        string.Equals(founder.FounderApprovalStatus, "disabled", StringComparison.OrdinalIgnoreCase);

    private static bool IsApprovedFounder(MemberRow founder) =>
        !IsPendingFounder(founder) && !IsDisabledFounder(founder);

    private void AddFounderSummary(
        VerticalStackLayout root,
        string title,
        IReadOnlyList<MemberRow> founders,
        string emptyTitle,
        string emptyMessage)
    {
        var summary = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                UiKit.Title($"{title} ({founders.Count})"),
                UiKit.Body(
                    founders.Count == 0 ? emptyMessage : $"Có {founders.Count} account trong nhóm này.",
                    UiKit.TextSecondary)
            }
        };
        var filter = title.Contains("chờ", StringComparison.OrdinalIgnoreCase)
            ? AdminFounderFilter.Pending
            : title.Contains("vô hiệu", StringComparison.OrdinalIgnoreCase)
                ? AdminFounderFilter.Disabled
                : AdminFounderFilter.Active;
        var open = UiKit.SecondaryButton("Xem chi tiết");
        open.Clicked += async (_, _) =>
            await Navigation.PushAsync(new AdminFounderListPage(
                _database, _session, title, filter, emptyTitle, emptyMessage));
        summary.Children.Add(open);
        root.Children.Add(UiKit.Card(summary));
    }

    private View CreateFounderCard(MemberRow founder)
    {
        var pending = IsPendingFounder(founder);
        var disabled = IsDisabledFounder(founder);
        var statusText = pending
            ? "Đang chờ xác nhận"
            : disabled
                ? "Đang bị vô hiệu hóa"
                : "Đang hoạt động";
        var statusColor = pending
            ? UiKit.Warning
            : disabled
                ? UiKit.Danger
                : UiKit.Success;
        var details = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                UiKit.Headline(founder.DisplayName),
                UiKit.Caption($"@{founder.Account.Username}"),
                UiKit.Body(
                    string.IsNullOrWhiteSpace(founder.Profile.Email)
                        ? "Email: Chưa cập nhật"
                        : $"Email: {founder.Profile.Email}",
                    UiKit.TextSecondary),
                UiKit.StatusBadge(
                    statusText,
                    statusColor)
            }
        };
        var approve = UiKit.PrimaryButton("Xác nhận thành lập");
        approve.IsVisible = pending;
        approve.Clicked += async (_, _) =>
        {
            approve.IsEnabled = false;
            try
            {
                await _database.SetFounderActiveByAdminAsync(
                    _session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ."),
                    founder.Account.Id,
                    true);
                await ReloadAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể xác nhận", exception.Message, "Đóng");
                approve.IsEnabled = true;
            }
        };
        var reset = UiKit.SecondaryButton("Đổi mật khẩu");
        reset.Clicked += async (_, _) =>
            await Navigation.PushAsync(new AdminFounderPasswordPage(
                _database,
                _session,
                founder.Account.Id,
                founder.DisplayName));
        reset.IsEnabled = !pending;

        var toggle = disabled
            ? UiKit.SecondaryButton("Kích hoạt lại tài khoản")
            : UiKit.DestructiveButton("Vô hiệu hóa tài khoản");
        toggle.IsVisible = !pending;
        toggle.Clicked += async (_, _) =>
        {
            var activating = disabled;
            var confirmed = await DisplayAlertAsync(
                activating ? "Kích hoạt lại tài khoản?" : "Vô hiệu hóa tài khoản?",
                activating
                    ? $"Account @{founder.Account.Username} sẽ được phép đăng nhập lại."
                    : $"Account @{founder.Account.Username} sẽ không thể đăng nhập cho đến khi được kích hoạt lại.",
                activating ? "Kích hoạt lại" : "Vô hiệu hóa",
                "Hủy");
            if (!confirmed)
            {
                return;
            }

            toggle.IsEnabled = false;
            try
            {
                await _database.SetFounderActiveByAdminAsync(
                    _session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ."),
                    founder.Account.Id,
                    activating);
                await ReloadAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể cập nhật trạng thái", exception.Message, "Đóng");
                toggle.IsEnabled = true;
            }
        };

        var delete = UiKit.DestructiveButton("Xóa account");
        delete.Clicked += async (_, _) =>
        {
            var confirmed = await DisplayAlertAsync(
                "Xóa account Founder?",
                $"Xóa vĩnh viễn account @{founder.Account.Username} và toàn bộ dữ liệu đội bóng đã tạo? Thao tác này không thể hoàn tác.",
                "Xóa",
                "Hủy");
            if (!confirmed)
            {
                return;
            }

            delete.IsEnabled = false;
            try
            {
                await _database.DeleteFounderAccountAsync(
                    _session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ."),
                    founder.Account.Id);
                await ReloadAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể xóa account", exception.Message, "Đóng");
                delete.IsEnabled = true;
            }
        };
        return UiKit.Card(new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                details,
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children = { approve, reset, toggle, delete }
                }
            }
        });
    }
}

public enum AdminFounderFilter
{
    Pending,
    Disabled,
    Active
}

public sealed class AdminFounderListPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly SessionService _session;
    private readonly string _sectionTitle;
    private readonly AdminFounderFilter _filter;
    private readonly string _emptyTitle;
    private readonly string _emptyMessage;
    private bool _loading;

    public AdminFounderListPage(
        AppDatabase database,
        SessionService session,
        string sectionTitle,
        AdminFounderFilter filter,
        string emptyTitle,
        string emptyMessage)
    {
        _database = database;
        _session = session;
        _sectionTitle = sectionTitle;
        _filter = filter;
        _emptyTitle = emptyTitle;
        _emptyMessage = emptyMessage;
        Title = sectionTitle;
        BackgroundColor = UiKit.Background;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loading) return;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _loading = true;
        try
        {
            var adminId = _session.CurrentUser?.Id
                          ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ.");
            var all = await _database.GetFounderAccountsAsync(adminId);
            var founders = all.Where(MatchesFilter).ToList();
            var root = new VerticalStackLayout
            {
                Padding = UiKit.PagePadding,
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.LargeTitle($"{_sectionTitle} ({founders.Count})"),
                    UiKit.Body("Bấm vào các nút thao tác để quản lý account.", UiKit.TextSecondary)
                }
            };
            if (founders.Count == 0)
            {
                root.Children.Add(UiKit.EmptyState(_emptyTitle, _emptyMessage));
            }
            else
            {
                foreach (var founder in founders)
                {
                    root.Children.Add(CreateFounderCard(founder));
                }
            }

            Content = UiKit.KeyboardAwareScroll(root);
        }
        catch (Exception exception)
        {
            Content = UiKit.ScrollBody(
                UiKit.EmptyState("Không thể tải danh sách Founder", exception.Message));
        }
        finally
        {
            _loading = false;
        }
    }

    private bool MatchesFilter(MemberRow founder) => _filter switch
    {
        AdminFounderFilter.Pending => IsPendingFounder(founder),
        AdminFounderFilter.Disabled => IsDisabledFounder(founder),
        _ => IsApprovedFounder(founder)
    };

    private static bool IsPendingFounder(MemberRow founder) =>
        string.Equals(founder.FounderApprovalStatus, "pending", StringComparison.OrdinalIgnoreCase)
        || (founder.FounderApprovalStatus is null && !founder.Account.IsActive);

    private static bool IsDisabledFounder(MemberRow founder) =>
        string.Equals(founder.FounderApprovalStatus, "disabled", StringComparison.OrdinalIgnoreCase);

    private static bool IsApprovedFounder(MemberRow founder) =>
        !IsPendingFounder(founder) && !IsDisabledFounder(founder);

    private View CreateFounderCard(MemberRow founder)
    {
        var pending = IsPendingFounder(founder);
        var disabled = IsDisabledFounder(founder);
        var statusText = pending
            ? "Đang chờ xác nhận"
            : disabled
                ? "Đang bị vô hiệu hóa"
                : "Đang hoạt động";
        var statusColor = pending ? UiKit.Warning : disabled ? UiKit.Danger : UiKit.Success;
        var teamName = string.IsNullOrWhiteSpace(founder.TeamName) ? "Chưa cập nhật" : founder.TeamName;
        var details = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                UiKit.Headline(founder.DisplayName),
                UiKit.Caption($"@{founder.Account.Username}"),
                UiKit.Body($"Tên đội: {teamName}", UiKit.TextSecondary),
                UiKit.Body(
                    string.IsNullOrWhiteSpace(founder.Profile.Email)
                        ? "Email: Chưa cập nhật"
                        : $"Email: {founder.Profile.Email}",
                    UiKit.TextSecondary),
                UiKit.StatusBadge(statusText, statusColor)
            }
        };

        var actions = new VerticalStackLayout { Spacing = 8 };
        if (pending)
        {
            var approve = UiKit.PrimaryButton("Xác nhận thành lập");
            approve.Clicked += async (_, _) =>
            {
                approve.IsEnabled = false;
                try
                {
                    await _database.SetFounderActiveByAdminAsync(
                        _session.CurrentUser?.Id
                        ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ."),
                        founder.Account.Id,
                        true);
                    await ReloadAsync();
                }
                catch (Exception exception)
                {
                    await DisplayAlertAsync("Chưa thể xác nhận", exception.Message, "Đóng");
                    approve.IsEnabled = true;
                }
            };
            actions.Children.Add(approve);
        }
        else
        {
            var reset = UiKit.SecondaryButton("Đổi mật khẩu");
            reset.Clicked += async (_, _) =>
                await Navigation.PushAsync(new AdminFounderPasswordPage(
                    _database, _session, founder.Account.Id, founder.DisplayName));
            actions.Children.Add(reset);

            var toggle = disabled
                ? UiKit.SecondaryButton("Kích hoạt lại tài khoản")
                : UiKit.DestructiveButton("Vô hiệu hóa tài khoản");
            toggle.Clicked += async (_, _) =>
            {
                var activating = disabled;
                var confirmed = await DisplayAlertAsync(
                    activating ? "Kích hoạt lại tài khoản?" : "Vô hiệu hóa tài khoản?",
                    activating
                        ? $"Account @{founder.Account.Username} sẽ được phép đăng nhập lại."
                        : $"Account @{founder.Account.Username} sẽ không thể đăng nhập cho đến khi được kích hoạt lại.",
                    activating ? "Kích hoạt lại" : "Vô hiệu hóa",
                    "Hủy");
                if (!confirmed) return;
                toggle.IsEnabled = false;
                try
                {
                    await _database.SetFounderActiveByAdminAsync(
                        _session.CurrentUser?.Id
                        ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ."),
                        founder.Account.Id,
                        activating);
                    await ReloadAsync();
                }
                catch (Exception exception)
                {
                    await DisplayAlertAsync("Chưa thể cập nhật trạng thái", exception.Message, "Đóng");
                    toggle.IsEnabled = true;
                }
            };
            actions.Children.Add(toggle);
        }

        var delete = UiKit.DestructiveButton("Xóa account");
        delete.Clicked += async (_, _) =>
        {
            var confirmed = await DisplayAlertAsync(
                "Xóa account Founder?",
                $"Xóa vĩnh viễn account @{founder.Account.Username} và toàn bộ dữ liệu đội bóng đã tạo? Thao tác này không thể hoàn tác.",
                "Xóa",
                "Hủy");
            if (!confirmed) return;
            delete.IsEnabled = false;
            try
            {
                await _database.DeleteFounderAccountAsync(
                    _session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ."),
                    founder.Account.Id);
                await ReloadAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể xóa account", exception.Message, "Đóng");
                delete.IsEnabled = true;
            }
        };
        actions.Children.Add(delete);

        if (actions.Children.Count > 0)
        {
            actions.Spacing = 6;
            actions.HorizontalOptions = LayoutOptions.End;
            actions.VerticalOptions = LayoutOptions.Start;
            foreach (var button in actions.Children.OfType<Button>())
            {
                MakeCompactActionButton(button);
            }

            var compactCard = new Grid
            {
                ColumnSpacing = 10,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(112))
                }
            };
            compactCard.Add(details, 0, 0);
            compactCard.Add(actions, 1, 0);
            return UiKit.Card(compactCard);
        }

        return UiKit.Card(new VerticalStackLayout
        {
            Spacing = 10,
            Children = { details, actions }
        });
    }

    private static void MakeCompactActionButton(Button button)
    {
        button.WidthRequest = 112;
        button.HeightRequest = 34;
        button.MinimumWidthRequest = 0;
        button.MinimumHeightRequest = 0;
        button.Padding = new Thickness(8, 0);
        button.CornerRadius = 10;
        button.FontSize = 11;
        button.HorizontalOptions = LayoutOptions.End;
    }
}

public sealed class AdminFounderEditorPage : ContentPage
{
    public AdminFounderEditorPage(AppDatabase database, SessionService session)
    {
        Title = "Tạo account Founder";
        BackgroundColor = UiKit.Background;
        var username = new Entry { Placeholder = "Username" };
        var fullName = new Entry { Placeholder = "Tên Sáng lập & Điều hành" };
        var email = new Entry { Placeholder = "Email", Keyboard = Keyboard.Email };
        var save = UiKit.PrimaryButton("Tạo account Founder");
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                await database.CreateFounderByAdminAsync(
                    session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ."),
                    username.Text ?? string.Empty,
                    fullName.Text ?? string.Empty,
                    email.Text ?? string.Empty,
                    AppDatabase.NewAccountDefaultPassword);
                await DisplayAlertAsync(
                    "Đã tạo account",
                    "Account Founder sẽ được yêu cầu đổi mật khẩu ở lần đăng nhập đầu tiên.",
                    "OK");
                await Navigation.PopAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể tạo account", exception.Message, "Đóng");
            }
            finally
            {
                save.IsEnabled = true;
            }
        };

        Content = UiKit.ScrollBody(
            UiKit.LargeTitle("Tạo account Sáng lập & Điều hành"),
            UiKit.Body(
                "Account này có toàn quyền vận hành một đội bóng. Mật khẩu mặc định là 12345678 và sẽ được yêu cầu đổi ở lần đăng nhập đầu tiên.",
                UiKit.TextSecondary),
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.LabeledField("USERNAME", username),
                    UiKit.LabeledField("HỌ VÀ TÊN", fullName),
                    UiKit.LabeledField("EMAIL", email),
                    save
                }
            }));
    }
}

public sealed class AdminFounderPasswordPage : ContentPage
{
    public AdminFounderPasswordPage(
        AppDatabase database,
        SessionService session,
        string targetUserId,
        string founderName)
    {
        Title = "Đổi mật khẩu Founder";
        BackgroundColor = UiKit.Background;
        var password = new Entry { Placeholder = "Mật khẩu mới" };
        var confirm = new Entry { Placeholder = "Nhập lại mật khẩu mới" };
        var save = UiKit.PrimaryButton("Đặt lại mật khẩu");
        save.Clicked += async (_, _) =>
        {
            if (!string.Equals(password.Text, confirm.Text, StringComparison.Ordinal))
            {
                await DisplayAlertAsync("Chưa thể đổi", "Hai mật khẩu không trùng nhau.", "Đóng");
                return;
            }

            save.IsEnabled = false;
            try
            {
                await database.ResetFounderPasswordByAdminAsync(
                    session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ."),
                    targetUserId,
                    password.Text ?? string.Empty);
                await DisplayAlertAsync(
                    "Đã đổi mật khẩu",
                    $"Account {founderName} sẽ phải đổi lại mật khẩu ở lần đăng nhập tiếp theo.",
                    "OK");
                await Navigation.PopAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể đổi mật khẩu", exception.Message, "Đóng");
            }
            finally
            {
                save.IsEnabled = true;
            }
        };

        Content = UiKit.ScrollBody(
            UiKit.LargeTitle($"Đổi mật khẩu · {founderName}"),
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.LabeledField("MẬT KHẨU MỚI", UiKit.PasswordField(password)),
                    UiKit.LabeledField("XÁC NHẬN", UiKit.PasswordField(confirm)),
                    save
                }
            }));
    }
}

public sealed class AdminPasswordPage : ContentPage
{
    public AdminPasswordPage(AppDatabase database, SessionService session)
    {
        Title = "Đổi mật khẩu Admin";
        BackgroundColor = UiKit.Background;
        var current = new Entry { Placeholder = "Mật khẩu hiện tại" };
        var password = new Entry { Placeholder = "Mật khẩu mới" };
        var confirm = new Entry { Placeholder = "Nhập lại mật khẩu mới" };
        var save = UiKit.PrimaryButton("Đổi mật khẩu");
        save.Clicked += async (_, _) =>
        {
            if (!string.Equals(password.Text, confirm.Text, StringComparison.Ordinal))
            {
                await DisplayAlertAsync("Chưa thể đổi", "Hai mật khẩu mới không trùng nhau.", "Đóng");
                return;
            }

            save.IsEnabled = false;
            try
            {
                await database.ChangePasswordAsync(
                    session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ."),
                    current.Text ?? string.Empty,
                    password.Text ?? string.Empty);
                await DisplayAlertAsync("Đã đổi mật khẩu", "Mật khẩu Admin đã được cập nhật.", "OK");
                await Navigation.PopAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể đổi mật khẩu", exception.Message, "Đóng");
            }
            finally
            {
                save.IsEnabled = true;
            }
        };

        Content = UiKit.ScrollBody(
            UiKit.LargeTitle("Đổi mật khẩu Admin"),
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.LabeledField("MẬT KHẨU HIỆN TẠI", UiKit.PasswordField(current)),
                    UiKit.LabeledField("MẬT KHẨU MỚI", UiKit.PasswordField(password)),
                    UiKit.LabeledField("XÁC NHẬN", UiKit.PasswordField(confirm)),
                    save
                }
            }));
    }
}
