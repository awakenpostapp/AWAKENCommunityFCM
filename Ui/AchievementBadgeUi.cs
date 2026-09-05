using CommunityFootballClubManager.Models;
using Microsoft.Maui.Layouts;

namespace CommunityFootballClubManager.Ui;

/// <summary>
/// Shared presentation helpers for the 21 achievement assets.  The backend
/// keeps the badge catalog and points snapshot per trainee; this class only
/// projects that role-scoped data into a compact identity strip.
/// </summary>
public static class AchievementBadgeUi
{
    public static Grid Gallery(IEnumerable<AchievementRow> rows)
    {
        var gallery = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 6, RowSpacing = 14
        };
        var index = 0;
        foreach (var row in rows)
        {
            var badge = BadgeImage(row.Badge, 88);
            badge.HorizontalOptions = LayoutOptions.Center;
            var name = UiKit.Headline(row.Badge.Name);
            name.FontSize = 14;
            name.HorizontalTextAlignment = TextAlignment.Center;
            var points = UiKit.Caption($"{row.Achievement.Points:+#;-#;0} điểm");
            points.HorizontalTextAlignment = TextAlignment.Center;
            var tile = new VerticalStackLayout
            {
                Spacing = 6,
                Children = { badge, name, points }
            };
            if (index % 3 == 0) gallery.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(tile, index % 3);
            Grid.SetRow(tile, index / 3);
            gallery.Children.Add(tile);
            index++;
        }
        return gallery;
    }

    /// <summary>Default projection used for a trainee with no awards yet.</summary>
    public static TraineeAchievementSummary EmptySummary() =>
        new([], 0);

    /// <summary>
    /// Groups the feed by trainee so each member receives their own badges and
    /// accumulated points.  When a trainee feed is filtered by the API, the
    /// aggregate returned by the API is retained even if older badges are no
    /// longer in the 30-day visible list.
    /// </summary>
    public static IReadOnlyDictionary<string, TraineeAchievementSummary> Summarize(
        AchievementFeed feed,
        string? filteredTraineeUserId = null)
    {
        var summaries = feed.Achievements
            .GroupBy(item => item.Achievement.TraineeUserId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new TraineeAchievementSummary(
                    group.Where(item => item.IsVisibleNow)
                        .OrderByDescending(item => item.Achievement.AwardedForDateUtc)
                        .ThenByDescending(item => item.Achievement.CreatedAtUtc)
                        .ToList(),
                    group.Where(item => item.RetainsPoints)
                        .Sum(item => item.Achievement.Points)),
                StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(filteredTraineeUserId))
        {
            var traineeId = filteredTraineeUserId.Trim();
            if (summaries.TryGetValue(traineeId, out var current))
            {
                // The filtered API response exposes the complete immutable
                // total separately from its visible (30-day) rows.
                summaries[traineeId] = current with { TotalPoints = feed.TotalPoints };
            }
            else
            {
                summaries[traineeId] = new TraineeAchievementSummary([], feed.TotalPoints);
            }
        }

        return summaries;
    }

    /// <summary>Maps a catalog asset key to the bundled transparent PNG.</summary>
    public static string AssetSource(AchievementBadge badge)
    {
        var key = (badge.Key ?? string.Empty).Trim().ToLowerInvariant();
        var asset = (badge.AssetKey ?? string.Empty)
            .Trim()
            .Replace('\\', '/');
        var leaf = asset.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? string.Empty;

        if (leaf.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return leaf;
        }

        if (leaf.StartsWith("achievement_badge_", StringComparison.Ordinal))
        {
            return $"{leaf}.png";
        }

        if (!string.IsNullOrWhiteSpace(leaf))
        {
            return $"achievement_badge_{leaf}.png";
        }

        return string.IsNullOrWhiteSpace(key)
            ? "icon_trophy.svg"
            : $"achievement_badge_{key}.png";
    }

    /// <summary>Creates one transparent badge image without a square frame.</summary>
    public static Image BadgeImage(AchievementBadge badge, double size = 26)
    {
        var image = new Image
        {
            Source = AssetSource(badge),
            WidthRequest = size,
            HeightRequest = size,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(image, badge.Name);
        return image;
    }

    /// <summary>
    /// Compact identity strip. Only the presentation is capped: the complete
    /// award collection and personal total remain available in the profile.
    /// </summary>
    public static View SummaryView(
        TraineeAchievementSummary? summary,
        double iconSize = 23,
        bool showTotal = true)
    {
        if (summary is null)
        {
            return new BoxView { HeightRequest = 0, IsVisible = false };
        }

        var strip = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            AlignItems = FlexAlignItems.Center,
            JustifyContent = FlexJustify.Start,
            Margin = new Thickness(0, 1, 0, 1)
        };

        foreach (var row in summary.VisibleBadges.Take(3))
        {
            var badge = BadgeImage(row.Badge, iconSize);
            badge.Margin = new Thickness(0, 0, 1, 0);
            strip.Children.Add(badge);
        }

        if (summary.VisibleBadges.Count > 3)
        {
            var more = UiKit.Caption($"+{summary.VisibleBadges.Count - 3}", UiKit.Primary);
            more.Margin = new Thickness(4, 0, 6, 0);
            SemanticProperties.SetDescription(more,
                $"Còn {summary.VisibleBadges.Count - 3} biểu trưng; mở hồ sơ để xem đầy đủ");
            strip.Children.Add(more);
        }

        if (showTotal)
        {
            var scoreColor = summary.TotalPoints >= 0 ? UiKit.Primary : UiKit.Danger;
            strip.Children.Add(UiKit.StatusBadge(
                summary.TotalPoints >= 0
                    ? $"{summary.TotalPoints:+#;-#;0} điểm"
                    : $"{summary.TotalPoints} điểm",
                scoreColor));
        }
        return strip;
    }
}
