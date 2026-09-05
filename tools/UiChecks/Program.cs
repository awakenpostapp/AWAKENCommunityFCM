using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Ui;

var failed = 0;
void Check(string name, Action assertion)
{
    try { assertion(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
double Luminance(Color c)
{
    double Linear(double x) => x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
    return .2126 * Linear(c.Red) + .7152 * Linear(c.Green) + .0722 * Linear(c.Blue);
}
double Contrast(Color a, Color b)
{
    var x = Luminance(a); var y = Luminance(b);
    return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
}
Check("primary action has readable text and an accessible touch target", () =>
{
    var button = UiKit.PrimaryButton("Hoàn tất điểm danh");
    var ratio = Contrast(button.TextColor, button.BackgroundColor);
    Require(ratio >= 4.5, $"Expected >= 4.5:1; got {ratio:F2}:1");
    Require(button.MinimumHeightRequest >= 44, "Touch target is too small");
    Require(SemanticProperties.GetDescription(button) == button.Text, "Action has no accessible name");
});
Check("secondary page copy has readable contrast", () =>
{
    var label = UiKit.Caption("Biểu trưng hiển thị trong 30 ngày");
    var ratio = Contrast(label.TextColor, UiKit.Background);
    Require(ratio >= 4.5, $"Expected >= 4.5:1; got {ratio:F2}:1");
});
Check("success state has a readable white label", () =>
{
    var badge = UiKit.SuccessStatusBadge("Đã xác nhận");
    Require(Contrast(((Label)badge.Content!).TextColor, badge.BackgroundColor) >= 4.5,
        "Success label has insufficient contrast");
});
Check("compact badge strip retains points without crowding the member name", () =>
{
    var rows = Enumerable.Range(0, 8).Select(i => new AchievementRow(
        new TraineeAchievement { TraineeUserId = "trainee-a", Points = 20 },
        new AchievementBadge { Key = "tien_bo", Name = "Tiến bộ" }, "Minh", "U12", "Coach")).ToList();
    var summary = new TraineeAchievementSummary(rows, 365);
    var view = (FlexLayout)AchievementBadgeUi.SummaryView(summary);
    Require(view.Children.OfType<Image>().Count() <= 3, "All 8 badges compete with the name");
    Require(view.Children.OfType<Label>().Any(label => label.Text == "+5"), "Hidden badge count is missing");
    Require(summary.VisibleBadges.Count == 8 && summary.TotalPoints == 365, "Projection changed real awards or points");
});
Check("expired awards stay in personal totals but not active badges", () =>
{
    var rows = new[]
    {
        new AchievementRow(new TraineeAchievement { TraineeUserId = "a", Points = 20,
            Status = AchievementStatus.Approved, VisibleUntilUtc = DateTime.UtcNow.AddDays(2) },
            new AchievementBadge(), "A", "U12", "Coach"),
        new AchievementRow(new TraineeAchievement { TraineeUserId = "a", Points = -10,
            Status = AchievementStatus.Expired, VisibleUntilUtc = DateTime.UtcNow.AddDays(-2) },
            new AchievementBadge(), "A", "U12", "Coach"),
        new AchievementRow(new TraineeAchievement { TraineeUserId = "b", Points = 500,
            Status = AchievementStatus.Approved, VisibleUntilUtc = DateTime.UtcNow.AddDays(2) },
            new AchievementBadge(), "B", "U12", "Coach")
    };
    var summaries = AchievementBadgeUi.Summarize(new AchievementFeed(rows, 510, 0));
    Require(summaries["a"].TotalPoints == 10 && summaries["a"].VisibleBadges.Count == 1,
        "Expired/negative points or personal scoping was lost");
    Require(summaries["b"].TotalPoints == 500, "Points leaked between trainees");
});
Check("personal gallery renders every award with three bounded columns", () =>
{
    var rows = Enumerable.Range(0, 8).Select(i => new AchievementRow(
        new TraineeAchievement { Points = 20 }, new AchievementBadge { Name = "Tiến bộ" }, "A", "U12", "Coach"));
    View gallery = AchievementBadgeUi.Gallery(rows);
    Require(gallery is Grid, "Gallery needs bounded grid tracks to prevent native wrapping to two columns");
    var grid = (Grid)gallery;
    Require(grid.Children.Count == 8 && grid.ColumnDefinitions.Count == 3, "Full gallery dropped awards or columns");
    for (var i = 0; i < grid.Children.Count; i++)
    {
        var tile = (View)grid.Children[i];
        Require(Grid.GetColumn(tile) == i % 3 && Grid.GetRow(tile) == i / 3, "Award is on the wrong grid track");
    }
});
Check("tab reset returns a nested tab to its home page", () =>
{
    var root = new ContentPage { Title = "Home" };
    var navigation = new NavigationPage(root);
    navigation.PushAsync(new ContentPage { Title = "Child" }).GetAwaiter().GetResult();

    TabNavigationPolicy.ResetToHomeAsync(navigation).GetAwaiter().GetResult();

    Require(navigation.Navigation.NavigationStack.Count == 1, "Nested page remained on the tab stack");
    Require(ReferenceEquals(navigation.Navigation.NavigationStack[0], root), "Tab did not return to its original home page");
});
Check("Coach catalog includes assistant and intern positions", () =>
{
    Require(CoachPositionCatalog.Options.Any(option => option.Key == "assistant_coach"),
        "Assistant Coach position is missing");
    Require(CoachPositionCatalog.Options.Any(option => option.Key == "intern"),
        "Intern position is missing");
});
Check("Intern Coach position is unpaid", () =>
{
    Require(!CoachPositionCatalog.IsSalaryEligible("intern"),
        "Intern position must never create a salary amount");
    Require(CoachPositionCatalog.IsSalaryEligible("head_coach_manager"),
        "Paid Coach positions must remain salary eligible");
});
Console.WriteLine($"RESULT: {failed} failures");
return failed == 0 ? 0 : 1;
