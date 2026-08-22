using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

/// <summary>
/// Manager landing page. It deliberately exposes only the operations granted
/// to Manager: creating operational members and opening the approval/finance flows.
/// The server remains the authority; these cards are only the navigation
/// surface for the same guarded AppDatabase methods.
/// </summary>
public sealed class ManagerDashboardPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly IImageSaveService _imageSave;
    private readonly RememberedLoginService _rememberedLogin;

    public ManagerDashboardPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        IImageSaveService imageSave,
        RememberedLoginService rememberedLogin)
        : base(session, string.Empty)
    {
        _database = database;
        _media = media;
        _imageSave = imageSave;
        _rememberedLogin = rememberedLogin;
    }

    protected override async Task LoadAsync()
    {
        var classes = await _database.GetClassesAsync(CurrentUserId);
        var members = await _database.GetMembersAsync(CurrentUserId, includeInactive: false);
        var pendingCheckouts = await _database.GetPendingCoachCheckInsAsync(CurrentUserId);
        var invoices = await _database.GetInvoicesAsync(CurrentUserId);
        var salaries = await _database.GetSalariesAsync(CurrentUserId, DateTime.Today.ToString("yyyy-MM"));

        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.Headline("Quản lý vận hành"),
                UiKit.Caption("Tạo thành viên và xử lý các nghiệp vụ được phân công."),
                UiKit.MetricGrid(
                    (classes.Count(item => item.Class.IsActive).ToString(), "Lớp đang hoạt động", UiKit.Primary),
                    (members.Count(item => item.Account.Role is UserRole.Coach or UserRole.Trainee).ToString(), "Thành viên", UiKit.Success),
                    (pendingCheckouts.Count.ToString(), "Chờ duyệt check-out", UiKit.Warning),
                    (invoices.Count(item => item.Invoice.Status == InvoiceStatus.ProofSubmitted).ToString(), "Bill chờ duyệt", UiKit.Danger))
            }
        };

        root.Children.Add(ActionCard(
            "Thêm Huấn Luyện Viên / Cầu Thủ Học Viên",
            "Tạo account mới trong đội.",
            UiKit.Primary,
            async () => await PushPageAsync(new MemberEditorPage(_database, Session, _media))));
        root.Children.Add(ActionCard(
            "Duyệt check-in / check-out Coach",
            pendingCheckouts.Count == 0
                ? "Không có ca đang chờ xác nhận."
                : $"Có {pendingCheckouts.Count} ca cần kiểm tra ảnh.",
            UiKit.Warning,
            async () => await PushPageAsync(new CoachCheckInReviewPage(_database, Session))));
        root.Children.Add(ActionCard(
            "Học phí Cầu Thủ Học Viên",
            $"{invoices.Count(item => item.Invoice.Status == InvoiceStatus.ProofSubmitted)} bill chờ duyệt · {invoices.Count(item => item.Invoice.Status != InvoiceStatus.Paid)} khoản chưa hoàn tất.",
            UiKit.Danger,
            async () => await PushPageAsync(new FounderTuitionManagementPage(_database, Session, _imageSave))));
        root.Children.Add(ActionCard(
            "Lương Huấn Luyện Viên",
            $"{salaries.Count(item => item.Salary.Status == SalaryStatus.Pending)} kỳ chưa thanh toán trong tháng này.",
            UiKit.Success,
            async () => await PushPageAsync(new FounderSalaryManagementPage(
                _database,
                Session,
                DateTime.Today.ToString("yyyy-MM")))));

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private static View ActionCard(
        string title,
        string description,
        Color accent,
        Func<Task> open)
    {
        var arrow = UiKit.Body("›", UiKit.TextSecondary);
        arrow.FontSize = 24;
        arrow.VerticalTextAlignment = TextAlignment.Center;
        var header = new Grid
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
                        UiKit.Headline(title),
                        UiKit.Caption(description)
                    }
                },
                arrow
            }
        };
        Grid.SetColumn(arrow, 1);
        var card = UiKit.Card(new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                header,
                UiKit.StatusBadge("Mở nghiệp vụ", accent)
            }
        });
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await open();
        card.GestureRecognizers.Add(tap);
        return card;
    }
}

