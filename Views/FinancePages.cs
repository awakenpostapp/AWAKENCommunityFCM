using System.Globalization;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;

namespace CommunityFootballClubManager.Views;

public sealed class FounderFinancePage : AsyncContentPage, IResettableTabPage
{
    private readonly AppDatabase _database;
    private readonly IImageSaveService _imageSave;
    private string _period = DateTime.Today.ToString("yyyy-MM");

    public FounderFinancePage(
        AppDatabase database,
        SessionService session,
        IImageSaveService imageSave)
        : base(session, "Tài chính")
    {
        _database = database;
        _imageSave = imageSave;
    }

    public void ResetTabState()
    {
        _period = DateTime.Today.ToString("yyyy-MM");
    }

    protected override async Task LoadAsync()
    {
        await _database.EnsureRecurringDataAsync(DateTime.Today);
        // Tuition is cycle-based now; the year/month picker continues to
        // filter Coach salaries only.
        var invoices = await _database.GetInvoicesAsync(CurrentUserId);
        var salaries = await _database.GetSalariesAsync(CurrentUserId, _period);
        var periodPickers = CreatePeriodPickers();
        var pendingTuition = invoices.Count(item => item.Invoice.Status != InvoiceStatus.Paid);
        var submittedProofs = invoices.Count(item =>
            item.Invoice.Status == InvoiceStatus.ProofSubmitted);
        var pendingSalaries = salaries.Count(item =>
            item.Salary.Status == SalaryStatus.Pending);
        var periodRow = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };
        periodRow.Add(UiKit.LabeledField("NĂM", periodPickers.Year), 0);
        periodRow.Add(UiKit.LabeledField("THÁNG", periodPickers.Month), 1);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                periodRow,
                UiKit.Caption("Chọn từng mục để xem danh sách và xử lý chi tiết.")
            }
        };
        root.Children.Add(CreateCategoryCard(
            "Học phí Cầu Thủ Học Viên",
            $"{invoices.Count} khoản · {pendingTuition} chưa đóng · {submittedProofs} bill chờ xác nhận",
            $"Tổng phải thu: {UiKit.Money(invoices.Sum(item => item.Invoice.AmountVnd))}",
            UiKit.Primary,
            async () => await PushPageAsync(new FounderTuitionManagementPage(
                _database,
                Session,
                _imageSave))));
        var supportedTrainees = (await _database.GetMembersAsync(
                CurrentUserId,
                UserRole.Trainee))
            .Where(item => item.Account.IsTuitionSupported)
            .ToList();
        root.Children.Add(CreateCategoryCard(
            "C\u1ea7u th\u1ee7 h\u1ecdc vi\u00ean \u0111\u01b0\u1ee3c h\u1ed7 tr\u1ee3",
            $"{supportedTrainees.Count} Cầu thủ học viên \u00b7 mi\u1ec5n h\u1ecdc ph\u00ed",
            "B\u1ea5m \u0111\u1ec3 xem danh s\u00e1ch",
            UiKit.Success,
            async () => await PushPageAsync(new FounderSupportedTraineesPage(
                _database,
                Session))));
        root.Children.Add(CreateCategoryCard(
            "Lương Huấn Luyện Viên",
            $"{salaries.Count} kỳ lương · {pendingSalaries} chưa thanh toán",
            $"Tổng lương đã tính: {UiKit.Money(salaries.Sum(item => item.Salary.AmountVnd))}",
            UiKit.Warning,
            async () => await PushPageAsync(new FounderSalaryManagementPage(
                _database,
                Session,
                _period))));

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private (Picker Year, Picker Month) CreatePeriodPickers()
    {
        var selectedDate = DateTime.TryParseExact(
            _period,
            "yyyy-MM",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : DateTime.Today;
        var firstYear = 2026;
        var lastYear = Math.Max(firstYear, DateTime.Today.Year);
        var years = Enumerable
            .Range(firstYear, lastYear - firstYear + 1)
            .Reverse()
            .ToList();
        if (selectedDate.Year < firstYear)
        {
            selectedDate = new DateTime(firstYear, selectedDate.Month, 1);
        }

        var yearPicker = new Picker { Title = "Chọn năm" };
        foreach (var year in years)
        {
            yearPicker.Items.Add(year.ToString(CultureInfo.InvariantCulture));
        }

        var monthPicker = new Picker { Title = "Chọn tháng" };
        for (var month = 1; month <= 12; month++)
        {
            monthPicker.Items.Add($"Tháng {month:00}");
        }

        yearPicker.SelectedIndex = years.IndexOf(selectedDate.Year);
        monthPicker.SelectedIndex = selectedDate.Month - 1;

        async void SelectionChanged(object? sender, EventArgs eventArgs)
        {
            if (yearPicker.SelectedIndex < 0 || monthPicker.SelectedIndex < 0)
            {
                return;
            }

            var selectedYear = years[yearPicker.SelectedIndex];
            var selectedMonth = monthPicker.SelectedIndex + 1;
            var newPeriod = $"{selectedYear:0000}-{selectedMonth:00}";
            if (newPeriod == _period)
            {
                return;
            }

            _period = newPeriod;
            yearPicker.IsEnabled = false;
            monthPicker.IsEnabled = false;
            try
            {
                await ReloadAsync();
            }
            finally
            {
                yearPicker.IsEnabled = true;
                monthPicker.IsEnabled = true;
            }
        }

        yearPicker.SelectedIndexChanged += SelectionChanged;
        monthPicker.SelectedIndexChanged += SelectionChanged;
        return (yearPicker, monthPicker);
    }

    private static View CreateCategoryCard(
        string title,
        string summary,
        string total,
        Color accent,
        Func<Task> open)
    {
        var header = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Children.Add(UiKit.Title(title));
        var arrow = UiKit.Body("›", UiKit.TextSecondary);
        arrow.FontSize = 22;
        Grid.SetColumn(arrow, 1);
        header.Children.Add(arrow);

        var card = UiKit.Card(new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                header,
                UiKit.Body(summary, UiKit.TextSecondary),
                UiKit.StatusBadge(total, accent)
            }
        });
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await open();
        card.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(card, $"Mở {title}. {summary}");
        SemanticProperties.SetHint(card, "Nhấn hai lần để xem chi tiết");
        return card;
    }
}

