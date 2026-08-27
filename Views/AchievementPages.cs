using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

/// <summary>
/// Compact role-aware achievement hub. The Worker remains the authority for
/// role checks, approval and points; this page only presents the scoped feed
/// returned by the dedicated achievement endpoints.
/// </summary>
public sealed class AchievementHubPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly Picker _categoryPicker = new() { Title = "Hạng mục" };
    private readonly VerticalStackLayout _list = new() { Spacing = UiKit.SectionSpacing };
    private AchievementFeed _feed = new([], 0, 0);
    private IReadOnlyList<AchievementBadge> _badges = [];
    private bool _compactModeEnabled;
    private bool _rendering;

    public AchievementHubPage(AppDatabase database, SessionService session)
        : base(session, "Thành tích")
    {
        _database = database;
        _categoryPicker.ItemsSource = new[]
        {
            "Tất cả hạng mục",
            DomainText.AchievementCategory(AchievementCategory.MatchRanking),
            DomainText.AchievementCategory(AchievementCategory.WeeklyClassRanking)
        };
        _categoryPicker.SelectedIndex = 0;
        _categoryPicker.SelectedIndexChanged += (_, _) => Render();
        Content = UiKit.ScrollBody(_categoryPicker, _list);
    }

    protected override async Task LoadAsync()
    {
        var role = Session.CurrentUser?.Role;
        if (role is not (UserRole.Founder or UserRole.CoFounder or UserRole.Coach or UserRole.Trainee))
        {
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem thành tích.");
        }

        _badges = await _database.GetAchievementBadgesAsync(
            CurrentUserId);
        _feed = role == UserRole.Trainee
            ? await _database.GetAchievementsAsync(CurrentUserId, CurrentUserId)
            : await _database.GetAchievementsAsync(CurrentUserId);
        Render();
    }

    private void Render()
    {
        if (_rendering)
            return;
        _rendering = true;
        try
        {
            _list.Children.Clear();
            var role = Session.CurrentUser?.Role;
            var category = SelectedCategory();
            var rows = category is null
                ? _feed.Achievements
                : _feed.Achievements.Where(item => item.Achievement.Category == category.Value).ToList();

            var summary = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 12
            };
            var score = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    UiKit.Caption(
                        role == UserRole.Trainee
                            ? "Điểm cá nhân tích lũy"
                            : "Tổng điểm trong phạm vi",
                        UiKit.TextSecondary),
                    UiKit.LargeTitle(_feed.TotalPoints.ToString("+#;-#;0"))
                }
            };
            summary.Children.Add(score);
            var pending = UiKit.StatusBadge(
                _feed.PendingCount > 0 ? $"{_feed.PendingCount} chờ duyệt" : "Đã cập nhật",
                _feed.PendingCount > 0 ? UiKit.Warning : UiKit.Success);
            var summaryActions = new VerticalStackLayout
            {
                Spacing = 6,
                HorizontalOptions = LayoutOptions.End,
                Children =
                {
                    pending,
                    UiKit.StatusBadge("Đổi quà · Coming soon", UiKit.Primary)
                }
            };
            Grid.SetColumn(summaryActions, 1);
            summary.Children.Add(summaryActions);
            _list.Children.Add(UiKit.Card(summary));

            // Render can run more than once (for example after changing the
            // category, toggling compact mode or refreshing after an action).
            // Create a fresh switch each time instead of re-parenting a shared
            // VisualElement, which makes MAUI throw and surface as the generic
            // "Không thể tải dữ liệu" state on Android.
            var compactMode = new Switch
            {
                IsToggled = _compactModeEnabled,
                HorizontalOptions = LayoutOptions.End
            };
            compactMode.Toggled += (_, args) =>
            {
                if (_compactModeEnabled == args.Value)
                    return;

                _compactModeEnabled = args.Value;
                Render();
            };
            var displayMode = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8,
                Children =
                {
                    UiKit.Caption("Hiển thị dạng danh sách gọn", UiKit.TextSecondary),
                    compactMode
                }
            };
            Grid.SetColumn(compactMode, 1);
            _list.Children.Add(displayMode);

            if (RoleCapabilities.CanCreateAchievements(role))
            {
                var add = UiKit.PrimaryButton("Thêm thành tích");
                add.Clicked += async (_, _) =>
                {
                    add.IsEnabled = false;
                    try
                    {
                        await Navigation.PushAsync(new AchievementCreatePage(_database, Session));
                    }
                    finally
                    {
                        add.IsEnabled = true;
                    }
                };
                _list.Children.Add(add);
            }

            _list.Children.Add(UiKit.Caption(
                "Mỗi Cầu thủ học viên có biểu trưng và điểm riêng. Biểu trưng hiển thị trong 30 ngày; điểm đã ghi nhận được giữ lại vĩnh viễn để tích lũy và đổi quà (Coming soon).",
                UiKit.TextSecondary));

            if (rows.Count == 0)
            {
                _list.Children.Add(UiKit.EmptyState(
                    role == UserRole.Trainee ? "Chưa có thành tích được xác nhận" : "Chưa có thành tích",
                    role == UserRole.Trainee
                        ? "Khi Founder xác nhận, biểu trưng sẽ xuất hiện tại đây."
                        : "Thêm biểu trưng cho Cầu thủ học viên hoặc chờ Coach gửi đề xuất."));
                return;
            }

            var pendingRows = rows
                .Where(item => item.Achievement.Status == AchievementStatus.Pending)
                .ToList();
            var historyRows = rows
                .Where(item => item.Achievement.Status != AchievementStatus.Pending)
                .ToList();

            if (pendingRows.Count > 0)
            {
                _list.Children.Add(UiKit.Title("Chờ Founder xác nhận"));
                foreach (var row in pendingRows)
                {
                    _list.Children.Add(_compactModeEnabled
                        ? BuildCompactAchievementCard(row, role)
                        : BuildAchievementCard(row, role));
                }
            }

            if (historyRows.Count > 0)
            {
                _list.Children.Add(UiKit.Title(
                    role == UserRole.Trainee ? "Biểu trưng đã nhận" : "Lịch sử thành tích"));
                foreach (var row in historyRows)
                {
                    _list.Children.Add(_compactModeEnabled
                        ? BuildCompactAchievementCard(row, role)
                        : BuildAchievementCard(row, role));
                }
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private View BuildAchievementCard(AchievementRow row, UserRole? role)
    {
        var heading = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        var icon = AchievementBadgeUi.BadgeImage(row.Badge, 36);
        icon.VerticalOptions = LayoutOptions.Start;
        heading.Children.Add(icon);
        var title = string.IsNullOrWhiteSpace(row.Achievement.Title)
            ? row.Badge.Name
            : row.Achievement.Title;
        var details = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                UiKit.Headline(title),
                UiKit.Caption(row.Badge.Name, UiKit.Primary),
                UiKit.Caption($"{row.TraineeName} · {DomainText.AchievementCategory(row.Achievement.Category)}")
            }
        };
        Grid.SetColumn(details, 1);
        heading.Children.Add(details);
        var points = UiKit.StatusBadge(
            row.Achievement.Points >= 0
                ? $"+{row.Achievement.Points} điểm"
                : $"{row.Achievement.Points} điểm",
            row.Achievement.Points >= 0 ? UiKit.Success : UiKit.Danger);
        Grid.SetColumn(points, 2);
        heading.Children.Add(points);

        var content = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                heading,
                UiKit.Caption(
                    $"Ngày ghi nhận: {row.Achievement.AwardedForDateUtc.ToLocalTime():dd/MM/yyyy}"
                    + (string.IsNullOrWhiteSpace(row.ClassName) ? string.Empty : $" · Lớp: {row.ClassName}")),
                UiKit.Caption($"Ghi nhận bởi: {row.CreatorName}", UiKit.TextSecondary),
                UiKit.StatusBadge(DomainText.AchievementStatus(row.Achievement.Status), StatusColor(row.Achievement.Status))
            }
        };
        if (!string.IsNullOrWhiteSpace(row.Achievement.EventName))
            content.Children.Add(UiKit.Caption($"Sự kiện: {row.Achievement.EventName}"));
        if (!string.IsNullOrWhiteSpace(row.Achievement.Reason))
            content.Children.Add(UiKit.Caption($"Lý do: {row.Achievement.Reason}"));
        if (!string.IsNullOrWhiteSpace(row.Achievement.ReviewNote))
            content.Children.Add(UiKit.Caption($"Phản hồi: {row.Achievement.ReviewNote}"));

        var actions = new HorizontalStackLayout { Spacing = 8 };
        if (RoleCapabilities.CanReviewAchievements(role)
            && row.Achievement.Status == AchievementStatus.Pending)
        {
            var approve = UiKit.PrimaryButton("Xác nhận");
            approve.Clicked += async (_, _) => await ReviewAsync(row, true, approve);
            var reject = UiKit.DestructiveButton("Từ chối");
            reject.Clicked += async (_, _) => await ReviewAsync(row, false, reject);
            actions.Children.Add(approve);
            actions.Children.Add(reject);
        }
        if (RoleCapabilities.CanRemoveAchievements(role)
            && row.Achievement.Status != AchievementStatus.Removed)
        {
            var remove = UiKit.DestructiveButton("Gỡ biểu trưng");
            remove.Clicked += async (_, _) =>
            {
                if (!await DisplayAlertAsync("Gỡ thành tích",
                        "Biểu trưng sẽ bị gỡ khỏi danh sách hiển thị nhưng điểm vẫn được giữ lại.",
                        "Gỡ", "Hủy"))
                    return;
                await RunActionAsync(
                    () => _database.RemoveAchievementAsync(CurrentUserId, row.Achievement.Id),
                    remove,
                    reload: true);
            };
            actions.Children.Add(remove);
        }
        var detailsButton = UiKit.SecondaryButton("Xem chi tiết");
        detailsButton.Clicked += async (_, _) =>
            await PushPageAsync(new AchievementDetailsPage(row));
        actions.Children.Add(detailsButton);
        if (actions.Children.Count > 0)
            content.Children.Add(actions);
        return UiKit.Card(content, new Thickness(12));
    }

    private View BuildCompactAchievementCard(AchievementRow row, UserRole? role)
    {
        var title = string.IsNullOrWhiteSpace(row.Achievement.Title)
            ? row.Badge.Name
            : row.Achievement.Title;
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        grid.Children.Add(AchievementBadgeUi.BadgeImage(row.Badge, 28));
        var text = new VerticalStackLayout
        {
            Spacing = 1,
            Children =
            {
                UiKit.Headline(title),
                UiKit.Caption($"{row.TraineeName} · {row.Achievement.AwardedForDateUtc.ToLocalTime():dd/MM/yyyy}")
            }
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var right = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.End,
            Children =
            {
                UiKit.StatusBadge(
                    row.Achievement.Points >= 0 ? $"+{row.Achievement.Points}" : row.Achievement.Points.ToString(),
                    row.Achievement.Points >= 0 ? UiKit.Success : UiKit.Danger),
                UiKit.Caption(DomainText.AchievementStatus(row.Achievement.Status))
            }
        };
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        return UiKit.Card(grid, new Thickness(10));
    }

    private async Task ReviewAsync(AchievementRow row, bool approved, Button source)
    {
        source.IsEnabled = false;
        try
        {
            var note = approved
                ? string.Empty
                : await DisplayPromptAsync("Từ chối đề xuất", "Nêu lý do (không bắt buộc):", "Gửi", "Hủy");
            if (note is null)
                return;
            await _database.ReviewAchievementAsync(CurrentUserId, row.Achievement.Id, approved, note);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Chưa thể duyệt thành tích", UserMessage(exception), "Đóng");
        }
        finally
        {
            source.IsEnabled = true;
        }
    }

    private AchievementCategory? SelectedCategory() => _categoryPicker.SelectedIndex switch
    {
        1 => AchievementCategory.MatchRanking,
        2 => AchievementCategory.WeeklyClassRanking,
        _ => null
    };

    private static Color StatusColor(AchievementStatus status) => status switch
    {
        AchievementStatus.Approved => UiKit.Success,
        AchievementStatus.Removed or AchievementStatus.Expired => UiKit.TextSecondary,
        AchievementStatus.Rejected => UiKit.Danger,
        _ => UiKit.Warning
    };
}

