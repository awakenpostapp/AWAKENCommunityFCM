using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;
using Microsoft.Maui.Dispatching;

namespace CommunityFootballClubManager.Views;

public sealed class FounderDashboardPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly RememberedLoginService _rememberedLogin;
    private readonly IImageSaveService _imageSave;

    public FounderDashboardPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin,
        IImageSaveService imageSave)
        : base(session, "Tổng quan")
    {
        _database = database;
        _media = media;
        _rememberedLogin = rememberedLogin;
        _imageSave = imageSave;
    }

    protected override async Task LoadAsync()
    {
        var metrics = await _database.GetDashboardMetricsAsync(CurrentUserId);
        var club = await _database.GetClubAsync();
        var founderName = Session.CurrentProfile?.FullName;
        if (string.IsNullOrWhiteSpace(founderName))
        {
            founderName = "Điều hành & Sáng lập";
        }
        var teamName = string.IsNullOrWhiteSpace(club.TeamName)
            ? "Community Football Club"
            : club.TeamName.Trim();

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing
        };
        root.Children.Add(UiKit.SportsHero(
            club.LogoPath,
            "Community Football Club",
            $"Chào {teamName}",
            founderName,
            $"{DateTime.Today:dd/MM/yyyy}",
            subtitleFontSize: 14));
        root.Children.Add(UiKit.OfflineBanner());
        root.Children.Add(UiKit.Title("Bảng điều hành"));

        if (metrics.OverdueSalaries > 0
            || metrics.PendingTuitionProofs > 0
            || metrics.PendingCoachCheckOuts > 0)
        {
            var alerts = new VerticalStackLayout { Spacing = 6 };
            alerts.Children.Add(UiKit.Headline("Cần xử lý"));
            if (metrics.PendingTuitionProofs > 0)
            {
                alerts.Children.Add(UiKit.StatusBadge(
                    $"{metrics.PendingTuitionProofs} bill học phí chờ xác nhận",
                    UiKit.Primary));
            }

            if (metrics.OverdueSalaries > 0)
            {
                alerts.Children.Add(UiKit.StatusBadge(
                    $"{metrics.OverdueSalaries} kỳ lương đến hạn thanh toán",
                    UiKit.Warning));
            }

            if (metrics.PendingCoachCheckOuts > 0)
            {
                var pendingCheckout = UiKit.StatusBadge(
                    $"{metrics.PendingCoachCheckOuts} chờ xác nhận check-out",
                    UiKit.Warning);
                var checkoutTap = new TapGestureRecognizer();
                checkoutTap.Tapped += async (_, _) => await PushPageAsync(
                    new CoachCheckInReviewPage(_database, Session));
                pendingCheckout.GestureRecognizers.Add(checkoutTap);
                SemanticProperties.SetDescription(
                    pendingCheckout,
                    "Mở danh sách Coach chờ xác nhận check-out");
                alerts.Children.Add(pendingCheckout);
            }

            root.Children.Add(UiKit.Card(alerts));
        }

        var metricsGrid = UiKit.MetricGrid(
            (metrics.ActiveClasses.ToString(), "Lớp đang hoạt động", UiKit.Primary),
            (metrics.ActiveTrainees.ToString(), "Học viên", UiKit.Accent),
            (metrics.PendingTuitionProofs.ToString(), "Bill chờ duyệt", UiKit.Warning),
            (metrics.UnpaidTuition.ToString(), "Học phí chưa đóng", UiKit.Danger));
        void AttachMetricTap(int index, Func<Task> action, string description)
        {
            if (metricsGrid.Children.Count <= index
                || metricsGrid.Children[index] is not View metricCard)
            {
                return;
            }

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await action();
            metricCard.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(
                metricCard,
                description);
        }

        AttachMetricTap(
            0,
            async () => await PushPageAsync(
                new FounderFixedClassesPage(
                    _database,
                    Session,
                    _media,
                    _rememberedLogin)),
            "Mở danh sách lớp học cố định");
        AttachMetricTap(
            1,
            async () => await PushPageAsync(
                new MemberRoleListPage(
                    _database,
                    Session,
                    _media,
                    _rememberedLogin,
                    UserRole.Trainee)),
            "Mở danh sách Cầu Thủ Học Viên");
        AttachMetricTap(
            2,
            async () => await PushPageAsync(
                new FounderInvoiceListPage(
                    _database,
                    Session,
                    _imageSave,
                    FounderInvoiceFilter.ProofSubmitted)),
            "Mở danh sách bill học phí chờ xác nhận");
        AttachMetricTap(
            3,
            async () => await PushPageAsync(
                new FounderInvoiceListPage(
                    _database,
                    Session,
                    _imageSave,
                    FounderInvoiceFilter.Unpaid)),
            "Mở danh sách học phí chưa đóng");
        root.Children.Add(metricsGrid);

        var actions = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        AddAction(actions, "＋ Thêm account", 0, 0, async () =>
            await PushPageAsync(new MemberEditorPage(_database, Session, _media)));
        AddAction(actions, "＋ Tạo lớp học", 1, 0, async () =>
            await PushPageAsync(new ClassEditorPage(_database, Session)));
        AddAction(actions, "✉ Gửi thông báo", 0, 1, async () =>
            await PushPageAsync(new AnnouncementComposerPage(_database, Session)));
        AddAction(actions, "✓ Điểm danh thay", 1, 1, async () =>
            await PushPageAsync(new AttendanceHubPage(_database, Session)));
        root.Children.Add(actions);

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private static void AddAction(
        Grid grid,
        string text,
        int column,
        int row,
        Func<Task> action)
    {
        var button = UiKit.SecondaryButton(text);
        button.Clicked += async (_, _) => await action();
        Grid.SetColumn(button, column);
        Grid.SetRow(button, row);
        grid.Children.Add(button);
    }
}

