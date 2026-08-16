using System.Globalization;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

public sealed class ClassListPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly RememberedLoginService _rememberedLogin;

    public ClassListPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin)
        : base(session, session.CurrentUser?.Role == UserRole.Trainee ? string.Empty : "Lớp học")
    {
        _database = database;
        _media = media;
        _rememberedLogin = rememberedLogin;
    }

    protected override async Task LoadAsync()
    {
        var classes = await _database.GetClassesAsync(CurrentUserId);
        var role = Session.CurrentUser?.Role ?? UserRole.Founder;
        var activeClasses = classes.Where(item => item.Class.IsActive).ToList();
        var inactiveClasses = classes.Where(item => !item.Class.IsActive).ToList();
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = 9,
            Children =
            {
                UiKit.OfflineBanner()
            }
        };
        if (activeClasses.Count == 0 && inactiveClasses.Count == 0)
        {
            var action = role == UserRole.Founder
                ? UiKit.PrimaryButton("Tạo lớp đầu tiên", async (_, _) =>
                    await PushPageAsync(new ClassEditorPage(_database, Session)))
                : null;
            root.Children.Add(UiKit.EmptyState(
                "Chưa có lớp học",
                role == UserRole.Founder
                    ? "Tạo lớp để thêm lịch, Coach và học viên."
                    : "Founder chưa phân công lớp cho account này.",
                action));
        }
        else
        {
            var sessionsByClass = new Dictionary<string, IReadOnlyList<TrainingSession>>();
            foreach (var row in classes)
            {
                sessionsByClass[row.Class.Id] = await _database.GetSessionsForClassAsync(
                    CurrentUserId,
                    row.Class.Id,
                    limit: 400);
            }

            IReadOnlyList<CoachCheckInHistoryRow> coachHistory = [];
            if (role is UserRole.Founder or UserRole.Coach)
            {
                coachHistory = await _database.GetCoachCheckInHistoryAsync(CurrentUserId);
            }

            var visibleClasses = role == UserRole.Founder ? activeClasses : activeClasses;
            root.Children.Add(WeeklyScheduleView.Build(
                visibleClasses,
                role,
                sessionsByClass,
                coachHistory,
                row => PushPageAsync(new ClassDetailsPage(
                    _database,
                    Session,
                    _media,
                    _rememberedLogin,
                    row)),
                role == UserRole.Founder
                    ? async () => await PushPageAsync(new ClassEditorPage(_database, Session))
                    : null,
                role == UserRole.Founder
                    ? async (row, date) => await PushPageAsync(new AttendancePage(
                        _database,
                        Session,
                        row,
                        date,
                        historicalMode: true))
                    : null));
            if (role == UserRole.Founder)
            {
                if (activeClasses.Count > 0)
                {
                    var fixedClassesButton = UiKit.PrimaryButton(
                        "Lớp học cố định trong tháng",
                        async (_, _) => await PushPageAsync(
                            new FounderFixedClassesPage(
                                _database,
                                Session,
                                _media,
                                _rememberedLogin)));
                    root.Children.Add(fixedClassesButton);
                }

                var history = UiKit.SecondaryButton(
                    "L\u1ecbch s\u1eed l\u1edbp h\u1ecdc",
                    async (_, _) => await PushPageAsync(
                        new FounderClassHistoryPage(
                            _database,
                            Session,
                            _media,
                            _rememberedLogin)));
                root.Children.Add(history);

                var coachTeachingHistory = UiKit.SecondaryButton(
                    "L\u1ecbch s\u1eed d\u1ea1y h\u1ecdc Hu\u1ea5n Luy\u1ec7n Vi\u00ean",
                    async (_, _) => await PushPageAsync(
                        new CoachCheckInHistoryPage(_database, Session)));
                root.Children.Add(coachTeachingHistory);

                if (inactiveClasses.Count > 0)
                {
                    root.Children.Add(UiKit.Title("Lớp học ngừng hoạt động"));
                    root.Children.Add(UiKit.Caption(
                        "Các lớp này không còn xuất hiện trong lịch dạy mới; dữ liệu lịch sử vẫn được giữ."));
                    foreach (var row in inactiveClasses)
                    {
                        root.Children.Add(CreateClassCard(row, showInactiveStatus: true));
                    }
                }
            }

        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private static bool IsFounderTodayClass(ClassRow row)
    {
        var today = DateTime.Today;
        return row.Class.IsActive
               && row.Class.StartDate.Date <= today
               && row.Class.ScheduleDays
                   .Split(',', StringSplitOptions.RemoveEmptyEntries)
                   .Any(value => int.TryParse(value, out var day)
                                 && day == (int)today.DayOfWeek);
    }

    private View CreateClassCard(ClassRow row, bool showInactiveStatus = false)
    {
        var children = new List<View>
        {
            UiKit.Headline(row.Class.Name),
            UiKit.Body(row.ScheduleText, UiKit.TextSecondary),
            UiKit.Body($"Coach: {row.CoachNames}", UiKit.TextSecondary),
            UiKit.Body($"Sân: {row.Venue?.Name ?? "Chưa cập nhật"}", UiKit.TextSecondary)
        };
        if (showInactiveStatus)
        {
            children.Add(UiKit.StatusBadge("Ngừng hoạt động", UiKit.TextSecondary));
        }
        var content = new VerticalStackLayout { Spacing = 5 };
        foreach (var child in children)
        {
            content.Children.Add(child);
        }
        var card = UiKit.Card(content);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            await PushPageAsync(new ClassDetailsPage(
                _database,
                Session,
                _media,
                _rememberedLogin,
                row));
        };
        card.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(card, $"Mở lớp {row.Class.Name}");
        return card;
    }
}

