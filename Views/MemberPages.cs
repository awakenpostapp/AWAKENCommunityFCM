using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

public sealed class MemberManagementPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly RememberedLoginService _rememberedLogin;

    public MemberManagementPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin)
        : base(session, "Thành viên")
    {
        _database = database;
        _media = media;
        _rememberedLogin = rememberedLogin;
    }

    protected override async Task LoadAsync()
    {
        var actorRole = Session.CurrentUser?.Role;
        var visibleRoles = RoleCapabilities.IsFounderLike(actorRole)
            ? new[] { UserRole.CoFounder, UserRole.Manager, UserRole.Coach, UserRole.Trainee }
            : new[] { UserRole.Coach, UserRole.Trainee };
        var members = (await _database.GetMembersAsync(CurrentUserId, includeInactive: true))
            .Where(item => visibleRoles.Contains(item.Account.Role))
            .ToList();
        var addMember = RoleCapabilities.CanManageMembers(actorRole)
            ? UiKit.PrimaryButton(
                "Thêm Huấn Luyện Viên/Cầu Thủ Học Viên",
                async (_, _) => await PushPageAsync(
                    new MemberEditorPage(_database, Session, _media)))
            : null;

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner()
            }
        };
        if (addMember is not null)
        {
            root.Children.Add(addMember);
        }
        foreach (var role in visibleRoles)
        {
            root.Children.Add(CreateRoleCard(role, members.Where(item => item.Account.Role == role).ToList()));
        }
        Content = UiKit.KeyboardAwareScroll(root);
    }

    private View CreateRoleCard(UserRole role, IReadOnlyCollection<MemberRow> members)
    {
        var title = RoleTitle(role);
        var activeCount = members.Count(item => item.Account.IsActive);
        var lockedCount = members.Count - activeCount;
        var header = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Children.Add(UiKit.Title(title));

        var count = UiKit.StatusBadge(
            $"{members.Count} account",
            role == UserRole.Coach
                ? UiKit.Primary
                : role == UserRole.Trainee
                    ? UiKit.Success
                    : UiKit.Warning);
        Grid.SetColumn(count, 1);
        header.Children.Add(count);

        var arrow = UiKit.Body("›", UiKit.TextSecondary);
        arrow.FontSize = 22;
        arrow.VerticalTextAlignment = TextAlignment.Center;
        Grid.SetColumn(arrow, 2);
        header.Children.Add(arrow);

        var status = lockedCount == 0
            ? $"{activeCount} đang hoạt động"
            : $"{activeCount} đang hoạt động · {lockedCount} đã khóa";
        var card = UiKit.Card(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                header,
                UiKit.Body(
                    role == UserRole.Coach
                        ? "Quản lý account và hồ sơ Huấn Luyện Viên."
                        : "Quản lý account và hồ sơ Cầu Thủ Học Viên.",
                    UiKit.TextSecondary),
                UiKit.Caption(status)
            }
        });
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
            await PushPageAsync(new MemberRoleListPage(
                _database,
                Session,
                _media,
                _rememberedLogin,
                role));
        card.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(card, $"Mở danh sách {title}, {members.Count} account");
        SemanticProperties.SetHint(card, "Nhấn hai lần để mở danh sách");
        return card;
    }

    private static string RoleTitle(UserRole role) =>
        role switch
        {
            UserRole.Coach => "Huấn Luyện Viên",
            UserRole.Trainee => "Cầu Thủ Học Viên",
            UserRole.CoFounder => "Đồng Sáng Lập",
            UserRole.Manager => "Quản Lý",
            _ => DomainText.Role(role)
        };
}

