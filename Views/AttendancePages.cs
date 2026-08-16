using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;
using Microsoft.Maui.Dispatching;

namespace CommunityFootballClubManager.Views;

public sealed class AttendanceHubPage : AsyncContentPage
{
    private readonly AppDatabase _database;

    public AttendanceHubPage(AppDatabase database, SessionService session)
        : base(session, "Điểm danh")
    {
        _database = database;
    }

    protected override async Task LoadAsync()
    {
        var classes = await _database.GetClassesAsync(CurrentUserId);
        var picker = new Picker { Title = "Chọn lớp" };
        foreach (var row in classes)
        {
            picker.Items.Add(row.Class.Name);
        }

        if (classes.Count > 0)
        {
            picker.SelectedIndex = 0;
        }

        var datePicker = new DatePicker
        {
            Date = DateTime.Today,
            MaximumDate = DateTime.Today,
            MinimumDate = DateTime.Today.AddYears(-1)
        };
        var open = UiKit.PrimaryButton(
            Session.CurrentUser?.Role == UserRole.Founder
                ? "Điểm danh thay Coach"
                : "Mở danh sách điểm danh");
        open.Clicked += async (_, _) =>
        {
            if (picker.SelectedIndex < 0 || picker.SelectedIndex >= classes.Count)
            {
                await DisplayAlertAsync("Chưa chọn lớp", "Vui lòng chọn lớp học.", "Đóng");
                return;
            }

            await Navigation.PushAsync(new AttendancePage(
                _database,
                Session,
                classes[picker.SelectedIndex],
                datePicker.Date ?? DateTime.Today));
        };

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                Session.CurrentUser?.Role == UserRole.Founder
                    ? UiKit.StatusBadge("Bạn đang ở chế độ điểm danh thay Coach", UiKit.Warning)
                    : UiKit.Caption("Chọn lớp và ngày học để điểm danh hoặc sửa học viên đi trễ."),
                UiKit.Card(new VerticalStackLayout
                {
                    Spacing = UiKit.SectionSpacing,
                    Children =
                    {
                        UiKit.LabeledField("LỚP HỌC", picker),
                        UiKit.LabeledField("NGÀY HỌC", datePicker),
                        open
                    }
                })
            }
        };

        if (Session.CurrentUser?.Role == UserRole.Founder)
        {
            var pendingCheckIns = await _database.GetPendingCoachCheckInsAsync(CurrentUserId);
            var review = UiKit.PrimaryButton(
                $"Chờ xác nhận check-out ({pendingCheckIns.Count})");
            review.Clicked += async (_, _) =>
                await Navigation.PushAsync(new CoachCheckInReviewPage(
                    _database,
                    Session));
            var reviewCard = UiKit.Card(new VerticalStackLayout
            {
                Spacing = 7,
                Children =
                {
                    UiKit.Title("Điểm danh Huấn Luyện Viên"),
                    UiKit.Body(
                        "Kiểm tra ảnh selfie và xác nhận trước khi buổi dạy được tính lương.",
                        UiKit.TextSecondary),
                    review
                }
            });
            root.Children.Add(reviewCard);
        }

        if (classes.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có lớp được phân công",
                "Founder cần tạo lớp và phân công account trước."));
            open.IsEnabled = false;
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }
}