/// <summary>
/// Founder-only view of the recurring classes that are currently active.
/// The same fixed-class content is reachable from the dashboard metric and
/// from the class tab, keeping both entry points consistent.
/// </summary>
public sealed class FounderFixedClassesPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly RememberedLoginService _rememberedLogin;

    public FounderFixedClassesPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin)
        : base(session, "Lớp học cố định")
    {
        _database = database;
        _media = media;
        _rememberedLogin = rememberedLogin;
    }

    protected override async Task LoadAsync()
    {
        var classes = (await _database.GetClassesAsync(CurrentUserId))
            .Where(item => item.Class.IsActive
                           && !string.IsNullOrWhiteSpace(item.Class.ScheduleDays))
            .OrderBy(item => item.Class.StartTimeMinutes)
            .ThenBy(item => item.Class.Name)
            .ToList();
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.Caption($"Các lớp học cố định trong tháng {DateTime.Today:MM/yyyy}.")
            }
        };

        if (classes.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có lớp học cố định",
                "Các lớp đang hoạt động có lịch cố định sẽ hiển thị tại đây."));
        }
        else
        {
            foreach (var row in classes)
            {
                root.Children.Add(CreateClassCard(row));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private View CreateClassCard(ClassRow row)
    {
        var content = new VerticalStackLayout
        {
            Spacing = 5,
            Children =
            {
                UiKit.Headline(row.Class.Name),
                UiKit.Body(row.ScheduleText, UiKit.TextSecondary),
                UiKit.Body($"Coach: {row.CoachNames}", UiKit.TextSecondary),
                UiKit.Body($"Sân: {row.Venue?.Name ?? "Chưa cập nhật"}", UiKit.TextSecondary)
            }
        };
        var card = UiKit.Card(content);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Navigation.PushAsync(new ClassDetailsPage(
            _database,
            Session,
            _media,
            _rememberedLogin,
            row));
        card.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(card, $"Mở lớp {row.Class.Name}");
        return card;
    }

}

public sealed class FounderClassHistoryPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly RememberedLoginService _rememberedLogin;

    public FounderClassHistoryPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin)
        : base(session, "L\u1ecbch s\u1eed l\u1edbp h\u1ecdc")
    {
        _database = database;
        _media = media;
        _rememberedLogin = rememberedLogin;
    }

    protected override async Task LoadAsync()
    {
        var classes = await _database.GetClassesAsync(CurrentUserId);
        var sessionsByClass = new Dictionary<string, IReadOnlyList<TrainingSession>>();
        foreach (var row in classes)
        {
            sessionsByClass[row.Class.Id] = await _database.GetSessionsForClassAsync(
                CurrentUserId,
                row.Class.Id,
                limit: 400);
        }

        var coachHistory = await _database.GetCoachCheckInHistoryAsync(CurrentUserId);
        var checkInsBySession = coachHistory
            .GroupBy(item => item.CheckIn.SessionId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.CheckIn).ToList());
        var today = DateTime.Today;
        var history = classes
            .SelectMany(row => sessionsByClass.GetValueOrDefault(row.Class.Id)
                ?.Where(session => session.SessionDate.Date < today
                                   && session.SessionDate.Date >= row.Class.StartDate.Date)
                    .Select(session => new HistoryItem(
                        row,
                        session,
                        session.Status is SessionStatus.Submitted or SessionStatus.Locked
                        || checkInsBySession.GetValueOrDefault(session.Id)
                            ?.Any(CoachCheckInTime.HasCoachCheckout) == true,
                        checkInsBySession.GetValueOrDefault(session.Id)
                            ?.Any(CoachCheckInTime.IsFounderSubstitution) == true))
                ?? Array.Empty<HistoryItem>())
            .OrderByDescending(item => item.Session.SessionDate)
            .ThenBy(item => item.Row.Class.StartTimeMinutes)
            .ToList();

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
            }
        };

        if (history.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Ch\u01b0a c\u00f3 l\u1ecbch s\u1eed",
                "C\u00e1c bu\u1ed5i h\u1ecdc \u0111\u00e3 ho\u00e0n t\u1ea5t s\u1ebd \u0111\u01b0\u1ee3c chuy\u1ec3n v\u00e0o \u0111\u00e2y."));
        }
        else
        {
            foreach (var item in history)
            {
                var statusText = item.Taught ? "\u0110\u00e3 d\u1ea1y" : "Coach kh\u00f4ng d\u1ea1y";
                var statusColor = item.Taught ? UiKit.Success : UiKit.Danger;
                var dateLabel = new Label
                {
                    Text = item.Session.SessionDate.ToString("dd/MM/yyyy"),
                    FontFamily = "OpenSansSemibold",
                    FontSize = 11,
                    TextColor = UiKit.TextPrimary,
                    LineBreakMode = LineBreakMode.NoWrap,
                    MaxLines = 1,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    MinimumWidthRequest = 78
                };
                var summary = new VerticalStackLayout
                {
                    Spacing = 1,
                    Children =
                    {
                        UiKit.Body(item.Row.Class.Name, UiKit.TextPrimary),
                        UiKit.Caption($"Coach: {item.Row.CoachNames}"),
                        UiKit.Caption(
                            $"{DomainText.TimeRange(item.Row.Class.StartTimeMinutes, item.Row.Class.EndTimeMinutes)} \u00b7 {item.Row.Venue?.Name ?? "Ch\u01b0a c\u1eadp nh\u1eadt s\u00e2n"}",
                            UiKit.TextSecondary)
                    }
                };
                if (item.CoachSubstituted)
                {
                    summary.Children.Add(UiKit.Caption(
                        "Founder \u0111i\u1ec3m danh thay Coach",
                        UiKit.Warning));
                }
                var badge = UiKit.StatusBadge(statusText, statusColor);
                summary.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, 1);
                badge.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, 2);
                var contentGrid = new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(82)),
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    },
                    Children = { dateLabel, summary, badge }
                };
                var card = UiKit.Card(contentGrid, new Thickness(10));
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) => await Navigation.PushAsync(
                    new ClassHistoryDetailsPage(
                        _database,
                        Session,
                        _media,
                        _rememberedLogin,
                        item.Row,
                        item.Session));
                card.GestureRecognizers.Add(tap);
                root.Children.Add(card);
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private sealed record HistoryItem(
        ClassRow Row,
        TrainingSession Session,
        bool Taught,
        bool CoachSubstituted);
}

