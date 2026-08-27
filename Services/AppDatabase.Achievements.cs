using System.Globalization;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services.Online;

namespace CommunityFootballClubManager.Services;

/// <summary>
/// Dedicated achievement data access. Achievement records are deliberately
/// kept outside the broad operational snapshot so points/badges do not make
/// login and ordinary tab navigation slower as the history grows.
/// </summary>
public sealed partial class AppDatabase
{
    private static readonly IReadOnlyList<AchievementBadge> DefaultAchievementBadges =
        CreateDefaultAchievementBadges();

    public async Task<IReadOnlyList<AchievementBadge>> GetAchievementBadgesAsync(
        string actorUserId,
        AchievementCategory? category = null)
    {
        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            try
            {
                var response = await _cloudApi.GetAchievementBadgesAsync(category);
                var badges = response.Badges
                    .Where(item => item.IsActive)
                    .Select(ToAchievementBadge)
                    .OrderBy(item => item.Category)
                    .ThenBy(item => item.SortOrder)
                    .ToList();
                foreach (var badge in badges)
                {
                    Online.Upsert(Online.AchievementBadges, badge, item => item.Id == badge.Id);
                }
                return badges;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        await EnsureLocalAchievementCatalogAsync();
        var actor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanCreateAchievements(actor.Role)
            && actor.Role != UserRole.Trainee)
        {
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem biểu trưng.");
        }

        var badgesLocal = await Database.Table<AchievementBadge>()
            .Where(item => item.IsActive)
            .ToListAsync();
        return badgesLocal
            .Where(item => category is null || item.Category == category.Value)
            .OrderBy(item => item.Category)
            .ThenBy(item => item.SortOrder)
            .ToList();
    }

