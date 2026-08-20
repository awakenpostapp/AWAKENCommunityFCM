using System.Globalization;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

/// <summary>
/// Founder entry point for classes where the Coach evaluation request is open.
/// The list stays intentionally compact; selecting a class opens its normal
/// class detail page, where the Founder can review the evaluation history.
/// </summary>
public sealed class FounderEvaluationRequestPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly RememberedLoginService _rememberedLogin;

    public FounderEvaluationRequestPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin)
        : base(session, "Lớp mở yêu cầu đánh giá")
    {
        _database = database;
        _media = media;
        _rememberedLogin = rememberedLogin;
    }

    protected override async Task LoadAsync()
    {
        if (!RoleCapabilities.IsFounderLike(Session.CurrentUser?.Role))
        {
            throw new UnauthorizedAccessException(
                "Chỉ Sáng lập & Điều hành được mở danh sách yêu cầu đánh giá.");
        }

        var classes = await _database.GetClassesAsync(
            CurrentUserId,
            refreshOnline: true);
        var openClasses = classes
            .Where(row => row.Class.IsActive && row.Class.EvaluationRequestOpen)
            .OrderBy(row => row.Class.Name)
            .ToList();

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing
        };
        root.Children.Add(UiKit.Caption(
            "Các lớp đang mở yêu cầu để Coach nhập đánh giá học viên.",
            UiKit.TextSecondary));

        if (openClasses.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có lớp mở yêu cầu đánh giá",
                "Mở yêu cầu trong chi tiết lớp học để Coach có thể đánh giá."));
            Content = UiKit.KeyboardAwareScroll(root);
            return;
        }

        foreach (var row in openClasses)
        {
            var details = new VerticalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    UiKit.Headline(row.Class.Name),
                    UiKit.Caption(row.ScheduleText, UiKit.TextSecondary),
                    UiKit.Caption($"Coach: {row.CoachNames}", UiKit.TextSecondary),
                    UiKit.Caption($"Sân: {row.Venue?.Name ?? "Chưa cập nhật"}", UiKit.TextSecondary),
                    UiKit.SuccessStatusBadge("Đang mở yêu cầu đánh giá"),
                    UiKit.Caption("Chạm để xem lớp và lịch sử đánh giá.", UiKit.Primary)
                }
            };
            var card = UiKit.Card(details);
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
                await PushPageAsync(new ClassDetailsPage(
                    _database,
                    Session,
                    _media,
                    _rememberedLogin,
                    row));
            card.GestureRecognizers.Add(tap);
            root.Children.Add(card);
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }
}

/// <summary>
/// Coach entry point for the evaluation workflow. The first screen is kept
/// deliberately compact: it contains classes only. A Coach taps a class to
/// open the limited trainee roster for that class.
/// </summary>
public sealed class CoachEvaluationPage : AsyncContentPage
{
    private readonly AppDatabase _database;

    public CoachEvaluationPage(
        AppDatabase database,
        SessionService session)
        : base(session, "Đánh giá học viên")
    {
        _database = database;
    }

    protected override async Task LoadAsync()
    {
        if (Session.CurrentUser?.Role != UserRole.Coach)
        {
            throw new UnauthorizedAccessException("Chỉ Huấn luyện viên được mở trang đánh giá.");
        }

        var classes = await _database.GetClassesAsync(CurrentUserId, refreshOnline: true);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.Caption(
                    "Chọn một lớp học để xem danh sách Cầu thủ học viên và đánh giá.",
                    UiKit.TextSecondary)
            }
        };

        if (classes.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có lớp được phân công",
                "Khi Founder phân công lớp, lớp sẽ xuất hiện tại đây."));
            Content = UiKit.KeyboardAwareScroll(root);
            return;
        }

        foreach (var row in classes.OrderBy(item => item.Class.Name))
        {
            var classContent = new VerticalStackLayout { Spacing = 6 };
            classContent.Children.Add(UiKit.Headline(row.Class.Name));
            classContent.Children.Add(UiKit.Caption(row.ScheduleText, UiKit.TextSecondary));
            classContent.Children.Add(UiKit.Caption(
                $"Sân: {row.Venue?.Name ?? "Chưa cập nhật"}",
                UiKit.TextSecondary));

            classContent.Children.Add(UiKit.Caption(
                "Chạm để xem Cầu thủ học viên",
                UiKit.Primary));

            var card = UiKit.Card(classContent);
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
                await Navigation.PushAsync(new CoachEvaluationClassPage(
                    _database,
                    Session,
                    row));
            card.GestureRecognizers.Add(tap);
            root.Children.Add(card);
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }
}