/// <summary>
/// Read-only detail for one completed class session. It is intentionally
/// separate from the current class detail so a historical snapshot cannot
/// show newly enrolled trainees or expose Founder edit/delete actions.
/// </summary>
public sealed class ClassHistoryDetailsPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly RememberedLoginService _rememberedLogin;
    private readonly ClassRow _row;
    private readonly TrainingSession _sessionRecord;

    public ClassHistoryDetailsPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin,
        ClassRow row,
        TrainingSession sessionRecord)
        : base(session, row.Class.Name)
    {
        _database = database;
        _media = media;
        _rememberedLogin = rememberedLogin;
        _row = row;
        _sessionRecord = sessionRecord;
    }

    protected override async Task LoadAsync()
    {
        var roster = await _database.GetAttendanceRosterAsync(
            CurrentUserId,
            _sessionRecord.Id,
            historicalSnapshot: true);
        var sessionCheckIns = await _database.GetCoachCheckInsForSessionAsync(
            CurrentUserId,
            _sessionRecord.Id);
        var substituted = sessionCheckIns.Any(item =>
            CoachCheckInTime.IsFounderSubstitution(item.CheckIn));
        var taught = _sessionRecord.Status is SessionStatus.Submitted or SessionStatus.Locked
                     || sessionCheckIns.Any(item => CoachCheckInTime.HasCoachCheckout(item.CheckIn));
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children = { }
        };

        var details = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                UiKit.LargeTitle(_row.Class.Name),
                UiKit.Body($"Ngày học: {_sessionRecord.SessionDate:dd/MM/yyyy}"),
                UiKit.Body(
                    $"{DomainText.TimeRange(_row.Class.StartTimeMinutes, _row.Class.EndTimeMinutes)} · Sân: {_row.Venue?.Name ?? "Chưa cập nhật sân"}",
                    UiKit.TextSecondary),
                UiKit.Body($"Địa chỉ: {_row.Venue?.Address ?? "Chưa cập nhật"}", UiKit.TextSecondary),
                UiKit.Body($"Coach: {_row.CoachNames}", UiKit.TextSecondary),
                UiKit.StatusBadge(
                    substituted
                        ? "Đã dạy · Founder điểm danh thay Coach"
                        : taught ? "Đã dạy" : "Coach không dạy",
                    substituted || taught ? UiKit.Success : UiKit.Danger)
            }
        };
        root.Children.Add(UiKit.Card(details));

        root.Children.Add(UiKit.Title("Huấn luyện viên"));
        if (_row.Coaches.Count == 0)
        {
            root.Children.Add(UiKit.Caption("Chưa phân công Coach."));
        }
        else
        {
            foreach (var coach in _row.Coaches)
            {
                root.Children.Add(CreateHistoryMemberCard(coach));
            }
        }

        root.Children.Add(UiKit.Title("Cầu thủ học viên"));

        if (roster.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có dữ liệu điểm danh",
                "Buổi học này chưa lưu danh sách học viên đã được ghi nhận."));
        }
        else
        {
            var members = (await _database.GetMembersAsync(CurrentUserId, UserRole.Trainee))
                .ToDictionary(item => item.Account.Id, StringComparer.Ordinal);
            var invoices = await _database.GetInvoicesAsync(CurrentUserId);
            var enrollments = await _database.GetClassEnrollmentsAsync(_row.Class.Id);
            foreach (var attendance in roster)
            {
                if (!members.TryGetValue(attendance.TraineeUserId, out var member))
                {
                    member = new MemberRow(
                        new UserAccount
                        {
                            Id = attendance.TraineeUserId,
                            Username = attendance.TraineeName,
                            Role = UserRole.Trainee
                        },
                        new PersonProfile
                        {
                            UserId = attendance.TraineeUserId,
                            FullName = attendance.TraineeName,
                            PhotoPath = attendance.PhotoPath
                        });
                }

                var invoice = invoices
                    .Where(item => item.Invoice.ClassId == _row.Class.Id
                                   && item.Invoice.TraineeUserId == attendance.TraineeUserId)
                    .OrderByDescending(item => item.Invoice.CycleNumber)
                    .FirstOrDefault();
                var enrollment = enrollments.FirstOrDefault(item =>
                    item.TraineeUserId == attendance.TraineeUserId);
                var progress = member.Account.IsTuitionSupported
                    ? new TuitionCycleProgress(0, 0, false, false)
                    : invoice?.Progress
                      ?? await _database.GetDisplayedTuitionProgressAsync(
                          CurrentUserId,
                          attendance.TraineeUserId,
                          _row.Class.Id,
                          invoice?.Invoice);

                root.Children.Add(CreateHistoryTraineeCard(
                    member,
                    attendance,
                    enrollment,
                    progress));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private View CreateHistoryMemberCard(MemberRow member)
    {
        var grid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(52),
                new ColumnDefinition(GridLength.Star)
            }
        };
        grid.Children.Add(UiKit.Avatar(member.Profile.PhotoPath, 48));
        var text = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                UiKit.Headline(member.DisplayName),
                member.Account.Role == UserRole.Coach
                    ? UiKit.Caption(CoachPositionCatalog.Label(member.Profile.CoachPosition), UiKit.Primary)
                    : UiKit.Caption(DomainText.Role(member.Account.Role)),
            }
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var card = UiKit.Card(grid, new Thickness(12));
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
            await Navigation.PushAsync(new MemberProfilePage(
                _database,
                Session,
                _media,
                _rememberedLogin,
                member.Account.Id));
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private View CreateHistoryTraineeCard(
        MemberRow member,
        AttendanceRosterItem attendance,
        ClassEnrollment? enrollment,
        TuitionCycleProgress progress)
    {
        var grid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(52),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        grid.Children.Add(UiKit.Avatar(member.Profile.PhotoPath, 48));
        var text = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                UiKit.Headline(member.DisplayName),
                UiKit.Caption(DomainText.Role(member.Account.Role)),
                UiKit.Caption(
                    member.Account.IsTuitionSupported
                        ? "Miễn phí"
                        : enrollment?.IsTrial == true
                            ? $"Tiến độ học thử: {progress.AttendedSessions}/{Math.Clamp(enrollment.TrialSessionCount, 1, 5)} buổi"
                            : $"Tiến độ: {progress.AttendedSessions}/{progress.PlannedSessions} buổi",
                    UiKit.TextSecondary)
            }
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var status = UiKit.StatusBadge(
            DomainText.Attendance(attendance.Status),
            UiKit.AttendanceColor(attendance.Status));
        status.HorizontalOptions = LayoutOptions.End;
        status.VerticalOptions = LayoutOptions.Start;
        Grid.SetColumn(status, 2);
        grid.Children.Add(status);
        var card = UiKit.Card(grid, new Thickness(12));
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
            await Navigation.PushAsync(new MemberProfilePage(
                _database,
                Session,
                _media,
                _rememberedLogin,
                member.Account.Id));
        card.GestureRecognizers.Add(tap);
        return card;
    }

}

