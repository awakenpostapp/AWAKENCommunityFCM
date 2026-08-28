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
    /// Compact identity strip shown directly below a trainee's name. It wraps
    /// naturally when a trainee has earned many badges and always shows that
    /// trainee's own accumulated points.
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

        foreach (var row in summary.VisibleBadges)
        {
            var badge = BadgeImage(row.Badge, iconSize);
            badge.Margin = new Thickness(0, 0, 1, 0);
            strip.Children.Add(badge);
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