/// <summary>Limited trainee roster shown after a Coach selects one class.</summary>
public sealed class CoachEvaluationClassPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly ClassRow _classRow;
    private readonly VerticalStackLayout _list = new() { Spacing = UiKit.SectionSpacing };

    public CoachEvaluationClassPage(
        AppDatabase database,
        SessionService session,
        ClassRow classRow)
        : base(session, classRow.Class.Name)
    {
        _database = database;
        _classRow = classRow;
        Content = UiKit.ScrollBody(_list);
    }

    protected override async Task LoadAsync()
    {
        _list.Children.Clear();
        _list.Children.Add(UiKit.Title(_classRow.Class.Name));
        _list.Children.Add(UiKit.Caption(_classRow.ScheduleText, UiKit.TextSecondary));
        _list.Children.Add(UiKit.Caption(
            $"Sân: {_classRow.Venue?.Name ?? "Chưa cập nhật"}",
            UiKit.TextSecondary));

        var requestOpen = await _database.IsTraineeEvaluationRequestOpenAsync(
            CurrentUserId,
            _classRow.Class.Id);
        if (!requestOpen)
        {
            _list.Children.Add(UiKit.StatusBadge("Founder chưa mở yêu cầu", UiKit.TextSecondary));
            _list.Children.Add(UiKit.Caption(
                "Danh sách Cầu thủ học viên sẽ xuất hiện sau khi Founder mở yêu cầu đánh giá cho lớp này.",
                UiKit.TextSecondary));
            return;
        }

        _list.Children.Add(UiKit.StatusBadge("Đang mở yêu cầu đánh giá", UiKit.Success));
        var trainees = await _database.GetTraineeEvaluationRosterAsync(
            CurrentUserId,
            _classRow.Class.Id);
        if (trainees.Count == 0)
        {
            _list.Children.Add(UiKit.EmptyState(
                "Chưa có Cầu thủ học viên",
                "Lớp chưa có học viên đang hoạt động."));
            return;
        }

        _list.Children.Add(UiKit.Caption(
            "Chọn một Cầu thủ học viên để xem lịch sử hoặc nhập đánh giá mới.",
            UiKit.TextSecondary));
        foreach (var trainee in trainees.OrderBy(item => item.FullName))
        {
            var details = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    UiKit.Headline(trainee.FullName),
                    UiKit.Caption($"Ngày sinh: {FormatBirthDate(trainee.DateOfBirth)}", UiKit.TextSecondary),
                    UiKit.Caption($"Chiều cao: {FormatDimension(trainee.HeightCm, "cm")} · Cân nặng: {FormatDimension(trainee.WeightKg, "kg")}", UiKit.TextSecondary),
                    UiKit.Caption("Chạm để xem đánh giá", UiKit.Primary)
                }
            };
            var card = UiKit.Card(details);
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
                await Navigation.PushAsync(new TraineeEvaluationHistoryPage(
                    _database,
                    Session,
                    trainee.TraineeUserId,
                    trainee.FullName,
                    _classRow.Class.Id));
            card.GestureRecognizers.Add(tap);
            _list.Children.Add(card);
        }
    }

    private static string FormatBirthDate(DateTime? date) =>
        date is null ? "Chưa cập nhật" : date.Value.ToString("dd/MM/yyyy");

    private static string FormatDimension(double value, string unit) =>
        value <= 0 ? "Chưa cập nhật" : $"{value:0.#} {unit}";
}

public sealed class TraineeEvaluationHistoryPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly string _traineeUserId;
    private readonly string? _classId;
    private readonly string _traineeName;
    private readonly VerticalStackLayout _list = new() { Spacing = UiKit.SectionSpacing };
    private bool _evaluationRequestOpen;

    public TraineeEvaluationHistoryPage(
        AppDatabase database,
        SessionService session,
        string traineeUserId,
        string traineeName,
        string? classId = null)
        : base(session, "Lịch sử đánh giá học viên")
    {
        _database = database;
        _traineeUserId = traineeUserId;
        _classId = classId;
        _traineeName = traineeName;
        Content = UiKit.ScrollBody(_list);
    }

    protected override async Task LoadAsync()
    {
        _list.Children.Clear();
        var rows = await _database.GetTraineeEvaluationsAsync(
            CurrentUserId,
            _traineeUserId,
            _classId);
        var role = Session.CurrentUser?.Role;
        _evaluationRequestOpen = !string.IsNullOrWhiteSpace(_classId)
            && (RoleCapabilities.IsFounderLike(role)
                || role is UserRole.Coach or UserRole.Trainee)
            && await _database.IsTraineeEvaluationRequestOpenAsync(CurrentUserId, _classId!);
        var canCreate = role == UserRole.Coach
                        && !string.IsNullOrWhiteSpace(_classId)
                        && _evaluationRequestOpen;
        var canReview = RoleCapabilities.IsFounderLike(role);

        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        header.Children.Add(UiKit.Title(_traineeName));
        var count = UiKit.StatusBadge($"{rows.Count} lần đánh giá", UiKit.Primary);
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        _list.Children.Add(header);
        _list.Children.Add(UiKit.Caption(
            "Lịch sử được giữ lại để học viên, Coach và Founder theo dõi tiến bộ. Đánh giá đã được Founder xác nhận sẽ không thể sửa."));

        if (RoleCapabilities.IsFounderLike(role) && !string.IsNullOrWhiteSpace(_classId))
        {
            var requestButton = UiKit.PrimaryButton(
                _evaluationRequestOpen
                    ? "Đóng yêu cầu đánh giá Coach"
                    : "Mở yêu cầu Coach đánh giá lớp");
            requestButton.Clicked += async (_, _) =>
            {
                requestButton.IsEnabled = false;
                try
                {
                    await _database.SetTraineeEvaluationRequestAsync(
                        CurrentUserId,
                        _classId!,
                        !_evaluationRequestOpen);
                    await ReloadAsync();
                }
                catch (Exception exception)
                {
                    await DisplayAlertAsync("Chưa thể cập nhật yêu cầu", exception.Message, "Đóng");
                    requestButton.IsEnabled = true;
                }
            };
            _list.Children.Add(requestButton);
            _list.Children.Add(UiKit.Caption(
                _evaluationRequestOpen
                    ? "Yêu cầu đang mở: Coach có thể nhập đánh giá cho học viên trong lớp."
                    : "Đánh giá chỉ xuất hiện sau khi Founder mở yêu cầu cho lớp này.",
                UiKit.TextSecondary));
        }
        else if (role == UserRole.Coach && !string.IsNullOrWhiteSpace(_classId) && !canCreate)
        {
            _list.Children.Add(UiKit.Caption(
                "Founder chưa mở yêu cầu đánh giá cho lớp này.", UiKit.TextSecondary));
        }

        if (canCreate)
        {
            var create = UiKit.PrimaryButton("Tạo đánh giá theo yêu cầu Founder");
            create.Clicked += async (_, _) =>
            {
                await Navigation.PushAsync(new TraineeEvaluationEditorPage(
                    _database,
                    Session,
                    _classId!,
                    _traineeUserId,
                    _traineeName));
            };
            _list.Children.Add(create);
        }

        if (rows.Count == 0)
        {
            _list.Children.Add(UiKit.EmptyState(
                "Chưa có đánh giá",
                canCreate
                    ? "Founder đã mở yêu cầu. Coach có thể tạo đánh giá sau một khoảng thời gian học hoặc sau trận đấu."
                    : "Khi Coach gửi đánh giá, lịch sử sẽ xuất hiện tại đây."));
            return;
        }

        foreach (var row in rows)
        {
            _list.Children.Add(BuildEvaluationCard(row, canReview));
        }
    }

    private View BuildEvaluationCard(TraineeEvaluationRow row, bool canReview)
    {
        var evaluation = row.Evaluation;
        var title = string.IsNullOrWhiteSpace(evaluation.Title)
            ? DomainText.EvaluationType(evaluation.EvaluationType)
            : evaluation.Title;
        var statusColor = evaluation.Status switch
        {
            TraineeEvaluationStatus.Approved => UiKit.Success,
            TraineeEvaluationStatus.Rejected => UiKit.Danger,
            _ => UiKit.Warning
        };
        var heading = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        heading.Children.Add(UiKit.Headline(title));
        var status = UiKit.StatusBadge(DomainText.EvaluationStatus(evaluation.Status), statusColor);
        Grid.SetColumn(status, 1);
        heading.Children.Add(status);
        var content = new VerticalStackLayout
        {
            Spacing = 5,
            Children =
            {
                heading,
                UiKit.Caption(
                    $"{evaluation.EvaluationDateUtc.ToLocalTime():dd/MM/yyyy} · Lớp: {row.ClassName} · Coach: {row.CoachName} · {CoachPositionCatalog.Label(row.CoachPosition)}"),
                UiKit.Body($"Điểm tổng quan: {evaluation.OverallScore}/5", UiKit.Primary),
                UiKit.Caption(ScoreText(evaluation), UiKit.TextSecondary)
            }
        };
        if (!string.IsNullOrWhiteSpace(evaluation.Strengths))
        {
            content.Children.Add(UiKit.Body($"Điểm mạnh: {evaluation.Strengths}"));
        }
        if (!string.IsNullOrWhiteSpace(evaluation.Improvements))
        {
            content.Children.Add(UiKit.Body($"Cần cải thiện: {evaluation.Improvements}"));
        }
        if (!string.IsNullOrWhiteSpace(evaluation.Notes))
        {
            content.Children.Add(UiKit.Caption($"Ghi chú: {evaluation.Notes}"));
        }
        if (row.Previous is not null)
        {
            content.Children.Add(UiKit.Caption(
                $"So với lần trước: {row.Previous.OverallScore}/5 → {evaluation.OverallScore}/5",
                evaluation.OverallScore >= row.Previous.OverallScore ? UiKit.Success : UiKit.Warning));
        }
        if (!string.IsNullOrWhiteSpace(evaluation.ReviewNote))
        {
            content.Children.Add(UiKit.Caption($"Phản hồi Founder: {evaluation.ReviewNote}", UiKit.TextSecondary));
        }

        var actions = new HorizontalStackLayout { Spacing = 8 };
        if (canReview && evaluation.Status != TraineeEvaluationStatus.Approved)
        {
            var approve = UiKit.PrimaryButton("Xác nhận");
            approve.HorizontalOptions = LayoutOptions.Fill;
            approve.Clicked += async (_, _) => await ReviewAsync(evaluation, true, approve);
            var reject = UiKit.DestructiveButton("Yêu cầu chỉnh sửa");
            reject.HorizontalOptions = LayoutOptions.Fill;
            reject.Clicked += async (_, _) => await ReviewAsync(evaluation, false, reject);
            actions.Children.Add(approve);
            actions.Children.Add(reject);
        }
        if (Session.CurrentUser?.Role == UserRole.Coach
            && evaluation.CoachUserId == CurrentUserId
            && evaluation.Status != TraineeEvaluationStatus.Approved
            && _evaluationRequestOpen
            && !string.IsNullOrWhiteSpace(_classId))
        {
            var edit = UiKit.SecondaryButton("Sửa");
            edit.Clicked += async (_, _) => await Navigation.PushAsync(
                new TraineeEvaluationEditorPage(
                    _database,
                    Session,
                    evaluation.ClassId,
                    evaluation.TraineeUserId,
                    _traineeName,
                    evaluation));
            actions.Children.Add(edit);
        }
        if (actions.Children.Count > 0)
        {
            content.Children.Add(actions);
        }
        return UiKit.Card(content, new Thickness(12));
    }

    private async Task ReviewAsync(TraineeEvaluation evaluation, bool approved, Button source)
    {
        source.IsEnabled = false;
        try
        {
            var note = approved
                ? string.Empty
                : await DisplayPromptAsync("Yêu cầu chỉnh sửa", "Nêu ngắn gọn nội dung cần bổ sung (không bắt buộc):", "Gửi", "Hủy");
            if (note is null)
            {
                return;
            }
            await _database.ReviewTraineeEvaluationAsync(CurrentUserId, evaluation.Id, approved, note);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Chưa thể xác nhận", exception.Message, "Đóng");
        }
        finally
        {
            source.IsEnabled = true;
        }
    }

    private static string ScoreText(TraineeEvaluation evaluation)
    {
        static string Part(string label, int value) => value <= 0 ? $"{label}: —" : $"{label}: {value}/5";
        return string.Join(" · ",
            Part("Kỹ thuật", evaluation.TechnicalScore),
            Part("Chiến thuật", evaluation.TacticalScore),
            Part("Thể lực", evaluation.PhysicalScore),
            Part("Thái độ", evaluation.AttitudeScore));
    }
}

public sealed class TraineeEvaluationEditorPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly SessionService _session;
    private readonly string _classId;
    private readonly string _traineeUserId;
    private readonly string _traineeName;
    private readonly TraineeEvaluation? _existing;
    private readonly Picker _type = new() { Title = "Loại đánh giá" };
    private readonly Entry _title = new() { Placeholder = "Ví dụ: Đánh giá giữa chu kỳ / Giải mùa hè" };
    private readonly DatePicker _date = new() { Date = DateTime.Today, Format = "dd/MM/yyyy" };
    private readonly Picker _overall = new() { Title = "Điểm tổng quan (1–5)" };
    private readonly Picker _technical = new() { Title = "Kỹ thuật" };
    private readonly Picker _tactical = new() { Title = "Chiến thuật" };
    private readonly Picker _physical = new() { Title = "Thể lực" };
    private readonly Picker _attitude = new() { Title = "Thái độ" };
    private readonly Editor _strengths = new() { Placeholder = "Điểm mạnh của học viên", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 72 };
    private readonly Editor _improvements = new() { Placeholder = "Nội dung cần cải thiện", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 72 };
    private readonly Editor _notes = new() { Placeholder = "Ghi chú thêm", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 72 };
    private bool _loaded;

    public TraineeEvaluationEditorPage(
        AppDatabase database,
        SessionService session,
        string classId,
        string traineeUserId,
        string traineeName,
        TraineeEvaluation? existing = null)
    {
        _database = database;
        _session = session;
        _classId = classId;
        _traineeUserId = traineeUserId;
        _traineeName = traineeName;
        _existing = existing;
        Title = existing is null ? "Tạo đánh giá" : "Sửa đánh giá";
        BackgroundColor = UiKit.Background;
        _type.ItemsSource = new[] { "Đánh giá định kỳ", "Sau trận đấu / giải" };
        _overall.ItemsSource = Enumerable.Range(1, 5).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToList();
        foreach (var picker in new[] { _technical, _tactical, _physical, _attitude })
        {
            picker.ItemsSource = new[] { "Chưa đánh giá" }.Concat(
                Enumerable.Range(1, 5).Select(value => value.ToString(CultureInfo.InvariantCulture))).ToList();
        }
        Content = new Grid
        {
            Children =
            {
                new ActivityIndicator { IsRunning = true, Color = UiKit.Primary, VerticalOptions = LayoutOptions.Center }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        try
        {
            await BuildAsync();
        }
        catch (Exception exception)
        {
            Content = UiKit.ScrollBody(UiKit.EmptyState("Không thể mở đánh giá", exception.Message));
        }
    }

    private async Task BuildAsync()
    {
        if (_session.CurrentUser?.Role != UserRole.Coach)
            throw new UnauthorizedAccessException("Chỉ Coach được tạo hoặc sửa đánh giá.");
        if (!await _database.IsTraineeEvaluationRequestOpenAsync(_session.CurrentUser.Id, _classId))
            throw new InvalidOperationException("Founder chưa mở yêu cầu đánh giá cho lớp này.");

        var previous = (await _database.GetTraineeEvaluationsAsync(
                _session.CurrentUser.Id,
                _traineeUserId,
                _classId))
            .FirstOrDefault(row => _existing is null || row.Evaluation.Id != _existing.Id);
        var evaluation = _existing ?? new TraineeEvaluation
        {
            Id = string.Empty,
            ClassId = _classId,
            TraineeUserId = _traineeUserId,
            CoachUserId = _session.CurrentUser.Id,
            EvaluationDateUtc = DateTime.UtcNow
        };
        _type.SelectedIndex = evaluation.EvaluationType == TraineeEvaluationType.TournamentMatch ? 1 : 0;
        _title.Text = evaluation.Title;
        _date.Date = evaluation.EvaluationDateUtc.ToLocalTime().Date;
        _overall.SelectedIndex = Math.Clamp(evaluation.OverallScore, 1, 5) - 1;
        _technical.SelectedIndex = Math.Clamp(evaluation.TechnicalScore, 0, 5);
        _tactical.SelectedIndex = Math.Clamp(evaluation.TacticalScore, 0, 5);
        _physical.SelectedIndex = Math.Clamp(evaluation.PhysicalScore, 0, 5);
        _attitude.SelectedIndex = Math.Clamp(evaluation.AttitudeScore, 0, 5);
        _strengths.Text = evaluation.Strengths;
        _improvements.Text = evaluation.Improvements;
        _notes.Text = evaluation.Notes;

        var root = new VerticalStackLayout
        {
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.Title(_traineeName),
                UiKit.Caption("Đánh giá sẽ ở trạng thái chờ Founder xác nhận. Sau khi xác nhận, nội dung được khóa và giữ lại vĩnh viễn.")
            }
        };
        if (previous is not null)
        {
            root.Children.Add(UiKit.Card(new VerticalStackLayout
            {
                Spacing = 3,
                Children =
                {
                    UiKit.Caption("LẦN ĐÁNH GIÁ GẦN NHẤT", UiKit.TextSecondary),
                    UiKit.Body($"{previous.Evaluation.EvaluationDateUtc.ToLocalTime():dd/MM/yyyy} · {previous.Evaluation.OverallScore}/5 · {DomainText.EvaluationStatus(previous.Evaluation.Status)}")
                }
            }, new Thickness(12, 9)));
        }
        root.Children.Add(UiKit.LabeledField("LOẠI ĐÁNH GIÁ", _type));
        root.Children.Add(UiKit.LabeledField("TIÊU ĐỀ / SỰ KIỆN", _title));
        root.Children.Add(UiKit.LabeledField("NGÀY ĐÁNH GIÁ", _date));
        root.Children.Add(UiKit.LabeledField("ĐIỂM TỔNG QUAN", _overall));
        root.Children.Add(UiKit.Title("Chi tiết chất lượng (không bắt buộc)"));
        root.Children.Add(UiKit.LabeledField("KỸ THUẬT", _technical));
        root.Children.Add(UiKit.LabeledField("CHIẾN THUẬT", _tactical));
        root.Children.Add(UiKit.LabeledField("THỂ LỰC", _physical));
        root.Children.Add(UiKit.LabeledField("THÁI ĐỘ", _attitude));
        root.Children.Add(UiKit.LabeledField("ĐIỂM MẠNH", _strengths));
        root.Children.Add(UiKit.LabeledField("CẦN CẢI THIỆN", _improvements));
        root.Children.Add(UiKit.LabeledField("GHI CHÚ", _notes));
        var save = UiKit.PrimaryButton(_existing is null ? "Gửi đánh giá" : "Lưu và gửi lại");
        save.Clicked += async (_, _) => await SaveAsync(save, evaluation);
        root.Children.Add(save);
        Content = UiKit.ScrollBody(root);
    }

    private async Task SaveAsync(Button source, TraineeEvaluation evaluation)
    {
        source.IsEnabled = false;
        try
        {
            evaluation.EvaluationType = _type.SelectedIndex == 1
                ? TraineeEvaluationType.TournamentMatch
                : TraineeEvaluationType.Periodic;
            evaluation.Title = _title.Text?.Trim() ?? string.Empty;
            evaluation.EvaluationDateUtc = DateTime.SpecifyKind(_date.Date ?? DateTime.Today, DateTimeKind.Local).ToUniversalTime();
            evaluation.OverallScore = _overall.SelectedIndex + 1;
            evaluation.TechnicalScore = Math.Max(0, _technical.SelectedIndex);
            evaluation.TacticalScore = Math.Max(0, _tactical.SelectedIndex);
            evaluation.PhysicalScore = Math.Max(0, _physical.SelectedIndex);
            evaluation.AttitudeScore = Math.Max(0, _attitude.SelectedIndex);
            evaluation.Strengths = _strengths.Text?.Trim() ?? string.Empty;
            evaluation.Improvements = _improvements.Text?.Trim() ?? string.Empty;
            evaluation.Notes = _notes.Text?.Trim() ?? string.Empty;
            await _database.SaveTraineeEvaluationAsync(
                _session.CurrentUser?.Id ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc."),
                evaluation);
            await DisplayAlertAsync("Đã gửi", "Đánh giá đã được lưu và chờ Founder xác nhận.", "OK");
            await Navigation.PopAsync();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Chưa thể lưu đánh giá", exception.Message, "Đóng");
        }
        finally
        {
            source.IsEnabled = true;
        }
    }
}