public sealed class ClassDetailsPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly SessionService _session;
    private readonly ClassRow _row;
    private readonly VerticalStackLayout _traineeHost = new() { Spacing = UiKit.SectionSpacing };
    private bool _traineesLoaded;

    public ClassDetailsPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin,
        ClassRow row)
    {
        _database = database;
        _session = session;
        _row = row;
        Title = row.Class.Name;
        BackgroundColor = UiKit.Background;
        var root = new VerticalStackLayout { Spacing = UiKit.SectionSpacing };
        var taughtSessions = UiKit.Body("Coach đã dạy: đang tải...", UiKit.TextSecondary);
        var details = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                UiKit.LargeTitle(row.Class.Name),
                UiKit.Body(row.ScheduleText),
                UiKit.Body($"Sân: {row.Venue?.Name ?? "Chưa cập nhật"}"),
                UiKit.Body($"Địa chỉ: {row.Venue?.Address ?? "Chưa cập nhật"}", UiKit.TextSecondary)
            }
        };
        if (session.CurrentUser?.Role == UserRole.Founder)
        {
            details.Children.Add(UiKit.StatusBadge(
                row.Class.IsActive ? "Đang hoạt động" : "Ngừng hoạt động",
                row.Class.IsActive ? UiKit.Success : UiKit.TextSecondary));
        }
        if (session.CurrentUser?.Role != UserRole.Coach)
        {
            details.Children.Add(UiKit.Body(
                $"Học phí: {Math.Max(1, row.Class.TuitionSessionCount)} buổi / chu kỳ · {UiKit.Money(row.Class.DefaultFeeVnd)}",
                UiKit.TextSecondary));
        }

        root.Children.Add(UiKit.Card(details));

        Button? evaluationRequestButton = null;
        var evaluationRequestOpen = false;
        if (session.CurrentUser?.Role == UserRole.Founder)
        {
            evaluationRequestButton = UiKit.SecondaryButton("Mở yêu cầu Coach đánh giá lớp");
            evaluationRequestButton.Clicked += async (_, _) =>
            {
                evaluationRequestButton.IsEnabled = false;
                try
                {
                    await database.SetTraineeEvaluationRequestAsync(
                        session.CurrentUser?.Id
                        ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                        row.Class.Id,
                        !evaluationRequestOpen);
                    evaluationRequestOpen = !evaluationRequestOpen;
                    evaluationRequestButton.Text = evaluationRequestOpen
                        ? "Đóng yêu cầu đánh giá Coach"
                        : "Mở yêu cầu Coach đánh giá lớp";
                }
                catch (Exception exception)
                {
                    await DisplayAlertAsync("Chưa thể cập nhật yêu cầu", exception.Message, "Đóng");
                }
                finally
                {
                    evaluationRequestButton.IsEnabled = true;
                }
            };
            root.Children.Add(evaluationRequestButton);

            var edit = UiKit.PrimaryButton("Sửa lớp học");
            edit.Clicked += async (_, _) =>
                await Navigation.PushAsync(new ClassEditorPage(database, session, row));
            root.Children.Add(edit);

            var delete = UiKit.DestructiveButton("Xóa lớp học");
            delete.Clicked += async (_, _) =>
            {
                var confirmed = await DisplayAlertAsync(
                    "Xóa lớp học?",
                    "Lớp, lịch học, điểm danh và dữ liệu học phí liên quan sẽ bị xóa vĩnh viễn. Lương Coach theo tháng vẫn được giữ.",
                    "Xóa lớp",
                    "Hủy");
                if (!confirmed)
                {
                    return;
                }

                delete.IsEnabled = false;
                try
                {
                    await database.DeleteClassAsync(
                        session.CurrentUser?.Id
                        ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                        row.Class.Id);
                    await Navigation.PopAsync();
                }
                catch (Exception exception)
                {
                    delete.IsEnabled = true;
                    await DisplayAlertAsync("Chưa thể xóa lớp", exception.Message, "Đóng");
                }
            };
            root.Children.Add(delete);
        }

        root.Children.Add(UiKit.Title("Huấn luyện viên"));
        if (row.Coaches.Count == 0)
        {
            root.Children.Add(UiKit.Caption("Chưa phân công Coach."));
        }
        else
        {
            foreach (var coach in row.Coaches)
            {
                root.Children.Add(MemberCard(
                    coach,
                    database,
                    session,
                    media,
                    rememberedLogin));
            }
        }

        root.Children.Add(taughtSessions);
        root.Children.Add(_traineeHost);

        Content = UiKit.ScrollBody(root);
        Loaded += async (_, _) =>
        {
            try
            {
                if (evaluationRequestButton is not null)
                {
                    evaluationRequestOpen = await _database.IsTraineeEvaluationRequestOpenAsync(
                        session.CurrentUser?.Id
                        ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                        row.Class.Id);
                    evaluationRequestButton.Text = evaluationRequestOpen
                        ? "Đóng yêu cầu đánh giá Coach"
                        : "Mở yêu cầu Coach đánh giá lớp";
                }

                var count = await _database.GetClassTaughtSessionCountAsync(
                    session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                    row.Class.Id);
                taughtSessions.Text = $"Coach đã dạy: {count} buổi";
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Không thể tải tổng số buổi Coach đã dạy: {exception.Message}");
                taughtSessions.Text = "Coach đã dạy: chưa có dữ liệu";
            }

            await LoadTraineesAsync(media, rememberedLogin);
        };
    }

    private async Task LoadTraineesAsync(
        MediaService media,
        RememberedLoginService rememberedLogin)
    {
        if (_traineesLoaded)
        {
            return;
        }

        _traineesLoaded = true;
        var role = _session.CurrentUser?.Role;
        var title = role == UserRole.Trainee
            ? "Cầu thủ học viên cùng lớp"
            : "Cầu thủ học viên";
        _traineeHost.Children.Add(UiKit.Title(title));

        // Founder reviews the tuition state in the class detail itself.  The
        // fixed-class list remains a compact index; all trainee payment and
        // cycle details live alongside the roster shown on this page.
        IReadOnlyList<InvoiceRow> invoices = [];
        IReadOnlyList<ClassEnrollment> enrollments = [];
        if (role == UserRole.Founder)
        {
            invoices = await _database.GetInvoicesAsync(
                _session.CurrentUser?.Id
                ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."));
            enrollments = await _database.GetClassEnrollmentsAsync(_row.Class.Id);
        }

        var canShowTrainees = role is not UserRole.Coach
                              || await IsCoachSessionOpenAsync();
        if (!canShowTrainees)
        {
            _traineeHost.Children.Add(UiKit.EmptyState(
                "Danh sách đang được bảo vệ",
                "Coach cần chụp selfie check-in của buổi hôm nay để mở danh sách. Sau khi check-out, danh sách sẽ tự động ẩn."));
            return;
        }

        if (_row.Trainees.Count == 0)
        {
            _traineeHost.Children.Add(UiKit.Caption("Chưa có học viên."));
            return;
        }

        foreach (var trainee in _row.Trainees)
        {
            var memberCard = MemberCard(
                trainee,
                _database,
                _session,
                media,
                rememberedLogin);
            var canOpenEvaluations = role == UserRole.Founder
                                      || role == UserRole.Coach
                                      || (role == UserRole.Trainee
                                          && trainee.Account.Id == _session.CurrentUser?.Id);
            var memberAndEvaluation = new VerticalStackLayout
            {
                Spacing = 6,
                Children = { memberCard }
            };
            if (canOpenEvaluations)
            {
                var evaluationButton = UiKit.SecondaryButton(
                    role == UserRole.Coach && trainee.Account.Id != _session.CurrentUser?.Id
                        ? "Đánh giá / lịch sử"
                        : "Lịch sử đánh giá");
                evaluationButton.Clicked += async (_, _) =>
                    await Navigation.PushAsync(new TraineeEvaluationHistoryPage(
                        _database,
                        _session,
                        trainee.Account.Id,
                        trainee.DisplayName,
                        _row.Class.Id));
                memberAndEvaluation.Children.Add(evaluationButton);
            }
            if (role != UserRole.Founder)
            {
                _traineeHost.Children.Add(memberAndEvaluation);
                continue;
            }

            var invoiceRow = invoices
                .Where(item => item.Invoice.ClassId == _row.Class.Id
                               && item.Invoice.TraineeUserId == trainee.Account.Id)
                .OrderByDescending(item => item.Invoice.CycleNumber)
                .FirstOrDefault();
            var enrollment = enrollments.FirstOrDefault(item =>
                item.TraineeUserId == trainee.Account.Id);
            var progress = invoiceRow?.Progress
                           ?? await _database.GetDisplayedTuitionProgressAsync(
                               _session.CurrentUser?.Id
                               ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                               trainee.Account.Id,
                               _row.Class.Id);
            var attendance = await _database.GetClassTraineeAttendanceSummaryAsync(
                _session.CurrentUser?.Id
                ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                _row.Class.Id,
                trainee.Account.Id);
            _traineeHost.Children.Add(new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    memberAndEvaluation,
                    CreateClassAttendanceSummary(attendance),
                    CreateTraineeTuitionSummary(trainee, enrollment, invoiceRow, progress)
                }
            });
        }
    }

    private static View CreateClassAttendanceSummary(MemberAttendanceSummary summary)
    {
        var present = Math.Max(0, summary.AttendedCount - summary.LateCount);
        var details =
            $"Có mặt {present} · Đi trễ {summary.LateCount} · Vắng {summary.AbsentCount} · Có phép {summary.ExcusedCount}";
        return UiKit.Card(
            new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    UiKit.Caption("ĐIỂM DANH", UiKit.TextSecondary),
                    UiKit.Caption(details),
                    UiKit.Caption($"Đã ghi nhận {summary.SubmittedSessionCount} buổi", UiKit.TextSecondary)
                }
            },
            new Thickness(12, 8));
    }

    private static View CreateTraineeTuitionSummary(
        MemberRow trainee,
        ClassEnrollment? enrollment,
        InvoiceRow? invoiceRow,
        TuitionCycleProgress progress)
    {
        var supported = trainee.Account.IsTuitionSupported;
        var isTrial = !supported && enrollment?.IsTrial == true;
        var status = supported
            ? "Miễn học phí"
            : isTrial
                ? "Học thử"
            : invoiceRow?.Invoice.Status switch
            {
                InvoiceStatus.Paid when progress.IsComplete => "Đã đóng · hoàn tất chu kỳ",
                InvoiceStatus.Paid => "Đã đóng",
                InvoiceStatus.ProofSubmitted => "Bill chờ xác nhận",
                InvoiceStatus.Rejected => "Chưa đóng · cần tải lại bill",
                InvoiceStatus.Overdue => "Chưa đóng · quá hạn",
                InvoiceStatus.Pending => "Chưa đóng",
                _ => "Chưa có bill"
            };
        var statusColor = supported
            ? UiKit.Success
            : invoiceRow?.Invoice.Status switch
            {
                InvoiceStatus.Paid => UiKit.Success,
                InvoiceStatus.ProofSubmitted => UiKit.Warning,
                _ => UiKit.Danger
            };
        if (isTrial)
        {
            statusColor = UiKit.Primary;
        }
        var summary = new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                UiKit.Caption("HỌC PHÍ THEO CHU KỲ", UiKit.TextSecondary),
                UiKit.StatusBadge(status, statusColor)
            }
        };
        if (!supported)
        {
            summary.Children.Add(UiKit.Caption(
                $"Tiến độ chu kỳ: {progress.AttendedSessions}/{progress.PlannedSessions} buổi"));
        }
        if (!supported && progress.NeedsPaymentWarning)
        {
            summary.Children.Add(UiKit.Caption(
                "Cảnh báo: đã học đủ buổi thứ 2 nhưng chưa đóng học phí",
                UiKit.Danger));
        }

        return UiKit.Card(summary, new Thickness(12, 8));
    }

    private async Task<bool> IsCoachSessionOpenAsync()
    {
        var coach = _session.CurrentUser;
        if (coach?.Role != UserRole.Coach)
        {
            return true;
        }

        var todayValue = ((int)DateTime.Today.DayOfWeek).ToString();
        var isScheduledToday = _row.Class.ScheduleDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Contains(todayValue);
        if (!isScheduledToday)
        {
            return false;
        }

        try
        {
            var trainingSession = await _database.GetOrCreateSessionAsync(
                coach.Id,
                _row.Class.Id,
                DateTime.Today);
            var checkIn = await _database.GetCoachCheckInAsync(
                trainingSession.Id,
                coach.Id);
            return checkIn is not null
                   && checkIn.CheckedOutAtUtc is null
                   && checkIn.ApprovalStatus != CoachCheckInApprovalStatus.Rejected;
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Không thể kiểm tra quyền xem danh sách học viên: {exception.Message}");
            return false;
        }
    }

    private View MemberCard(
        MemberRow member,
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin)
    {
        var grid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(52),
                new ColumnDefinition(GridLength.Star)
            }
        };
        var avatar = UiKit.Avatar(member.Profile.PhotoPath, 48);
        grid.Children.Add(avatar);
        var text = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                UiKit.Headline(member.DisplayName),
                member.Account.Role == UserRole.Coach
                    ? UiKit.Caption(CoachPositionCatalog.Label(member.Profile.CoachPosition), UiKit.Primary)
                    : UiKit.Caption(DomainText.Role(member.Account.Role))
            }
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var card = UiKit.Card(grid, new Thickness(12));
        if (session.CurrentUser?.Role != UserRole.Trainee
            || member.Account.Role is UserRole.Coach or UserRole.Trainee)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
                await Navigation.PushAsync(new MemberProfilePage(
                    database,
                    session,
                    media,
                    rememberedLogin,
                    member.Account.Id));
            card.GestureRecognizers.Add(tap);
        }

        return card;
    }
}