/// <summary>
/// Read-only detail view for one achievement. Review and remove actions stay
/// on the hub so the role-scoped feed is refreshed in one place.
/// </summary>
public sealed class AchievementDetailsPage : ContentPage
{
    public AchievementDetailsPage(AchievementRow row)
    {
        var title = string.IsNullOrWhiteSpace(row.Achievement.Title)
            ? row.Badge.Name
            : row.Achievement.Title;
        Title = title;
        BackgroundColor = UiKit.Background;

        var points = row.Achievement.Points >= 0
            ? $"+{row.Achievement.Points} điểm"
            : $"{row.Achievement.Points} điểm";
        var details = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                AchievementBadgeUi.BadgeImage(row.Badge, 96),
                UiKit.LargeTitle(title),
                UiKit.StatusBadge(points,
                    row.Achievement.Points >= 0 ? UiKit.Success : UiKit.Danger),
                UiKit.StatusBadge(
                    DomainText.AchievementStatus(row.Achievement.Status),
                    row.Achievement.Status == AchievementStatus.Approved
                        ? UiKit.Success
                        : row.Achievement.Status == AchievementStatus.Rejected
                            ? UiKit.Danger
                            : UiKit.TextSecondary),
                UiKit.Caption($"Biểu trưng: {row.Badge.Name}"),
                UiKit.Caption($"Hạng mục: {DomainText.AchievementCategory(row.Achievement.Category)}"),
                UiKit.Caption($"Cầu thủ học viên: {row.TraineeName}"),
                UiKit.Caption($"Ghi nhận bởi: {row.CreatorName}"),
                UiKit.Caption($"Ngày ghi nhận: {row.Achievement.AwardedForDateUtc.ToLocalTime():dd/MM/yyyy}"),
                string.IsNullOrWhiteSpace(row.ClassName)
                    ? UiKit.Caption("Lớp học: Không gắn lớp")
                    : UiKit.Caption($"Lớp học: {row.ClassName}")
            }
        };

        if (!string.IsNullOrWhiteSpace(row.Achievement.EventName))
            details.Children.Add(UiKit.Caption($"Sự kiện: {row.Achievement.EventName}"));
        if (!string.IsNullOrWhiteSpace(row.Achievement.Reason))
            details.Children.Add(UiKit.Caption($"Lý do: {row.Achievement.Reason}"));
        if (!string.IsNullOrWhiteSpace(row.Achievement.ReviewNote))
            details.Children.Add(UiKit.Caption($"Phản hồi duyệt: {row.Achievement.ReviewNote}"));
        if (row.Achievement.Status == AchievementStatus.Approved)
        {
            details.Children.Add(UiKit.Caption(
                $"Hiển thị đến: {row.Achievement.VisibleUntilUtc.ToLocalTime():dd/MM/yyyy}",
                UiKit.TextSecondary));
        }

        Content = UiKit.ScrollBody(UiKit.Card(details));
    }
}

