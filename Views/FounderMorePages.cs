using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

public sealed class MorePage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly AppNavigator _navigator;
    private readonly RememberedLoginService _rememberedLogin;

    public MorePage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        AppNavigator navigator,
        RememberedLoginService rememberedLogin)
        : base(session, "Khác")
    {
        _database = database;
        _media = media;
        _navigator = navigator;
        _rememberedLogin = rememberedLogin;
    }

    protected override Task LoadAsync()
    {
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = 9,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.LargeTitle("Quản lý đội"),
                UiKit.NavigationRow("Thành viên", "Đồng sáng lập, Quản lý, Coach và học viên", "tab_people.svg",
                    async (_, _) => await PushPageAsync(new MemberManagementPage(
                        _database, Session, _media, _rememberedLogin))),
                MenuCard("Hồ sơ Founder", "Xem hồ sơ; chỉnh sửa ảnh, liên hệ và mật khẩu.",
                    async () => await PushPageAsync(new PersonalProfilePage(
                        _database,
                        Session,
                        _media,
                        _rememberedLogin))),
                MenuCard("Thông tin đội & ngân hàng", "Tên đội, logo và tài khoản nhận học phí.",
                    async () => await PushPageAsync(new ClubProfilePage(
                        _database,
                        Session,
                        _media,
                        editable: true))),
                MenuCard("Quản lý sân", "Tạo và cập nhật địa chỉ sân.",
                    async () => await PushPageAsync(new VenueManagementPage(
                        _database,
                        Session))),
                MenuCard("Gửi thông báo", "Gửi riêng hoặc gửi tất cả học viên.",
                    async () => await PushPageAsync(new AnnouncementComposerPage(
                        _database,
                        Session))),
                MenuCard("Thông báo của Founder", "Bill mới và nhắc lương sau ngày 10.",
                    async () => await PushPageAsync(new NotificationsPage(
                        _database,
                        Session))),
                MenuCard("Lịch sử thao tác", "Audit log điểm danh, học phí và account.",
                    async () => await PushPageAsync(new AuditLogPage(
                        _database,
                        Session)))
            }
        };
        var logoutNotice = UiKit.LoadingOverlay("Đang đăng xuất");
        // Audit history is retained for diagnostics but is not exposed in the
        // Founder menu anymore.
        if (root.Children.Count > 0)
            root.Children.RemoveAt(root.Children.Count - 1);
        logoutNotice.IsVisible = false;
        var logout = UiKit.DestructiveButton("Đăng xuất");
        logout.Clicked += async (_, _) =>
        {
            var confirmed = await DisplayAlertAsync(
                "Đăng xuất?",
                "Phiên đăng nhập trên thiết bị này sẽ kết thúc.",
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
        return Task.CompletedTask;
    }

    private static View MenuCard(string title, string subtitle, Func<Task> action)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        grid.Children.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children = { UiKit.Headline(title), UiKit.Caption(subtitle) }
        });
        var arrow = UiKit.Body("›", UiKit.TextSecondary);
        arrow.FontSize = 22;
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(arrow);
        var card = UiKit.Card(grid);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await action();
        card.GestureRecognizers.Add(tap);
        return card;
    }
}