public sealed class ClassEditorPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly SessionService _session;
    private readonly ClassRow? _existing;
    private readonly Entry _name = new() { Placeholder = "Ví dụ: U10 Cơ bản" };
    private readonly Picker _venue = new() { Title = "Chọn sân" };
    private readonly DatePicker _startDate = new()
    {
        Date = DateTime.Today,
        Format = "dd/MM/yyyy",
        MinimumDate = new DateTime(2020, 1, 1),
        MaximumDate = new DateTime(2100, 12, 31)
    };
    private readonly TimePicker _start = new() { Time = new TimeSpan(17, 0, 0) };
    private readonly TimePicker _end = new() { Time = new TimeSpan(18, 30, 0) };
    private readonly Entry _tuitionSessions = new()
    {
        Placeholder = "Ví dụ: 4",
        Keyboard = Keyboard.Numeric
    };
    private readonly Entry _defaultFee = UiKit.MoneyEntry("Ví dụ: 1,000,000 VNĐ");
    private readonly Dictionary<DayOfWeek, CheckBox> _days = [];
    private readonly Dictionary<string, (CheckBox Check, Entry SalaryPerSession)> _coachRows = [];
    private readonly Dictionary<string, CheckBox> _traineeChecks = [];
    private readonly Dictionary<string, bool> _traineeTuitionSupport = [];
    private readonly Dictionary<string, Switch> _traineeTrialSwitches = [];
    private readonly Dictionary<string, Picker> _traineeTrialPickers = [];
    private readonly VerticalStackLayout _form = new() { Spacing = UiKit.SectionSpacing };
    private bool _loaded;

    public ClassEditorPage(
        AppDatabase database,
        SessionService session,
        ClassRow? existing = null)
    {
        _database = database;
        _session = session;
        _existing = existing;
        Title = existing is null ? "Tạo lớp học" : "Sửa lớp";
        BackgroundColor = UiKit.Background;
        Content = new Grid
        {
            Children =
            {
                new ActivityIndicator
                {
                    IsRunning = true,
                    Color = UiKit.Primary,
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            await BuildFormAsync();
        }
        catch (Exception exception)
        {
            Content = UiKit.ScrollBody(UiKit.EmptyState("Không thể mở lớp", exception.Message));
        }
    }

    private async Task BuildFormAsync()
    {
        var founderId = CurrentFounderId;
        var venues = (await _database.GetVenuesAsync()).ToList();
        var coaches = await _database.GetMembersAsync(founderId, UserRole.Coach);
        var trainees = await _database.GetMembersAsync(founderId, UserRole.Trainee);
        var assignments = _existing is null
            ? Array.Empty<ClassCoachAssignment>()
            : await _database.GetClassCoachesAsync(_existing.Class.Id);
        var enrollments = _existing is null
            ? Array.Empty<ClassEnrollment>()
            : await _database.GetClassEnrollmentsAsync(_existing.Class.Id);
        var attendanceCounts = new Dictionary<string, int>();
        if (_existing is not null)
        {
            foreach (var enrollment in enrollments.Where(item => item.IsActive))
            {
                attendanceCounts[enrollment.TraineeUserId] =
                    await _database.GetCompletedAttendanceCountAsync(
                        CurrentFounderId,
                        _existing.Class.Id,
                        enrollment.TraineeUserId);
            }
        }

        _venue.ItemsSource = venues;
        _venue.ItemDisplayBinding = new Binding(nameof(Venue.Name));
        if (_existing is not null)
        {
            _name.Text = _existing.Class.Name;
            _venue.SelectedItem = venues.FirstOrDefault(item => item.Id == _existing.Class.VenueId);
            _startDate.Date = _existing.Class.StartDate.Date;
            _start.Time = TimeSpan.FromMinutes(_existing.Class.StartTimeMinutes);
            _end.Time = TimeSpan.FromMinutes(_existing.Class.EndTimeMinutes);
            _tuitionSessions.Text = Math.Max(1, _existing.Class.TuitionSessionCount)
                .ToString(CultureInfo.InvariantCulture);
            _defaultFee.Text = UiKit.Money(_existing.Class.DefaultFeeVnd);
        }

        _form.Children.Add(UiKit.LabeledField("TÊN LỚP", _name));
        _form.Children.Add(UiKit.LabeledField("SÂN", _venue));
        _form.Children.Add(UiKit.Caption(
            venues.Count == 0
                ? "Chưa có sân. Hãy tạo sân tại Khác > Quản lý sân trước khi tạo lớp."
                : $"{venues.Count} sân đang hoạt động."));

        _form.Children.Add(UiKit.Headline("Ngày học cố định"));
        _form.Children.Add(UiKit.LabeledField(
            "NG\u00c0Y B\u1eaeT \u0110\u1ea6U",
            _startDate,
            "L\u1ecbch c\u1ed1 \u0111\u1ecbnh ch\u1ec9 hi\u1ec3n th\u1ecb v\u00e0 t\u1ea1o bu\u1ed5i h\u1ecdc t\u1eeb ng\u00e0y n\u00e0y."));

        var existingDays = (_existing?.Class.ScheduleDays ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        View DayCell(DayOfWeek day, string label)
        {
            var check = new CheckBox
            {
                IsChecked = existingDays.Contains(((int)day).ToString()),
                HorizontalOptions = LayoutOptions.Center
            };
            _days[day] = check;
            var labelView = UiKit.Caption(label, UiKit.TextPrimary);
            labelView.HorizontalTextAlignment = TextAlignment.Center;
            var card = UiKit.Card(new VerticalStackLayout
            {
                Spacing = 0,
                Children = { check, labelView }
            }, new Thickness(2, 3));
            return card;
        }

        Grid DayRow(params (DayOfWeek Day, string Label)[] options)
        {
            var grid = new Grid { ColumnSpacing = 6 };
            foreach (var _ in options)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }

            for (var index = 0; index < options.Length; index++)
            {
                var cell = DayCell(options[index].Day, options[index].Label);
                Grid.SetColumn(cell, index);
                grid.Children.Add(cell);
            }

            return grid;
        }

        _form.Children.Add(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                DayRow(
                    (DayOfWeek.Monday, "Thứ 2"),
                    (DayOfWeek.Tuesday, "Thứ 3"),
                    (DayOfWeek.Wednesday, "Thứ 4"),
                    (DayOfWeek.Thursday, "Thứ 5")),
                DayRow(
                    (DayOfWeek.Friday, "Thứ 6"),
                    (DayOfWeek.Saturday, "Thứ 7"),
                    (DayOfWeek.Sunday, "Chủ nhật"))
            }
        });

        var timeGrid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };
        var startField = UiKit.LabeledField("BẮT ĐẦU", _start);
        var endField = UiKit.LabeledField("KẾT THÚC", _end);
        timeGrid.Children.Add(startField);
        Grid.SetColumn(endField, 1);
        timeGrid.Children.Add(endField);
        _form.Children.Add(timeGrid);
        var tuitionGrid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };
        var sessionField = UiKit.LabeledField(
            "SỐ BUỔI / CHU KỲ",
            _tuitionSessions,
            "Số buổi dùng làm mốc tính đơn giá.");
        var feeField = UiKit.LabeledField(
            "TỔNG HỌC PHÍ / CHU KỲ",
            _defaultFee,
            "Điểm danh thực tế × đơn giá/buổi; tháng có 5 tuần sẽ tự tính đủ.");
        tuitionGrid.Children.Add(sessionField);
        Grid.SetColumn(feeField, 1);
        tuitionGrid.Children.Add(feeField);
        _form.Children.Add(tuitionGrid);
        _form.Children.Add(UiKit.Caption(
            "Ví dụ: 4 buổi = 1,000,000 VNĐ thì mỗi buổi là 250,000 VNĐ. Học viên đi 5 buổi sẽ được tính theo 5 buổi thực tế."));

        _form.Children.Add(UiKit.Title("Huấn luyện viên"));
        if (coaches.Count == 0)
        {
            _form.Children.Add(UiKit.EmptyState(
                "Chưa có Coach",
                "Hãy tạo account Coach trong mục Thành viên."));
        }
        else
        {
            foreach (var coach in coaches)
            {
                var assignment = assignments.FirstOrDefault(item =>
                    item.CoachUserId == coach.Account.Id);
                var check = new CheckBox
                {
                    IsChecked = assignment is not null
                };
                var salary = UiKit.MoneyEntry(
                    "Nhập lương mỗi buổi",
                    assignment?.SalaryPerSessionVnd ?? 0);
                var salaryField = UiKit.LabeledField(
                    "LƯƠNG / BUỔI HỌC",
                    salary,
                    "Lương được cộng khi Founder xác nhận selfie check-in của Huấn Luyện Viên.");
                salaryField.IsVisible = check.IsChecked;
                check.CheckedChanged += (_, args) => salaryField.IsVisible = args.Value;
                _coachRows[coach.Account.Id] = (check, salary);

                var coachHeader = new Grid
                {
                    ColumnSpacing = 6,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(44),
                        new ColumnDefinition(GridLength.Star)
                    }
                };
                coachHeader.Children.Add(check);
                var coachName = UiKit.Body(coach.DisplayName);
                coachName.VerticalTextAlignment = TextAlignment.Center;
                Grid.SetColumn(coachName, 1);
                coachHeader.Children.Add(coachName);

                _form.Children.Add(UiKit.Card(new VerticalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        coachHeader,
                        salaryField
                    }
                }, new Thickness(10, 7)));
            }
        }

        _form.Children.Add(UiKit.Title("Cầu thủ học viên"));
        _form.Children.Add(UiKit.Caption(
            $"Học viên được chọn sẽ dùng đúng mức học phí mặc định của lớp; {DomainText.SupportedTraineeLabel} được miễn học phí."));
        if (trainees.Count == 0)
        {
            _form.Children.Add(UiKit.EmptyState(
                "Chưa có học viên",
                "Hãy tạo account Trainee trong mục Thành viên."));
        }
        else
        {
            foreach (var trainee in trainees)
            {
                var check = new CheckBox
                {
                    IsChecked = enrollments.Any(item =>
                        item.TraineeUserId == trainee.Account.Id)
                };
                _traineeChecks[trainee.Account.Id] = check;
                _traineeTuitionSupport[trainee.Account.Id] = trainee.Account.IsTuitionSupported;

                var existingEnrollment = enrollments.FirstOrDefault(item =>
                    item.TraineeUserId == trainee.Account.Id);
                var hasDeliveredAttendance = attendanceCounts.GetValueOrDefault(trainee.Account.Id) > 0;
                var canTrial = !trainee.Account.IsTuitionSupported
                               && (!hasDeliveredAttendance || existingEnrollment?.IsTrial == true);

                var nameLabel = new VerticalStackLayout
                {
                    Spacing = 3,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        UiKit.Body(trainee.DisplayName)
                    }
                };
                if (trainee.Account.IsTuitionSupported)
                {
                    nameLabel.Children.Add(UiKit.Caption(
                        DomainText.SupportedTraineeTuitionLabel,
                        UiKit.Success));
                }
                // Keep the trial control below the trainee name. Every card
                // reserves the same second-row height for a tidy roster even
                // when trial is unavailable or already locked.
                var trialRow = new Grid
                {
                    ColumnSpacing = 8,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Auto),
                        new ColumnDefinition(GridLength.Star)
                    },
                    HeightRequest = 32,
                    VerticalOptions = LayoutOptions.Center
                };
                var trialLabel = new Label
                {
                    Text = canTrial || existingEnrollment?.IsTrial == true ? "Học thử" : string.Empty,
                    FontSize = 12,
                    TextColor = UiKit.TextSecondary,
                    VerticalTextAlignment = TextAlignment.Center,
                    IsVisible = canTrial || existingEnrollment?.IsTrial == true
                };
                var trialSwitch = new Switch
                {
                    IsToggled = canTrial && existingEnrollment?.IsTrial == true,
                    IsEnabled = canTrial,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Start
                };
                var trialPicker = new Picker
                {
                    Title = "1–5 buổi",
                    ItemsSource = Enumerable.Range(1, 5).Select(item => $"{item} buổi").ToList(),
                    SelectedIndex = Math.Clamp((existingEnrollment?.TrialSessionCount ?? 1) - 1, 0, 4),
                    IsVisible = canTrial && trialSwitch.IsToggled,
                    IsEnabled = canTrial,
                    FontSize = 12,
                    HorizontalOptions = LayoutOptions.Start,
                    VerticalOptions = LayoutOptions.Center
                };
                trialSwitch.Toggled += (_, args) =>
                {
                    trialPicker.IsVisible = args.Value;
                    trialLabel.TextColor = args.Value ? UiKit.PrimaryDark : UiKit.TextSecondary;
                };
                _traineeTrialSwitches[trainee.Account.Id] = trialSwitch;
                _traineeTrialPickers[trainee.Account.Id] = trialPicker;
                trialRow.Children.Add(trialLabel);
                Grid.SetColumn(trialSwitch, 1);
                trialRow.Children.Add(trialSwitch);
                Grid.SetColumn(trialPicker, 2);
                trialRow.Children.Add(trialPicker);

                var content = new Grid
                {
                    ColumnSpacing = 8,
                    RowSpacing = 2,
                    RowDefinitions =
                    {
                        new RowDefinition(GridLength.Auto),
                        new RowDefinition(32)
                    },
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(36),
                        new ColumnDefinition(GridLength.Star)
                    },
                    MinimumHeightRequest = 78
                };
                Grid.SetRowSpan(check, 2);
                content.Children.Add(check);
                Grid.SetColumn(nameLabel, 1);
                content.Children.Add(nameLabel);
                Grid.SetColumn(trialRow, 1);
                Grid.SetRow(trialRow, 1);
                content.Children.Add(trialRow);
                _form.Children.Add(UiKit.Card(content, new Thickness(10, 8)));
            }
        }

        var save = UiKit.PrimaryButton(_existing is null ? "Tạo lớp học" : "Lưu thay đổi");
        save.Clicked += async (_, _) => await SaveAsync(save);
        _form.Children.Add(save);
        if (_existing is not null)
        {
            var deactivate = UiKit.DestructiveButton(
                _existing.Class.IsActive ? "Ngừng hoạt động lớp" : "Kích hoạt lại lớp");
            deactivate.Clicked += async (_, _) => await DeactivateAsync();
            _form.Children.Add(deactivate);

            var delete = UiKit.DestructiveButton("Xóa lớp học");
            delete.Clicked += async (_, _) => await DeleteAsync(delete);
            _form.Children.Add(delete);
        }

        Content = _existing is null
            ? UiKit.ScrollBody(_form)
            : UiKit.ScrollBody(
                UiKit.LargeTitle(_existing.Class.Name),
                _form);
    }

    private async Task SaveAsync(Button source)
    {
        source.IsEnabled = false;
        try
        {
            if (_venue.SelectedItem is not Venue selectedVenue)
            {
                throw new InvalidOperationException("Vui lòng chọn sân.");
            }

            var selectedDays = _days
                .Where(pair => pair.Value.IsChecked)
                .Select(pair => ((int)pair.Key).ToString(CultureInfo.InvariantCulture));
            var defaultFee = UiKit.ParseMoney(_defaultFee.Text);
            if (!int.TryParse(
                    _tuitionSessions.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var tuitionSessions)
                || tuitionSessions <= 0)
            {
                throw new InvalidOperationException(
                    "Vui lòng nhập số buổi trong chu kỳ học phí lớn hơn 0.");
            }
            var hasStandardTrainee = _traineeChecks.Any(pair =>
                pair.Value.IsChecked && !(_traineeTuitionSupport.GetValueOrDefault(pair.Key)));
            if (hasStandardTrainee && defaultFee <= 0)
            {
                throw new InvalidOperationException(
                    "Vui lòng nhập học phí mặc định trước khi chọn học viên.");
            }

            var trainingClass = _existing?.Class ?? new TrainingClass();
            trainingClass.Name = _name.Text ?? string.Empty;
            trainingClass.VenueId = selectedVenue.Id;
            trainingClass.ScheduleDays = string.Join(",", selectedDays);
            trainingClass.StartDate = DateTime.SpecifyKind(
                _startDate.Date.GetValueOrDefault().Date,
                DateTimeKind.Unspecified);
            trainingClass.StartTimeMinutes = (int)(_start.Time ?? TimeSpan.Zero).TotalMinutes;
            trainingClass.EndTimeMinutes = (int)(_end.Time ?? TimeSpan.Zero).TotalMinutes;
            trainingClass.TuitionSessionCount = tuitionSessions;
            trainingClass.DefaultFeeVnd = defaultFee;
            trainingClass.IsActive = _existing?.Class.IsActive ?? true;

            var coachRates = _coachRows
                .Where(pair => pair.Value.Check.IsChecked)
                .ToDictionary(
                    pair => pair.Key,
                    pair => UiKit.ParseMoney(pair.Value.SalaryPerSession.Text));
            if (coachRates.Any(pair => pair.Value <= 0))
            {
                throw new InvalidOperationException(
                    "Vui lòng nhập lương mỗi buổi cho Huấn Luyện Viên đã chọn.");
            }

            var traineeFees = _traineeChecks
                .Where(pair => pair.Value.IsChecked)
                .ToDictionary(
                    pair => pair.Key,
                    pair => _traineeTuitionSupport.GetValueOrDefault(pair.Key) ? 0 : defaultFee);

            var traineeTrialSessions = _traineeChecks
                .Where(pair => pair.Value.IsChecked)
                .ToDictionary(
                    pair => pair.Key,
                    pair => _traineeTrialSwitches.TryGetValue(pair.Key, out var trialSwitch)
                            && trialSwitch.IsToggled
                        ? Math.Clamp(
                            _traineeTrialPickers.GetValueOrDefault(pair.Key)?.SelectedIndex + 1 ?? 1,
                            1,
                            5)
                        : 0);

            await _database.SaveClassAsync(
                CurrentFounderId,
                trainingClass,
                coachRates,
                traineeFees,
                traineeTrialSessions);
            await DisplayAlertAsync("Đã lưu", "Lớp học đã được cập nhật.", "OK");
            await Navigation.PopAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Chưa thể lưu", exception.Message, "Đóng");
        }
        finally
        {
            source.IsEnabled = true;
        }
    }

    private async Task DeactivateAsync()
    {
        if (_existing is null)
        {
            return;
        }

        var isActive = _existing.Class.IsActive;
        var confirmed = await DisplayAlertAsync(
            isActive ? "Ngừng hoạt động lớp?" : "Kích hoạt lại lớp?",
            isActive
                ? "Lớp sẽ ẩn khỏi lịch hiện tại nhưng dữ liệu lịch sử được giữ."
                : "Lớp sẽ xuất hiện lại trong lịch cố định và các buổi học mới.",
            isActive ? "Ngừng" : "Kích hoạt",
            "Hủy");
        if (!confirmed)
        {
            return;
        }

        await _database.SetClassActiveAsync(CurrentFounderId, _existing.Class.Id, !isActive);
        await Navigation.PopAsync();
    }

    private async Task DeleteAsync(Button source)
    {
        if (_existing is null)
        {
            return;
        }

        var confirmed = await DisplayAlertAsync(
            "Xóa lớp học?",
            "Lớp, lịch học, điểm danh và dữ liệu học phí liên quan sẽ bị xóa vĩnh viễn. Lương Coach theo tháng vẫn được giữ.",
            "Xóa lớp",
            "Hủy");
        if (!confirmed)
        {
            return;
        }

        source.IsEnabled = false;
        try
        {
            await _database.DeleteClassAsync(CurrentFounderId, _existing.Class.Id);
            await Navigation.PopAsync();
        }
        catch (Exception exception)
        {
            source.IsEnabled = true;
            await DisplayAlertAsync("Chưa thể xóa lớp", exception.Message, "Đóng");
        }
    }

    private string CurrentFounderId =>
        _session.CurrentUser?.Id
        ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc.");

}