public sealed class MemberRoleListPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly RememberedLoginService _rememberedLogin;
    private readonly UserRole _role;
    private readonly SearchBar _searchBar;
    private readonly VerticalStackLayout _list;
    private readonly Label _summary;
    private List<MemberRow> _members = [];
    private IReadOnlyDictionary<string, TraineeTuitionSummary> _traineeTuition =
        new Dictionary<string, TraineeTuitionSummary>(StringComparer.Ordinal);

    public MemberRoleListPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin,
        UserRole role)
        : base(session, RoleTitle(role))
    {
        if (role is not (UserRole.Coach or UserRole.Trainee or UserRole.CoFounder or UserRole.Manager))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        _database = database;
        _media = media;
        _rememberedLogin = rememberedLogin;
        _role = role;
        _searchBar = new SearchBar
        {
            Placeholder = "Tìm theo tên hoặc username",
            BackgroundColor = UiKit.Surface
        };
        _searchBar.TextChanged += (_, _) => RenderMembers();
        _list = new VerticalStackLayout { Spacing = 8 };
        _summary = UiKit.Caption(string.Empty);

        var title = RoleTitle(_role);
        Content = UiKit.KeyboardAwareScroll(new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                _summary,
                _searchBar,
                _list
            }
        });
    }

    protected override async Task LoadAsync()
    {
        _members = (await _database.GetMembersAsync(
                CurrentUserId,
                _role,
                includeInactive: true))
            .ToList();
        if (_role == UserRole.Trainee)
        {
            var invoices = await _database.GetInvoicesAsync(CurrentUserId);
            _traineeTuition = invoices
                .Where(item => item.Invoice.Status == InvoiceStatus.Paid)
                .GroupBy(item => item.Invoice.TraineeUserId)
                .ToDictionary(
                    group => group.Key,
                    group => new TraineeTuitionSummary(
                        group.Sum(item => Math.Max(1, item.Invoice.CycleCount)),
                        group.Sum(item => Math.Max(0, item.Progress.AttendedSessions))),
                    StringComparer.Ordinal);
        }
        else
        {
            _traineeTuition = new Dictionary<string, TraineeTuitionSummary>(StringComparer.Ordinal);
        }
        _summary.Text =
            $"{_members.Count} account · Chạm vào một hồ sơ để xem thông tin.";
        RenderMembers();
    }

    private void RenderMembers()
    {
        _list.Children.Clear();
        var query = (_searchBar.Text ?? string.Empty).Trim();
        var visible = _members
            .Where(item => string.IsNullOrWhiteSpace(query)
                           || item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                           || item.Account.Username.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (visible.Count == 0)
        {
            _list.Children.Add(UiKit.EmptyState(
                _members.Count == 0 ? $"Chưa có {RoleTitle(_role)}" : "Không tìm thấy kết quả",
                _members.Count == 0
                    ? "Quay lại trang Thành viên và chọn “Thêm Huấn Luyện Viên/Cầu Thủ Học Viên”."
                    : "Hãy thử từ khóa khác."));
            return;
        }

        foreach (var member in visible)
        {
            _list.Children.Add(CreateMemberCard(member));
        }
    }

    private View CreateMemberCard(MemberRow member)
    {
        var grid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(50),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        var avatar = UiKit.Avatar(member.Profile.PhotoPath);
        grid.Children.Add(avatar);

        var textChildren = new List<View>
        {
            UiKit.Headline(member.DisplayName)
        };
        if (member.Account.Role == UserRole.Trainee)
        {
            textChildren.Add(CreateTraineeTuitionStatus(member));
        }
        textChildren.Add(UiKit.Caption(
            $"@{member.Account.Username} · {DomainText.Role(member.Account.Role)}"));
        if (member.Account.Role == UserRole.Coach)
        {
            textChildren.Add(UiKit.Caption(
                CoachPositionCatalog.Label(member.Profile.CoachPosition),
                UiKit.Primary));
        }
        textChildren.Add(UiKit.Caption(member.Account.Role == UserRole.Trainee
            ? (string.IsNullOrWhiteSpace(member.Profile.GuardianPhone)
                ? "Chưa có SĐT phụ huynh"
                : $"Phụ huynh: {member.Profile.GuardianPhone}")
            : (string.IsNullOrWhiteSpace(member.Profile.Phone)
                ? "Chưa có số điện thoại"
                : member.Profile.Phone)));
        textChildren.Add(UiKit.Caption(
            member.Account.IsActive ? "Đang hoạt động" : "Account đã khóa",
            member.Account.IsActive ? UiKit.Success : UiKit.Danger));
        var text = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center
        };
        foreach (var child in textChildren)
        {
            text.Children.Add(child);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var arrow = UiKit.Body("›", UiKit.TextSecondary);
        arrow.FontSize = 22;
        arrow.VerticalTextAlignment = TextAlignment.Center;
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);

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
        SemanticProperties.SetDescription(card, $"Mở hồ sơ {member.DisplayName}");
        SemanticProperties.SetHint(card, "Nhấn hai lần để xem hồ sơ");
        return card;
    }

    private View CreateTraineeTuitionStatus(MemberRow member)
    {
        if (member.Account.Role != UserRole.Trainee)
        {
            return UiKit.Caption(string.Empty);
        }

        if (member.Account.IsTuitionSupported)
        {
            return UiKit.Caption(
                DomainText.SupportedTraineeTuitionLabel,
                UiKit.Success);
        }

        if (_traineeTuition.TryGetValue(member.Account.Id, out var summary))
        {
            return UiKit.Caption(
                $"Đã học {summary.PaidCycles} chu kỳ · {summary.AttendedSessions} buổi",
                UiKit.Primary);
        }

        return UiKit.Caption("Chưa đóng học phí", UiKit.TextSecondary);
    }

    private sealed record TraineeTuitionSummary(int PaidCycles, int AttendedSessions);

    private static string RoleTitle(UserRole role) =>
        role switch
        {
            UserRole.Coach => "Huấn Luyện Viên",
            UserRole.Trainee => "Cầu Thủ Học Viên",
            UserRole.CoFounder => "Đồng Sáng Lập",
            UserRole.Manager => "Quản Lý",
            _ => DomainText.Role(role)
        };
}

