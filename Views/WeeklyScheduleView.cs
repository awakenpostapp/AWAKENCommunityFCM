using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Ui;
using Microsoft.Maui.Controls.Shapes;

namespace CommunityFootballClubManager.Views;

/// <summary>
/// Compact month calendar used by Founder, Coach and Trainee class tabs.
/// Each scheduled date is filled with a status color; class details remain in
/// the summary cards below the calendar.
/// </summary>
public static class WeeklyScheduleView
{
    private enum DayStatus
    {
        Empty,
        NotTaught,
        Today,
        Abandoned,
        Taught
    }

    private static readonly (int Value, string Label)[] Days =
    [
        ((int)DayOfWeek.Monday, "THỨ 2"),
        ((int)DayOfWeek.Tuesday, "THỨ 3"),
        ((int)DayOfWeek.Wednesday, "THỨ 4"),
        ((int)DayOfWeek.Thursday, "THỨ 5"),
        ((int)DayOfWeek.Friday, "THỨ 6"),
        ((int)DayOfWeek.Saturday, "THỨ 7"),
        ((int)DayOfWeek.Sunday, "CHỦ NHẬT")
    ];

    private static readonly Color HeaderColor = UiKit.PrimaryDark;
    private static readonly Color CalendarBackground = Color.FromArgb("#EAF5F3");
    private static readonly Color NotTaughtColor = Color.FromArgb("#E9EEF5");
    private static readonly Color TodayColor = Color.FromArgb("#BCE8F2");
    private static readonly Color AbandonedColor = Color.FromArgb("#FFD8D8");
    private static readonly Color TaughtColor = Color.FromArgb("#CFF3DD");