public sealed class FounderTuitionManagementPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly IImageSaveService _imageSave;

    public FounderTuitionManagementPage(
        AppDatabase database,
        SessionService session,
        IImageSaveService imageSave)
        : base(session, "Học phí Cầu Thủ Học Viên")
    {
        _database = database;
        _imageSave = imageSave;
    }

    protected override async Task LoadAsync()
    {
        var invoices = await _database.GetInvoicesAsync(CurrentUserId);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
            }
        };

        var completed = invoices
            .Where(item => item.Invoice.Status == InvoiceStatus.Paid && item.Progress.IsComplete)
            .ToList();
        var active = invoices
            .Where(item => !(item.Invoice.Status == InvoiceStatus.Paid && item.Progress.IsComplete))
            .ToList();
        var paidRows = active.Where(item => item.Invoice.Status == InvoiceStatus.Paid).ToList();
        var unpaidRows = active.Where(item => item.Invoice.Status is InvoiceStatus.Pending
                                               or InvoiceStatus.Rejected
                                               or InvoiceStatus.Overdue).ToList();
        var proofRows = active.Where(item => item.Invoice.Status == InvoiceStatus.ProofSubmitted).ToList();

        root.Children.Add(CreateTuitionCategoryCard(
            "Bill chờ xác nhận",
            proofRows.Count,
            UiKit.Warning,
            async () => await Navigation.PushAsync(new FounderInvoiceListPage(
                _database,
                Session,
                _imageSave,
                FounderInvoiceFilter.ProofSubmitted))));
        root.Children.Add(CreateTuitionCategoryCard(
            "Học viên chưa đóng",
            unpaidRows.Count,
            UiKit.Danger,
            async () => await Navigation.PushAsync(new FounderInvoiceListPage(
                _database,
                Session,
                _imageSave,
                FounderInvoiceFilter.Unpaid))));
        root.Children.Add(CreateTuitionCategoryCard(
            "Học viên đã đóng",
            paidRows.Count,
            UiKit.Success,
            async () => await Navigation.PushAsync(new FounderInvoiceListPage(
                _database,
                Session,
                _imageSave,
                FounderInvoiceFilter.Paid))));

        var totalPaid = invoices
            .Where(item => item.Invoice.Status == InvoiceStatus.Paid)
            .Sum(item => item.Invoice.AmountVnd);
        root.Children.Add(UiKit.Card(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                UiKit.Caption("TỔNG TIỀN ĐÃ ĐÓNG"),
                UiKit.Title(UiKit.Money(totalPaid)),
                UiKit.Caption("Tính trên tất cả các bill đã được Founder xác nhận.")
            }
        }));

        if (invoices.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có học phí",
                "Học phí sẽ được tạo ngay khi học viên được phân công vào lớp học."));
        }
        else if (completed.Count > 0)
        {
            root.Children.Add(UiKit.Caption(
                $"{completed.Count} bill đã hoàn tất đủ số buổi và được tự động ẩn khỏi danh sách đang theo dõi."));
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private static View CreateTuitionCategoryCard(
        string title,
        int count,
        Color color,
        Func<Task> open)
    {
        var arrow = new Label
        {
            Text = "›",
            FontSize = 24,
            TextColor = UiKit.TextSecondary,
            VerticalTextAlignment = TextAlignment.Center
        };
        var header = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        UiKit.Headline(title),
                        UiKit.Caption("Bấm để xem chi tiết")
                    }
                },
                arrow
            }
        };
        Grid.SetColumn(arrow, 1);
        var card = UiKit.Card(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                header,
                UiKit.StatusBadge($"{count} học viên", color)
            }
        });
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await open();
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private View CreateInvoiceCard(InvoiceRow row)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                UiKit.Headline(row.TraineeName),
                UiKit.Caption($"{row.ClassName} · {DomainText.TuitionCycle(row.Invoice)}"),
                UiKit.Body(UiKit.Money(row.Invoice.AmountVnd)),
                UiKit.StatusBadge(
                    DomainText.Invoice(row.Invoice.Status),
                    UiKit.InvoiceColor(row.Invoice.Status))
            }
        };

        if (row.Progress.PlannedSessions > 0)
        {
            stack.Children.Insert(
                3,
                UiKit.Caption(
                    $"Tiến độ chu kỳ: {row.Progress.AttendedSessions}/{row.Progress.PlannedSessions} buổi · {UiKit.Money(row.Invoice.CycleFeeVnd > 0 ? row.Invoice.CycleFeeVnd : row.Invoice.AmountVnd)} / chu kỳ",
                    UiKit.TextSecondary));
        }

        if (row.Progress.NeedsPaymentWarning)
        {
            stack.Children.Add(UiKit.StatusBadge(
                "Cảnh báo: đã học đủ 2 buổi nhưng chưa đóng học phí",
                UiKit.Danger));
        }

        if (row.LatestProof is not null && File.Exists(row.LatestProof.ImagePath))
        {
            var proof = new Image
            {
                Source = ImageSource.FromFile(row.LatestProof.ImagePath),
                HeightRequest = 150,
                Aspect = Aspect.AspectFit,
                BackgroundColor = UiKit.SurfaceSecondary
            };
            var openProof = new TapGestureRecognizer();
            openProof.Tapped += async (_, _) =>
                await Navigation.PushAsync(new ImagePreviewPage(
                    "Bill thanh toán",
                    row.LatestProof.ImagePath,
                    $"Bill-{row.Invoice.Period}-{row.LatestProof.Id}",
                    _imageSave));
            proof.GestureRecognizers.Add(openProof);
            SemanticProperties.SetDescription(
                proof,
                $"Xem lớn bill thanh toán của {row.TraineeName}");
            SemanticProperties.SetHint(
                proof,
                "Nhấn hai lần để xem lớn và lưu hình ảnh");
            stack.Children.Add(proof);
            stack.Children.Add(UiKit.Caption(
                $"Bill tải lúc {row.LatestProof.SubmittedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm} · Chạm ảnh để xem lớn hoặc lưu."));
        }

        if (row.Invoice.Status == InvoiceStatus.ProofSubmitted)
        {
            var confirm = UiKit.PrimaryButton("Xác nhận đã đóng");
            confirm.Clicked += async (_, _) =>
            {
                var accepted = await DisplayAlertAsync(
                    "Xác nhận học phí?",
                    $"{row.TraineeName} · {UiKit.Money(row.Invoice.AmountVnd)}",
                    "Xác nhận",
                    "Hủy");
                if (!accepted)
                {
                    return;
                }

                await RunActionAsync(
                    () => _database.ConfirmTuitionAsync(CurrentUserId, row.Invoice.Id),
                    confirm,
                    "Đã xác nhận học phí và gửi thông báo cho học viên.");
            };
            var reject = UiKit.SecondaryButton("Yêu cầu tải lại bill");
            reject.Clicked += async (_, _) =>
            {
                var reason = await DisplayPromptAsync(
                    "Yêu cầu tải lại bill",
                    "Nhập lý do hoặc thông tin cần bổ sung.",
                    "Gửi",
                    "Hủy");
                if (reason is null)
                {
                    return;
                }

                await RunActionAsync(
                    () => _database.RejectTuitionProofAsync(
                        CurrentUserId,
                        row.Invoice.Id,
                        reason),
                    reject,
                    "Đã gửi yêu cầu cho học viên.");
            };
            stack.Children.Add(confirm);
            stack.Children.Add(reject);
        }

        if (row.Receipt is not null)
        {
            stack.Children.Add(UiKit.Caption($"Hóa đơn: {row.Receipt.ReceiptNumber}"));
        }
        return UiKit.Card(stack);
    }
}