public sealed class CoachDashboardPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly List<(CoachCheckIn CheckIn, Label Label)> _runningTimers = [];
    private IDispatcherTimer? _elapsedTimer;

    public CoachDashboardPage(
        AppDatabase database,
        SessionService session,
        MediaService media)
        : base(session, "Hôm nay")
    {
        _database = database;
        _media = media;
    }

    protected override async Task LoadAsync()
    {
        _elapsedTimer?.Stop();
        _elapsedTimer = null;
        _runningTimers.Clear();

        var classes = await _database.GetClassesAsync(CurrentUserId);
        var club = await _database.GetClubAsync();
        var founder = await _database.GetFounderAsync(CurrentUserId);
        var todayValue = ((int)DateTime.Today.DayOfWeek).ToString();
        var todayClasses = classes
            .Where(item => item.Class.ScheduleDays
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Contains(todayValue))
            .ToList();

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.SportsHero(
                    club.LogoPath,
                    "Lịch huấn luyện hôm nay",
                    string.IsNullOrWhiteSpace(club.TeamName)
                    ? "Community Football Club"
                    : club.TeamName,
                    $"Chào {Session.CurrentProfile?.FullName ?? "Coach"}",
                    $"Founder: {founder.DisplayName}\n{DateTime.Today:dd/MM/yyyy}"),
                UiKit.OfflineBanner(),
                UiKit.Title("Lớp hôm nay")
            }
        };

        if (todayClasses.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Hôm nay không có buổi học",
                "Các lớp được phân công sẽ xuất hiện tại đây theo lịch cố định."));
        }
        else
        {
            foreach (var row in todayClasses)
            {
                var session = await _database.GetOrCreateSessionAsync(
                    CurrentUserId,
                    row.Class.Id,
                    DateTime.Today);
                var checkIn = await _database.GetCoachCheckInAsync(session.Id, CurrentUserId);
                root.Children.Add(CreateTodayClassCard(row, session, checkIn));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
        if (_runningTimers.Count > 0)
        {
            _elapsedTimer = Dispatcher.CreateTimer();
            _elapsedTimer.Interval = TimeSpan.FromSeconds(1);
            _elapsedTimer.Tick += (_, _) => UpdateRunningTimers();
            _elapsedTimer.Start();
            UpdateRunningTimers();
        }
    }

    protected override void OnDisappearing()
    {
        _elapsedTimer?.Stop();
        _elapsedTimer = null;
        base.OnDisappearing();
    }

    private View CreateTodayClassCard(
        ClassRow row,
        TrainingSession session,
        CoachCheckIn? checkIn)
    {
        var isCheckedOut = checkIn?.CheckedOutAtUtc is not null;
        var isFounderSubstitution = checkIn is not null && CoachCheckInTime.IsFounderSubstitution(checkIn);
        var isSafetyClosed = !isFounderSubstitution
                             && checkIn is not null
                             && CoachCheckInTime.IsSafetyClosed(checkIn);
        var isAutoAbsent = checkIn is not null && CoachCheckInTime.IsAutoAbsent(checkIn);
        var isCheckInRejected = checkIn?.ApprovalStatus == CoachCheckInApprovalStatus.Rejected
                                && !isAutoAbsent;
        var isTooEarly = checkIn is null
                         && CoachCheckInTime.IsCheckInWindowTooEarly(row.Class, session.SessionDate);
        var isWindowLocked = checkIn is null
                             && CoachCheckInTime.IsCheckInWindowLocked(row.Class, session.SessionDate);
        var checkInButton = UiKit.SecondaryButton(
            isAutoAbsent
                ? "Check-in đã khóa"
                : isFounderSubstitution
                ? "Lớp đã hoàn tất"
                : isSafetyClosed
                ? "Chụp selfie check-out"
                : isCheckedOut
                ? "Lớp đã hoàn tất"
                : checkIn is null ? "Chụp selfie check-in" : "Chụp lại selfie");
        checkInButton.IsEnabled = !isAutoAbsent
                                  && !isFounderSubstitution
                                  && (!isCheckedOut || isSafetyClosed)
                                  && (!isTooEarly && !isWindowLocked);
        checkInButton.Clicked += async (_, _) =>
        {
            await RunActionAsync(
                async () =>
                {
                    var path = await _media.CapturePhotoAsync(
                        isSafetyClosed ? "coach_checkout" : "coach_checkin");
                    if (path is null)
                    {
                        return;
                    }

                    if (isSafetyClosed)
                    {
                        await _database.SaveCoachCheckOutAsync(CurrentUserId, session.Id, path);
                    }
                    else
                    {
                        await _database.SaveCoachCheckInAsync(CurrentUserId, session.Id, path);
                    }
                },
                checkInButton,
                isSafetyClosed
                    ? "Đã gửi selfie check-out. Founder sẽ kiểm tra và xác nhận lương."
                    : "Đã check-in và mở ca. Hãy chụp selfie check-out khi kết thúc; Founder sẽ xác nhận sau đó.");
        };

        var content = new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                UiKit.Headline(row.Class.Name),
                UiKit.Body(row.ScheduleText, UiKit.TextSecondary),
                UiKit.Body($"Coach: {row.CoachNames}", UiKit.TextSecondary),
                UiKit.Body($"Sân: {row.Venue?.Name ?? "Chưa cập nhật"}", UiKit.TextSecondary),
                CoachCheckInStatusBadge(checkIn)
            }
        };

        if (checkIn is not null && !isCheckInRejected && !isFounderSubstitution)
        {
            var elapsed = UiKit.Body(
                $"Thời gian dạy: {CoachCheckInTime.FormatDuration(CoachCheckInTime.ElapsedSeconds(checkIn))}",
                UiKit.Primary);
            elapsed.FontAttributes = FontAttributes.Bold;
            content.Children.Add(elapsed);
            if (!isCheckedOut)
            {
                _runningTimers.Add((checkIn, elapsed));
            }
        }

        content.Children.Add(checkInButton);
        if (isAutoAbsent)
        {
            content.Children.Add(UiKit.StatusBadge(
                "Vắng check-in · Đã khóa",
                UiKit.Danger));
            content.Children.Add(UiKit.Caption(
                $"Coach chưa check-in trước {CoachCheckInTime.CheckInLocksLocal(row.Class, session.SessionDate):HH:mm}. Hệ thống đã tự ghi nhận vắng và khóa ca."));
        }
        else if (isFounderSubstitution)
        {
            content.Children.Add(UiKit.StatusBadge(
                "Coach không dạy · Founder đã điểm danh",
                UiKit.Warning));
            content.Children.Add(UiKit.Caption(
                "Buổi học vẫn được ghi nhận hoàn tất. Coach không được tính lương cho buổi này."));
        }
        else if (isTooEarly)
        {
            content.Children.Add(UiKit.Caption(
                $"Chưa mở check-in. Coach có thể check-in từ {CoachCheckInTime.CheckInOpensLocal(row.Class, session.SessionDate):HH:mm} ({CoachCheckInTime.CheckInOpenLeadMinutes} phút trước giờ học)."));
        }
        else if (isWindowLocked)
        {
            content.Children.Add(UiKit.Caption(
                $"Đã quá 2 giờ sau giờ kết thúc ({CoachCheckInTime.CheckInLocksLocal(row.Class, session.SessionDate):HH:mm}); check-in đã bị khóa."));
        }
        else if (checkIn is null || isCheckInRejected)
        {
            content.Children.Add(UiKit.Caption(
                "Chụp selfie check-in để mở danh sách học viên và điểm danh."));
        }
        else if (isSafetyClosed)
        {
            content.Children.Add(UiKit.StatusBadge(
                "Ca đã tự khóa sau 8 giờ · Chưa tính lương",
                UiKit.Warning));
            content.Children.Add(UiKit.Caption(
                "Coach quên check-out nên hệ thống đã dừng đồng hồ và đóng danh sách học viên. Hãy chụp selfie check-out để hoàn tất ca; Founder chỉ duyệt lương khi đủ hai ảnh."));
        }
        else if (isCheckedOut)
        {
            content.Children.Add(UiKit.Caption(
                "Lớp đã kết thúc; danh sách học viên đã được đóng."));
        }
        else
        {
            var attendanceButton = UiKit.PrimaryButton("Điểm danh học viên");
            attendanceButton.Clicked += async (_, _) =>
                await Navigation.PushAsync(new AttendancePage(
                    _database,
                    Session,
                    row,
                    DateTime.Today));
            content.Children.Add(attendanceButton);

            var checkOutButton = UiKit.PrimaryButton("Chụp selfie check-out");
            checkOutButton.Clicked += async (_, _) =>
            {
                await RunActionAsync(
                    async () =>
                    {
                        var path = await _media.CapturePhotoAsync("coach_checkout");
                        if (path is null)
                        {
                            return;
                        }

                        await _database.SaveCoachCheckOutAsync(
                            CurrentUserId,
                            session.Id,
                            path);
                    },
                    checkOutButton,
                    "Đã gửi selfie check-out. Lớp học đã được hoàn tất.");
            };
            content.Children.Add(checkOutButton);
        }

        return UiKit.Card(content);
    }

    private void UpdateRunningTimers()
    {
        foreach (var item in _runningTimers)
        {
            item.Label.Text = $"Thời gian dạy: {CoachCheckInTime.FormatDuration(CoachCheckInTime.ElapsedSeconds(item.CheckIn))}";
        }
    }

    private static string CoachCheckInStatusText(CoachCheckIn? checkIn) => checkIn switch
    {
        null => "Chưa check-in",
        _ when CoachCheckInTime.IsFounderSubstitution(checkIn) => "Coach không dạy · Founder điểm danh",
        _ when CoachCheckInTime.IsAutoAbsent(checkIn) =>
            "Vắng check-in · Đã khóa",
        _ when CoachCheckInTime.IsSafetyClosed(checkIn) =>
            "Ca đã tự khóa · Cần selfie check-out",
        { CheckedOutAtUtc: not null } =>
            $"Đã check-out · {checkIn.CheckedOutAtUtc.Value.ToLocalTime():HH:mm}",
        { ApprovalStatus: CoachCheckInApprovalStatus.Approved } =>
            $"Đã xác nhận · {checkIn.CheckedInAtUtc.ToLocalTime():HH:mm}",
        { ApprovalStatus: CoachCheckInApprovalStatus.Rejected } =>
            "Check-in bị từ chối · Vui lòng chụp lại",
        _ => $"Check-in thành công · {checkIn.CheckedInAtUtc.ToLocalTime():HH:mm}"
    };

    private static Color CoachCheckInStatusColor(CoachCheckIn? checkIn) =>
        checkIn is not null && CoachCheckInTime.IsFounderSubstitution(checkIn)
            ? UiKit.Warning
            : checkIn is not null && CoachCheckInTime.IsAutoAbsent(checkIn)
            ? UiKit.Danger
            : checkIn is not null && CoachCheckInTime.IsSafetyClosed(checkIn)
            ? UiKit.Warning
            : checkIn?.ApprovalStatus switch
    {
        { } when checkIn.CheckedOutAtUtc is not null => UiKit.Success,
        CoachCheckInApprovalStatus.Approved => UiKit.Success,
        CoachCheckInApprovalStatus.Rejected => UiKit.Danger,
        _ => UiKit.Warning
    };

    private static View CoachCheckInStatusBadge(CoachCheckIn? checkIn)
    {
        var text = CoachCheckInStatusText(checkIn);
        return checkIn is not null
               && checkIn.ApprovalStatus == CoachCheckInApprovalStatus.Pending
               && checkIn.CheckedOutAtUtc is null
               && !CoachCheckInTime.IsAutoAbsent(checkIn)
            ? UiKit.SuccessStatusBadge(text)
            : UiKit.StatusBadge(text, CoachCheckInStatusColor(checkIn));
    }
}