public sealed class AttendancePage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly ClassRow _classRow;
    private readonly DateTime _date;
    private readonly bool _historicalMode;
    private readonly List<(AttendanceRosterItem Item, Picker Picker)> _statusPickers = [];
    private Picker? _founderModePicker;
    private TrainingSession? _trainingSession;

    public AttendancePage(
        AppDatabase database,
        SessionService session,
        ClassRow classRow,
        DateTime date,
        bool historicalMode = false)
        : base(session, "Điểm danh")
    {
        _database = database;
        _classRow = classRow;
        _date = date.Date;
        _historicalMode = historicalMode;
    }

    protected override async Task LoadAsync()
    {
        _statusPickers.Clear();
        _trainingSession = await _database.GetOrCreateSessionAsync(
            CurrentUserId,
            _classRow.Class.Id,
            _date);
        var roster = await _database.GetAttendanceRosterAsync(
            CurrentUserId,
            _trainingSession.Id);

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = 9,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.LargeTitle(_classRow.Class.Name),
                UiKit.Caption($"{_date:dddd, dd/MM/yyyy} · {_classRow.ScheduleText}"),
                UiKit.StatusBadge(
                    _trainingSession.Status == SessionStatus.Submitted
                        ? "Đã hoàn tất · Có thể sửa người đi trễ"
                        : "Đang điểm danh",
                    _trainingSession.Status == SessionStatus.Submitted
                        ? UiKit.Success
                        : UiKit.Warning)
            }
        };

        if (Session.CurrentUser?.Role == UserRole.Founder)
        {
            if (_historicalMode)
            {
                _founderModePicker = new Picker
                {
                    Title = "Chọn trạng thái buổi học",
                    ItemsSource = new[]
                    {
                        "Đã dạy (ghi nhận thủ công)",
                        "Coach không dạy (Founder điểm danh thay)"
                    },
                    SelectedIndex = 0
                };
                root.Children.Add(UiKit.Card(new VerticalStackLayout
                {
                    Spacing = 5,
                    Children =
                    {
                        UiKit.Headline("Trạng thái buổi học cũ"),
                        UiKit.Caption(
                            "Dùng cho lớp đã diễn ra trước khi đưa vào phần mềm. Chọn Coach đã dạy để tính buổi và lương; chọn Coach không dạy nếu Founder điểm danh thay."),
                        UiKit.LabeledField("TRẠNG THÁI", _founderModePicker)
                    }
                }));
            }
            root.Children.Add(UiKit.StatusBadge(
                _historicalMode
                    ? "Founder đang bổ sung điểm danh cho ngày cũ"
                    : "Founder đang điểm danh thay Huấn luyện viên",
                UiKit.Warning));
            var checkIns = await _database.GetCoachCheckInsForSessionAsync(
                CurrentUserId,
                _trainingSession.Id);
            root.Children.Add(UiKit.Title("Selfie check-in Coach"));
            if (checkIns.Count == 0)
            {
                root.Children.Add(UiKit.StatusBadge("Chưa có Coach check-in", UiKit.Warning));
            }
            else
            {
                foreach (var checkIn in checkIns)
                {
                    var checkInStack = new VerticalStackLayout
                    {
                        Spacing = 5,
                        Children =
                        {
                            UiKit.Headline(checkIn.CoachName),
                            UiKit.Caption(CoachPositionCatalog.Label(checkIn.CoachPosition), UiKit.Primary),
                            UiKit.Caption(
                                $"{CoachCheckInTime.Range(checkIn.CheckIn)} · {CoachCheckInTime.FormatDuration(CoachCheckInTime.ElapsedSeconds(checkIn.CheckIn))}"),
                            UiKit.StatusBadge(
                                DomainText.CoachCheckInApproval(checkIn.CheckIn.ApprovalStatus),
                                checkIn.CheckIn.ApprovalStatus switch
                                {
                                    CoachCheckInApprovalStatus.Approved => UiKit.Success,
                                    CoachCheckInApprovalStatus.Rejected => UiKit.Danger,
                                    _ => UiKit.Warning
                                })
                        }
                    };
                    if (File.Exists(checkIn.CheckIn.SelfiePath))
                    {
                        checkInStack.Children.Add(new Image
                        {
                            Source = ImageSource.FromFile(checkIn.CheckIn.SelfiePath),
                            HeightRequest = 150,
                            Aspect = Aspect.AspectFit
                        });
                    }

                    root.Children.Add(UiKit.Card(checkInStack));
                }
            }
        }

        var markAll = UiKit.SecondaryButton("Chọn tất cả có mặt");
        markAll.Clicked += (_, _) =>
        {
            foreach (var pair in _statusPickers)
            {
                pair.Picker.SelectedIndex = (int)AttendanceStatus.Present;
            }
        };
        root.Children.Add(markAll);

        if (roster.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Lớp chưa có học viên",
                "Founder cần thêm học viên vào lớp trước khi điểm danh."));
        }
        else
        {
            foreach (var item in roster)
            {
                root.Children.Add(CreateRosterRow(item));
            }
        }

        var overrideReason = new Editor
        {
            Placeholder = "Lý do điểm danh thay hoặc sửa dữ liệu",
            MinimumHeightRequest = 72,
            IsVisible = Session.CurrentUser?.Role == UserRole.Founder
        };
        if (Session.CurrentUser?.Role == UserRole.Founder)
        {
            root.Children.Add(UiKit.LabeledField("LÝ DO (BẮT BUỘC)", overrideReason));
        }

        var draft = UiKit.SecondaryButton("Lưu bản nháp");
        var submit = UiKit.PrimaryButton(
            _trainingSession.Status == SessionStatus.Submitted
                ? "Lưu chỉnh sửa điểm danh"
                : "Điểm danh hoàn tất");
        draft.IsEnabled = roster.Count > 0;
        draft.IsVisible = _trainingSession.Status != SessionStatus.Submitted;
        submit.IsEnabled = roster.Count > 0;
        draft.Clicked += async (_, _) =>
            await SaveAsync(false, overrideReason.Text ?? string.Empty, draft);
        submit.Clicked += async (_, _) =>
            await SubmitAsync(overrideReason.Text ?? string.Empty, submit);
        root.Children.Add(draft);
        root.Children.Add(submit);

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private View CreateRosterRow(AttendanceRosterItem item)
    {
        var picker = new Picker();
        foreach (var status in Enum.GetValues<AttendanceStatus>())
        {
            picker.Items.Add(DomainText.Attendance(status));
        }

        picker.SelectedIndex = (int)item.Status;
        _statusPickers.Add((item, picker));

        var grid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(44),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(112)
            }
        };
        grid.Children.Add(UiKit.Avatar(item.PhotoPath, 42));
        var name = UiKit.Headline(item.TraineeName);
        name.VerticalTextAlignment = TextAlignment.Center;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var field = UiKit.LabeledField("TRẠNG THÁI", picker);
        Grid.SetColumn(field, 2);
        grid.Children.Add(field);
        return UiKit.Card(grid, new Thickness(7));
    }

    private async Task SubmitAsync(string overrideReason, Button button)
    {
        if (_historicalMode && string.IsNullOrWhiteSpace(overrideReason))
        {
            overrideReason = "Bổ sung buổi học cũ theo lịch lớp";
        }
        var unmarked = _statusPickers
            .Where(pair => pair.Picker.SelectedIndex == (int)AttendanceStatus.Unmarked)
            .ToList();
        if (unmarked.Count > 0)
        {
            var convert = await DisplayAlertAsync(
                "Còn học viên chưa ghi nhận",
                $"Chuyển {unmarked.Count} học viên chưa ghi nhận thành Vắng?",
                "Chuyển thành Vắng",
                "Quay lại");
            if (!convert)
            {
                return;
            }

            foreach (var pair in unmarked)
            {
                pair.Picker.SelectedIndex = (int)AttendanceStatus.Absent;
            }
        }

        var confirmed = await DisplayAlertAsync(
            "Hoàn tất điểm danh?",
            "Trạng thái sẽ hiển thị cho account học viên.",
            "Hoàn tất",
            "Hủy");
        if (!confirmed)
        {
            return;
        }

        await SaveAsync(true, overrideReason, button);
    }

    private async Task SaveAsync(bool submit, string overrideReason, Button button)
    {
        if (_trainingSession is null)
        {
            return;
        }

        foreach (var pair in _statusPickers)
        {
            pair.Item.Status = pair.Picker.SelectedIndex < 0
                ? AttendanceStatus.Unmarked
                : (AttendanceStatus)pair.Picker.SelectedIndex;
        }

        await RunActionAsync(
            () => _database.SaveAttendanceAsync(
                CurrentUserId,
                _trainingSession.Id,
                _statusPickers.Select(pair => pair.Item),
                submit,
                overrideReason,
                founderCoachTaughtManually: _historicalMode
                    && _founderModePicker?.SelectedIndex == 0),
            button,
            submit ? "Đã hoàn tất điểm danh." : "Đã lưu bản nháp.");
    }
}