public sealed class MemberProfilePage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly RememberedLoginService _rememberedLogin;
    private readonly string _targetUserId;

    public MemberProfilePage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        RememberedLoginService rememberedLogin,
        string targetUserId)
        : base(session, "Hồ sơ thành viên")
    {
        _database = database;
        _media = media;
        _rememberedLogin = rememberedLogin;
        _targetUserId = targetUserId;
    }

    protected override async Task LoadAsync()
    {
        var includeInactive = RoleCapabilities.IsFounderLike(Session.CurrentUser?.Role);
        var member = (await _database.GetMembersAsync(
                CurrentUserId,
                includeInactive: includeInactive))
            .FirstOrDefault(item => item.Account.Id == _targetUserId)
            ?? throw new UnauthorizedAccessException(
                "Bạn không có quyền xem hồ sơ này.");
        Title = member.DisplayName;

        var details = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                UiKit.Body($"Username: @{member.Account.Username}"),
                UiKit.Body($"Email: {Value(member.Profile.Email)}")
            }
        };
        if (member.Account.Role != UserRole.Trainee)
        {
            details.Children.Insert(1, UiKit.Body($"Số điện thoại: {Value(member.Profile.Phone)}"));
            if (member.Account.Role == UserRole.Coach)
            {
                details.Children.Insert(2, UiKit.Body(
                    $"Vị trí dạy: {CoachPositionCatalog.Label(member.Profile.CoachPosition)}"));
            }
        }

        if (member.Account.Role == UserRole.Trainee)
        {
            details.Children.Add(UiKit.StatusBadge(
                member.Account.IsTuitionSupported
                    ? DomainText.SupportedTraineeTuitionLabel
                    : "Học phí theo lớp",
                member.Account.IsTuitionSupported ? UiKit.Success : UiKit.TextSecondary));
            details.Children.Add(
                UiKit.Body($"Ngày tháng năm sinh: {BirthDate(member.Profile.DateOfBirth)}"));
            details.Children.Add(
                UiKit.Body($"Chiều cao: {Dimension(member.Profile.HeightCm, "cm")}"));
            details.Children.Add(
                UiKit.Body($"Cân nặng: {Dimension(member.Profile.WeightKg, "kg")}"));
            if (RoleCapabilities.CanManageMembers(Session.CurrentUser?.Role)
                || CurrentUserId == member.Account.Id)
            {
                details.Children.Add(
                    UiKit.Body($"Người giám hộ: {Value(member.Profile.GuardianName)}"));
                details.Children.Add(
                    UiKit.Body($"SĐT người giám hộ: {Value(member.Profile.GuardianPhone)}"));
            }
        }

        var profileName = UiKit.LargeTitle(member.DisplayName);
        profileName.HorizontalOptions = LayoutOptions.Fill;
        profileName.HorizontalTextAlignment = TextAlignment.Center;

        var roleBadge = UiKit.StatusBadge(
            DomainText.Role(member.Account.Role),
            member.Account.Role == UserRole.Coach
                ? UiKit.Primary
                : UiKit.Success);
        roleBadge.HorizontalOptions = LayoutOptions.Center;

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.Avatar(member.Profile.PhotoPath, 104),
                profileName,
                roleBadge,
                UiKit.Card(details)
            }
        };

        if (RoleCapabilities.IsFounderLike(Session.CurrentUser?.Role)
            && member.Account.Role == UserRole.Trainee
            && Application.Current?.Handler?.MauiContext?.Services is { } cardServices
            && cardServices.GetService<IPlayerCardPngService>() is { } cardPng
            && cardServices.GetService<IImageSaveService>() is { } cardImageSave)
        {
            var createPng = UiKit.SecondaryButton("Tạo PNG");
            createPng.Margin = new Thickness(0, 6, 0, 0);
            createPng.Clicked += async (_, _) =>
                await CreatePlayerCardPngAsync(
                    member,
                    cardPng,
                    cardImageSave,
                    createPng);
            details.Children.Add(createPng);
        }

        if (member.Account.Role == UserRole.Trainee)
        {
            var evaluations = UiKit.SecondaryButton("Lịch sử đánh giá học viên");
            evaluations.Clicked += async (_, _) =>
                await Navigation.PushAsync(new TraineeEvaluationHistoryPage(
                    _database,
                    Session,
                    member.Account.Id,
                    member.DisplayName));
            root.Children.Add(evaluations);
        }

        if (RoleCapabilities.CanApproveOperations(Session.CurrentUser?.Role)
            && member.Account.Role is UserRole.Coach or UserRole.Trainee)
        {
            root.Children.Add(UiKit.StatusBadge(
                member.Account.IsActive ? "Account đang hoạt động" : "Account đã khóa",
                member.Account.IsActive ? UiKit.Success : UiKit.Danger));

            var attendance = await _database.GetMemberAttendanceSummaryAsync(
                CurrentUserId,
                member.Account.Id);
            root.Children.Add(UiKit.Title("Điểm danh"));
            if (attendance.Role == UserRole.Coach)
            {
                root.Children.Add(UiKit.MetricGrid(
                    (attendance.AttendedCount.ToString(), "Đã check-in", UiKit.Success),
                    (attendance.AbsentCount.ToString(), "Vắng check-in", UiKit.Danger),
                    (attendance.SubmittedSessionCount.ToString(), "Buổi đã hoàn tất", UiKit.Primary)));
                var reviewCheckIns = UiKit.SecondaryButton(
                    $"Chờ xác nhận check-out ({attendance.PendingCheckInCount})");
                reviewCheckIns.Clicked += async (_, _) =>
                    await Navigation.PushAsync(new CoachCheckInReviewPage(
                        _database,
                        Session,
                        member.Account.Id));
                root.Children.Add(reviewCheckIns);
                root.Children.Add(UiKit.Caption(
                    "Chỉ ca đã check-out đủ ảnh và được Founder xác nhận mới được tính vào số buổi dạy và tiền lương."));

                var allCoachHistory = (await _database.GetCoachCheckInHistoryAsync(CurrentUserId))
                    .Where(item => item.CheckIn.CoachUserId == member.Account.Id)
                    .ToList();
                root.Children.Add(BuildCoachHistorySection(allCoachHistory, member.Account.Id));
            }
            else
            {
                root.Children.Add(UiKit.MetricGrid(
                    (attendance.AttendedCount.ToString(), "Điểm danh", UiKit.Success),
                    (attendance.AbsentCount.ToString(), "Vắng mặt", UiKit.Danger),
                    (attendance.LateCount.ToString(), "Trong đó đi trễ", UiKit.Warning),
                    (attendance.ExcusedCount.ToString(), "Vắng có phép", UiKit.Primary)));
                root.Children.Add(UiKit.Caption(
                    "Điểm danh gồm trạng thái Có mặt và Đi trễ; chỉ tính buổi đã hoàn tất."));

                var allTraineeHistory = await _database.GetAttendanceHistoryAsync(
                        CurrentUserId,
                        member.Account.Id);
                root.Children.Add(BuildTraineeHistorySection(allTraineeHistory, member.Account.Id));
            }
        }

        if (RoleCapabilities.CanApproveOperations(Session.CurrentUser?.Role)
            && member.Account.Role == UserRole.Trainee
            && await CanShowFounderParentPaymentAsync(member)
            && Application.Current?.Handler?.MauiContext?.Services is { } services)
        {
            var qrCode = services.GetService<QrCodeService>();
            var pdfService = services.GetService<IReceiptPdfService>();
            var imageSave = services.GetService<IImageSaveService>();
            if (qrCode is not null && pdfService is not null && imageSave is not null)
            {
                var parentPayment = UiKit.PrimaryButton("Đóng học phí thay Phụ huynh");
                parentPayment.Clicked += async (_, _) =>
                    await Navigation.PushAsync(new FounderParentTuitionPage(
                        _database,
                        Session,
                        member.Account.Id,
                        member.DisplayName,
                        qrCode,
                        pdfService,
                        imageSave));
                // Keep this action above profile editing so payment handling is
                // clearly separated from personal-data changes.
                root.Children.Add(parentPayment);
            }
        }

        var canEditAsFounder =
            RoleCapabilities.IsFounderLike(Session.CurrentUser?.Role)
            && member.Account.Role != UserRole.Admin;
        var canEditSelf = CurrentUserId == member.Account.Id
                          && Session.CurrentUser?.Role != UserRole.Manager;
        if (canEditAsFounder || canEditSelf)
        {
            var edit = UiKit.PrimaryButton("Sửa hồ sơ");
            edit.Clicked += async (_, _) =>
            {
                if (canEditAsFounder)
                {
                    await Navigation.PushAsync(new MemberEditorPage(
                        _database,
                        Session,
                        _media,
                        member));
                }
                else
                {
                    await Navigation.PushAsync(new PersonalProfilePage(
                        _database,
                        Session,
                        _media,
                        _rememberedLogin,
                        startInEdit: true,
                        closeAfterEdit: true));
                }
            };
            root.Children.Add(edit);
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private async Task<bool> CanShowFounderParentPaymentAsync(MemberRow member)
    {
        if (member.Account.IsTuitionSupported)
        {
            return false;
        }

        var activeClasses = await _database.GetClassesAsync(CurrentUserId);
        var enrollments = new List<ClassEnrollment>();
        foreach (var classRow in activeClasses.Where(item => item.Class.IsActive))
        {
            enrollments.AddRange((await _database.GetClassEnrollmentsAsync(classRow.Class.Id))
                .Where(item => item.TraineeUserId == member.Account.Id && item.IsActive));
        }

        var officialEnrollments = enrollments
            .Where(item => !item.IsTrial)
            .ToList();
        if (officialEnrollments.Count == 0)
        {
            // Supported and trial-only Trainees must not see a parent-payment
            // action. A trial becomes eligible automatically after the server
            // converts it to an official enrollment and creates its invoice.
            return false;
        }

        var enrollmentIds = officialEnrollments
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var invoiceRows = (await _database.GetInvoicesAsync(CurrentUserId))
            .Where(item => item.Invoice.TraineeUserId == member.Account.Id
                           && enrollmentIds.Contains(item.Invoice.EnrollmentId))
            .ToList();

        // Show the action only for an unpaid/new cycle. A paid cycle hides it
        // until its full attendance is delivered and the next cycle exists.
        foreach (var enrollment in officialEnrollments)
        {
            var enrollmentInvoices = invoiceRows
                .Where(item => item.Invoice.EnrollmentId == enrollment.Id)
                .OrderBy(item => item.Invoice.CycleNumber)
                .ToList();
            var latest = enrollmentInvoices.LastOrDefault();
            if (latest is null)
            {
                return true;
            }

            // Cycle 2+ is valid only after every preceding paid cycle is
            // complete. This guards the Founder list against a stale/partial
            // invoice projection and prevents showing a new payment action
            // before the old cycle's planned sessions are delivered.
            if (latest.Invoice.CycleNumber > 1
                && enrollmentInvoices
                    .Where(item => item.Invoice.CycleNumber < latest.Invoice.CycleNumber)
                    .Any(item => item.Invoice.Status != InvoiceStatus.Paid
                                 || !item.Progress.IsComplete))
            {
                continue;
            }

            if (latest.Invoice.Status == InvoiceStatus.Paid)
            {
                // A paid cycle stays hidden while it is still being delivered
                // and after completion until maintenance has created the next
                // cycle. This prevents the parent-payment action from showing
                // twice for the same cycle and makes the next action advance
                // only after all planned sessions (including absences) are
                // recorded.
                if (!latest.Progress.IsComplete)
                {
                    continue;
                }

                // The next pending invoice, when present, is selected as
                // `latest` on the following iteration/read and will make the
                // action visible again. Do not expose a duplicate action while
                // the server is still deriving that invoice.
                continue;
            }

            if (latest.Invoice.Status == InvoiceStatus.ProofSubmitted)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private async Task CreatePlayerCardPngAsync(
        MemberRow member,
        IPlayerCardPngService cardPng,
        IImageSaveService imageSave,
        Button source)
    {
        if (!source.IsEnabled)
        {
            return;
        }

        source.IsEnabled = false;
        try
        {
            var club = await _database.GetClubAsync();
            var bytes = await cardPng.CreateAsync(new PlayerCardPngData(
                member.DisplayName,
                club.TeamName,
                member.Profile.PhotoPath,
                member.Profile.DateOfBirth,
                member.Profile.HeightCm,
                member.Profile.WeightKg));
            var safeName = string.Concat(member.DisplayName
                    .Where(character => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character)))
                .Trim()
                .Replace(' ', '-');
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "cau-thu-hoc-vien";
            }

            var location = await imageSave.SavePngAsync(
                bytes,
                $"AWAKEN-player-{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await DisplayAlertAsync(
                "Đã tạo PNG",
                $"Ảnh thông tin học viên 590 × 1004 px đã được lưu tại {location}.",
                "OK");
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "Chưa thể tạo PNG",
                exception.Message,
                "Đóng");
        }
        finally
        {
            source.IsEnabled = true;
        }
    }

    private static string Value(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Chưa cập nhật" : value;

    private static string Dimension(double value, string unit) =>
        value <= 0 ? "Chưa cập nhật" : $"{value:0.#} {unit}";

    private static string BirthDate(DateTime? value) =>
        value?.ToString("dd/MM/yyyy") ?? "Chưa cập nhật";

    private View BuildTraineeHistorySection(
        IReadOnlyList<AttendanceHistoryRow> allRows,
        string traineeUserId)
    {
        var years = new[] { "Tất cả" }
            .Concat(allRows
                .Select(item => item.SessionDate.Year)
                .Append(DateTime.Today.Year)
                .Distinct()
                .OrderByDescending(item => item)
                .Select(item => item.ToString(CultureInfo.InvariantCulture)))
            .ToList();
        var months = new[] { "Tất cả" }
            .Concat(Enumerable.Range(1, 12).Select(item => item.ToString("00", CultureInfo.InvariantCulture)))
            .ToList();
        var yearPicker = new Picker { Title = "Năm", ItemsSource = years, SelectedIndex = 0 };
        var monthPicker = new Picker { Title = "Tháng", ItemsSource = months, SelectedIndex = 0 };
        var historyHost = new VerticalStackLayout { Spacing = 7 };

        void Refresh()
        {
            var year = ParseFilterNumber(yearPicker.SelectedItem?.ToString());
            var month = ParseFilterNumber(monthPicker.SelectedItem?.ToString());
            var filtered = allRows
                .Where(item => (!year.HasValue || item.SessionDate.Year == year.Value)
                               && (!month.HasValue || item.SessionDate.Month == month.Value))
                .ToList();
            historyHost.Children.Clear();
            historyHost.Children.Add(BuildTraineeAttendanceHistory(filtered.Take(5).ToList()));
            if (filtered.Count > 5)
            {
                var viewAll = UiKit.SecondaryButton("Xem đầy đủ điểm danh");
                viewAll.Clicked += async (_, _) =>
                    await Navigation.PushAsync(new AttendanceHistoryPage(
                        _database,
                        Session,
                        traineeUserId,
                        year,
                        month));
                historyHost.Children.Add(viewAll);
            }
        }

        yearPicker.SelectedIndexChanged += (_, _) => Refresh();
        monthPicker.SelectedIndexChanged += (_, _) => Refresh();
        var filterGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8,
            Children = { yearPicker, monthPicker }
        };
        Grid.SetColumn(monthPicker, 1);
        var section = new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                UiKit.Caption("Lọc điểm danh theo năm và tháng"),
                filterGrid,
                historyHost
            }
        };
        Refresh();
        return section;
    }

    private View BuildCoachHistorySection(
        IReadOnlyList<CoachCheckInHistoryRow> allRows,
        string coachUserId)
    {
        var years = new[] { "Tất cả" }
            .Concat(allRows
                .Select(item => item.SessionDate.Year)
                .Append(DateTime.Today.Year)
                .Distinct()
                .OrderByDescending(item => item)
                .Select(item => item.ToString(CultureInfo.InvariantCulture)))
            .ToList();
        var months = new[] { "Tất cả" }
            .Concat(Enumerable.Range(1, 12).Select(item => item.ToString("00", CultureInfo.InvariantCulture)))
            .ToList();
        var classes = new[] { "Tất cả" }
            .Concat(allRows
                .Select(item => item.ClassName)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item))
            .ToList();
        var classPicker = new Picker { Title = "Lớp dạy", ItemsSource = classes, SelectedIndex = 0 };
        var yearPicker = new Picker { Title = "Năm", ItemsSource = years, SelectedIndex = 0 };
        var monthPicker = new Picker { Title = "Tháng", ItemsSource = months, SelectedIndex = 0 };
        var historyHost = new VerticalStackLayout { Spacing = 7 };

        void Refresh()
        {
            var year = ParseFilterNumber(yearPicker.SelectedItem?.ToString());
            var month = ParseFilterNumber(monthPicker.SelectedItem?.ToString());
            var className = classPicker.SelectedItem?.ToString();
            var filtered = allRows
                .Where(item => (className is null || className == "Tất cả" || item.ClassName == className)
                               && (!year.HasValue || item.SessionDate.Year == year.Value)
                               && (!month.HasValue || item.SessionDate.Month == month.Value))
                .ToList();
            historyHost.Children.Clear();
            historyHost.Children.Add(BuildCoachCheckInHistory(filtered.Take(5).ToList()));
            if (filtered.Count > 5)
            {
                var viewAll = UiKit.SecondaryButton("Xem đầy đủ check-in");
                viewAll.Clicked += async (_, _) =>
                    await Navigation.PushAsync(new CoachCheckInHistoryPage(
                        _database,
                        Session,
                        coachUserId,
                        className is "Tất cả" ? null : className,
                        year,
                        month));
                historyHost.Children.Add(viewAll);
            }
        }

        classPicker.SelectedIndexChanged += (_, _) => Refresh();
        yearPicker.SelectedIndexChanged += (_, _) => Refresh();
        monthPicker.SelectedIndexChanged += (_, _) => Refresh();
        var filterGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 6,
            Children = { classPicker, yearPicker, monthPicker }
        };
        Grid.SetColumn(yearPicker, 1);
        Grid.SetColumn(monthPicker, 2);
        var section = new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                UiKit.Caption("Lọc check-in theo lớp dạy, năm và tháng"),
                filterGrid,
                historyHost
            }
        };
        Refresh();
        return section;
    }

    private static int? ParseFilterNumber(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static View BuildTraineeAttendanceHistory(
        IReadOnlyList<AttendanceHistoryRow> rows)
    {
        var content = new VerticalStackLayout { Spacing = 5 };
        content.Children.Add(UiKit.Caption("Chi tiết điểm danh gần nhất"));
        if (rows.Count == 0)
        {
            content.Children.Add(UiKit.Caption("Chưa có buổi học đã hoàn tất."));
        }
        else
        {
            foreach (var row in rows)
            {
                var line = UiKit.Body(
                    $"{row.SessionDate:dd/MM} · {row.ClassName} · {DomainText.Attendance(row.Status)}",
                    UiKit.AttendanceColor(row.Status));
                line.FontSize = 12;
                line.LineBreakMode = LineBreakMode.TailTruncation;
                line.MaxLines = 1;
                content.Children.Add(line);
            }

            content.Children.Add(UiKit.Caption("Hiển thị tối đa 5 buổi gần nhất."));
        }

        return UiKit.Card(content, new Thickness(10));
    }

    private static View BuildCoachCheckInHistory(
        IReadOnlyList<CoachCheckInHistoryRow> rows)
    {
        var content = new VerticalStackLayout { Spacing = 5 };
        content.Children.Add(UiKit.Caption("Chi tiết check-in gần nhất"));
        if (rows.Count == 0)
        {
            content.Children.Add(UiKit.Caption("Chưa có check-in."));
        }
        else
        {
            foreach (var row in rows)
            {
                var statusColor = row.CheckIn.ApprovalStatus switch
                {
                    CoachCheckInApprovalStatus.Approved => UiKit.Success,
                    CoachCheckInApprovalStatus.Rejected => UiKit.Danger,
                    _ => UiKit.Warning
                };
                var line = UiKit.Body(
                    $"{row.SessionDate:dd/MM} · {row.ClassName} · {CoachCheckInTime.Range(row.CheckIn)} · {CoachCheckInTime.FormatDuration(CoachCheckInTime.ElapsedSeconds(row.CheckIn))} · {DomainText.CoachCheckInApproval(row.CheckIn.ApprovalStatus)}",
                    statusColor);
                line.FontSize = 12;
                line.LineBreakMode = LineBreakMode.TailTruncation;
                line.MaxLines = 1;
                content.Children.Add(line);
            }

            content.Children.Add(UiKit.Caption("Hiển thị tối đa 5 check-in gần nhất."));
        }

        return UiKit.Card(content, new Thickness(10));
    }
}

