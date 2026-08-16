using System.Globalization;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

public sealed class NotificationsPage : AsyncContentPage
{
    private readonly AppDatabase _database;

    public NotificationsPage(AppDatabase database, SessionService session)
        : base(session, "Thông báo")
    {
        _database = database;
    }

    protected override async Task LoadAsync()
    {
        var notifications = await _database.GetNotificationsAsync(CurrentUserId);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = 9,
            Children =
            {
                UiKit.OfflineBanner(),
                
            }
        };

        var markAll = UiKit.SecondaryButton("Đánh dấu đã đọc tất cả");
        markAll.HorizontalOptions = LayoutOptions.Fill;
        markAll.Clicked += async (_, _) => await RunActionAsync(
            () => _database.MarkAllNotificationsReadAsync(CurrentUserId), markAll, reload: true);
        var deleteAll = UiKit.DestructiveButton("Xóa tất cả");
        deleteAll.HorizontalOptions = LayoutOptions.Fill;
        deleteAll.Clicked += async (_, _) => await RunActionAsync(
            () => _database.DeleteAllNotificationsAsync(CurrentUserId), deleteAll, reload: true);
        root.Children.Add(new HorizontalStackLayout
        {
            Spacing = 8,
            Children = { markAll, deleteAll }
        });

        if (notifications.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có thông báo",
                "Thông báo của đội và trạng thái học phí sẽ xuất hiện tại đây."));
        }
        else
        {
            foreach (var item in notifications)
            {
                var card = UiKit.Card(new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new HorizontalStackLayout
                        {
                            Spacing = 5,
                            Children =
                            {
                                item.IsRead
                                    ? UiKit.Caption("○")
                                    : UiKit.Caption("●", UiKit.Primary),
                                UiKit.Headline(item.Title),
                                item.IsRead
                                    ? UiKit.Caption(string.Empty)
                                    : UiKit.Body("●", UiKit.Primary)
                            }
                        },
                        UiKit.Body(item.Message, UiKit.TextSecondary),
                        UiKit.Caption(item.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"))
                    }
                });
                if (!item.IsRead)
                {
                    var tap = new TapGestureRecognizer();
                    tap.Tapped += async (_, _) =>
                        await RunActionAsync(
                            () => _database.MarkNotificationReadAsync(CurrentUserId, item.Id),
                            reload: true);
                    card.GestureRecognizers.Add(tap);
                }

                root.Children.Add(card);
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }
}

public sealed class AnnouncementComposerPage : ContentPage
{
    public AnnouncementComposerPage(AppDatabase database, SessionService session)
    {
        Title = "Gửi thông báo";
        BackgroundColor = UiKit.Background;
        var recipient = new Picker { Title = "Tất cả học viên" };
        var title = new Entry { Placeholder = "Tiêu đề" };
        var message = new Editor
        {
            Placeholder = "Nội dung thông báo",
            MinimumHeightRequest = 110
        };
        var send = UiKit.PrimaryButton("Gửi thông báo");
        var recipients = new List<(string? UserId, UserRole Role)>();

        send.Clicked += async (_, _) =>
        {
            send.IsEnabled = false;
            try
            {
                if (recipient.SelectedIndex < 0
                    || recipient.SelectedIndex >= recipients.Count)
                {
                    throw new InvalidOperationException("Vui lòng chọn người nhận.");
                }

                var selectedRecipient = recipients[recipient.SelectedIndex];
                await database.SendAnnouncementAsync(
                    session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                    selectedRecipient.UserId,
                    title.Text ?? string.Empty,
                    message.Text ?? string.Empty,
                    selectedRecipient.Role);
                await DisplayAlertAsync("Đã gửi", "Thông báo đã được lưu vào account người nhận.", "OK");
                await Navigation.PopAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể gửi", exception.Message, "Đóng");
            }
            finally
            {
                send.IsEnabled = true;
            }
        };

        Content = UiKit.ScrollBody(
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.LabeledField("NGƯỜI NHẬN", recipient),
                    UiKit.LabeledField("TIÊU ĐỀ", title),
                    UiKit.LabeledField("NỘI DUNG", message),
                    send
                }
            }));

        Loaded += async (_, _) =>
        {
            try
            {
                var trainees = (await database.GetMembersAsync(
                        session.CurrentUser?.Id
                        ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                        UserRole.Trainee))
                    .ToList();
                var coaches = (await database.GetMembersAsync(
                        session.CurrentUser?.Id
                        ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                        UserRole.Coach))
                    .ToList();
                recipient.Items.Clear();
                recipient.Items.Add("Tất cả học viên");
                recipients.Clear();
                recipients.Add((null, UserRole.Trainee));
                recipient.Items.Add("Tất cả Huấn Luyện Viên");
                recipients.Add((null, UserRole.Coach));
                foreach (var trainee in trainees)
                {
                    recipient.Items.Add($"Học viên: {trainee.DisplayName}");
                    recipients.Add((trainee.Account.Id, UserRole.Trainee));
                }
                foreach (var coach in coaches)
                {
                    recipient.Items.Add($"Huấn Luyện Viên: {coach.DisplayName}");
                    recipients.Add((coach.Account.Id, UserRole.Coach));
                }

                recipient.SelectedIndex = 0;
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Không thể tải học viên", exception.Message, "Đóng");
            }
        };
    }
}