public sealed class TraineeDashboardPage : AsyncContentPage
{
    private readonly AppDatabase _database;

    public TraineeDashboardPage(AppDatabase database, SessionService session)
        : base(session, string.Empty)
    {
        _database = database;
    }

    protected override async Task LoadAsync()
    {
        await _database.EnsureRecurringDataAsync(DateTime.Today);
        var classes = await _database.GetClassesAsync(CurrentUserId);
        var club = await _database.GetClubAsync();
        var founder = await _database.GetFounderAsync(CurrentUserId);
        var invoices = await _database.GetInvoicesAsync(CurrentUserId);
        var notifications = await _database.GetNotificationsAsync(CurrentUserId);
        var nextClass = FindNextClass(classes);
        var isTuitionSupported = Session.CurrentUser?.IsTuitionSupported == true;
        var trialRows = new List<(ClassRow Row, ClassEnrollment Enrollment, TuitionCycleProgress Progress)>();
        if (!isTuitionSupported)
        {
            foreach (var row in classes)
            {
                var enrollment = (await _database.GetClassEnrollmentsAsync(row.Class.Id))
                    .FirstOrDefault(item => item.TraineeUserId == CurrentUserId && item.IsTrial);
                if (enrollment is null)
                {
                    continue;
                }

                trialRows.Add((
                    row,
                    enrollment,
                    await _database.GetDisplayedTuitionProgressAsync(
                        CurrentUserId,
                        CurrentUserId,
                        row.Class.Id)));
            }
        }

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.SportsHero(
                    club.LogoPath,
                    "Lịch thi đấu & học tập",
                    string.IsNullOrWhiteSpace(club.TeamName)
                    ? "Community Football Club"
                    : club.TeamName,
                    $"Chào {Session.CurrentProfile?.FullName ?? "học viên"}",
                    $"Founder: {founder.DisplayName}\n{DateTime.Today:dd/MM/yyyy}"),
                UiKit.OfflineBanner(),
                UiKit.Title("Lớp kế tiếp")
            }
        };

        if (nextClass is null)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có lịch học",
                "Founder chưa thêm bạn vào lớp học."));
        }
        else
        {
            root.Children.Add(UiKit.Card(new VerticalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    UiKit.StatusBadge($"SẮP DIỄN RA  ·  {nextClass.Value.Date:dd/MM}", UiKit.Accent),
                    UiKit.Headline(nextClass.Value.Row.Class.Name),
                    UiKit.Body(
                        nextClass.Value.Row.ScheduleText,
                        UiKit.TextSecondary),
                    UiKit.Body($"Coach: {nextClass.Value.Row.CoachNames}", UiKit.TextSecondary),
                    UiKit.Body($"Sân: {nextClass.Value.Row.Venue?.Name ?? "Chưa cập nhật"}")
                }
            }));
        }

        root.Children.Add(UiKit.Title("Học phí theo chu kỳ"));
        if (isTuitionSupported)
        {
            root.Children.Add(UiKit.EmptyState(
                DomainText.SupportedTraineeLabel,
                "Bạn được miễn toàn bộ học phí. Không cần thanh toán hoặc gửi bill."));
        }
        else if (invoices.Count == 0 && trialRows.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có học phí",
                "Học phí sẽ được tạo ngay khi bạn được thêm vào lớp."));
        }
        else
        {
            foreach (var trial in trialRows)
            {
                root.Children.Add(UiKit.Card(new VerticalStackLayout
                {
                    Spacing = 5,
                    Children =
                    {
                        UiKit.Headline(trial.Row.Class.Name),
                        UiKit.StatusBadge("Học thử", UiKit.Primary),
                        UiKit.Caption(
                            $"Tiến độ học thử: {trial.Progress.AttendedSessions}/{Math.Clamp(trial.Enrollment.TrialSessionCount, 1, 5)} buổi"),
                        UiKit.Body(trial.Row.ScheduleText, UiKit.TextSecondary)
                    }
                }));
            }

            foreach (var invoice in invoices)
            {
                root.Children.Add(UiKit.Card(new VerticalStackLayout
                {
                    Spacing = 5,
                    Children =
                    {
                        UiKit.Headline(invoice.ClassName),
                        UiKit.Caption(DomainText.TuitionPrepaidCycles(invoice.Invoice)),
                        UiKit.Body(UiKit.Money(invoice.Invoice.AmountVnd)),
                        UiKit.StatusBadge(
                            DomainText.Invoice(invoice.Invoice.Status),
                            UiKit.InvoiceColor(invoice.Invoice.Status))
                    }
                }));
            }
        }

        var unread = notifications.Count(item => !item.IsRead);
        if (unread > 0)
        {
            root.Children.Add(UiKit.StatusBadge($"{unread} thông báo chưa đọc", UiKit.Primary));
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private static (ClassRow Row, DateTime Date)? FindNextClass(IReadOnlyList<ClassRow> classes)
    {
        (ClassRow Row, DateTime Date)? best = null;
        foreach (var row in classes)
        {
            var days = row.Class.ScheduleDays
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, out var day) ? day : -1)
                .ToHashSet();
            for (var offset = 0; offset < 8; offset++)
            {
                var date = DateTime.Today.AddDays(offset);
                if (date.Date < row.Class.StartDate.Date)
                {
                    continue;
                }
                if (!days.Contains((int)date.DayOfWeek))
                {
                    continue;
                }

                if (best is null || date < best.Value.Date)
                {
                    best = (row, date);
                }

                break;
            }
        }

        return best;
    }
}