public sealed class MemberEditorPage : ContentPage
{
    private readonly AppDatabase _database;
    private readonly SessionService _session;
    private readonly MediaService _media;
    private readonly MemberRow? _existing;
    private readonly IReadOnlyList<UserRole> _roleOptions;
    private readonly Picker _role;
    private readonly Entry _username;
    private readonly Entry _fullName;
    private readonly Entry _email;
    private readonly Entry _phone;
    private readonly Picker _coachPosition;
    private readonly DatePicker _dateOfBirth;
    private readonly Entry _height;
    private readonly Entry _weight;
    private readonly Entry _guardianName;
    private readonly Entry _guardianPhone;
    private readonly Switch _tuitionSupported;
    private readonly Image _photo;
    private string _photoPath;

    public MemberEditorPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        MemberRow? existing = null,
        UserRole? initialRole = null)
    {
        _database = database;
        _session = session;
        _media = media;
        _existing = existing;
        _photoPath = existing?.Profile.PhotoPath ?? string.Empty;
        Title = existing is null ? "Tạo account" : "Sửa hồ sơ";
        BackgroundColor = UiKit.Background;

        var actorRole = _session.CurrentUser?.Role;
        _roleOptions = RoleCapabilities.IsFounderLike(actorRole)
            ? [UserRole.Coach, UserRole.Trainee, UserRole.CoFounder, UserRole.Manager]
            : [UserRole.Coach, UserRole.Trainee];
        _role = new Picker { Title = "Chọn vai trò" };
        var selectedRole = existing?.Account.Role ?? initialRole ?? UserRole.Coach;
        if (!_roleOptions.Contains(selectedRole))
        {
            selectedRole = UserRole.Coach;
        }
        foreach (var roleOption in _roleOptions)
        {
            _role.Items.Add(DomainText.Role(roleOption));
        }
        _role.SelectedIndex = _roleOptions.ToList().IndexOf(selectedRole);
        _role.IsEnabled = existing is null;

        _coachPosition = new Picker
        {
            Title = "Chọn vị trí dạy",
            ItemsSource = CoachPositionCatalog.Options.Select(option => option.Label).ToList(),
            SelectedIndex = PositionIndex(existing?.Profile.CoachPosition)
        };

        _username = new Entry
        {
            Placeholder = "Ví dụ: coach.anh",
            Text = existing?.Account.Username ?? string.Empty,
            IsEnabled = existing is null
        };
        var defaultPassword = new Label
        {
            Text = AppDatabase.NewAccountDefaultPassword,
            FontFamily = "OpenSansSemibold",
            FontSize = 15,
            TextColor = UiKit.TextPrimary,
            VerticalTextAlignment = TextAlignment.Center,
            MinimumHeightRequest = 46
        };
        var defaultPasswordField = UiKit.LabeledField(
            "MẬT KHẨU MẶC ĐỊNH",
            defaultPassword,
            "Account sẽ được yêu cầu đổi password.");
        defaultPasswordField.IsVisible = existing is null;
        _fullName = new Entry { Placeholder = "Họ và tên", Text = existing?.Profile.FullName ?? string.Empty };
        _email = new Entry
        {
            Placeholder = "email@example.com",
            Keyboard = Keyboard.Email,
            Text = existing?.Profile.Email ?? string.Empty
        };
        _phone = new Entry
        {
            Placeholder = "Số điện thoại",
            Keyboard = Keyboard.Telephone,
            Text = existing?.Profile.Phone ?? string.Empty
        };
        _dateOfBirth = new DatePicker
        {
            Date = existing?.Profile.DateOfBirth?.Date,
            Format = "dd/MM/yyyy",
            MinimumDate = new DateTime(1900, 1, 1),
            MaximumDate = DateTime.Today
        };
        _height = new Entry
        {
            Placeholder = "cm",
            Keyboard = Keyboard.Numeric,
            Text = existing?.Profile.HeightCm > 0
                ? existing.Profile.HeightCm.ToString("0.#", CultureInfo.InvariantCulture)
                : string.Empty
        };
        _weight = new Entry
        {
            Placeholder = "kg",
            Keyboard = Keyboard.Numeric,
            Text = existing?.Profile.WeightKg > 0
                ? existing.Profile.WeightKg.ToString("0.#", CultureInfo.InvariantCulture)
                : string.Empty
        };
        _guardianName = new Entry
        {
            Placeholder = "Họ tên phụ huynh/người giám hộ",
            Text = existing?.Profile.GuardianName ?? string.Empty
        };
        _guardianPhone = new Entry
        {
            Placeholder = "Số điện thoại phụ huynh",
            Keyboard = Keyboard.Telephone,
            Text = existing?.Profile.GuardianPhone ?? string.Empty
        };
        _tuitionSupported = new Switch
        {
            IsToggled = existing?.Account.IsTuitionSupported ?? false,
            OnColor = UiKit.Success
        };

        _photo = UiKit.Avatar(_photoPath, 96);
        var changePhoto = UiKit.SecondaryButton("Thay hình ảnh", async (_, _) => await SelectPhotoAsync());

        var traineeFields = new VerticalStackLayout
        {
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.LabeledField("NGÀY THÁNG NĂM SINH", _dateOfBirth),
                UiKit.LabeledField("CHIỀU CAO", _height),
                UiKit.LabeledField("CÂN NẶNG", _weight),
                UiKit.LabeledField("NGƯỜI GIÁM HỘ", _guardianName),
                UiKit.LabeledField("SĐT NGƯỜI GIÁM HỘ", _guardianPhone)
            }
        };
        traineeFields.IsVisible = SelectedRole() == UserRole.Trainee;
        var tuitionSupportField = BuildTuitionSupportField();
        tuitionSupportField.IsVisible = SelectedRole() == UserRole.Trainee;
        var phoneField = UiKit.LabeledField("SỐ ĐIỆN THOẠI", _phone);
        phoneField.IsVisible = SelectedRole() is UserRole.Coach or UserRole.CoFounder or UserRole.Manager;
        var coachPositionField = UiKit.LabeledField("VỊ TRÍ DẠY", _coachPosition);
        coachPositionField.IsVisible = SelectedRole() == UserRole.Coach;
        _role.SelectedIndexChanged += (_, _) =>
        {
            var role = SelectedRole();
            traineeFields.IsVisible = role == UserRole.Trainee;
            phoneField.IsVisible = role is UserRole.Coach or UserRole.CoFounder or UserRole.Manager;
            coachPositionField.IsVisible = role == UserRole.Coach;
            tuitionSupportField.IsVisible = role == UserRole.Trainee;
            if (role != UserRole.Trainee)
            {
                _tuitionSupported.IsToggled = false;
            }
        };

        var save = UiKit.PrimaryButton(existing is null ? "Tạo account" : "Lưu thay đổi");
        save.Clicked += async (_, _) => await SaveAsync(save);

        var form = new VerticalStackLayout
        {
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                _photo,
                changePhoto,
                UiKit.LabeledField("VAI TRÒ", _role),
                UiKit.LabeledField("USERNAME", _username),
                defaultPasswordField,
                UiKit.LabeledField("HỌ VÀ TÊN", _fullName),
                UiKit.LabeledField("EMAIL", _email),
                phoneField,
                coachPositionField,
                traineeFields,
                tuitionSupportField,
                save
            }
        };

        if (existing is not null)
        {
            var reset = UiKit.SecondaryButton("Đặt lại mật khẩu", async (_, _) => await ResetPasswordAsync());
            var deactivate = existing.Account.IsActive
                ? UiKit.DestructiveButton("Khóa account", async (_, _) => await ToggleActiveAsync())
                : UiKit.SecondaryButton("Kích hoạt lại account", async (_, _) => await ToggleActiveAsync());
            form.Children.Add(reset);
            form.Children.Add(deactivate);
        }

        Content = existing is null
            ? UiKit.ScrollBody(UiKit.Card(form))
            : UiKit.ScrollBody(
                UiKit.LargeTitle($"Sửa {existing.DisplayName}"),
                UiKit.Card(form));
    }

    private async Task SelectPhotoAsync()
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
                return;
            }

            _photoPath = path;
            _photo.Source = ImageSource.FromFile(path);
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Không thể lấy hình ảnh", exception.Message, "Đóng");
        }
    }

    private async Task SaveAsync(Button source)
    {
        source.IsEnabled = false;
        try
        {
            var role = SelectedRole();
            var coachPosition = role == UserRole.Coach ? SelectedCoachPositionKey() : string.Empty;
            if (_existing is null)
            {
                var account = await _database.CreateUserAsync(
                    CurrentFounderId,
                    role,
                    _username.Text ?? string.Empty,
                    _fullName.Text ?? string.Empty,
                    _email.Text ?? string.Empty,
                    role == UserRole.Trainee ? string.Empty : _phone.Text ?? string.Empty,
                    isTuitionSupported: role == UserRole.Trainee && _tuitionSupported.IsToggled,
                    coachPosition: coachPosition,
                    guardianName: role == UserRole.Trainee ? _guardianName.Text ?? string.Empty : string.Empty,
                    guardianPhone: role == UserRole.Trainee ? _guardianPhone.Text ?? string.Empty : string.Empty);
                // The online create endpoint persists the complete profile in
                // one transaction.  A Manager is intentionally not allowed to
                // issue a follow-up profile mutation.
                if (_session.CurrentUser?.Role != UserRole.Manager)
                {
                    var profile = await _database.GetProfileAsync(account.Id);
                    ApplyProfileFields(profile);
                    await _database.SaveProfileAsync(CurrentFounderId, profile);
                }
            }
            else
            {
                ApplyProfileFields(_existing.Profile);
                await _database.SaveProfileAsync(CurrentFounderId, _existing.Profile);
                if (_existing.Account.Role == UserRole.Trainee
                    && RoleCapabilities.IsFounderLike(_session.CurrentUser?.Role))
                {
                    await _database.SetTuitionSupportAsync(
                        CurrentFounderId,
                        _existing.Account.Id,
                        _tuitionSupported.IsToggled);
                }
            }

            await DisplayAlertAsync("Đã lưu", "Thông tin thành viên đã được cập nhật.", "OK");
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

    private void ApplyProfileFields(PersonProfile profile)
    {
        var role = SelectedRole();
        profile.FullName = _fullName.Text ?? string.Empty;
        profile.Email = _email.Text ?? string.Empty;
        profile.Phone = role == UserRole.Trainee
            ? string.Empty
            : _phone.Text ?? string.Empty;
        profile.CoachPosition = role == UserRole.Coach
            ? SelectedCoachPositionKey()
            : string.Empty;
        profile.PhotoPath = _photoPath;
        if (role == UserRole.Trainee)
        {
            profile.DateOfBirth = _dateOfBirth.Date?.Date;
            profile.HeightCm = ParseDouble(_height.Text);
            profile.WeightKg = ParseDouble(_weight.Text);
            profile.GuardianName = _guardianName.Text ?? string.Empty;
            profile.GuardianPhone = _guardianPhone.Text ?? string.Empty;
        }
    }

    private UserRole SelectedRole() =>
        _role.SelectedIndex >= 0 && _role.SelectedIndex < _roleOptions.Count
            ? _roleOptions[_role.SelectedIndex]
            : UserRole.Coach;

    private View BuildTuitionSupportField()
    {
        var title = UiKit.Caption(
            DomainText.SupportedTraineeLabel.ToUpperInvariant(),
            UiKit.TextSecondary);
        title.VerticalTextAlignment = TextAlignment.Center;
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        row.Children.Add(title);
        Grid.SetColumn(_tuitionSupported, 1);
        row.Children.Add(_tuitionSupported);

        var field = new VerticalStackLayout { Spacing = 4 };
        field.Children.Add(row);
        field.Children.Add(UiKit.Caption(
            "Khi bật, học viên được miễn học phí và không cần thanh toán hoặc upload bill."));
        return field;
    }

    private async Task ResetPasswordAsync()
    {
        if (_existing is null)
        {
            return;
        }

        await Navigation.PushAsync(new FounderPasswordResetPage(
            _database,
            CurrentFounderId,
            _existing.Account.Id,
            _existing.DisplayName));
    }

    private async Task ToggleActiveAsync()
    {
        if (_existing is null)
        {
            return;
        }

        var activate = !_existing.Account.IsActive;
        var confirmed = await DisplayAlertAsync(
            activate ? "Kích hoạt lại account?" : "Khóa account?",
            activate
                ? $"{_existing.DisplayName} sẽ có thể đăng nhập trở lại."
                : $"{_existing.DisplayName} sẽ không thể đăng nhập. Dữ liệu lịch sử vẫn được giữ.",
            activate ? "Kích hoạt" : "Khóa",
            "Hủy");
        if (!confirmed)
        {
            return;
        }

        await _database.SetUserActiveAsync(CurrentFounderId, _existing.Account.Id, activate);
        await Navigation.PopAsync();
    }

    private string CurrentFounderId =>
        _session.CurrentUser?.Id
        ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc.");

    private static double ParseDouble(string? value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var current))
        {
            return current;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant)
            ? invariant
            : 0;
    }

    private string SelectedCoachPositionKey() =>
        _coachPosition.SelectedIndex >= 0
            && _coachPosition.SelectedIndex < CoachPositionCatalog.Options.Count
            ? CoachPositionCatalog.Options[_coachPosition.SelectedIndex].Key
            : string.Empty;

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