public sealed class ProfileHubPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly AppNavigator _navigator;
    private readonly RememberedLoginService _rememberedLogin;

    public ProfileHubPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        AppNavigator navigator,
        RememberedLoginService rememberedLogin)
        : base(session, "Hồ sơ")
    {
        _database = database;
        _media = media;
        _navigator = navigator;
        _rememberedLogin = rememberedLogin;
    }

    protected override async Task LoadAsync()
    {
        var role = Session.CurrentUser?.Role
                   ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ.");
        var club = await _database.GetClubAsync();
        var teamName = string.IsNullOrWhiteSpace(club.TeamName)
            ? "Community Football Club"
            : club.TeamName.Trim();
        var profileName = UiKit.LargeTitle(
            Session.CurrentProfile?.FullName ?? Session.CurrentUser?.Username ?? "Hồ sơ");
        profileName.HorizontalOptions = LayoutOptions.Fill;
        profileName.HorizontalTextAlignment = TextAlignment.Center;

        var roleBadge = UiKit.StatusBadge(DomainText.Role(role), UiKit.Primary);
        roleBadge.HorizontalOptions = LayoutOptions.Center;

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = 9,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.Avatar(Session.CurrentProfile?.PhotoPath ?? string.Empty, 96),
                profileName,
                roleBadge,
                MenuCard("Thông tin cá nhân", "Xem hồ sơ; chỉnh sửa khi cần.",
                    async () => await Navigation.PushAsync(new PersonalProfilePage(
                        _database,
                        Session,
                        _media,
                        _rememberedLogin))),
                MenuCard("Thông tin đội", $"Xem thông tin {teamName}.",
                    async () => await Navigation.PushAsync(new ClubProfilePage(
                        _database,
                        Session,
                        _media,
                        editable: false)))
            }
        };

        if (role == UserRole.Trainee)
        {
            root.Children.Add(MenuCard(
                "Lịch sử điểm danh",
                "Xem ngày đi học, đi trễ hoặc vắng.",
                async () => await Navigation.PushAsync(new AttendanceHistoryPage(
                    _database,
                    Session))));
        }
        else if (role == UserRole.Coach)
        {
            root.Children.Add(MenuCard(
                "Đánh giá học viên",
                "Chọn lớp và gửi đánh giá khi Founder mở yêu cầu.",
                async () => await Navigation.PushAsync(new CoachEvaluationPage(
                    _database,
                    Session))));
            root.Children.Add(MenuCard(
                "Lịch sử dạy học",
                "Xem các buổi dạy, check-in, check-out và thời gian dạy.",
                async () => await Navigation.PushAsync(new CoachCheckInHistoryPage(
                    _database,
                    Session))));
        }

        var logoutNotice = UiKit.LoadingOverlay("Đang đăng xuất");
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