    public async Task<AchievementFeed> GetAchievementsAsync(
        string actorUserId,
        string? traineeUserId = null,
        string? classId = null,
        AchievementCategory? category = null,
        AchievementStatus? status = null)
    {
        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            try
            {
                var response = await _cloudApi.GetAchievementsAsync(
                    traineeUserId,
                    classId,
                    category,
                    status);
                var rows = response.Achievements
                    .Select(ToAchievementRow)
                    .OrderByDescending(item => item.Achievement.AwardedForDateUtc)
                    .ThenByDescending(item => item.Achievement.CreatedAtUtc)
                    .ToList();
                foreach (var row in rows)
                {
                    Online.Upsert(Online.AchievementBadges, row.Badge, item => item.Id == row.Badge.Id);
                    Online.Upsert(Online.TraineeAchievements, row.Achievement,
                        item => item.Id == row.Achievement.Id);
                }
                _ = actor;
                return new AchievementFeed(rows, response.TotalPoints, response.PendingCount);
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        await EnsureLocalAchievementCatalogAsync();
        var actorLocal = await RequireUserAsync(actorUserId);
        var requestedTrainee = string.IsNullOrWhiteSpace(traineeUserId)
            ? string.Empty
            : traineeUserId.Trim();
        var requestedClass = string.IsNullOrWhiteSpace(classId) ? string.Empty : classId.Trim();
        if (actorLocal.Role == UserRole.Trainee
            && !string.IsNullOrWhiteSpace(requestedTrainee)
            && requestedTrainee != actorLocal.Id)
        {
            throw new UnauthorizedAccessException("Bạn chỉ có thể xem thành tích của chính mình.");
        }

        var all = await Database.Table<TraineeAchievement>().ToListAsync();
        var expired = all
            .Where(item => item.Status == AchievementStatus.Approved
                           && item.VisibleUntilUtc < DateTime.UtcNow)
            .ToList();
        foreach (var item in expired)
        {
            item.Status = AchievementStatus.Expired;
            item.UpdatedAtUtc = DateTime.UtcNow;
            await Database.UpdateAsync(item);
        }

        var scoped = all.Where(item => string.IsNullOrWhiteSpace(actorLocal.TenantId)
                                       || item.TenantId == actorLocal.TenantId)
            .Where(item => string.IsNullOrWhiteSpace(requestedTrainee)
                           || item.TraineeUserId == requestedTrainee)
            .Where(item => string.IsNullOrWhiteSpace(requestedClass)
                           || item.ClassId == requestedClass)
            .Where(item => category is null || item.Category == category.Value)
            .ToList();

        if (actorLocal.Role == UserRole.Trainee)
        {
            scoped = scoped.Where(item => item.TraineeUserId == actorLocal.Id)
                .ToList();
        }
        else if (actorLocal.Role == UserRole.Coach)
        {
            var assignedClassIds = (await Database.Table<ClassCoachAssignment>()
                    .Where(item => item.CoachUserId == actorLocal.Id && item.IsActive)
                    .ToListAsync())
                .Select(item => item.ClassId)
                .ToHashSet(StringComparer.Ordinal);
            scoped = scoped.Where(item => item.CreatedByUserId == actorLocal.Id
                                          || assignedClassIds.Contains(item.ClassId))
                .ToList();
        }
        else if (!RoleCapabilities.IsFounderLike(actorLocal.Role))
        {
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem thành tích.");
        }

        var points = scoped
            .Where(item => item.Status is AchievementStatus.Approved
                or AchievementStatus.Removed
                or AchievementStatus.Expired)
            .Sum(item => item.Points);
        var pendingCount = RoleCapabilities.IsFounderLike(actorLocal.Role)
            ? await Database.Table<TraineeAchievement>()
                .Where(item => (string.IsNullOrWhiteSpace(actorLocal.TenantId)
                                || item.TenantId == actorLocal.TenantId)
                && item.Status == AchievementStatus.Pending)
                .CountAsync()
            : 0;

        var visibleRows = status is null
            ? scoped
            : scoped.Where(item => item.Status == status.Value).ToList();
        all = actorLocal.Role == UserRole.Trainee
            ? visibleRows.Where(item => item.Status == AchievementStatus.Approved
                                        && item.VisibleUntilUtc >= DateTime.UtcNow)
                .ToList()
            : visibleRows;

        var badges = (await Database.Table<AchievementBadge>().ToListAsync())
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId, StringComparer.Ordinal);
        var users = (await Database.Table<UserAccount>().ToListAsync())
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var classes = (await Database.Table<TrainingClass>().ToListAsync())
            .ToDictionary(item => item.Id, StringComparer.Ordinal);

        var rowsLocal = all
            .Where(item => badges.ContainsKey(item.BadgeId))
            .Select(item => new AchievementRow(
                item,
                badges[item.BadgeId],
                DisplayName(item.TraineeUserId, users, profiles, "Cầu thủ học viên"),
                string.IsNullOrWhiteSpace(item.ClassId)
                    ? string.Empty
                    : classes.GetValueOrDefault(item.ClassId)?.Name ?? "Lớp học",
                DisplayName(item.CreatedByUserId, users, profiles, "Huấn luyện viên")))
            .OrderByDescending(item => item.Achievement.AwardedForDateUtc)
            .ThenByDescending(item => item.Achievement.CreatedAtUtc)
            .ToList();
        return new AchievementFeed(rowsLocal, points, pendingCount);
    }