internal sealed class FounderPasswordResetPage : ContentPage
{
    public FounderPasswordResetPage(
        AppDatabase database,
        string founderUserId,
        string targetUserId,
        string displayName)
    {
        Title = "Đặt lại mật khẩu";
        BackgroundColor = UiKit.Background;

        var password = new Entry
        {
            Placeholder = "Mật khẩu tạm mới",
            ReturnType = ReturnType.Next
        };
        var confirm = new Entry
        {
            Placeholder = "Nhập lại mật khẩu tạm",
            ReturnType = ReturnType.Go
        };
        var passwordField = UiKit.PasswordField(password);
        var confirmField = UiKit.PasswordField(confirm);
        var save = UiKit.PrimaryButton("Lưu mật khẩu tạm");

        async Task SaveAsync()
        {
            if (password.Text != confirm.Text)
            {
                await DisplayAlertAsync(
                    "Chưa thể đặt lại",
                    "Hai mật khẩu không trùng nhau.",
                    "Đóng");
                return;
            }

            save.IsEnabled = false;
            try
            {
                await database.ResetPasswordByFounderAsync(
                    founderUserId,
                    targetUserId,
                    password.Text ?? string.Empty);
                await DisplayAlertAsync(
                    "Đã đặt lại",
                    "Account sẽ được yêu cầu đổi mật khẩu ở lần đăng nhập tiếp theo.",
                    "OK");
                await Navigation.PopAsync();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync(
                    "Chưa thể đặt lại",
                    exception.Message,
                    "Đóng");
            }
            finally
            {
                save.IsEnabled = true;
            }
        }

        save.Clicked += async (_, _) => await SaveAsync();
        confirm.Completed += async (_, _) =>
        {
            if (save.IsEnabled)
            {
                await SaveAsync();
            }
        };

        Content = UiKit.ScrollBody(
            UiKit.LargeTitle("Đặt lại mật khẩu"),
            UiKit.Body(
                $"Tạo mật khẩu tạm mới cho {displayName}.",
                UiKit.TextSecondary),
            UiKit.Card(new VerticalStackLayout
            {
                Spacing = UiKit.SectionSpacing,
                Children =
                {
                    UiKit.LabeledField("MẬT KHẨU TẠM MỚI", passwordField),
                    UiKit.LabeledField("XÁC NHẬN", confirmField),
                    save
                }
            }));
    }
}