public sealed class PersonalProfilePage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly RememberedLoginService _rememberedLogin;
    private readonly bool _closeAfterEdit;
    private bool _editing;
    private ProfileEditDraft? _draft;

    public PersonalProfilePage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin,
        bool startInEdit = false,
        bool closeAfterEdit = false)
        : base(session, "Thông tin cá nhân")
    {
        _database = database;
        _media = media;
        _rememberedLogin = rememberedLogin;
        _editing = startInEdit;
        _closeAfterEdit = closeAfterEdit;
    }

    protected override async Task LoadAsync()
    {
        var profile = await _database.GetProfileAsync(CurrentUserId);
        if (!_editing)
        {
            _draft = null;
            BuildReadOnlyView(profile);
            return;
        }

        _draft ??= ProfileEditDraft.From(profile);
        BuildEditView(profile, _draft);
    }

    private void BuildReadOnlyView(PersonProfile profile)
    {
        var role = Session.CurrentUser?.Role
                   ?? throw new UnauthorizedAccessException(
                       "Phiên đăng nhập không hợp lệ.");
        var details = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                UiKit.Body($"Email: {Value(profile.Email)}")
            }
        };
        if (role != UserRole.Trainee)
        {
            details.Children.Insert(0, UiKit.Body($"Số điện thoại: {Value(profile.Phone)}"));
            if (role == UserRole.Coach)
            {
                details.Children.Insert(1, UiKit.Body(
                    $"Vị trí dạy: {CoachPositionCatalog.Label(profile.CoachPosition)}"));
            }
        }

        if (role == UserRole.Trainee)
        {
            details.Children.Add(
                UiKit.Body($"Ngày tháng năm sinh: {BirthDate(profile.DateOfBirth)}"));
            details.Children.Add(
                UiKit.Body($"Chiều cao: {Dimension(profile.HeightCm, "cm")}"));
            details.Children.Add(
                UiKit.Body($"Cân nặng: {Dimension(profile.WeightKg, "kg")}"));
        }

        var edit = UiKit.PrimaryButton("Sửa hồ sơ");
        edit.Clicked += async (_, _) =>
        {
            _editing = true;
            _draft = ProfileEditDraft.From(profile);
            await ReloadAsync();
        };
        var bindAccount = UiKit.SecondaryButton("Bind Account");
        bindAccount.Clicked += async (_, _) =>
                    await Navigation.PushAsync(new BindAccountsPage(_database, Session));

        var evaluationHistory = UiKit.SecondaryButton("Lịch sử đánh giá học viên");
        evaluationHistory.Clicked += async (_, _) =>
            await Navigation.PushAsync(new TraineeEvaluationHistoryPage(
                _database,
                Session,
                CurrentUserId,
                string.IsNullOrWhiteSpace(profile.FullName)
                    ? Session.CurrentUser?.Username ?? "Cầu thủ học viên"
                    : profile.FullName));

        var profileName = UiKit.LargeTitle(
            string.IsNullOrWhiteSpace(profile.FullName)
                ? Session.CurrentUser?.Username ?? "Hồ sơ"
                : profile.FullName);
        profileName.HorizontalOptions = LayoutOptions.Fill;
        profileName.HorizontalTextAlignment = TextAlignment.Center;

        var roleBadge = UiKit.StatusBadge(DomainText.Role(role), UiKit.Primary);
        roleBadge.HorizontalOptions = LayoutOptions.Center;

        var children = new List<View>
        {
            UiKit.Avatar(profile.PhotoPath, 104),
            profileName,
            roleBadge,
            UiKit.Card(details),
            edit,
            bindAccount
        };
        if (role == UserRole.Trainee)
        {
            children.Add(evaluationHistory);
        }

        Content = UiKit.ScrollBody(
            children.ToArray());
    }

    private void BuildEditView(PersonProfile profile, ProfileEditDraft draft)
    {
        var photo = UiKit.Avatar(draft.PhotoPath, 104);
        var fullName = new Entry { Text = draft.FullName, Placeholder = "Họ và tên" };
        var phone = new Entry
        {
            Text = draft.Phone,
            Placeholder = "Số điện thoại",
            Keyboard = Keyboard.Telephone
        };
        var email = new Entry
        {
            Text = draft.Email,
            Placeholder = "Email",
            Keyboard = Keyboard.Email
        };
        var coachPosition = new Picker
        {
            Title = "Chọn vị trí dạy",
            ItemsSource = CoachPositionCatalog.Options.Select(option => option.Label).ToList(),
            SelectedIndex = PositionIndex(draft.CoachPosition),
            IsVisible = Session.CurrentUser?.Role == UserRole.Coach
        };
        coachPosition.SelectedIndexChanged += (_, _) =>
        {
            draft.CoachPosition = coachPosition.SelectedIndex >= 0
                && coachPosition.SelectedIndex < CoachPositionCatalog.Options.Count
                ? CoachPositionCatalog.Options[coachPosition.SelectedIndex].Key
                : string.Empty;
        };
        var dateOfBirth = new DatePicker
        {
            Date = draft.DateOfBirth?.Date,
            Format = "dd/MM/yyyy",
            MinimumDate = new DateTime(1900, 1, 1),
            MaximumDate = DateTime.Today
        };
        var height = new Entry
        {
            Text = draft.Height,
            Placeholder = "cm",
            Keyboard = Keyboard.Numeric
        };
        var weight = new Entry
        {
            Text = draft.Weight,
            Placeholder = "kg",
            Keyboard = Keyboard.Numeric
        };
        fullName.TextChanged += (_, args) => draft.FullName = args.NewTextValue ?? string.Empty;
        phone.TextChanged += (_, args) => draft.Phone = args.NewTextValue ?? string.Empty;
        email.TextChanged += (_, args) => draft.Email = args.NewTextValue ?? string.Empty;
        dateOfBirth.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == DatePicker.DateProperty.PropertyName)
            {
                draft.DateOfBirth = dateOfBirth.Date?.Date;
            }
        };
        height.TextChanged += (_, args) => draft.Height = args.NewTextValue ?? string.Empty;
        weight.TextChanged += (_, args) => draft.Weight = args.NewTextValue ?? string.Empty;
        var photoButton = UiKit.SecondaryButton("Thay hình ảnh");
        photoButton.Clicked += async (_, _) =>
        {
            if (await ChangePhotoAsync())
            {
                await ReloadAsync();
            }
        };
        var save = UiKit.PrimaryButton("Lưu thông tin");
        save.Clicked += async (_, _) =>
        {
            var saved = false;
            profile.FullName = draft.FullName;
            profile.Phone = Session.CurrentUser?.Role == UserRole.Trainee
                ? string.Empty
                : draft.Phone;
            profile.Email = draft.Email;
            profile.PhotoPath = draft.PhotoPath;
            profile.CoachPosition = Session.CurrentUser?.Role == UserRole.Coach
                ? draft.CoachPosition
                : string.Empty;
            profile.DateOfBirth = draft.DateOfBirth;
            profile.HeightCm = ParseDouble(draft.Height);
            profile.WeightKg = ParseDouble(draft.Weight);
            await RunActionAsync(
                async () =>
                {
                    await _database.SaveProfileAsync(CurrentUserId, profile);
                    Session.RefreshProfile(profile);
                    saved = true;
                },
                save,
                "Thông tin cá nhân đã được cập nhật.",
                reload: false);
            if (!saved)
            {
                return;
            }

            await ExitEditAsync();
        };
        var cancel = UiKit.SecondaryButton("Hủy chỉnh sửa");
        cancel.Clicked += async (_, _) => await ExitEditAsync();

        var form = new VerticalStackLayout
        {
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                photo,
                photoButton,
                UiKit.LabeledField("HỌ VÀ TÊN", fullName),
                UiKit.LabeledField("EMAIL", email)
            }
        };
        if (Session.CurrentUser?.Role != UserRole.Trainee)
        {
            form.Children.Insert(3, UiKit.LabeledField("SỐ ĐIỆN THOẠI", phone));
        }

        if (Session.CurrentUser?.Role == UserRole.Coach)
        {
            form.Children.Add(UiKit.LabeledField("VỊ TRÍ DẠY", coachPosition));
        }

        if (Session.CurrentUser?.Role == UserRole.Trainee)
        {
            form.Children.Add(UiKit.LabeledField("NGÀY THÁNG NĂM SINH", dateOfBirth));
            form.Children.Add(UiKit.LabeledField("CHIỀU CAO", height));
            form.Children.Add(UiKit.LabeledField("CÂN NẶNG", weight));
        }

        form.Children.Add(save);
        form.Children.Add(cancel);
        form.Children.Add(UiKit.Title("Đổi mật khẩu"));
        var currentPassword = new Entry { Placeholder = "Mật khẩu hiện tại" };
        var newPassword = new Entry { Placeholder = "Mật khẩu mới" };
        var confirmPassword = new Entry { Placeholder = "Nhập lại mật khẩu mới" };
        var currentPasswordField = UiKit.PasswordField(currentPassword);
        var newPasswordField = UiKit.PasswordField(newPassword);
        var confirmPasswordField = UiKit.PasswordField(confirmPassword);
        var changePassword = UiKit.SecondaryButton("Đổi mật khẩu");
        changePassword.Clicked += async (_, _) =>
        {
            if (newPassword.Text != confirmPassword.Text)
            {
                await DisplayAlertAsync("Chưa thể đổi", "Hai mật khẩu mới không trùng nhau.", "Đóng");
                return;
            }

            var passwordChanged = false;
            await RunActionAsync(
                async () =>
                {
                    await _database.ChangePasswordAsync(
                        CurrentUserId,
                        currentPassword.Text ?? string.Empty,
                        newPassword.Text ?? string.Empty);
                    _rememberedLogin.Forget();
                    passwordChanged = true;
                },
                changePassword,
                "Mật khẩu đã được thay đổi.",
                reload: false);
            if (!passwordChanged)
            {
                return;
            }

            currentPassword.Text = string.Empty;
            newPassword.Text = string.Empty;
            confirmPassword.Text = string.Empty;
        };
        form.Children.Add(UiKit.LabeledField("MẬT KHẨU HIỆN TẠI", currentPasswordField));
        form.Children.Add(UiKit.LabeledField("MẬT KHẨU MỚI", newPasswordField));
        form.Children.Add(UiKit.LabeledField("XÁC NHẬN", confirmPasswordField));
        form.Children.Add(changePassword);

        Content = UiKit.ScrollBody(
            UiKit.Card(form));
    }

    private async Task<bool> ChangePhotoAsync()
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
                "Chụp ảnh" => await _media.CapturePhotoAsync("profiles"),
                "Chọn từ thư viện" => await _media.PickPhotoAsync("profiles"),
                _ => null
            };
            if (path is null)
            {
                return false;
            }

            _draft ??= new ProfileEditDraft();
            _draft.PhotoPath = path;
            return true;
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Không thể lấy ảnh", exception.Message, "Đóng");
            return false;
        }
    }

    private static double ParseDouble(string? value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var result))
        {
            return result;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            ? result
            : 0;
    }

    private static string Value(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Chưa cập nhật" : value;

    private static string Dimension(double value, string unit) =>
        value <= 0 ? "Chưa cập nhật" : $"{value:0.#} {unit}";

    private static string BirthDate(DateTime? value) =>
        value?.ToString("dd/MM/yyyy") ?? "Chưa cập nhật";

    private async Task ExitEditAsync()
    {
        _editing = false;
        _draft = null;
        if (_closeAfterEdit)
        {
            await Navigation.PopAsync();
            return;
        }

        await ReloadAsync();
    }

    private sealed class ProfileEditDraft
    {
        public string FullName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhotoPath { get; set; } = string.Empty;
        public DateTime? DateOfBirth { get; set; }
        public string Height { get; set; } = string.Empty;
        public string Weight { get; set; } = string.Empty;
        public string CoachPosition { get; set; } = string.Empty;

        public static ProfileEditDraft From(PersonProfile profile) => new()
        {
            FullName = profile.FullName,
            Phone = profile.Phone,
            Email = profile.Email,
            PhotoPath = profile.PhotoPath,
            DateOfBirth = profile.DateOfBirth?.Date,
            Height = profile.HeightCm > 0
                ? profile.HeightCm.ToString("0.#", CultureInfo.InvariantCulture)
                : string.Empty,
            Weight = profile.WeightKg > 0
                ? profile.WeightKg.ToString("0.#", CultureInfo.InvariantCulture)
                : string.Empty,
            CoachPosition = profile.CoachPosition
        };
    }

    private static int PositionIndex(string? key)
    {
        for (var index = 0; index < CoachPositionCatalog.Options.Count; index++)
        {
            if (string.Equals(CoachPositionCatalog.Options[index].Key, key, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}

public sealed class AttendanceHistoryPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly string? _traineeUserId;
    private readonly int? _year;
    private readonly int? _month;

    public AttendanceHistoryPage(
        AppDatabase database,
        SessionService session,
        string? traineeUserId = null,
        int? year = null,
        int? month = null)
        : base(session, session.CurrentUser?.Role == UserRole.Trainee
            ? string.Empty
            : "Lịch sử điểm danh")
    {
        _database = database;
        _traineeUserId = traineeUserId;
        _year = year;
        _month = month;
    }

    protected override async Task LoadAsync()
    {
        var rows = await _database.GetAttendanceHistoryAsync(
            CurrentUserId,
            _traineeUserId ?? CurrentUserId);
        rows = rows
            .Where(item => (!_year.HasValue || item.SessionDate.Year == _year.Value)
                           && (!_month.HasValue || item.SessionDate.Month == _month.Value))
            .ToList();
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = 9,
            Children =
            {
                UiKit.OfflineBanner()
            }
        };
        if (Session.CurrentUser?.Role != UserRole.Trainee)
        {
            root.Children.Add(UiKit.LargeTitle("Lịch sử điểm danh"));
        }
        if (rows.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có điểm danh",
                "Điểm danh do Coach hoặc Founder hoàn tất sẽ xuất hiện tại đây."));
        }
        else
        {
            foreach (var row in rows)
            {
                root.Children.Add(UiKit.Card(new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    },
                    Children =
                    {
                        new VerticalStackLayout
                        {
                            Spacing = 3,
                            Children =
                            {
                                UiKit.Headline(row.ClassName),
                                UiKit.Caption(row.SessionDate.ToString("dd/MM/yyyy"))
                            }
                        },
                        BadgeAtColumnOne(row.Status)
                    }
                }));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private static View BadgeAtColumnOne(AttendanceStatus status)
    {
        var badge = UiKit.StatusBadge(
            DomainText.Attendance(status),
            UiKit.AttendanceColor(status));
        Grid.SetColumn(badge, 1);
        return badge;
    }
}