public sealed class VenueManagementPage : AsyncContentPage
{
    private readonly AppDatabase _database;

    public VenueManagementPage(AppDatabase database, SessionService session)
        : base(session, "Quản lý sân")
    {
        _database = database;
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "＋",
            Command = new Command(async () =>
                await Navigation.PushAsync(new VenueEditorPage(_database, Session)))
        });
    }

    protected override async Task LoadAsync()
    {
        var venues = await _database.GetVenuesAsync(includeInactive: true);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = 9,
            Children =
            {
                UiKit.OfflineBanner(),
            }
        };

        if (venues.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có sân",
                "Tạo sân đầu tiên để dùng khi tạo lớp.",
                UiKit.PrimaryButton("Tạo sân", async (_, _) =>
                    await Navigation.PushAsync(new VenueEditorPage(_database, Session)))));
        }
        else
        {
            foreach (var venue in venues)
            {
                var card = UiKit.Card(new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        UiKit.Headline(venue.Name),
                        UiKit.Body(string.IsNullOrWhiteSpace(venue.Address)
                            ? "Chưa có địa chỉ"
                            : venue.Address, UiKit.TextSecondary),
                        UiKit.StatusBadge(
                            venue.IsActive ? "Đang hoạt động" : "Ngừng hoạt động",
                            venue.IsActive ? UiKit.Success : UiKit.TextSecondary)
                    }
                });
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) =>
                    await Navigation.PushAsync(new VenueEditorPage(_database, Session, venue));
                card.GestureRecognizers.Add(tap);
                root.Children.Add(card);
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }
}