/// <summary>
/// Founder-only audit trail for Coach selfie check-ins.  Unlike the approval
/// queue, it keeps pending, approved and rejected rows visible by class date.
/// </summary>
public sealed class CoachCheckInHistoryPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly string? _coachUserId;
    private readonly string? _className;
    private readonly int? _year;
    private readonly int? _month;
    private readonly List<(CoachCheckIn CheckIn, Label Label)> _runningTimers = [];
    private IDispatcherTimer? _elapsedTimer;

    public CoachCheckInHistoryPage(
        AppDatabase database,
        SessionService session,
        string? coachUserId = null,
        string? className = null,
        int? year = null,
        int? month = null)
        : base(session, "Lịch sử dạy học")
    {
        _database = database;
        _coachUserId = coachUserId;
        _className = className;
        _year = year;
        _month = month;
    }

    protected override async Task LoadAsync()
    {
        _elapsedTimer?.Stop();
        _elapsedTimer = null;
        _runningTimers.Clear();

        var rows = await _database.GetCoachCheckInHistoryAsync(CurrentUserId);
        if (!string.IsNullOrWhiteSpace(_coachUserId))
        {
            rows = rows
                .Where(item => item.CheckIn.CoachUserId == _coachUserId)
                .ToList();
        }
        rows = rows
            .Where(item => string.IsNullOrWhiteSpace(_className) || item.ClassName == _className)
            .ToList();
        if (Session.CurrentUser?.Role == UserRole.Coach)
        {
            Content = BuildCoachFilteredHistory(rows);
            return;
        }

        rows = rows
            .Where(item => (!_year.HasValue || item.SessionDate.Year == _year.Value)
                           && (!_month.HasValue || item.SessionDate.Month == _month.Value))
            .ToList();
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.StatusBadge($"{rows.Count} buổi dạy", UiKit.Primary)
            }
        };

        // Founder starts from a compact Coach directory.  The detailed
        // timeline (including selfies and filters) is opened only after a
        // Coach is selected, so a large team does not create a very long
        // history page.
        if (Session.CurrentUser?.Role == UserRole.Founder
            && string.IsNullOrWhiteSpace(_coachUserId))
        {
            var coaches = await _database.GetMembersAsync(CurrentUserId, UserRole.Coach);
            if (coaches.Count == 0)
            {
                root.Children.Add(UiKit.EmptyState(
                    "Chưa có Huấn luyện viên",
                    "Các Coach được phân công vào lớp sẽ xuất hiện tại đây."));
            }
            else
            {
                foreach (var coach in coaches)
                {
                    var coachRows = rows
                        .Where(item => item.CheckIn.CoachUserId == coach.Account.Id)
                        .ToList();
                    root.Children.Add(CreateCoachSummaryCard(coach, coachRows));
                }
            }

            Content = UiKit.KeyboardAwareScroll(root);
            StartElapsedTimer();
            return;
        }

        // The Founder directory only needs summary data. Download the private
        // R2 selfies after a Coach is selected so opening the directory stays
        // instant even when the club has a long teaching history.
        if (Session.CurrentUser?.Role == UserRole.Founder)
        {
            await _database.EnsureCoachCheckInSelfieImagesAsync(CurrentUserId, rows);
        }

        if (rows.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có buổi dạy",
                "Các buổi dạy của Huấn Luyện Viên sẽ xuất hiện tại đây theo ngày học."));
        }
        else
        {
            foreach (var row in rows)
            {
                root.Children.Add(CreateCheckInCard(row));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
        StartElapsedTimer();
    }

    private View BuildCoachFilteredHistory(IReadOnlyList<CoachCheckInHistoryRow> allRows)
    {
        var years = allRows
            .Select(item => item.SessionDate.Year)
            .Append(DateTime.Today.Year)
            .Distinct()
            .OrderByDescending(item => item)
            .ToList();
        var yearPicker = new Picker { Title = "Năm" };
        yearPicker.Items.Add("Tất cả");
        foreach (var year in years)
        {
            yearPicker.Items.Add(year.ToString());
        }
        yearPicker.SelectedIndex = _year is { } initialYear
            ? Math.Max(0, years.IndexOf(initialYear) + 1)
            : 0;

        var monthPicker = new Picker { Title = "Tháng" };
        monthPicker.Items.Add("Tất cả");
        for (var month = 1; month <= 12; month++)
        {
            monthPicker.Items.Add($"Tháng {month}");
        }
        monthPicker.SelectedIndex = _month is >= 1 and <= 12
            ? _month.Value
            : 0;

        var filterGrid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };
        filterGrid.Children.Add(UiKit.LabeledField("NĂM", yearPicker));
        var monthField = UiKit.LabeledField("THÁNG", monthPicker);
        Grid.SetColumn(monthField, 1);
        filterGrid.Children.Add(monthField);

        var countBadge = UiKit.StatusBadge("0 buổi dạy", UiKit.Primary);
        var results = new VerticalStackLayout { Spacing = UiKit.SectionSpacing };
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.Card(filterGrid),
                countBadge,
                results
            }
        };

        void RenderResults()
        {
            _elapsedTimer?.Stop();
            _elapsedTimer = null;
            _runningTimers.Clear();
            results.Children.Clear();
            int? selectedYear = yearPicker.SelectedIndex > 0
                ? years[yearPicker.SelectedIndex - 1]
                : null;
            int? selectedMonth = monthPicker.SelectedIndex > 0
                ? monthPicker.SelectedIndex
                : null;
            var filtered = allRows
                .Where(item => (!selectedYear.HasValue || item.SessionDate.Year == selectedYear.Value)
                               && (!selectedMonth.HasValue || item.SessionDate.Month == selectedMonth.Value))
                .ToList();
            countBadge.Content = new Label
            {
                Text = $"{filtered.Count} buổi dạy",
                FontFamily = "OpenSansSemibold",
                FontSize = 11,
                TextColor = UiKit.Primary,
                HorizontalTextAlignment = TextAlignment.Center
            };
            if (filtered.Count == 0)
            {
                results.Children.Add(UiKit.EmptyState(
                    "Chưa có buổi dạy",
                    "Không có lịch sử dạy học phù hợp với năm và tháng đã chọn."));
            }
            else
            {
                foreach (var row in filtered)
                {
                    results.Children.Add(CreateCheckInCard(row));
                }
            }
            StartElapsedTimer();
        }

        yearPicker.SelectedIndexChanged += (_, _) => RenderResults();
        monthPicker.SelectedIndexChanged += (_, _) => RenderResults();
        RenderResults();
        return UiKit.KeyboardAwareScroll(root);
    }

    private void StartElapsedTimer()
    {
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
        _runningTimers.Clear();
        base.OnDisappearing();
    }

    private View CreateCoachSummaryCard(
        MemberRow coach,
        IReadOnlyList<CoachCheckInHistoryRow> rows)
    {
        var latest = rows
            .OrderByDescending(item => item.SessionDate)
            .ThenByDescending(item => item.CheckIn.CheckedInAtUtc)
            .FirstOrDefault();
        var detail = new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                UiKit.Headline(coach.DisplayName),
                UiKit.Caption(CoachPositionCatalog.Label(coach.Profile.CoachPosition), UiKit.Primary),
                UiKit.Caption(rows.Count == 0
                    ? "Chưa có lịch sử dạy"
                    : $"{rows.Count} buổi · Gần nhất {latest!.SessionDate:dd/MM/yyyy}"),
                        latest is null
                            ? UiKit.StatusBadge("Chưa check-in", UiKit.TextSecondary)
                            : CreateHistoryStatusBadge(latest.CheckIn)
            }
        };
        if (latest is not null)
        {
            var duration = UiKit.Caption(
                $"Thời gian dạy: {CoachCheckInTime.FormatDuration(CoachCheckInTime.ElapsedSeconds(latest.CheckIn))}");
            detail.Children.Add(duration);
            if (IsLiveCheckIn(latest.CheckIn))
            {
                _runningTimers.Add((latest.CheckIn, duration));
            }
        }
        var card = UiKit.Card(new HorizontalStackLayout
        {
            Spacing = 12,
            Children =
            {
                UiKit.Avatar(coach.Profile.PhotoPath, 52),
                detail,
                new Label
                {
                    Text = "›",
                    FontSize = 28,
                    TextColor = UiKit.TextSecondary,
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Center
                }
            }
        });
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
            await Navigation.PushAsync(new CoachCheckInHistoryPage(
                _database,
                Session,
                coach.Account.Id));
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private View CreateCheckInCard(CoachCheckInHistoryRow row)
    {
        var duration = UiKit.Caption(
            $"Thời gian dạy: {CoachCheckInTime.FormatDuration(CoachCheckInTime.ElapsedSeconds(row.CheckIn))}");
        var stack = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new HorizontalStackLayout
                {
                    Spacing = 10,
                    Children =
                    {
                        UiKit.Avatar(row.CoachPhotoPath, 44),
                        new VerticalStackLayout
                        {
                            Spacing = 2,
                            VerticalOptions = LayoutOptions.Center,
                            Children =
                            {
                                UiKit.Headline(row.CoachName),
                                UiKit.Caption(CoachPositionCatalog.Label(row.CoachPosition), UiKit.Primary),
                                UiKit.Caption(row.ClassName)
                            }
                        }
                    }
                },
                UiKit.Caption(
                    CoachCheckInTime.IsFounderSubstitution(row.CheckIn)
                        ? $"Ngày học {row.SessionDate:dd/MM/yyyy} · Founder điểm danh thay Coach"
                    : CoachCheckInTime.IsAutoAbsent(row.CheckIn)
                        ? $"Ngày học {row.SessionDate:dd/MM/yyyy} · Không check-in"
                        : $"Ngày học {row.SessionDate:dd/MM/yyyy} · {CoachCheckInTime.Range(row.CheckIn)}"),
                duration,
                CreateHistoryStatusBadge(row.CheckIn)
            }
        };

        if (IsLiveCheckIn(row.CheckIn))
        {
            _runningTimers.Add((row.CheckIn, duration));
        }

        if (!string.IsNullOrWhiteSpace(row.CheckIn.ReviewNote))
        {
            stack.Children.Add(UiKit.Caption($"Ghi chú: {row.CheckIn.ReviewNote}"));
        }

        if (File.Exists(row.CheckIn.SelfiePath))
        {
            var selfie = new Image
            {
                Source = ImageSource.FromFile(row.CheckIn.SelfiePath),
                HeightRequest = 180,
                Aspect = Aspect.AspectFit
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
                await Navigation.PushAsync(new CheckInSelfiePreviewPage(
                    row.CoachName,
                    row.CheckIn.SelfiePath));
            selfie.GestureRecognizers.Add(tap);
            stack.Children.Add(selfie);
            stack.Children.Add(UiKit.Caption("Chạm vào ảnh để xem lớn hơn."));
        }

        if (File.Exists(row.CheckIn.CheckOutSelfiePath))
        {
            stack.Children.Add(UiKit.Caption("Ảnh check-out"));
            var checkout = new Image
            {
                Source = ImageSource.FromFile(row.CheckIn.CheckOutSelfiePath),
                HeightRequest = 180,
                Aspect = Aspect.AspectFit
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
                await Navigation.PushAsync(new CheckInSelfiePreviewPage(
                    $"{row.CoachName} · check-out",
                    row.CheckIn.CheckOutSelfiePath));
            checkout.GestureRecognizers.Add(tap);
            stack.Children.Add(checkout);
        }

        return UiKit.Card(stack);
    }

    private static Color CoachCheckInColor(CoachCheckInApprovalStatus status) => status switch
    {
        CoachCheckInApprovalStatus.Approved => UiKit.Success,
        CoachCheckInApprovalStatus.Rejected => UiKit.Danger,
        _ => UiKit.Success
    };

    private static bool IsLiveCheckIn(CoachCheckIn checkIn) =>
        !CoachCheckInTime.IsAutoAbsent(checkIn)
        && !CoachCheckInTime.IsFounderSubstitution(checkIn)
        && checkIn.ApprovalStatus != CoachCheckInApprovalStatus.Rejected
        && checkIn.CheckedOutAtUtc is null;

    private static string HistoryStatusText(CoachCheckIn checkIn) =>
        CoachCheckInTime.IsFounderSubstitution(checkIn)
            ? "Coach không dạy · Founder điểm danh"
            : CoachCheckInTime.IsAutoAbsent(checkIn)
            ? "Vắng check-in · Đã khóa"
            : checkIn.CheckedOutAtUtc is { } checkedOutAt
                ? $"Đã check-out · {checkedOutAt.ToLocalTime():HH:mm}"
            : checkIn.ApprovalStatus == CoachCheckInApprovalStatus.Pending
                ? "Check-in thành công"
                : DomainText.CoachCheckInApproval(checkIn.ApprovalStatus);

    private static View CreateHistoryStatusBadge(CoachCheckIn checkIn) =>
        CoachCheckInTime.IsFounderSubstitution(checkIn)
            ? UiKit.StatusBadge(HistoryStatusText(checkIn), UiKit.Warning)
        : checkIn.ApprovalStatus == CoachCheckInApprovalStatus.Pending
        && checkIn.CheckedOutAtUtc is null
        && !CoachCheckInTime.IsAutoAbsent(checkIn)
            ? UiKit.SuccessStatusBadge(HistoryStatusText(checkIn))
            : UiKit.StatusBadge(
                HistoryStatusText(checkIn),
                CoachCheckInColor(checkIn.ApprovalStatus));

    private void UpdateRunningTimers()
    {
        foreach (var item in _runningTimers)
        {
            item.Label.Text =
                $"Thời gian dạy: {CoachCheckInTime.FormatDuration(CoachCheckInTime.ElapsedSeconds(item.CheckIn))}";
        }
    }
}