    public async Task<AchievementRow> CreateAchievementAsync(
        string actorUserId,
        TraineeAchievement achievement)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        if (string.IsNullOrWhiteSpace(achievement.TraineeUserId)
            || string.IsNullOrWhiteSpace(achievement.BadgeId))
        {
            throw new InvalidOperationException("Thiếu học viên hoặc biểu trưng thành tích.");
        }

        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanCreateAchievements(actor.Role))
            {
                throw new UnauthorizedAccessException("Tài khoản không có quyền thêm thành tích.");
            }

            try
            {
                var response = await _cloudApi.CreateAchievementAsync(
                    new CloudAchievementCreateRequest(
                        achievement.TraineeUserId,
                        achievement.BadgeId,
                        achievement.Category,
                        string.IsNullOrWhiteSpace(achievement.ClassId) ? null : achievement.ClassId,
                        achievement.Title,
                        achievement.EventName,
                        achievement.Reason,
                        achievement.AwardedForDateUtc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                var snapshot = response.Achievement
                    ?? throw new InvalidOperationException("Máy chủ không trả về thành tích vừa tạo.");
                var row = ToAchievementRow(snapshot);
                Online.Upsert(Online.AchievementBadges, row.Badge, item => item.Id == row.Badge.Id);
                Online.Upsert(Online.TraineeAchievements, row.Achievement,
                    item => item.Id == row.Achievement.Id);
                return row;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        await EnsureLocalAchievementCatalogAsync();
        var actorLocal = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanCreateAchievements(actorLocal.Role))
        {
            throw new UnauthorizedAccessException("Tài khoản không có quyền thêm thành tích.");
        }

        var trainee = await Database.FindAsync<UserAccount>(achievement.TraineeUserId)
                      ?? throw new InvalidOperationException("Không tìm thấy Cầu thủ học viên.");
        if (trainee.Role != UserRole.Trainee || !trainee.IsActive
            || (!string.IsNullOrWhiteSpace(actorLocal.TenantId)
                && trainee.TenantId != actorLocal.TenantId))
        {
            throw new UnauthorizedAccessException("Cầu thủ học viên không thuộc đội hiện tại.");
        }
        var badge = await Database.FindAsync<AchievementBadge>(achievement.BadgeId)
                    ?? throw new InvalidOperationException("Không tìm thấy biểu trưng thành tích.");
        if (!badge.IsActive || badge.Category != achievement.Category)
        {
            throw new InvalidOperationException("Biểu trưng không hợp lệ cho hạng mục đã chọn.");
        }

        if (actorLocal.Role == UserRole.Coach
            && string.IsNullOrWhiteSpace(achievement.Reason))
        {
            throw new InvalidOperationException("Coach phải nhập lý do thêm thành tích.");
        }

        TrainingClass? trainingClass = null;
        if (!string.IsNullOrWhiteSpace(achievement.ClassId))
        {
            trainingClass = await Database.FindAsync<TrainingClass>(achievement.ClassId)
                            ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
            var enrolled = await Database.Table<ClassEnrollment>()
                .Where(item => item.ClassId == trainingClass.Id
                               && item.TraineeUserId == trainee.Id
                               && item.IsActive)
                .CountAsync();
            if (enrolled == 0)
                throw new InvalidOperationException("Cầu thủ học viên chưa thuộc lớp học này.");

            if (actorLocal.Role == UserRole.Coach)
            {
                var assigned = await Database.Table<ClassCoachAssignment>()
                    .Where(item => item.ClassId == trainingClass.Id
                                   && item.CoachUserId == actorLocal.Id
                                   && item.IsActive)
                    .CountAsync();
                if (assigned == 0)
                    throw new UnauthorizedAccessException("Coach không được phân công vào lớp này.");
            }
        }
        else if (achievement.Category == AchievementCategory.WeeklyClassRanking
                 || actorLocal.Role == UserRole.Coach)
        {
            throw new InvalidOperationException("Hạng mục này cần chọn lớp học được phân công.");
        }

        var now = DateTime.UtcNow;
        var saved = new TraineeAchievement
        {
            Id = string.IsNullOrWhiteSpace(achievement.Id) ? EntityId.New() : achievement.Id,
            TenantId = actorLocal.TenantId,
            TraineeUserId = trainee.Id,
            BadgeId = badge.Id,
            ClassId = trainingClass?.Id ?? string.Empty,
            Category = badge.Category,
            Title = achievement.Title?.Trim() ?? string.Empty,
            EventName = achievement.EventName?.Trim() ?? string.Empty,
            Reason = achievement.Reason?.Trim() ?? string.Empty,
            AwardedForDateUtc = achievement.AwardedForDateUtc == default
                ? now.Date
                : achievement.AwardedForDateUtc.Date,
            Points = badge.Points,
            Status = actorLocal.Role == UserRole.Coach
                ? AchievementStatus.Pending
                : AchievementStatus.Approved,
            CreatedByUserId = actorLocal.Id,
            VisibleUntilUtc = now.AddDays(30),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        await Database.InsertAsync(saved);
        await AddAuditAsync(actorLocal.Id, "CreateAchievement", nameof(TraineeAchievement), saved.Id,
            saved.Reason);

        if (saved.Status == AchievementStatus.Pending)
        {
            var founders = await Database.Table<UserAccount>()
                .Where(item => (item.Role == UserRole.Founder || item.Role == UserRole.CoFounder)
                               && item.IsActive
                               && (string.IsNullOrWhiteSpace(actorLocal.TenantId)
                                   || item.TenantId == actorLocal.TenantId))
                .ToListAsync();
            var users = await UserMapAsync();
            var profiles = await ProfileMapAsync();
            var creatorName = DisplayName(actorLocal.Id, users, profiles, "Huấn luyện viên");
            var traineeName = DisplayName(trainee.Id, users, profiles, "Cầu thủ học viên");
            foreach (var founder in founders)
            {
                await AddNotificationAsync(
                    founder.Id,
                    NotificationKind.AchievementSubmitted,
                    "Có đề xuất thành tích cần duyệt",
                    $"{creatorName} đã đề xuất {badge.Name} cho {traineeName}.",
                    saved.Id,
                    writeCloud: false);
            }
        }

        var feed = await GetAchievementsAsync(actorLocal.Id, saved.TraineeUserId, saved.ClassId);
        return feed.Achievements.First(item => item.Achievement.Id == saved.Id);
    }

    public async Task<AchievementRow> ReviewAchievementAsync(
        string actorUserId,
        string achievementId,
        bool approved,
        string note = "")
    {
        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanReviewAchievements(actor.Role))
                throw new UnauthorizedAccessException("Chỉ Founder hoặc Đồng Sáng lập được duyệt thành tích.");
            try
            {
                var response = await _cloudApi.ReviewAchievementAsync(achievementId, approved, note);
                var snapshot = response.Achievement
                    ?? throw new InvalidOperationException("Máy chủ không trả về thành tích vừa duyệt.");
                var row = ToAchievementRow(snapshot);
                Online.Upsert(Online.AchievementBadges, row.Badge, item => item.Id == row.Badge.Id);
                Online.Upsert(Online.TraineeAchievements, row.Achievement,
                    item => item.Id == row.Achievement.Id);
                return row;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var founder = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanReviewAchievements(founder.Role))
            throw new UnauthorizedAccessException("Chỉ Founder hoặc Đồng Sáng lập được duyệt thành tích.");
        var current = await Database.FindAsync<TraineeAchievement>(achievementId)
                      ?? throw new InvalidOperationException("Không tìm thấy thành tích.");
        if (!string.IsNullOrWhiteSpace(founder.TenantId)
            && current.TenantId != founder.TenantId)
        {
            throw new UnauthorizedAccessException("Thành tích không thuộc đội hiện tại.");
        }
        if (current.Status != AchievementStatus.Pending)
            throw new InvalidOperationException("Thành tích này không còn chờ duyệt.");
        current.Status = approved ? AchievementStatus.Approved : AchievementStatus.Rejected;
        current.ReviewedByUserId = founder.Id;
        current.ReviewedAtUtc = DateTime.UtcNow;
        current.ReviewNote = note?.Trim() ?? string.Empty;
        current.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(current);
        await AddAuditAsync(founder.Id,
            approved ? "ApproveAchievement" : "RejectAchievement",
            nameof(TraineeAchievement), current.Id, current.ReviewNote);
        await AddNotificationAsync(
            current.TraineeUserId,
            approved ? NotificationKind.AchievementApproved : NotificationKind.AchievementRejected,
            approved ? "Thành tích đã được xác nhận" : "Đề xuất thành tích chưa được chấp nhận",
            approved ? "Founder đã xác nhận thành tích của bạn." : "Founder đã từ chối đề xuất thành tích của bạn.",
            current.Id,
            writeCloud: false);
        if (!string.IsNullOrWhiteSpace(current.CreatedByUserId)
            && current.CreatedByUserId != current.TraineeUserId
            && current.CreatedByUserId != founder.Id)
        {
            await AddNotificationAsync(
                current.CreatedByUserId,
                approved ? NotificationKind.AchievementApproved : NotificationKind.AchievementRejected,
                approved ? "Đề xuất thành tích đã được duyệt" : "Đề xuất thành tích bị từ chối",
                approved
                    ? "Founder đã xác nhận đề xuất thành tích của bạn."
                    : "Founder đã từ chối đề xuất thành tích của bạn.",
                current.Id,
                writeCloud: false);
        }

        var feed = await GetAchievementsAsync(founder.Id, current.TraineeUserId, current.ClassId);
        return feed.Achievements.First(item => item.Achievement.Id == current.Id);
    }

    public async Task RemoveAchievementAsync(string actorUserId, string achievementId)
    {
        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanRemoveAchievements(actor.Role))
                throw new UnauthorizedAccessException("Chỉ Founder được gỡ thành tích.");
            try
            {
                await _cloudApi.RemoveAchievementAsync(achievementId);
                Online.Remove(Online.TraineeAchievements, item => item.Id == achievementId);
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var founder = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanRemoveAchievements(founder.Role))
            throw new UnauthorizedAccessException("Chỉ Founder được gỡ thành tích.");
        var current = await Database.FindAsync<TraineeAchievement>(achievementId)
                      ?? throw new InvalidOperationException("Không tìm thấy thành tích.");
        if (!string.IsNullOrWhiteSpace(founder.TenantId)
            && current.TenantId != founder.TenantId)
        {
            throw new UnauthorizedAccessException("Thành tích không thuộc đội hiện tại.");
        }
        if (current.Status == AchievementStatus.Removed)
            return;
        current.Status = AchievementStatus.Removed;
        current.RemovedAtUtc = DateTime.UtcNow;
        current.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(current);
        await AddAuditAsync(founder.Id, "RemoveAchievement", nameof(TraineeAchievement), current.Id,
            "Founder gỡ thành tích; điểm vẫn được giữ lại.");
    }

    private async Task EnsureLocalAchievementCatalogAsync()
    {
        if (await Database.Table<AchievementBadge>().CountAsync() > 0)
            return;
        await Database.RunInTransactionAsync(connection =>
        {
            foreach (var badge in DefaultAchievementBadges)
            {
                connection.Insert(badge);
            }
        });
    }

    private static IReadOnlyList<AchievementBadge> CreateDefaultAchievementBadges() =>
    [
        Badge("badge_cup_ngoai_hang", "cup_ngoai_hang", "Cup Ngoại Hạng", AchievementCategory.MatchRanking, "achievement/cup_ngoai_hang", AchievementDisplaySize.Hero, 500, 10),
        Badge("badge_cup_hang_1", "cup_hang_1", "Cup Hạng 1", AchievementCategory.MatchRanking, "achievement/cup_hang_1", AchievementDisplaySize.Hero, 150, 20),
        Badge("badge_cup_hang_2", "cup_hang_2", "Cup Hạng 2", AchievementCategory.MatchRanking, "achievement/cup_hang_2", AchievementDisplaySize.Hero, 100, 30),
        Badge("badge_cup_hang_3", "cup_hang_3", "Cup Hạng 3", AchievementCategory.MatchRanking, "achievement/cup_hang_3", AchievementDisplaySize.Hero, 60, 40),
        Badge("badge_huy_chuong_vang", "huy_chuong_vang", "Huy Chương Vàng", AchievementCategory.MatchRanking, "achievement/huy_chuong_vang", AchievementDisplaySize.Hero, 150, 50),
        Badge("badge_huy_chuong_bac", "huy_chuong_bac", "Huy Chương Bạc", AchievementCategory.MatchRanking, "achievement/huy_chuong_bac", AchievementDisplaySize.Hero, 100, 60),
        Badge("badge_huy_chuong_dong", "huy_chuong_dong", "Huy Chương Đồng", AchievementCategory.MatchRanking, "achievement/huy_chuong_dong", AchievementDisplaySize.Hero, 60, 70),
        Badge("badge_gang_tay_vang", "gang_tay_vang", "Găng Tay Vàng", AchievementCategory.MatchRanking, "achievement/gang_tay_vang", AchievementDisplaySize.Hero, 100, 80),
        Badge("badge_qua_bong_vang", "qua_bong_vang", "Quả Bóng Vàng", AchievementCategory.MatchRanking, "achievement/qua_bong_vang", AchievementDisplaySize.Hero, 100, 90),
        Badge("badge_cau_thu_xuat_sac", "cau_thu_xuat_sac", "Cầu Thủ Xuất Sắc", AchievementCategory.MatchRanking, "achievement/cau_thu_xuat_sac", AchievementDisplaySize.Hero, 100, 100),
        Badge("badge_vong_nguyet_que", "vong_nguyet_que", "Vòng Nguyệt Quế", AchievementCategory.MatchRanking, "achievement/vong_nguyet_que", AchievementDisplaySize.Hero, 60, 110),
        Badge("badge_the_vang", "the_vang", "Thẻ Vàng", AchievementCategory.MatchRanking, "achievement/the_vang", AchievementDisplaySize.Compact, -10, 120),
        Badge("badge_the_do", "the_do", "Thẻ Đỏ", AchievementCategory.MatchRanking, "achievement/the_do", AchievementDisplaySize.Compact, -30, 130),
        Badge("badge_tham_gia", "tham_gia", "Tham Gia", AchievementCategory.WeeklyClassRanking, "achievement/tham_gia", AchievementDisplaySize.Medium, 10, 200),
        Badge("badge_tich_cuc", "tich_cuc", "Tích Cực", AchievementCategory.WeeklyClassRanking, "achievement/tich_cuc", AchievementDisplaySize.Medium, 15, 210),
        Badge("badge_ghi_ban", "ghi_ban", "Ghi Bàn", AchievementCategory.WeeklyClassRanking, "achievement/ghi_ban", AchievementDisplaySize.Medium, 15, 220),
        Badge("badge_giu_sach_luoi", "giu_sach_luoi", "Giữ Sạch Lưới", AchievementCategory.WeeklyClassRanking, "achievement/giu_sach_luoi", AchievementDisplaySize.Medium, 20, 230),
        Badge("badge_fair_play", "fair_play", "Fair Play", AchievementCategory.WeeklyClassRanking, "achievement/fair_play", AchievementDisplaySize.Medium, 10, 240),
        Badge("badge_tinh_than_tot", "tinh_than_tot", "Tinh Thần Tốt", AchievementCategory.WeeklyClassRanking, "achievement/tinh_than_tot", AchievementDisplaySize.Medium, 10, 250),
        Badge("badge_tien_bo", "tien_bo", "Tiến Bộ", AchievementCategory.WeeklyClassRanking, "achievement/tien_bo", AchievementDisplaySize.Medium, 20, 260),
        Badge("badge_no_luc_xuat_sac", "no_luc_xuat_sac", "Nỗ Lực Xuất Sắc", AchievementCategory.WeeklyClassRanking, "achievement/no_luc_xuat_sac", AchievementDisplaySize.Medium, 30, 270)
    ];

    private static AchievementBadge Badge(
        string id,
        string key,
        string name,
        AchievementCategory category,
        string assetKey,
        AchievementDisplaySize displaySize,
        int points,
        int sortOrder) => new()
    {
        Id = id,
        Key = key,
        Name = name,
        Category = category,
        AssetKey = assetKey,
        DisplaySize = displaySize,
        Points = points,
        SortOrder = sortOrder,
        IsActive = true
    };

    private async Task<Dictionary<string, UserAccount>> UserMapAsync() =>
        (await Database.Table<UserAccount>().ToListAsync())
        .ToDictionary(item => item.Id, StringComparer.Ordinal);

    private async Task<Dictionary<string, PersonProfile>> ProfileMapAsync() =>
        (await Database.Table<PersonProfile>().ToListAsync())
        .ToDictionary(item => item.UserId, StringComparer.Ordinal);

    private static string DisplayName(
        string userId,
        IReadOnlyDictionary<string, UserAccount> users,
        IReadOnlyDictionary<string, PersonProfile> profiles,
        string fallback)
    {
        if (profiles.GetValueOrDefault(userId) is { } profile
            && !string.IsNullOrWhiteSpace(profile.FullName))
            return profile.FullName;
        if (users.GetValueOrDefault(userId) is { } user
            && !string.IsNullOrWhiteSpace(user.Username))
            return user.Username;
        return fallback;
    }

    private static AchievementBadge ToAchievementBadge(CloudAchievementBadge source) => new()
    {
        Id = source.Id,
        Key = source.Key,
        Name = source.Name,
        Category = source.Category,
        AssetKey = source.AssetKey,
        DisplaySize = source.DisplaySize,
        Points = source.Points,
        SortOrder = source.SortOrder,
        IsActive = source.IsActive
    };

    private static AchievementRow ToAchievementRow(CloudAchievementSnapshot source)
    {
        var badge = new AchievementBadge
        {
            Id = source.BadgeId,
            Key = source.BadgeKey,
            Name = string.IsNullOrWhiteSpace(source.BadgeName)
                ? "Biểu trưng thành tích"
                : source.BadgeName,
            Category = source.Category,
            AssetKey = source.BadgeAssetKey,
            DisplaySize = source.BadgeDisplaySize,
            Points = source.Points,
            IsActive = true
        };
        var achievement = new TraineeAchievement
        {
            Id = source.Id,
            TenantId = source.TenantId,
            TraineeUserId = source.TraineeUserId,
            BadgeId = source.BadgeId,
            ClassId = source.ClassId,
            Category = source.Category,
            Title = source.Title,
            EventName = source.EventName,
            Reason = source.Reason,
            AwardedForDateUtc = UtcOrNow(source.AwardedForDate),
            Points = source.Points,
            Status = source.Status,
            CreatedByUserId = source.CreatedByUserId,
            ReviewedByUserId = source.ReviewedByUserId,
            ReviewedAtUtc = source.ReviewedAt?.UtcDateTime,
            ReviewNote = source.ReviewNote,
            VisibleUntilUtc = UtcOrNow(source.VisibleUntil),
            RemovedAtUtc = source.RemovedAt?.UtcDateTime,
            CreatedAtUtc = UtcOrNow(source.CreatedAt),
            UpdatedAtUtc = UtcOrNow(source.UpdatedAt)
        };
        return new AchievementRow(
            achievement,
            badge,
            string.IsNullOrWhiteSpace(source.TraineeName) ? "Cầu thủ học viên" : source.TraineeName,
            source.ClassName ?? string.Empty,
            string.IsNullOrWhiteSpace(source.CoachName) ? "Huấn luyện viên" : source.CoachName);
    }

    private static DateTime UtcOrNow(DateTimeOffset value) =>
        value == default ? DateTime.UtcNow : value.UtcDateTime;
}