public sealed class VenueEditorPage : ContentPage
{
    public VenueEditorPage(
        AppDatabase database,
        SessionService session,
        Venue? existing = null,
        Action<Venue>? onSaved = null)
    {
        Title = existing is null ? "Tạo sân" : "Sửa sân";
        BackgroundColor = UiKit.Background;
        var name = new Entry { Placeholder = "Tên sân", Text = existing?.Name ?? string.Empty };
        var address = new Entry { Placeholder = "Địa chỉ", Text = existing?.Address ?? string.Empty };
        var notes = new Editor
        {
            Placeholder = "Ghi chú",
            Text = existing?.Notes ?? string.Empty,
            MinimumHeightRequest = 80
        };
        var save = UiKit.PrimaryButton("Lưu sân");
        save.Clicked += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var venue = existing ?? new Venue();
                venue.Name = name.Text ?? string.Empty;
                venue.Address = address.Text ?? string.Empty;
                venue.Notes = notes.Text ?? string.Empty;
                venue.IsActive = true;
                await database.SaveVenueAsync(
                    session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                    venue);
                onSaved?.Invoke(venue);
                await Navigation.PopAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Chưa thể lưu", exception.Message, "Đóng");
            }
            finally
            {
                save.IsEnabled = true;
            }
        };

        var stack = new VerticalStackLayout
        {
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.LabeledField("TÊN SÂN", name),
                UiKit.LabeledField("ĐỊA CHỈ", address),
                UiKit.LabeledField("GHI CHÚ", notes),
                save
            }
        };
        if (existing is not null)
        {
            var deactivate = UiKit.DestructiveButton(
                existing.IsActive ? "Ngừng hoạt động sân" : "Kích hoạt lại sân");
            deactivate.Clicked += async (_, _) =>
            {
                await database.SetVenueActiveAsync(
                    session.CurrentUser?.Id
                    ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                    existing.Id,
                    !existing.IsActive);
                await Navigation.PopAsync();
            };
            stack.Children.Add(deactivate);
        }

        Content = UiKit.ScrollBody(UiKit.LargeTitle(Title), UiKit.Card(stack));
    }
}