    public static View Build(
        IReadOnlyList<ClassRow> classes,
        UserRole role,
        IReadOnlyDictionary<string, IReadOnlyList<TrainingSession>>? sessionsByClass = null,
        IReadOnlyList<CoachCheckInHistoryRow>? coachHistory = null,
        Func<ClassRow, Task>? openClass = null,
        Func<Task>? createClass = null,
        Func<ClassRow, DateTime, Task>? openClassOccurrence = null)
    {
        sessionsByClass ??= new Dictionary<string, IReadOnlyList<TrainingSession>>();
        coachHistory ??= [];
        var section = new VerticalStackLayout
        {
            Spacing = 7
        };
        var scheduledClasses = classes
            // A completed lesson is a real calendar occurrence even when its
            // date no longer matches the class's recurring weekday.  This can
            // happen when a Founder moves a lesson, creates a make-up session,
            // or records attendance on behalf of a Coach.  Keep those sessions
            // in the month calendar so a taught day is never silently omitted.
            .Where(item => HasRecurringDays(item.Class)
                           || HasAnySession(item, sessionsByClass))
            .OrderBy(item => item.Class.StartTimeMinutes)
            .ThenBy(item => item.Class.Name)
            .ToList();
        var title = role == UserRole.Trainee
            ? "Lịch học trong tháng"
            : "Lịch dạy trong tháng";

        var titleRow = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        titleRow.Children.Add(UiKit.Title(title));
        if (role == UserRole.Founder && createClass is not null)
        {
            var createButton = UiKit.SecondaryButton(
                "Tạo lớp học",
                async (_, _) => await createClass());
            createButton.FontSize = 12;
            createButton.Padding = new Thickness(12, 5);
            createButton.MinimumHeightRequest = 38;
            createButton.HeightRequest = 38;
            createButton.HorizontalOptions = LayoutOptions.End;
            Grid.SetColumn(createButton, 1);
            titleRow.Children.Add(createButton);
        }

        section.Children.Add(titleRow);

        if (scheduledClasses.Count == 0)
        {
            section.Children.Add(UiKit.Card(new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    UiKit.Headline("Chưa có lịch học"),
                    UiKit.Body(
                        "Lịch sẽ hiển thị sau khi lớp được thiết lập ngày và giờ cố định.",
                        UiKit.TextSecondary)
                }
            }));
            return section;
        }

        var month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var calendarHost = new VerticalStackLayout { Spacing = 6 };
        var selectedDayHost = new VerticalStackLayout { Spacing = 6 };
        var checkInsBySession = coachHistory
            .GroupBy(item => item.CheckIn.SessionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CoachCheckIn>)group
                    .Select(item => item.CheckIn)
                    .ToList());

        void RenderMonth()
        {
            calendarHost.Children.Clear();
            // Build fresh controls on every render. A MAUI visual element can
            // only have one parent; reusing old controls causes a parent error
            // when the month arrow is tapped.
            var previous = CreateMonthArrowButton("‹", "Tháng trước");
            var next = CreateMonthArrowButton("›", "Tháng sau");
            var monthTitle = new Label
            {
                Text = $"Tháng {month:MM/yyyy}",
                FontFamily = "OpenSansSemibold",
                FontSize = 16,
                TextColor = UiKit.TextPrimary,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Fill
            };
            var toolbar = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(46)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(46))
                },
                ColumnSpacing = 4,
                HeightRequest = 38
            };
            toolbar.Children.Add(previous);
            Grid.SetColumn(monthTitle, 1);
            toolbar.Children.Add(monthTitle);
            Grid.SetColumn(next, 2);
            toolbar.Children.Add(next);
            calendarHost.Children.Add(toolbar);

                calendarHost.Children.Add(new Border
            {
                BackgroundColor = CalendarBackground,
                Stroke = UiKit.Divider,
                StrokeThickness = 1,
                Padding = new Thickness(1),
                Content = BuildMonthGrid(
                    scheduledClasses,
                    sessionsByClass,
                    checkInsBySession,
                    month,
                    role,
                    date => RenderSelectedDay(
                        role,
                        date,
                        selectedDayHost,
                        scheduledClasses,
                        sessionsByClass,
                        checkInsBySession,
                        openClass,
                        openClassOccurrence))
            });
            calendarHost.Children.Add(BuildStatusLegend(role));
            // All three roles use the same compact month interaction. Class
            // details are created only after a filled date is selected.
            selectedDayHost.Children.Clear();
            selectedDayHost.Children.Add(UiKit.Caption(
                "Chạm vào một ngày được tô màu để xem lớp học trong ngày đó.",
                UiKit.TextSecondary));
            calendarHost.Children.Add(selectedDayHost);

            previous.Clicked += (_, _) =>
            {
                month = month.AddMonths(-1);
                RenderMonth();
            };
            next.Clicked += (_, _) =>
            {
                month = month.AddMonths(1);
                RenderMonth();
            };
        }

        RenderMonth();
        section.Children.Add(calendarHost);
        return section;
    }

    private static Button CreateMonthArrowButton(string text, string description)
    {
        var button = new Button
        {
            Text = text,
            FontSize = 25,
            FontFamily = "OpenSansSemibold",
            TextColor = UiKit.PrimaryDark,
            BackgroundColor = UiKit.TealSoft,
            BorderColor = UiKit.Primary.WithAlpha(0.28f),
            BorderWidth = 1,
            CornerRadius = 19,
            Padding = new Thickness(0, 0),
            WidthRequest = 42,
            HeightRequest = 38,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(button, description);
        return button;
    }

    private static void RenderSelectedDay(
        UserRole role,
        DateTime date,
        VerticalStackLayout host,
        IReadOnlyList<ClassRow> classes,
        IReadOnlyDictionary<string, IReadOnlyList<TrainingSession>> sessionsByClass,
        IReadOnlyDictionary<string, IReadOnlyList<CoachCheckIn>> checkInsBySession,
        Func<ClassRow, Task>? openClass,
        Func<ClassRow, DateTime, Task>? openClassOccurrence)
    {
        host.Children.Clear();
        host.Children.Add(BuildSelectedDayList(
            role,
            classes,
            sessionsByClass,
            checkInsBySession,
            date,
            openClass,
            openClassOccurrence));
    }

    private static Grid BuildMonthGrid(
        IReadOnlyList<ClassRow> classes,
        IReadOnlyDictionary<string, IReadOnlyList<TrainingSession>> sessionsByClass,
        IReadOnlyDictionary<string, IReadOnlyList<CoachCheckIn>> checkInsBySession,
        DateTime month,
        UserRole role,
        Action<DateTime>? daySelected = null)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        var firstColumn = ((int)first.DayOfWeek + 6) % 7;
        var weekCount = (int)Math.Ceiling((firstColumn + daysInMonth) / 7d);
        var grid = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            ColumnSpacing = 1,
            RowSpacing = 1,
            BackgroundColor = UiKit.Divider
        };
        foreach (var _ in Days)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(30)));
        for (var row = 0; row < weekCount; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(46)));
        }

        for (var dayIndex = 0; dayIndex < Days.Length; dayIndex++)
        {
            AddCell(grid, CreateHeaderCell(Days[dayIndex].Label), dayIndex, 0);
        }

        for (var dayNumber = 1; dayNumber <= daysInMonth; dayNumber++)
        {
            var date = new DateTime(month.Year, month.Month, dayNumber);
            var position = firstColumn + dayNumber - 1;
            var row = position / 7 + 1;
            var column = position % 7;
            var status = GetDayStatus(
                classes,
                sessionsByClass,
                checkInsBySession,
                date,
                role);
            AddCell(grid, CreateDayCell(date, status, daySelected), column, row);
        }

        // Fill leading/trailing cells so every month keeps the same compact grid.
        var totalCells = weekCount * 7;
        for (var position = 0; position < totalCells; position++)
        {
            var dayOffset = position - firstColumn + 1;
            if (dayOffset >= 1 && dayOffset <= daysInMonth)
            {
                continue;
            }

            AddCell(
                grid,
                new Border
                {
                    BackgroundColor = Color.FromArgb("#F7FAF9"),
                    StrokeThickness = 0,
                    Content = new Label { Text = string.Empty }
                },
                position % 7,
                position / 7 + 1);
        }

        return grid;
    }

    private static DayStatus GetDayStatus(
        IReadOnlyList<ClassRow> classes,
        IReadOnlyDictionary<string, IReadOnlyList<TrainingSession>> sessionsByClass,
        IReadOnlyDictionary<string, IReadOnlyList<CoachCheckIn>> checkInsBySession,
        DateTime date,
        UserRole role)
    {
        var scheduled = classes
            .Where(item => IsScheduledOccurrence(
                item,
                sessionsByClass,
                date))
            .ToList();
        if (scheduled.Count == 0)
        {
            return DayStatus.Empty;
        }

        // Keep scheduled future days filled as "Chưa dạy" for Founder too.
        // The previous Founder-only early return made upcoming fixed classes
        // look like empty calendar cells.
        if (role == UserRole.Founder && date.Date > DateTime.Today)
        {
            return DayStatus.NotTaught;
        }

        var statuses = scheduled
            .Select(item => GetClassDayStatus(
                item,
                sessionsByClass,
                checkInsBySession,
                date))
            .ToList();
        if (statuses.Any(item => item == DayStatus.Today))
        {
            return DayStatus.Today;
        }

        if (statuses.Any(item => item == DayStatus.Abandoned))
        {
            return DayStatus.Abandoned;
        }

        return statuses.All(item => item == DayStatus.Taught)
            ? DayStatus.Taught
            : DayStatus.NotTaught;
    }

    private static DayStatus GetClassDayStatus(
        ClassRow row,
        IReadOnlyDictionary<string, IReadOnlyList<TrainingSession>> sessionsByClass,
        IReadOnlyDictionary<string, IReadOnlyList<CoachCheckIn>> checkInsBySession,
        DateTime date)
    {
        if (date.Date < row.Class.StartDate.Date)
        {
            return DayStatus.Empty;
        }

        if (date.Date > DateTime.Today)
        {
            return DayStatus.NotTaught;
        }

        var session = sessionsByClass.GetValueOrDefault(row.Class.Id)?
            .FirstOrDefault(item => item.SessionDate.Date == date.Date);
        IReadOnlyList<CoachCheckIn> checkIns = session is null
            ? Array.Empty<CoachCheckIn>()
            : checkInsBySession.GetValueOrDefault(session.Id)
                ?? Array.Empty<CoachCheckIn>();

        if (session?.Status is SessionStatus.Submitted or SessionStatus.Locked
            || checkIns.Any(CoachCheckInTime.HasCoachCheckout))
        {
            return DayStatus.Taught;
        }

        if (date.Date == DateTime.Today)
        {
            if (checkIns.Any(item => !CoachCheckInTime.IsAutoAbsent(item)
                                     && item.ApprovalStatus != CoachCheckInApprovalStatus.Rejected))
            {
                return DayStatus.Today;
            }

            // Before the two-hour lock there is still an expected class today.
            // Once the lock has passed, the Worker/offline maintenance creates
            // an AUTO_ABSENT marker and the class becomes Coach không dạy.
            if (!CoachCheckInTime.IsCheckInWindowLocked(row.Class, date)
                && !checkIns.Any(CoachCheckInTime.IsAutoAbsent))
            {
                return DayStatus.Today;
            }
        }

        return DayStatus.Abandoned;
    }

    private static View BuildSelectedDayList(
        UserRole role,
        IReadOnlyList<ClassRow> classes,
        IReadOnlyDictionary<string, IReadOnlyList<TrainingSession>> sessionsByClass,
        IReadOnlyDictionary<string, IReadOnlyList<CoachCheckIn>> checkInsBySession,
        DateTime date,
        Func<ClassRow, Task>? openClass,
        Func<ClassRow, DateTime, Task>? openClassOccurrence)
    {
        var stack = new VerticalStackLayout { Spacing = 7 };
        stack.Children.Add(UiKit.Title($"L\u1edbp h\u1ecdc ng\u00e0y {date:dd/MM/yyyy}"));
        stack.Children.Add(UiKit.Caption(
            "C\u00e1c l\u1edbp c\u00f3 l\u1ecbch trong ng\u00e0y \u0111\u01b0\u1ee3c ch\u1ecdn; ch\u1ea1m v\u00e0o l\u1edbp \u0111\u1ec3 xem \u0111\u1ea7y \u0111\u1ee7 th\u00f4ng tin."));

        var occurrences = classes
            .Where(row => IsScheduledOccurrence(
                row,
                sessionsByClass,
                date))
            .Select(row => (Row: row, Date: date))
            .OrderBy(item => item.Row.Class.StartTimeMinutes)
            .ThenBy(item => item.Row.Class.Name)
            .ToList();
        if (occurrences.Count == 0)
        {
            stack.Children.Add(UiKit.Caption("Ng\u00e0y n\u00e0y kh\u00f4ng c\u00f3 l\u1edbp h\u1ecdc."));
            return stack;
        }

        foreach (var occurrence in occurrences)
        {
            var status = GetClassDayStatus(
                occurrence.Row,
                sessionsByClass,
                checkInsBySession,
                occurrence.Date);
            var card = CreateOccurrenceCard(occurrence.Row, occurrence.Date, status, role);
            if (openClass is not null || openClassOccurrence is not null)
            {
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) =>
                {
                    if (openClassOccurrence is not null)
                    {
                        await openClassOccurrence(occurrence.Row, occurrence.Date);
                    }
                    else if (openClass is not null)
                    {
                        await openClass(occurrence.Row);
                    }
                };
                card.GestureRecognizers.Add(tap);
            }

            stack.Children.Add(card);
        }

        return stack;
    }

    private static Border CreateOccurrenceCard(
        ClassRow row,
        DateTime date,
        DayStatus status,
        UserRole role)
    {
        var (label, color, textColor) = StatusPresentation(status, role);
        var dateLabel = new Label
        {
            Text = date.ToString("dd/MM"),
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            TextColor = UiKit.TextPrimary,
            VerticalTextAlignment = TextAlignment.Center
        };
        var details = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                UiKit.Headline(row.Class.Name),
                UiKit.Caption($"Coach: {row.CoachNames}"),
                UiKit.Caption(
                    $"{DomainText.TimeRange(row.Class.StartTimeMinutes, row.Class.EndTimeMinutes)} · {row.Venue?.Name ?? "Ch\u01b0a c\u1eadp nh\u1eadt s\u00e2n"}")
            }
        };
        Grid.SetColumn(details, 1);
        var badge = new Border
        {
            BackgroundColor = color,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 13 },
            Padding = new Thickness(9, 5),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = label,
                TextColor = textColor,
                FontFamily = "OpenSansSemibold",
                FontSize = 10,
                HorizontalTextAlignment = TextAlignment.Center
            }
        };
        Grid.SetColumn(badge, 2);
        return UiKit.Card(new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(52)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Children = { dateLabel, details, badge }
        }, new Thickness(11));
    }

    private static IEnumerable<DateTime> GetMonthDates(
        string scheduleDays,
        DateTime month,
        DateTime? startDate = null)
    {
        var scheduledDays = scheduleDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var day) ? day : -1)
            .ToHashSet();
        var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(month.Year, month.Month, day);
            if (scheduledDays.Contains((int)date.DayOfWeek)
                && (!startDate.HasValue || date.Date >= startDate.Value.Date))
            {
                yield return date;
            }
        }
    }

    private static (string Label, Color Background, Color Text) StatusPresentation(
        DayStatus status,
        UserRole role) =>
        status switch
        {
            DayStatus.NotTaught => (role == UserRole.Trainee ? "S\u1eafp t\u1edbi" : "Ch\u01b0a d\u1ea1y", NotTaughtColor, UiKit.TextSecondary),
            DayStatus.Today => ("H\u00f4m nay", TodayColor, UiKit.TextPrimary),
            DayStatus.Abandoned => ("Coach kh\u00f4ng d\u1ea1y", AbandonedColor, UiKit.Danger),
            DayStatus.Taught => (role == UserRole.Trainee ? "\u0110\u00e3 h\u1ecdc" : "\u0110\u00e3 d\u1ea1y", TaughtColor, UiKit.Success),
            _ => ("Kh\u00f4ng c\u00f3 l\u1edbp", Colors.White, UiKit.TextSecondary)
        };

    private static View BuildStatusLegend(UserRole role)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            RowSpacing = 4,
            Margin = new Thickness(2, 1, 2, 0)
        };
        AddLegendItem(grid, role == UserRole.Trainee ? "S\u1eafp t\u1edbi" : "Ch\u01b0a d\u1ea1y", NotTaughtColor, 0, 0);
        AddLegendItem(grid, role == UserRole.Trainee ? "L\u1ecbch h\u1ecdc h\u00f4m nay" : "L\u1ecbch d\u1ea1y h\u00f4m nay", TodayColor, 1, 0, UiKit.TextPrimary);
        AddLegendItem(grid, "Coach kh\u00f4ng d\u1ea1y", AbandonedColor, 0, 1, UiKit.Danger);
        AddLegendItem(grid, role == UserRole.Trainee ? "\u0110\u00e3 h\u1ecdc" : "\u0110\u00e3 d\u1ea1y", TaughtColor, 1, 1, UiKit.Success);
        return grid;
    }

    private static View BuildLegend()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            RowSpacing = 4,
            Margin = new Thickness(2, 1, 2, 0)
        };
        AddLegendItem(grid, "Chưa dạy", NotTaughtColor, 0, 0);
        AddLegendItem(grid, "Lịch dạy hôm nay", TodayColor, 1, 0, Colors.White);
        AddLegendItem(grid, "Bỏ dạy", AbandonedColor, 0, 1, UiKit.Danger);
        AddLegendItem(grid, "Đã dạy", TaughtColor, 1, 1, UiKit.Success);
        return grid;
    }

    private static void AddLegendItem(
        Grid grid,
        string text,
        Color background,
        int column,
        int row,
        Color? textColor = null)
    {
        var swatch = new Border
        {
            BackgroundColor = background,
            Stroke = UiKit.Divider,
            StrokeThickness = 0.5,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Center
        };
        var item = new HorizontalStackLayout
        {
            Spacing = 5,
            Children =
            {
                swatch,
                UiKit.Caption(text, textColor ?? UiKit.TextSecondary)
            }
        };
        Grid.SetColumn(item, column);
        Grid.SetRow(item, row);
        grid.Children.Add(item);
    }

    private static View CreateHeaderCell(string text)
    {
        return new Border
        {
            BackgroundColor = HeaderColor,
            StrokeThickness = 0,
            Padding = new Thickness(2, 3),
            Content = new Label
            {
                Text = text,
                FontFamily = "OpenSansSemibold",
                FontSize = 8,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 2
            }
        };
    }

    private static View CreateDayCell(
        DateTime date,
        DayStatus status,
        Action<DateTime>? daySelected = null)
    {
        var background = status switch
        {
            DayStatus.NotTaught => NotTaughtColor,
            DayStatus.Today => TodayColor,
            DayStatus.Abandoned => AbandonedColor,
            DayStatus.Taught => TaughtColor,
            _ => Colors.White
        };
        var textColor = status switch
        {
            DayStatus.Today => Colors.White,
            DayStatus.Abandoned => UiKit.Danger,
            DayStatus.Taught => UiKit.Success,
            _ => UiKit.TextPrimary
        };
        var isToday = date.Date == DateTime.Today;
        var cell = new Border
        {
            BackgroundColor = background,
            Stroke = isToday ? UiKit.PrimaryDark : UiKit.Divider,
            StrokeThickness = isToday ? 2 : 1,
            Padding = 0,
            Content = new Label
            {
                Text = date.Day.ToString("00"),
                FontFamily = "OpenSansSemibold",
                FontSize = 11,
                TextColor = textColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        if (daySelected is not null && status != DayStatus.Empty)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => daySelected(date);
            cell.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(cell, $"Xem lớp học ngày {date:dd/MM/yyyy}");
        }

        return cell;
    }

    private static bool HasDay(string values, int day) => values
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Any(value => int.TryParse(value, out var parsed) && parsed == day);

    private static bool HasRecurringDays(TrainingClass row) =>
        !string.IsNullOrWhiteSpace(row.ScheduleDays);

    private static bool HasAnySession(
        ClassRow row,
        IReadOnlyDictionary<string, IReadOnlyList<TrainingSession>> sessionsByClass) =>
        sessionsByClass.TryGetValue(row.Class.Id, out var sessions)
        && sessions.Count > 0;

    private static bool IsScheduledOccurrence(
        ClassRow row,
        IReadOnlyDictionary<string, IReadOnlyList<TrainingSession>> sessionsByClass,
        DateTime date)
    {
        if (date.Date < row.Class.StartDate.Date)
        {
            return false;
        }

        // Prefer the persisted session date when one exists.  The session is
        // the source of truth for a moved/make-up lesson; the recurring weekday
        // remains the source of truth for future planned lessons.
        var hasSession = sessionsByClass.TryGetValue(row.Class.Id, out var sessions)
                         && sessions.Any(item => item.SessionDate.Date == date.Date);
        return hasSession || HasDay(row.Class.ScheduleDays, (int)date.DayOfWeek);
    }

    private static void AddCell(Grid grid, View view, int column, int row)
    {
        Grid.SetColumn(view, column);
        Grid.SetRow(view, row);
        grid.Children.Add(view);
    }
}