public enum FounderInvoiceFilter
{
    Paid,
    Unpaid,
    ProofSubmitted
}

/// <summary>
/// Detail list opened from one of the compact Founder tuition summary cards.
/// Keeping the list on a separate page prevents the finance home screen from
/// becoming a long invoice feed while retaining bill review actions.
/// </summary>
public sealed class FounderInvoiceListPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly IImageSaveService _imageSave;
    private readonly FounderInvoiceFilter _filter;

    public FounderInvoiceListPage(
        AppDatabase database,
        SessionService session,
        IImageSaveService imageSave,
        FounderInvoiceFilter filter)
        : base(session, "Học phí")
    {
        _database = database;
        _imageSave = imageSave;
        _filter = filter;
    }

    protected override async Task LoadAsync()
    {
        var invoices = await _database.GetInvoicesAsync(CurrentUserId);
        var rows = invoices.Where(item => _filter switch
        {
            FounderInvoiceFilter.Paid => item.Invoice.Status == InvoiceStatus.Paid
                                         && !item.Progress.IsComplete,
            FounderInvoiceFilter.Unpaid => item.Invoice.Status is InvoiceStatus.Pending
                                           or InvoiceStatus.Rejected
                                           or InvoiceStatus.Overdue,
            _ => item.Invoice.Status == InvoiceStatus.ProofSubmitted
        }).ToList();
        // Only the selected category renders payment proofs. Avoid downloading
        // every bill image before the user opens the corresponding list.
        await _database.EnsurePaymentProofImagesAsync(CurrentUserId, rows);
        var title = _filter switch
        {
            FounderInvoiceFilter.Paid => "Học viên đã đóng",
            FounderInvoiceFilter.Unpaid => "Học viên chưa đóng",
            _ => "Bill chờ xác nhận"
        };
        var color = _filter switch
        {
            FounderInvoiceFilter.Paid => UiKit.Success,
            FounderInvoiceFilter.Unpaid => UiKit.Danger,
            _ => UiKit.Warning
        };
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.LargeTitle(title),
                UiKit.StatusBadge($"{rows.Count} học viên", color)
            }
        };
        if (rows.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Không có học viên",
                "Danh sách sẽ cập nhật tự động theo trạng thái học phí và tiến độ chu kỳ."));
        }
        else
        {
            foreach (var row in rows)
            {
                root.Children.Add(CreateInvoiceCard(row, color));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private View CreateInvoiceCard(InvoiceRow row, Color color)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                UiKit.Headline(row.TraineeName),
                UiKit.Caption($"{row.ClassName} · {DomainText.TuitionCycle(row.Invoice)}"),
                UiKit.StatusBadge(DomainText.Invoice(row.Invoice.Status), color),
                UiKit.Body(UiKit.Money(row.Invoice.AmountVnd)),
                UiKit.Caption($"Tiến độ: {row.Progress.AttendedSessions}/{row.Progress.PlannedSessions} buổi")
            }
        };
        if (row.Progress.NeedsPaymentWarning)
        {
            stack.Children.Add(UiKit.StatusBadge(
                "Cảnh báo: đã học đủ 2 buổi nhưng chưa đóng học phí",
                UiKit.Danger));
        }

        if (row.LatestProof is not null && File.Exists(row.LatestProof.ImagePath))
        {
            var proof = new Image
            {
                Source = ImageSource.FromFile(row.LatestProof.ImagePath),
                HeightRequest = 150,
                Aspect = Aspect.AspectFit,
                BackgroundColor = UiKit.SurfaceSecondary
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await Navigation.PushAsync(new ImagePreviewPage(
                "Bill thanh toán",
                row.LatestProof.ImagePath,
                $"Bill-{row.Invoice.Period}-{row.LatestProof.Id}",
                _imageSave));
            proof.GestureRecognizers.Add(tap);
            stack.Children.Add(proof);
        }

        if (row.Invoice.Status == InvoiceStatus.ProofSubmitted)
        {
            var confirm = UiKit.PrimaryButton("Xác nhận đã đóng");
            confirm.Clicked += async (_, _) =>
            {
                var accepted = await DisplayAlertAsync(
                    "Xác nhận học phí?",
                    $"{row.TraineeName} · {UiKit.Money(row.Invoice.AmountVnd)}",
                    "Xác nhận",
                    "Hủy");
                if (!accepted) return;
                await RunActionAsync(
                    () => _database.ConfirmTuitionAsync(CurrentUserId, row.Invoice.Id),
                    confirm,
                    "Đã xác nhận học phí và gửi thông báo cho học viên.");
            };
            var reject = UiKit.SecondaryButton("Yêu cầu tải lại bill");
            reject.Clicked += async (_, _) =>
            {
                var reason = await DisplayPromptAsync(
                    "Yêu cầu tải lại bill",
                    "Nhập lý do hoặc thông tin cần bổ sung.",
                    "Gửi",
                    "Hủy");
                if (reason is null) return;
                await RunActionAsync(
                    () => _database.RejectTuitionProofAsync(CurrentUserId, row.Invoice.Id, reason),
                    reject,
                    "Đã gửi yêu cầu cho học viên.");
            };
            stack.Children.Add(confirm);
            stack.Children.Add(reject);
        }

        return UiKit.Card(stack);
    }
}