/// <summary>
/// Compact operations directory kept separate from the Manager dashboard so
/// the tab bar remains predictable while every destination still has its own
/// page/back stack.
/// </summary>
public sealed class ManagerOperationsPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly IImageSaveService _imageSave;
    private readonly RememberedLoginService _rememberedLogin;

    public ManagerOperationsPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        IImageSaveService imageSave,
        RememberedLoginService rememberedLogin)
        : base(session, string.Empty)
    {
        _database = database;
        _media = media;
        _imageSave = imageSave;
        _rememberedLogin = rememberedLogin;
    }

    protected override async Task LoadAsync()
    {
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.Headline("Nghiệp vụ được giao"),
                UiKit.Caption("Manager chỉ thao tác trong các mục được Founder phân quyền.")
            }
        };
        root.Children.Add(UiKit.PrimaryButton(
            "Duyệt check-in / check-out Coach",
            async (_, _) => await PushPageAsync(new CoachCheckInReviewPage(_database, Session))));
        root.Children.Add(UiKit.PrimaryButton(
            "Duyệt bill học phí",
            async (_, _) => await PushPageAsync(new FounderTuitionManagementPage(_database, Session, _imageSave))));
        root.Children.Add(UiKit.PrimaryButton(
            "Duyệt lương Coach",
            async (_, _) => await PushPageAsync(new FounderSalaryManagementPage(
                _database,
                Session,
                DateTime.Today.ToString("yyyy-MM")))));
        root.Children.Add(UiKit.SecondaryButton(
            "Đóng học phí thay Phụ huynh",
            async (_, _) => await PushPageAsync(new MemberManagementPage(
                _database,
                Session,
                _media,
                _rememberedLogin))));
        Content = UiKit.KeyboardAwareScroll(root);
    }
}

/// <summary>
/// Manager finance surface. It deliberately omits Founder-only support
/// settings and structural controls; only tuition approvals and Coach salary
/// approvals are reachable from this tab.
/// </summary>
public sealed class ManagerFinancePage : AsyncContentPage, IResettableTabPage
{
    private readonly AppDatabase _database;
    private readonly IImageSaveService _imageSave;
    private string _period = DateTime.Today.ToString("yyyy-MM");

    public ManagerFinancePage(
        AppDatabase database,
        SessionService session,
        IImageSaveService imageSave)
        : base(session, string.Empty)
    {
        _database = database;
        _imageSave = imageSave;
    }

    public void ResetTabState() => _period = DateTime.Today.ToString("yyyy-MM");

    protected override async Task LoadAsync()
    {
        var invoices = await _database.GetInvoicesAsync(CurrentUserId);
        var salaries = await _database.GetSalariesAsync(CurrentUserId, _period);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.Caption("Chỉ hiển thị các nghiệp vụ tài chính Manager được phân công.")
            }
        };

        root.Children.Add(CreateFinanceAction(
            "Học phí Cầu Thủ Học Viên",
            $"{invoices.Count(item => item.Invoice.Status == InvoiceStatus.ProofSubmitted)} bill chờ duyệt · {invoices.Count(item => item.Invoice.Status != InvoiceStatus.Paid)} khoản chưa hoàn tất",
            UiKit.Primary,
            async () => await PushPageAsync(new FounderTuitionManagementPage(
                _database,
                Session,
                _imageSave))));
        root.Children.Add(CreateFinanceAction(
            "Lương Huấn Luyện Viên",
            $"{salaries.Count(item => item.Salary.Status == SalaryStatus.Pending)} kỳ chưa thanh toán · {UiKit.Money(salaries.Sum(item => item.Salary.AmountVnd))}",
            UiKit.Warning,
            async () => await PushPageAsync(new FounderSalaryManagementPage(
                _database,
                Session,
                _period))));

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private static View CreateFinanceAction(
        string title,
        string summary,
        Color accent,
        Func<Task> open)
    {
        var arrow = UiKit.Body("›", UiKit.TextSecondary);
        arrow.FontSize = 24;
        arrow.VerticalTextAlignment = TextAlignment.Center;
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 3,
                    Children =
                    {
                        UiKit.Headline(title),
                        UiKit.Caption(summary)
                    }
                },
                arrow
            }
        };
        Grid.SetColumn(arrow, 1);
        var card = UiKit.Card(new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                grid,
                UiKit.StatusBadge("Mở nghiệp vụ", accent)
            }
        });
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await open();
        card.GestureRecognizers.Add(tap);
        return card;
    }
}