/// <summary>
/// Chronological Founder view of one trainee attendance category.  Keeping a
/// dedicated page for each status makes the date list directly reachable from
/// the attendance hub rather than hiding it behind a filter.
/// </summary>
public sealed class FounderTraineeAttendanceHistoryPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly AttendanceStatus _status;

    public FounderTraineeAttendanceHistoryPage(
        AppDatabase database,
        SessionService session,
        AttendanceStatus status)
        : base(session, "Lịch sử điểm danh")
    {
        _database = database;
        _status = status;
    }

    protected override async Task LoadAsync()
    {
        var rows = await _database.GetFounderTraineeAttendanceHistoryAsync(
            CurrentUserId,
            _status);
        var statusName = DomainText.Attendance(_status);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.LargeTitle($"Học viên {statusName.ToLowerInvariant()}"),
                UiKit.Body(
                    "Danh sách được sắp xếp theo ngày học mới nhất.",
                    UiKit.TextSecondary),
                UiKit.StatusBadge(
                    $"{rows.Count} lượt {statusName.ToLowerInvariant()}",
                    UiKit.AttendanceColor(_status))
            }
        };

        if (rows.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                $"Chưa có học viên {statusName.ToLowerInvariant()}",
                "Các buổi điểm danh đã hoàn tất sẽ xuất hiện ở đây theo ngày học."));
        }
        else
        {
            foreach (var row in rows)
            {
                root.Children.Add(CreateAttendanceCard(row));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private static View CreateAttendanceCard(FounderTraineeAttendanceHistoryRow row)
    {
        return UiKit.Card(new HorizontalStackLayout
        {
            Spacing = 10,
            Children =
            {
                UiKit.Avatar(row.TraineePhotoPath, 46),
                new VerticalStackLayout
                {
                    Spacing = 3,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Fill,
                    Children =
                    {
                        UiKit.Headline(row.TraineeName),
                        UiKit.Caption($"{row.ClassName} · {row.SessionDate:dd/MM/yyyy}"),
                        UiKit.StatusBadge(
                            DomainText.Attendance(row.Status),
                            UiKit.AttendanceColor(row.Status))
                    }
                }
            }
        });
    }
}

public sealed class CoachCheckInReviewPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly string? _coachUserId;

    public CoachCheckInReviewPage(
        AppDatabase database,
        SessionService session,
        string? coachUserId = null)
        : base(session, "Chờ xác nhận check-out")
    {
        _database = database;
        _coachUserId = coachUserId;
    }

    protected override async Task LoadAsync()
    {
        var rows = await _database.GetPendingCoachCheckInsAsync(
            CurrentUserId,
            _coachUserId);
        await _database.EnsureCoachCheckInSelfieImagesAsync(CurrentUserId, rows);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.Body(
                    "Founder kiểm tra đủ ảnh check-in và check-out rồi mới xác nhận và tính lương.",
                    UiKit.TextSecondary),
                UiKit.StatusBadge($"{rows.Count} ca đang chờ xác nhận", UiKit.Warning)
            }
        };

        if (rows.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Không có ca chờ xác nhận",
                "Ca chỉ xuất hiện sau khi Huấn Luyện Viên đã gửi đủ selfie check-in và check-out."));
        }
        else
        {
            foreach (var row in rows)
            {
                root.Children.Add(CreateReviewCard(row));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private View CreateReviewCard(CoachCheckInReviewRow row)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                UiKit.Headline(row.CoachName),
                UiKit.Caption(CoachPositionCatalog.Label(row.CoachPosition), UiKit.Primary),
                UiKit.Body(row.ClassName, UiKit.TextSecondary),
                UiKit.Caption(
                    $"Ngày học {row.SessionDate:dd/MM/yyyy} · Gửi lúc {row.CheckIn.CheckedInAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}"),
                UiKit.Caption(
                    $"Thời gian dạy: {CoachCheckInTime.FormatDuration(CoachCheckInTime.ElapsedSeconds(row.CheckIn))}"),
                UiKit.StatusBadge("Chờ Founder xác nhận check-out", UiKit.Warning)
            }
        };

        AddSelfiePreview(
            stack,
            row.CoachName,
            "Ảnh check-in",
            row.CheckIn.SelfiePath,
            "Không tìm thấy tệp selfie check-in");
        AddSelfiePreview(
            stack,
            row.CoachName,
            "Ảnh check-out",
            row.CheckIn.CheckOutSelfiePath,
            "Không tìm thấy tệp selfie check-out");

        var buttons = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };
        var reject = UiKit.DestructiveButton("Từ chối");
        reject.Clicked += async (_, _) => await ReviewAsync(row, false, reject);
        buttons.Children.Add(reject);
        var approve = UiKit.PrimaryButton("Xác nhận");
        approve.IsEnabled = CoachCheckInTime.HasCoachCheckout(row.CheckIn)
                            && File.Exists(row.CheckIn.SelfiePath)
                            && File.Exists(row.CheckIn.CheckOutSelfiePath);
        approve.Clicked += async (_, _) => await ReviewAsync(row, true, approve);
        Grid.SetColumn(approve, 1);
        buttons.Children.Add(approve);
        stack.Children.Add(buttons);
        return UiKit.Card(stack);
    }

    private void AddSelfiePreview(
        VerticalStackLayout stack,
        string coachName,
        string title,
        string path,
        string missingText)
    {
        stack.Children.Add(UiKit.Caption(title));
        if (!File.Exists(path))
        {
            stack.Children.Add(UiKit.StatusBadge(missingText, UiKit.Danger));
            return;
        }

        var image = new Image
        {
            Source = ImageSource.FromFile(path),
            HeightRequest = 220,
            Aspect = Aspect.AspectFit,
            BackgroundColor = UiKit.SurfaceSecondary
        };
        var preview = new TapGestureRecognizer();
        preview.Tapped += async (_, _) =>
            await Navigation.PushAsync(new CheckInSelfiePreviewPage(
                $"{coachName} · {title}",
                path));
        image.GestureRecognizers.Add(preview);
        SemanticProperties.SetHint(image, "Nhấn hai lần để xem ảnh lớn");
        stack.Children.Add(image);
    }

    private async Task ReviewAsync(
        CoachCheckInReviewRow row,
        bool approve,
        Button source)
    {
        var note = string.Empty;
        if (!approve)
        {
            note = await DisplayPromptAsync(
                       "Từ chối ca dạy",
                       "Nhập lý do để Huấn Luyện Viên biết và gửi lại ảnh.",
                       "Từ chối",
                       "Hủy",
                       "Lý do từ chối")
                   ?? string.Empty;
            if (string.IsNullOrWhiteSpace(note))
            {
                return;
            }
        }
        else
        {
            var confirmed = await DisplayAlertAsync(
                "Xác nhận check-in và check-out?",
                "Đủ hai ảnh và thời lượng dạy sẽ được ghi nhận; ca này mới được tính vào lương Huấn Luyện Viên.",
                "Xác nhận",
                "Hủy");
            if (!confirmed)
            {
                return;
            }
        }

        await RunActionAsync(
            () => _database.ReviewCoachCheckInAsync(
                CurrentUserId,
                row.CheckIn.Id,
                approve,
                note),
            source,
            approve
                ? "Đã xác nhận check-in/check-out và cập nhật lương."
                : "Đã từ chối ca dạy và gửi thông báo cho Huấn Luyện Viên.");
    }
}

public sealed class CheckInSelfiePreviewPage : ContentPage
{
    public CheckInSelfiePreviewPage(string coachName, string imagePath)
    {
        Title = $"Selfie · {coachName}";
        BackgroundColor = Colors.Black;
        Content = new Image
        {
            Source = ImageSource.FromFile(imagePath),
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
    }
}