public sealed class FounderSupportedTraineesPage : AsyncContentPage
{
    private readonly AppDatabase _database;

    public FounderSupportedTraineesPage(
        AppDatabase database,
        SessionService session)
        : base(session, "C\u1ea7u th\u1ee7 h\u1ecdc vi\u00ean \u0111\u01b0\u1ee3c h\u1ed7 tr\u1ee3")
    {
        _database = database;
    }

    protected override async Task LoadAsync()
    {
        var trainees = (await _database.GetMembersAsync(
                CurrentUserId,
                UserRole.Trainee))
            .Where(item => item.Account.IsTuitionSupported)
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

        root.Children.Add(UiKit.StatusBadge(
            $"{trainees.Count} Cầu thủ học viên",
            UiKit.Success));
        if (trainees.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Ch\u01b0a c\u00f3 account \u0111\u01b0\u1ee3c h\u1ed7 tr\u1ee3",
                "B\u1eadt tr\u1ea1ng th\u00e1i C\u1ea7u Th\u1ee7 H\u1ecdc Vi\u00ean \u0110\u01b0\u1ee3c H\u1ed7 Tr\u1ee3 trong h\u1ed3 s\u01a1 Trainee."));
        }
        else
        {
            foreach (var trainee in trainees)
            {
                root.Children.Add(UiKit.Card(new Grid
                {
                    ColumnSpacing = 10,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(48)),
                        new ColumnDefinition(GridLength.Star)
                    },
                    Children =
                    {
                        UiKit.Avatar(trainee.Profile.PhotoPath, 44),
                        CreateSupportedTraineeDetails(trainee)
                    }
                }));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private static View CreateSupportedTraineeDetails(MemberRow trainee)
    {
        var details = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                UiKit.Headline(trainee.DisplayName),
                UiKit.Caption($"@{trainee.Account.Username} \u00b7 {Value(trainee.Profile.Email)}"),
                UiKit.StatusBadge(
                    DomainText.SupportedTraineeTuitionLabel,
                    UiKit.Success)
            }
        };
        Grid.SetColumn(details, 1);
        return details;
    }

    private static string Value(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Ch\u01b0a c\u1eadp nh\u1eadt" : value;
}