public sealed class ClubProfilePage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly bool _editable;
    private string _logoPath = string.Empty;

    public ClubProfilePage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        bool editable)
        : base(session, "Thông tin đội")
    {
        _database = database;
        _media = media;
        _editable = editable;
    }

    protected override async Task LoadAsync()
    {
        var club = await _database.GetClubAsync();
        _logoPath = club.LogoPath;

        if (!_editable)
        {
            var founder = await _database.GetFounderAsync(CurrentUserId);
            var displayTeamName = string.IsNullOrWhiteSpace(club.TeamName)
                ? "Community Football Club"
                : club.TeamName.Trim();
            var teamTitle = UiKit.LargeTitle(displayTeamName);
            teamTitle.HorizontalOptions = LayoutOptions.Fill;
            teamTitle.HorizontalTextAlignment = TextAlignment.Center;
            Content = UiKit.ScrollBody(
                UiKit.ClubLogo(club.LogoPath, 110, fillFrame: true),
                teamTitle,
                UiKit.Card(new VerticalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        UiKit.Title("Founder"),
                        UiKit.Avatar(founder.Profile.PhotoPath, 88),
                        UiKit.Headline(founder.DisplayName),
                        UiKit.Body($"Số điện thoại: {Value(founder.Profile.Phone)}"),
                        UiKit.Body($"Email: {Value(founder.Profile.Email)}")
                    }
                }));
            return;
        }

        var teamName = new Entry { Text = club.TeamName, Placeholder = "Tên đội" };
        var bankName = new Entry { Text = club.BankName, Placeholder = "Tên ngân hàng" };
        var bankBin = new Entry
        {
            Text = club.BankBin,
            Placeholder = "BIN ngân hàng, ví dụ 970436",
            Keyboard = Keyboard.Numeric
        };
        var accountNumber = new Entry
        {
            Text = club.BankAccountNumber,
            Placeholder = "Số tài khoản",
            Keyboard = Keyboard.Numeric
        };
        var accountName = new Entry
        {
            Text = club.BankAccountName,
            Placeholder = "Tên chủ tài khoản"
        };
        var logo = UiKit.Avatar(_logoPath, 104);
        var logoButton = UiKit.SecondaryButton("Thay logo đội");
        logoButton.Clicked += async (_, _) =>
            await PickImageAsync("club_logo", path =>
            {
                _logoPath = path;
                logo.Source = ImageSource.FromFile(path);
            });

        var save = UiKit.PrimaryButton("Lưu thông tin đội & ngân hàng");
        save.Clicked += async (_, _) =>
        {
            club.TeamName = teamName.Text ?? string.Empty;
            club.BankName = bankName.Text ?? string.Empty;
            club.BankBin = bankBin.Text ?? string.Empty;
            club.BankAccountNumber = accountNumber.Text ?? string.Empty;
            club.BankAccountName = accountName.Text ?? string.Empty;
            club.LogoPath = _logoPath;
            await RunActionAsync(
                () => _database.SaveClubAsync(CurrentUserId, club),
                save,
                "Thông tin đội đã được cập nhật.",
                reload: false);
        };

        Content = UiKit.ScrollBody(
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    logo,
                    logoButton,
                    UiKit.LabeledField("TÊN ĐỘI", teamName),
                    UiKit.Title("Thông tin ngân hàng / VietQR"),
                    UiKit.Caption("Có thể để trống và cập nhật sau. QR chỉ xuất hiện khi có BIN và số tài khoản."),
                    UiKit.LabeledField("NGÂN HÀNG", bankName),
                    UiKit.LabeledField("BANK BIN", bankBin),
                    UiKit.LabeledField("SỐ TÀI KHOẢN", accountNumber),
                    UiKit.LabeledField("TÊN TÀI KHOẢN", accountName),
                    save
                }
            }));
    }

    private async Task PickImageAsync(string category, Action<string> apply)
    {
        var choice = await DisplayActionSheetAsync(
            "Hình ảnh",
            "Hủy",
            null,
            "Chụp ảnh",
            "Chọn từ thư viện");
        try
        {
            var path = choice switch
            {
                "Chụp ảnh" => await _media.CapturePhotoAsync(category),
                "Chọn từ thư viện" => await _media.PickPhotoAsync(category),
                _ => null
            };
            if (path is not null)
            {
                apply(path);
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Không thể lấy ảnh", exception.Message, "Đóng");
        }
    }

    private static string Value(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Chưa cập nhật" : value;

}

public sealed class AuditLogPage : AsyncContentPage
{
    private readonly AppDatabase _database;

    public AuditLogPage(AppDatabase database, SessionService session)
        : base(session, "Lịch sử thao tác")
    {
        _database = database;
    }

    protected override async Task LoadAsync()
    {
        var logs = await _database.GetAuditLogsAsync(CurrentUserId);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = 8,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.LargeTitle("Lịch sử thao tác")
            }
        };
        if (logs.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có lịch sử",
                "Các thay đổi quan trọng sẽ được ghi lại tại đây."));
        }
        else
        {
            foreach (var log in logs)
            {
                root.Children.Add(UiKit.Card(new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        UiKit.Headline(log.Action),
                        UiKit.Caption($"{log.EntityType} · {log.EntityId}"),
                        UiKit.Body(log.Details, UiKit.TextSecondary),
                        UiKit.Caption(log.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))
                    }
                }));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }
}