/// <summary>Founder/Coach form for proposing or directly awarding a badge.</summary>
public sealed class AchievementCreatePage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly Picker _category = new() { Title = "Hạng mục thành tích" };
    private readonly Picker _badge = new() { Title = "Biểu trưng" };
    private readonly Picker _trainee = new() { Title = "Cầu thủ học viên" };
    private readonly Picker _class = new() { Title = "Lớp học (Coach bắt buộc chọn)" };
    private readonly Entry _title = new() { Placeholder = "Tiêu đề (không bắt buộc)" };
    private readonly Entry _event = new() { Placeholder = "Tên giao hữu / giải đấu (không bắt buộc)" };
    private readonly Editor _reason = new()
    {
        Placeholder = "Lý do ghi nhận (Coach bắt buộc nhập)",
        AutoSize = EditorAutoSizeOption.TextChanges,
        MinimumHeightRequest = 80
    };
    private readonly DatePicker _date = new() { Date = DateTime.Today, Format = "dd/MM/yyyy" };
    private readonly VerticalStackLayout _badgePreview = new()
    {
        Spacing = 2,
        HorizontalOptions = LayoutOptions.Center
    };
    private IReadOnlyList<AchievementBadge> _badges = [];
    private IReadOnlyList<MemberRow> _trainees = [];
    private IReadOnlyList<ClassRow> _classes = [];
    private bool _loaded;

    public AchievementCreatePage(AppDatabase database, SessionService session)
        : base(session, "Thêm thành tích")
    {
        _database = database;
        _category.ItemsSource = new[]
        {
            DomainText.AchievementCategory(AchievementCategory.MatchRanking),
            DomainText.AchievementCategory(AchievementCategory.WeeklyClassRanking)
        };
        _category.SelectedIndex = 0;
        _category.SelectedIndexChanged += (_, _) => RefreshBadges();
        _badge.SelectedIndexChanged += (_, _) => RefreshBadgePreview();
        _badge.ItemDisplayBinding = new Binding(nameof(AchievementBadge.Name));
        _trainee.ItemDisplayBinding = new Binding(nameof(MemberRow.DisplayName));
        _class.ItemDisplayBinding = new Binding(nameof(ClassRow.Class.Name));

        var save = UiKit.PrimaryButton("Lưu thành tích");
        save.Clicked += async (_, _) => await SaveAsync(save);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.Caption("Founder/Đồng Sáng lập được xác nhận trực tiếp. Đề xuất của Coach sẽ chờ Founder duyệt.", UiKit.TextSecondary),
                UiKit.LabeledField("HẠNG MỤC", _category),
                UiKit.LabeledField("BIỂU TRƯNG", _badge),
                _badgePreview,
                UiKit.LabeledField("CẦU THỦ HỌC VIÊN", _trainee),
                UiKit.LabeledField("LỚP HỌC", _class),
                UiKit.LabeledField("TIÊU ĐỀ", _title),
                UiKit.LabeledField("SỰ KIỆN", _event),
                UiKit.LabeledField("NGÀY GHI NHẬN", _date),
                UiKit.LabeledField("LÝ DO", _reason),
                save
            }
        };
        Content = UiKit.KeyboardAwareScroll(root);
    }

    protected override async Task LoadAsync()
    {
        if (_loaded)
            return;
        var role = Session.CurrentUser?.Role;
        if (!RoleCapabilities.CanCreateAchievements(role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền thêm thành tích.");

        _badges = await _database.GetAchievementBadgesAsync(CurrentUserId);
        _trainees = await _database.GetMembersAsync(CurrentUserId, UserRole.Trainee);
        _classes = await _database.GetClassesAsync(CurrentUserId);
        _trainee.ItemsSource = _trainees.ToList();
        _class.ItemsSource = _classes.ToList();
        RefreshBadges();
        _loaded = true;
    }

    private void RefreshBadges()
    {
        var category = _category.SelectedIndex == 1
            ? AchievementCategory.WeeklyClassRanking
            : AchievementCategory.MatchRanking;
        _badge.ItemsSource = _badges.Where(item => item.Category == category).ToList();
        if (_badge.ItemsSource is System.Collections.IList list && list.Count > 0)
            _badge.SelectedIndex = 0;
        RefreshBadgePreview();
    }

    private void RefreshBadgePreview()
    {
        _badgePreview.Children.Clear();
        if (_badge.SelectedItem is not AchievementBadge badge)
            return;

        var image = AchievementBadgeUi.BadgeImage(badge, 70);
        image.HorizontalOptions = LayoutOptions.Center;
        _badgePreview.Children.Add(image);
        _badgePreview.Children.Add(UiKit.Caption(
            badge.Points >= 0 ? $"+{badge.Points} điểm" : $"{badge.Points} điểm",
            badge.Points >= 0 ? UiKit.Success : UiKit.Danger));
    }

    private async Task SaveAsync(Button source)
    {
        source.IsEnabled = false;
        try
        {
            if (_trainee.SelectedItem is not MemberRow trainee
                || _badge.SelectedItem is not AchievementBadge badge)
            {
                await DisplayAlertAsync("Thiếu thông tin", "Vui lòng chọn Cầu thủ học viên và biểu trưng.", "Đóng");
                return;
            }
            var role = Session.CurrentUser?.Role;
            var classRow = _class.SelectedItem as ClassRow;
            if (role == UserRole.Coach && classRow is null)
            {
                await DisplayAlertAsync("Thiếu lớp học", "Coach phải chọn lớp học được phân công.", "Đóng");
                return;
            }
            if (badge.Category == AchievementCategory.WeeklyClassRanking && classRow is null)
            {
                await DisplayAlertAsync("Thiếu lớp học", "Hạng mục xếp hạng lớp học theo tuần cần chọn lớp.", "Đóng");
                return;
            }

            await _database.CreateAchievementAsync(CurrentUserId, new TraineeAchievement
            {
                TraineeUserId = trainee.Account.Id,
                BadgeId = badge.Id,
                Category = badge.Category,
                ClassId = classRow?.Class.Id ?? string.Empty,
                Title = _title.Text?.Trim() ?? string.Empty,
                EventName = _event.Text?.Trim() ?? string.Empty,
                Reason = _reason.Text?.Trim() ?? string.Empty,
                AwardedForDateUtc = _date.Date.GetValueOrDefault().ToUniversalTime()
            });

            if (Navigation.NavigationStack.OfType<AchievementHubPage>().LastOrDefault() is { } parent)
                parent.InvalidateLoadCache();
            await Navigation.PopAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Chưa thể thêm thành tích", UserMessage(exception), "Đóng");
        }
        finally
        {
            source.IsEnabled = true;
        }
    }
}