public sealed class FounderSalaryManagementPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly string _period;

    public FounderSalaryManagementPage(
        AppDatabase database,
        SessionService session,
        string period)
        : base(session, string.Empty)
    {
        _database = database;
        _period = period;
    }

    protected override async Task LoadAsync()
    {
        var salaries = await _database.GetSalariesAsync(CurrentUserId, _period);
        var total = salaries.Sum(item => item.Salary.AmountVnd);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.Card(new VerticalStackLayout
                {
                    Spacing = 3,
                    Children =
                    {
                        UiKit.Caption("TỔNG LƯƠNG ĐÃ TÍNH"),
                        UiKit.Title(UiKit.Money(total))
                    }
                })
            }
        };

        if (salaries.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có kỳ lương",
                "Kỳ lương được tạo tự động cho Huấn Luyện Viên đang hoạt động."));
        }
        else
        {
            foreach (var group in salaries
                         .GroupBy(item => item.Salary.CoachUserId)
                         .OrderBy(item => item.First().CoachName, StringComparer.OrdinalIgnoreCase))
            {
                root.Children.Add(CreateCoachCard(group.ToList()));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private View CreateCoachCard(IReadOnlyList<SalaryRow> rows)
    {
        var first = rows[0];
        var paidCount = rows.Count(item => item.Salary.Status == SalaryStatus.Paid);
        var status = paidCount == rows.Count
            ? "Đã thanh toán"
            : paidCount == 0
                ? "Chưa thanh toán"
                : $"{paidCount}/{rows.Count} kỳ đã thanh toán";
        var arrow = UiKit.Body("›", UiKit.TextSecondary);
        arrow.FontSize = 24;
        arrow.VerticalTextAlignment = TextAlignment.Center;
        var grid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        grid.Children.Add(new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                UiKit.Headline(first.CoachName),
                UiKit.Caption(CoachPositionCatalog.Label(first.CoachPosition), UiKit.Primary),
                UiKit.Caption($"{rows.Count} kỳ lương · {status}", UiKit.TextSecondary),
                UiKit.StatusBadge(UiKit.Money(rows.Sum(item => item.Salary.AmountVnd)), UiKit.Success)
            }
        });
        Grid.SetColumn(arrow, 1);
        grid.Children.Add(arrow);

        var card = UiKit.Card(grid, new Thickness(12));
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await PushPageAsync(
            new FounderCoachSalaryDetailPage(
                _database,
                Session,
                first.Salary.CoachUserId,
                _period));
        card.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(card, $"Xem chi tiết lương của {first.CoachName}");
        SemanticProperties.SetHint(card, "Chạm để mở chi tiết");
        return card;
    }
}

public sealed class FounderCoachSalaryDetailPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly string _coachUserId;
    private readonly string _period;

    public FounderCoachSalaryDetailPage(
        AppDatabase database,
        SessionService session,
        string coachUserId,
        string period)
        : base(session, string.Empty)
    {
        _database = database;
        _coachUserId = coachUserId;
        _period = period;
    }

    protected override async Task LoadAsync()
    {
        var salaries = (await _database.GetSalariesAsync(CurrentUserId))
            .Where(item => item.Salary.CoachUserId == _coachUserId)
            .OrderByDescending(item => item.Salary.Period)
            .ToList();
        var coachName = salaries.FirstOrDefault()?.CoachName ?? "Huấn luyện viên";
        var currentPeriod = salaries
            .Where(item => item.Salary.Period == _period)
            .Sum(item => item.Salary.AmountVnd);
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.LargeTitle(coachName),
                UiKit.Caption($"{DomainText.Period(_period)} · {UiKit.Money(currentPeriod)}", UiKit.TextSecondary)
            }
        };

        if (salaries.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có kỳ lương",
                "Kỳ lương sẽ được tạo sau khi Coach có check-in được xác nhận."));
        }
        else
        {
            root.Children.Add(UiKit.Caption("Lịch sử các kỳ lương của Huấn luyện viên", UiKit.TextSecondary));
            foreach (var salary in salaries)
            {
                root.Children.Add(CreateSalaryCard(salary));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private View CreateSalaryCard(SalaryRow row)
    {
        var isPaid = row.Salary.Status == SalaryStatus.Paid;
        var isDue = row.Salary.DueDate.Date <= DateTime.Today;
        var isFiveDaysOverdue = DateTime.Today >= row.Salary.DueDate.Date.AddDays(5);
        var paid = new Switch
        {
            IsToggled = isPaid,
            IsEnabled = !isPaid,
            OnColor = UiKit.Success
        };
        var notes = new Editor
        {
            Placeholder = "Ghi chú",
            Text = row.Salary.Notes,
            MinimumHeightRequest = 60
        };
        var paidRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        paidRow.Children.Add(UiKit.Body("Đã thanh toán"));
        Grid.SetColumn(paid, 1);
        paidRow.Children.Add(paid);
        var save = UiKit.SecondaryButton("Lưu trạng thái lương");
        save.Clicked += async (_, _) =>
            await RunActionAsync(
                () => _database.SaveSalaryAsync(
                    CurrentUserId,
                    row.Salary.Id,
                    paid.IsToggled,
                    notes.Text ?? string.Empty),
                save,
                "Đã cập nhật kỳ lương.");

        return UiKit.Card(new VerticalStackLayout
        {
            Spacing = 7,
            Children =
            {
                UiKit.Headline(DomainText.Period(row.Salary.Period)),
                UiKit.Caption(CoachPositionCatalog.Label(row.CoachPosition), UiKit.Primary),
                UiKit.Caption($"Lớp học: {row.ClassName}"),
                UiKit.Caption($"Hạn thanh toán {row.Salary.DueDate:dd/MM/yyyy}"),
                UiKit.Title(UiKit.Money(row.Salary.AmountVnd)),
                UiKit.Caption("Tính từ các check-in đã được Founder xác nhận."),
                UiKit.StatusBadge(
                    DomainText.Salary(row.Salary.Status),
                    isPaid
                        ? UiKit.Success
                        : isFiveDaysOverdue
                            ? UiKit.Danger
                            : isDue
                                ? UiKit.Warning
                                : UiKit.TextSecondary),
                paidRow,
                UiKit.LabeledField("GHI CHÚ", notes),
                save
            }
        });
    }
}

public sealed class TuitionPage : AsyncContentPage
{
    private readonly AppDatabase _database;
    private readonly MediaService _media;
    private readonly QrCodeService _qrCode;
    private readonly IReceiptPdfService _pdfService;
    private readonly IImageSaveService _imageSave;

    public TuitionPage(
        AppDatabase database,
        SessionService session,
        MediaService media,
        QrCodeService qrCode,
        IReceiptPdfService pdfService,
        IImageSaveService imageSave)
        : base(session, string.Empty)
    {
        _database = database;
        _media = media;
        _qrCode = qrCode;
        _pdfService = pdfService;
        _imageSave = imageSave;
    }

    protected override async Task LoadAsync()
    {
        await _database.EnsureRecurringDataAsync(DateTime.Today);
        var club = await _database.GetClubAsync();
        var invoices = await _database.GetInvoicesAsync(CurrentUserId);
        await _database.EnsurePaymentProofImagesAsync(CurrentUserId, invoices);
        var isTuitionSupported = Session.CurrentUser?.IsTuitionSupported == true;
        IReadOnlyList<TrialEnrollmentDisplay> trialRows = isTuitionSupported
            ? Array.Empty<TrialEnrollmentDisplay>()
            : await LoadTrialRowsAsync();
        var root = new VerticalStackLayout
        {
            Padding = UiKit.PagePadding,
            Spacing = UiKit.SectionSpacing,
            Children =
            {
                UiKit.OfflineBanner(),
                UiKit.Caption(isTuitionSupported
                    ? $"Account của bạn thuộc diện {DomainText.SupportedTraineeLabel}."
                    : "Mỗi chu kỳ gồm số buổi được quy định trong lớp. Bạn có thể chọn đóng trước nhiều chu kỳ; số tiền = học phí một chu kỳ × số chu kỳ.")
            }
        };

        if (isTuitionSupported)
        {
            root.Children.Add(UiKit.EmptyState(
                "Được miễn học phí",
                "Bạn không cần thanh toán, tải QR Code hoặc upload bill học phí."));
        }
        else if (invoices.Count == 0 && trialRows.Count == 0)
        {
            root.Children.Add(UiKit.EmptyState(
                "Chưa có học phí",
                "Học phí sẽ xuất hiện ngay khi Founder phân công bạn vào lớp học."));
        }
        else
        {
            foreach (var trial in trialRows)
            {
                root.Children.Add(CreateTrialCard(trial));
            }

            if (invoices.Count == 0)
            {
                Content = UiKit.KeyboardAwareScroll(root);
                return;
            }

            var totalPaid = invoices
                .Where(item => item.Invoice.Status == InvoiceStatus.Paid)
                .Sum(item => item.Invoice.AmountVnd);
            root.Children.Add(UiKit.Card(new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    UiKit.Caption("TỔNG TIỀN ĐÃ ĐÓNG"),
                    UiKit.Title(UiKit.Money(totalPaid)),
                    UiKit.Caption("Các bill đã được Founder xác nhận.")
                }
            }));

            var completed = invoices
                .Where(item => item.Invoice.Status == InvoiceStatus.Paid && item.Progress.IsComplete)
                .ToList();
            var active = invoices
                .Where(item => !(item.Invoice.Status == InvoiceStatus.Paid && item.Progress.IsComplete))
                .ToList();
            root.Children.Add(BuildInvoiceSection(
                "Đã đóng",
                UiKit.Success,
                active.Where(item => item.Invoice.Status == InvoiceStatus.Paid).ToList(),
                club));
            root.Children.Add(BuildInvoiceSection(
                "Chưa đóng",
                UiKit.Danger,
                active.Where(item => item.Invoice.Status is InvoiceStatus.Pending
                                     or InvoiceStatus.Rejected
                                     or InvoiceStatus.Overdue).ToList(),
                club));
            root.Children.Add(BuildInvoiceSection(
                "Bill chờ xác nhận",
                UiKit.Warning,
                active.Where(item => item.Invoice.Status == InvoiceStatus.ProofSubmitted).ToList(),
                club));
            if (completed.Count > 0)
            {
                root.Children.Add(UiKit.Caption(
                    $"{completed.Count} bill đã hoàn tất đủ số buổi và được tự động ẩn khỏi danh sách đang theo dõi."));
            }
        }

        Content = UiKit.KeyboardAwareScroll(root);
    }

    private async Task<IReadOnlyList<TrialEnrollmentDisplay>> LoadTrialRowsAsync()
    {
        var classes = await _database.GetClassesAsync(CurrentUserId);
        var rows = new List<TrialEnrollmentDisplay>();
        foreach (var row in classes)
        {
            var enrollment = (await _database.GetClassEnrollmentsAsync(row.Class.Id))
                .FirstOrDefault(item => item.TraineeUserId == CurrentUserId && item.IsTrial);
            if (enrollment is null)
            {
                continue;
            }

            var progress = await _database.GetDisplayedTuitionProgressAsync(
                CurrentUserId,
                CurrentUserId,
                row.Class.Id);
            rows.Add(new TrialEnrollmentDisplay(row, enrollment, progress));
        }

        return rows;
    }

    private static View CreateTrialCard(TrialEnrollmentDisplay trial)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 5,
            Children =
            {
                UiKit.Headline(trial.Row.Class.Name),
                UiKit.StatusBadge("Học thử", UiKit.Primary),
                UiKit.Body(trial.Row.ScheduleText, UiKit.TextSecondary),
                UiKit.Caption(
                    $"Tiến độ học thử: {trial.Progress.AttendedSessions}/{Math.Clamp(trial.Enrollment.TrialSessionCount, 1, 5)} buổi")
            }
        };
        return UiKit.Card(stack);
    }

    private sealed record TrialEnrollmentDisplay(
        ClassRow Row,
        ClassEnrollment Enrollment,
        TuitionCycleProgress Progress);

    private View BuildInvoiceSection(
        string title,
        Color color,
        IReadOnlyList<InvoiceRow> rows,
        ClubProfile club)
    {
        var section = new VerticalStackLayout { Spacing = 6 };
        var header = UiKit.SecondaryButton($"{title} ({rows.Count})");
        header.TextColor = color;
        var details = new VerticalStackLayout { Spacing = 6, IsVisible = false };
        header.Clicked += (_, _) => details.IsVisible = !details.IsVisible;
        section.Children.Add(header);
        if (rows.Count == 0)
        {
            section.Children.Add(UiKit.Caption("Không có khoản nào."));
        }
        else
        {
            foreach (var row in rows)
            {
                details.Children.Add(CreateInvoiceCard(row, club));
            }
        }

        section.Children.Add(details);
        return section;
    }

    private View CreateInvoiceCard(InvoiceRow row, ClubProfile club)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                UiKit.Headline($"{row.ClassName} · {DomainText.TuitionPrepaidCycles(row.Invoice)}"),
                UiKit.StatusBadge(
                    DomainText.Invoice(row.Invoice.Status),
                    UiKit.InvoiceColor(row.Invoice.Status)),
                UiKit.Body($"Số tiền: {UiKit.Money(row.Invoice.AmountVnd)}"),
                UiKit.Body($"Hạn đóng: {row.Invoice.DueDate:dd/MM/yyyy}"),
                UiKit.Body($"Nội dung: {row.Invoice.PaymentContent}")
            }
        };

        if (row.Progress.PlannedSessions > 0)
        {
            stack.Children.Insert(
                3,
                UiKit.Caption(
                    $"Tiến độ chu kỳ: {row.Progress.AttendedSessions}/{row.Progress.PlannedSessions} buổi · {UiKit.Money(row.Invoice.CycleFeeVnd > 0 ? row.Invoice.CycleFeeVnd : row.Invoice.AmountVnd)} / chu kỳ",
                    UiKit.TextSecondary));
        }

        if (row.Progress.NeedsPaymentWarning)
        {
            stack.Children.Add(UiKit.StatusBadge(
                "Cảnh báo: đã học đủ 2 buổi nhưng chưa đóng học phí",
                UiKit.Danger));
        }

        if (row.Invoice.Status is not (InvoiceStatus.Paid or InvoiceStatus.ProofSubmitted))
        {
            var cyclePicker = new Picker
            {
                Title = "Chọn số chu kỳ",
                ItemsSource = Enumerable.Range(1, 12)
                    .Select(item => $"{item} chu kỳ")
                    .ToList(),
                SelectedIndex = Math.Clamp(row.Invoice.CycleCount, 1, 12) - 1
            };
            var cycleField = UiKit.LabeledField(
                "ĐÓNG TRƯỚC BAO NHIÊU CHU KỲ",
                cyclePicker,
                "Số tiền sẽ tự tính: học phí một chu kỳ × số chu kỳ.");
            stack.Children.Add(cycleField);
            cyclePicker.SelectedIndexChanged += async (_, _) =>
            {
                if (cyclePicker.SelectedIndex < 0)
                {
                    return;
                }

                var selectedCycles = cyclePicker.SelectedIndex + 1;
                cyclePicker.IsEnabled = false;
                try
                {
                    await _database.SetInvoiceCycleCountAsync(
                        CurrentUserId,
                        row.Invoice.Id,
                        selectedCycles);
                    await ReloadAsync();
                }
                catch (Exception exception)
                {
                    await DisplayAlertAsync("Chưa thể cập nhật", exception.Message, "Đóng");
                    cyclePicker.SelectedIndex = Math.Clamp(row.Invoice.CycleCount, 1, 12) - 1;
                }
                finally
                {
                    cyclePicker.IsEnabled = true;
                }
            };
        }

        if (string.IsNullOrWhiteSpace(club.BankBin)
            || string.IsNullOrWhiteSpace(club.BankAccountNumber))
        {
            stack.Children.Add(UiKit.Card(new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    UiKit.Headline("Chưa có thông tin ngân hàng"),
                    UiKit.Body(
                        "Đội chưa cập nhật thông tin ngân hàng. Vui lòng quay lại sau hoặc liên hệ người điều hành.",
                        UiKit.TextSecondary)
                }
            }));
        }
        else
        {
            stack.Children.Add(UiKit.Card(new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    UiKit.Body($"Ngân hàng: {Value(club.BankName)}"),
                    UiKit.Body($"Số tài khoản: {club.BankAccountNumber}"),
                    UiKit.Body($"Tên tài khoản: {Value(club.BankAccountName)}")
                }
            }));

            var qr = _qrCode.CreatePaymentQr(club, row.Invoice);
            if (qr is not null)
            {
                var qrImage = new Image
                {
                    Source = ImageSource.FromStream(() => new MemoryStream(qr)),
                    HeightRequest = 220,
                    WidthRequest = 220,
                    HorizontalOptions = LayoutOptions.Center,
                    Aspect = Aspect.AspectFit,
                    BackgroundColor = Colors.White
                };
                stack.Children.Add(qrImage);
                var saveQr = UiKit.SecondaryButton("Lưu QR Code");
                saveQr.Clicked += async (_, _) =>
                    await SaveQrAsync(row, qr, saveQr);
                stack.Children.Add(saveQr);
            }

            var copy = UiKit.SecondaryButton("Sao chép nội dung chuyển khoản");
            copy.Clicked += async (_, _) =>
            {
                await Clipboard.Default.SetTextAsync(row.Invoice.PaymentContent);
                await DisplayAlertAsync("Đã sao chép", row.Invoice.PaymentContent, "OK");
            };
            stack.Children.Add(copy);
        }

        if (row.Invoice.Status != InvoiceStatus.Paid
            && row.Invoice.Status != InvoiceStatus.ProofSubmitted)
        {
            var upload = UiKit.PrimaryButton(
                row.Invoice.Status == InvoiceStatus.Rejected
                    ? "Tải lại bill thanh toán"
                    : "Upload bill thanh toán");
            upload.Clicked += async (_, _) => await UploadProofAsync(row, upload);
            stack.Children.Add(upload);
        }
        else if (row.Invoice.Status == InvoiceStatus.ProofSubmitted)
        {
            stack.Children.Add(UiKit.Caption(
                "Bill đã được gửi. Vui lòng chờ Founder xác nhận."));
        }

        if (row.LatestProof is not null && File.Exists(row.LatestProof.ImagePath))
        {
            var proof = new Image
            {
                Source = ImageSource.FromFile(row.LatestProof.ImagePath),
                HeightRequest = 130,
                Aspect = Aspect.AspectFit
            };
            var openProof = new TapGestureRecognizer();
            openProof.Tapped += async (_, _) =>
                await Navigation.PushAsync(new ImagePreviewPage(
                    "Bill đã gửi",
                    row.LatestProof.ImagePath,
                    $"Bill-{row.Invoice.Period}-{row.LatestProof.Id}",
                    _imageSave));
            proof.GestureRecognizers.Add(openProof);
            SemanticProperties.SetDescription(proof, "Xem lớn bill đã gửi");
            SemanticProperties.SetHint(
                proof,
                "Nhấn hai lần để xem lớn và lưu hình ảnh");
            stack.Children.Add(proof);
            stack.Children.Add(UiKit.Caption(
                "Chạm ảnh bill để xem lớn hoặc lưu về máy."));
        }

        if (row.Invoice.Status == InvoiceStatus.Paid && row.Receipt is not null)
        {
            var receiptButton = UiKit.PrimaryButton("In / lưu hóa đơn PDF");
            receiptButton.Clicked += async (_, _) =>
                await ExportReceiptAsync(row.Receipt, club, receiptButton);
            stack.Children.Add(receiptButton);
        }

        return UiKit.Card(stack);
    }

    private async Task SaveQrAsync(
        InvoiceRow row,
        byte[] qrBytes,
        Button source)
    {
        source.IsEnabled = false;
        try
        {
            var location = await _imageSave.SavePngAsync(
                qrBytes,
                $"QR-{row.Invoice.Period}-{row.Invoice.Id}.png");
            await DisplayAlertAsync(
                "Đã lưu QR Code",
                $"QR Code đã được lưu tại {location}.",
                "OK");
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "Chưa thể lưu QR Code",
                exception.Message,
                "Đóng");
        }
        finally
        {
            source.IsEnabled = true;
        }
    }

    private async Task UploadProofAsync(InvoiceRow row, Button source)
    {
        var choice = await DisplayActionSheetAsync(
            "Bill thanh toán",
            "Hủy",
            null,
            "Chụp ảnh",
            "Chọn ảnh chụp màn hình");
        try
        {
            var path = choice switch
            {
                "Chụp ảnh" => await _media.CapturePhotoAsync("payment_proofs"),
                "Chọn ảnh chụp màn hình" => await _media.PickPhotoAsync("payment_proofs"),
                _ => null
            };
            if (path is null)
            {
                return;
            }

            var confirmed = await DisplayAlertAsync(
                "Gửi bill này?",
                "Sau khi gửi, trạng thái sẽ chuyển sang Chờ xác nhận.",
                "Gửi bill",
                "Hủy");
            if (!confirmed)
            {
                return;
            }

            await RunActionAsync(
                () => _database.SubmitPaymentProofAsync(
                    CurrentUserId,
                    row.Invoice.Id,
                    path,
                    string.Empty),
                source,
                "Bill đã được gửi đến account Founder.");
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Không thể gửi bill", exception.Message, "Đóng");
        }
    }

    private async Task ExportReceiptAsync(
        Receipt receipt,
        ClubProfile club,
        Button source)
    {
        await RunActionAsync(
            async () =>
            {
                var path = receipt.PdfPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    path = await _pdfService.GenerateAsync(receipt, club);
                    await _database.UpdateReceiptPdfPathAsync(receipt.Id, path);
                }

                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Lưu hoặc chia sẻ hóa đơn học phí",
                    File = new ShareFile(path, "application/pdf")
                });
            },
            source,
            reload: false);
    }

    private static string Value(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Chưa cập nhật" : value;
}
