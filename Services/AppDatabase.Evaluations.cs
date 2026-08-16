using System.Globalization;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services.Online;

namespace CommunityFootballClubManager.Services;

public sealed partial class AppDatabase
{
    public async Task<IReadOnlyList<TraineeEvaluationRow>> GetTraineeEvaluationsAsync(
        string actorUserId,
        string? traineeUserId = null,
        string? classId = null)
    {
        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(traineeUserId))
            {
                query.Add($"traineeUserId={Uri.EscapeDataString(traineeUserId)}");
            }
            if (!string.IsNullOrWhiteSpace(classId))
            {
                query.Add($"classId={Uri.EscapeDataString(classId)}");
            }

            try
            {
                var path = "evaluations" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
                var response = await _cloudApi.GetAsync<CloudTraineeEvaluationListResponse>(path);
                if (!string.IsNullOrWhiteSpace(classId)
                    && Online.Class(classId) is { } onlineClass)
                {
                    onlineClass.EvaluationRequestOpen = response.EvaluationRequestOpen;
                }
                var rows = response.Evaluations
                    .Select(item => new TraineeEvaluationRow(
                        ToEvaluation(item),
                        string.IsNullOrWhiteSpace(item.TraineeName) ? "Cầu thủ học viên" : item.TraineeName,
                        string.IsNullOrWhiteSpace(item.CoachName) ? "Huấn luyện viên" : item.CoachName,
                        string.IsNullOrWhiteSpace(item.ClassName) ? "Lớp học" : item.ClassName,
                        item.CoachPosition ?? string.Empty))
                    .OrderByDescending(item => item.Evaluation.EvaluationDateUtc)
                    .ThenByDescending(item => item.Evaluation.CreatedAtUtc)
                    .ToList();
                CacheOnlineEvaluations(rows);
                return WithPrevious(rows);
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actorLocal = await RequireUserAsync(actorUserId);
        var requestedTrainee = string.IsNullOrWhiteSpace(traineeUserId) ? null : traineeUserId.Trim();
        if (actorLocal.Role == UserRole.Trainee
            && requestedTrainee is not null
            && requestedTrainee != actorLocal.Id)
        {
            throw new UnauthorizedAccessException("Bạn chỉ có thể xem đánh giá của chính mình.");
        }

        var evaluations = await Database.Table<TraineeEvaluation>().ToListAsync();
        var allowedClassIds = actorLocal.Role switch
        {
            UserRole.Founder => null,
            UserRole.Coach => (await Database.Table<ClassCoachAssignment>()
                    .Where(item => item.CoachUserId == actorLocal.Id && item.IsActive)
                    .ToListAsync())
                .Select(item => item.ClassId)
                .ToHashSet(),
            UserRole.Trainee => (await Database.Table<ClassEnrollment>()
                    .Where(item => item.TraineeUserId == actorLocal.Id && item.IsActive)
                    .ToListAsync())
                .Select(item => item.ClassId)
                .ToHashSet(),
            _ => []
        };
        evaluations = evaluations
            .Where(item => allowedClassIds is null || allowedClassIds.Contains(item.ClassId))
            .Where(item => requestedTrainee is null || item.TraineeUserId == requestedTrainee)
            .Where(item => string.IsNullOrWhiteSpace(classId) || item.ClassId == classId)
            .Where(item => actorLocal.Role != UserRole.Trainee || item.TraineeUserId == actorLocal.Id)
            .OrderByDescending(item => item.EvaluationDateUtc)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ToList();

        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);
        var classes = (await Database.Table<TrainingClass>().ToListAsync())
            .ToDictionary(item => item.Id);
        var rowsLocal = evaluations.Select(item => new TraineeEvaluationRow(
                item,
                profiles.GetValueOrDefault(item.TraineeUserId)?.FullName ?? "Cầu thủ học viên",
                profiles.GetValueOrDefault(item.CoachUserId)?.FullName ?? "Huấn luyện viên",
                classes.GetValueOrDefault(item.ClassId)?.Name ?? "Lớp học",
                CoachPositionCatalog.Label(profiles.GetValueOrDefault(item.CoachUserId)?.CoachPosition)))
            .ToList();
        return WithPrevious(rowsLocal);
    }

    /// <summary>
    /// Returns the Founder-controlled gate for a class. The online response is
    /// intentionally fetched from the Worker so a Coach who already has an
    /// in-memory page open observes a newly opened/closed request immediately.
    /// </summary>
    public async Task<bool> IsTraineeEvaluationRequestOpenAsync(
        string actorUserId,
        string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
            return false;

        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            try
            {
                var response = await _cloudApi.GetAsync<CloudTraineeEvaluationListResponse>(
                    $"evaluations?classId={Uri.EscapeDataString(classId)}");
                if (Online.Class(classId) is { } onlineClass)
                    onlineClass.EvaluationRequestOpen = response.EvaluationRequestOpen;
                return response.EvaluationRequestOpen;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        var trainingClass = await Database.FindAsync<TrainingClass>(classId)
                            ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
        if (actor.Role == UserRole.Coach)
        {
            var assigned = await Database.Table<ClassCoachAssignment>()
                .Where(item => item.ClassId == classId && item.CoachUserId == actor.Id && item.IsActive)
                .CountAsync();
            if (assigned == 0)
                throw new UnauthorizedAccessException("Coach không được phân công vào lớp này.");
        }
        else if (actor.Role == UserRole.Trainee)
        {
            var enrolled = await Database.Table<ClassEnrollment>()
                .Where(item => item.ClassId == classId && item.TraineeUserId == actor.Id && item.IsActive)
                .CountAsync();
            if (enrolled == 0)
                throw new UnauthorizedAccessException("Bạn không thuộc lớp này.");
        }
        else if (actor.Role != UserRole.Founder)
        {
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem yêu cầu đánh giá.");
        }
        return trainingClass.EvaluationRequestOpen;
    }

    /// <summary>
    /// Loads the limited roster details needed by the Coach evaluation page.
    /// This is intentionally separate from the attendance roster grant: a
    /// Founder-open evaluation request authorizes only name, birth date,
    /// height and weight for the assigned class.
    /// </summary>
    public async Task<IReadOnlyList<TraineeEvaluationRosterRow>>
        GetTraineeEvaluationRosterAsync(string actorUserId, string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
            throw new InvalidOperationException("Thiếu lớp học cần xem đánh giá.");

        if (IsOnline)
        {
            await RequireOnlineRoleAsync(actorUserId, UserRole.Coach);
            try
            {
                var response = await _cloudApi.GetAsync<CloudTraineeEvaluationRosterResponse>(
                    $"evaluations/roster?classId={Uri.EscapeDataString(classId)}");
                if (!response.EvaluationRequestOpen)
                    return [];
                return response.Trainees
                    .Where(item => !string.IsNullOrWhiteSpace(item.UserId))
                    .Select(item => new TraineeEvaluationRosterRow(
                        item.UserId,
                        string.IsNullOrWhiteSpace(item.FullName)
                            ? "Cầu thủ học viên"
                            : item.FullName,
                        item.DateOfBirth is { } date
                            ? date.ToDateTime(TimeOnly.MinValue)
                            : null,
                        item.HeightCm,
                        item.WeightKg))
                    .ToList();
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireRoleAsync(actorUserId, UserRole.Coach);
        var trainingClass = await Database.FindAsync<TrainingClass>(classId)
                            ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
        if (!trainingClass.EvaluationRequestOpen)
            return [];

        var assigned = await Database.Table<ClassCoachAssignment>()
            .Where(item => item.ClassId == classId
                           && item.CoachUserId == actor.Id
                           && item.IsActive)
            .CountAsync();
        if (assigned == 0)
            throw new UnauthorizedAccessException("Coach không được phân công vào lớp này.");

        var enrollments = await Database.Table<ClassEnrollment>()
            .Where(item => item.ClassId == classId && item.IsActive)
            .ToListAsync();
        var users = (await Database.Table<UserAccount>().ToListAsync())
            .Where(item => item.Role == UserRole.Trainee && item.IsActive)
            .ToDictionary(item => item.Id);
        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);
        return enrollments
            .Where(item => users.ContainsKey(item.TraineeUserId))
            .Select(item =>
            {
                var profile = profiles.GetValueOrDefault(item.TraineeUserId)
                              ?? new PersonProfile { UserId = item.TraineeUserId };
                return new TraineeEvaluationRosterRow(
                    item.TraineeUserId,
                    string.IsNullOrWhiteSpace(profile.FullName)
                        ? users[item.TraineeUserId].Username
                        : profile.FullName,
                    profile.DateOfBirth,
                    profile.HeightCm,
                    profile.WeightKg);
            })
            .OrderBy(item => item.FullName)
            .ToList();
    }

    /// <summary>Founder opens or closes the evaluation request for one class.</summary>
    public async Task SetTraineeEvaluationRequestAsync(
        string actorUserId,
        string classId,
        bool isOpen)
    {
        if (string.IsNullOrWhiteSpace(classId))
            throw new InvalidOperationException("Thiếu lớp học cần mở yêu cầu đánh giá.");

        if (IsOnline)
        {
            var actor = await RequireOnlineRoleAsync(actorUserId, UserRole.Founder);
            await EnsureOnlineSnapshotAsync();
            var trainingClass = Online.Class(classId)
                                ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
            trainingClass.EvaluationRequestOpen = isOpen;
            trainingClass.UpdatedAtUtc = DateTime.UtcNow;
            await PushOnlineDeltaAsync(actor, classes: new[] { trainingClass });
            Online.Upsert(Online.Classes, trainingClass, item => item.Id == trainingClass.Id);
            return;
        }

        await InitializeAsync();
        await RequireRoleAsync(actorUserId, UserRole.Founder);
        await EnsureCloudWriteReadyAsync(actorUserId);
        var localClass = await Database.FindAsync<TrainingClass>(classId)
                         ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
        localClass.EvaluationRequestOpen = isOpen;
        localClass.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(localClass);
        await AddAuditAsync(actorUserId,
            isOpen ? "OpenTraineeEvaluationRequest" : "CloseTraineeEvaluationRequest",
            nameof(TrainingClass), classId, localClass.Name);
        await PushCloudMutationAsync(actorUserId, classes: new[] { localClass });
    }

    public async Task<TraineeEvaluation> SaveTraineeEvaluationAsync(
        string actorUserId,
        TraineeEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        if (string.IsNullOrWhiteSpace(evaluation.ClassId)
            || string.IsNullOrWhiteSpace(evaluation.TraineeUserId))
        {
            throw new InvalidOperationException("Thiếu lớp học hoặc học viên cần đánh giá.");
        }
        if (evaluation.OverallScore is < 1 or > 5)
        {
            throw new InvalidOperationException("Điểm tổng quan phải từ 1 đến 5.");
        }

        if (IsOnline)
        {
            var actor = await RequireOnlineRoleAsync(actorUserId, UserRole.Coach);
            try
            {
                var request = new
                {
                    classId = evaluation.ClassId,
                    traineeUserId = evaluation.TraineeUserId,
                    evaluationType = EvaluationTypeKey(evaluation.EvaluationType),
                    title = evaluation.Title?.Trim() ?? string.Empty,
                    evaluationDate = evaluation.EvaluationDateUtc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    overallScore = evaluation.OverallScore,
                    technicalScore = Math.Clamp(evaluation.TechnicalScore, 0, 5),
                    tacticalScore = Math.Clamp(evaluation.TacticalScore, 0, 5),
                    physicalScore = Math.Clamp(evaluation.PhysicalScore, 0, 5),
                    attitudeScore = Math.Clamp(evaluation.AttitudeScore, 0, 5),
                    strengths = evaluation.Strengths?.Trim() ?? string.Empty,
                    improvements = evaluation.Improvements?.Trim() ?? string.Empty,
                    notes = evaluation.Notes?.Trim() ?? string.Empty
                };
                CloudTraineeEvaluationResponse response;
                if (string.IsNullOrWhiteSpace(evaluation.Id))
                {
                    response = await _cloudApi.PostAsync<object, CloudTraineeEvaluationResponse>(
                        "evaluations", request, EntityId.New());
                }
                else
                {
                    response = await _cloudApi.PatchAsync<object, CloudTraineeEvaluationResponse>(
                        $"evaluations/{Uri.EscapeDataString(evaluation.Id)}", request, EntityId.New());
                }

                var saved = response.Evaluation is null
                    ? evaluation
                    : ToEvaluation(response.Evaluation);
                saved.CoachUserId = actor.Id;
                Online.Upsert(Online.TraineeEvaluations, saved, item => item.Id == saved.Id);
                return saved;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actorLocal = await RequireRoleAsync(actorUserId, UserRole.Coach);
        await EnsureClassAccessAsync(actorLocal, evaluation.ClassId, writeAttendance: false);
        var localClass = await Database.FindAsync<TrainingClass>(evaluation.ClassId)
                         ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
        if (!localClass.EvaluationRequestOpen)
        {
            throw new InvalidOperationException("Founder chưa mở yêu cầu đánh giá cho lớp này.");
        }
        var enrollment = await Database.Table<ClassEnrollment>()
            .Where(item => item.ClassId == evaluation.ClassId
                           && item.TraineeUserId == evaluation.TraineeUserId
                           && item.IsActive)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Học viên không thuộc lớp này.");
        _ = enrollment;
        var existing = string.IsNullOrWhiteSpace(evaluation.Id)
            ? null
            : await Database.FindAsync<TraineeEvaluation>(evaluation.Id);
        if (existing?.Status == TraineeEvaluationStatus.Approved)
        {
            throw new InvalidOperationException("Đánh giá đã được Founder xác nhận và không thể chỉnh sửa.");
        }
        var savedLocal = existing ?? new TraineeEvaluation
        {
            Id = EntityId.New(),
            ClassId = evaluation.ClassId,
            TraineeUserId = evaluation.TraineeUserId,
            CoachUserId = actorLocal.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        CopyEvaluationFields(savedLocal, evaluation);
        savedLocal.CoachUserId = actorLocal.Id;
        savedLocal.Status = TraineeEvaluationStatus.Pending;
        savedLocal.ReviewNote = string.Empty;
        savedLocal.ReviewedByUserId = string.Empty;
        savedLocal.ReviewedAtUtc = null;
        savedLocal.UpdatedAtUtc = DateTime.UtcNow;
        if (existing is null)
        {
            await Database.InsertAsync(savedLocal);
        }
        else
        {
            await Database.UpdateAsync(savedLocal);
        }
        await AddAuditAsync(actorLocal.Id, existing is null ? "CreateTraineeEvaluation" : "UpdateTraineeEvaluation",
            nameof(TraineeEvaluation), savedLocal.Id, savedLocal.TraineeUserId);
        return savedLocal;
    }

    public async Task<TraineeEvaluation> ReviewTraineeEvaluationAsync(
        string actorUserId,
        string evaluationId,
        bool approved,
        string note = "")
    {
        if (IsOnline)
        {
            await RequireOnlineRoleAsync(actorUserId, UserRole.Founder);
            try
            {
                var response = await _cloudApi.PatchAsync<object, CloudTraineeEvaluationResponse>(
                    $"evaluations/{Uri.EscapeDataString(evaluationId)}/review",
                    new { approved, note = note?.Trim() ?? string.Empty },
                    EntityId.New());
                var reviewed = response.Evaluation is null
                    ? Online.TraineeEvaluations.FirstOrDefault(item => item.Id == evaluationId)
                    : ToEvaluation(response.Evaluation);
                if (reviewed is null)
                    throw new InvalidOperationException("Máy chủ không trả về đánh giá vừa xác nhận.");
                Online.Upsert(Online.TraineeEvaluations, reviewed, item => item.Id == reviewed.Id);
                return reviewed;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var founder = await RequireRoleAsync(actorUserId, UserRole.Founder);
        var current = await Database.FindAsync<TraineeEvaluation>(evaluationId)
                      ?? throw new InvalidOperationException("Không tìm thấy đánh giá học viên.");
        if (current.Status == TraineeEvaluationStatus.Approved)
        {
            return current;
        }
        current.Status = approved ? TraineeEvaluationStatus.Approved : TraineeEvaluationStatus.Rejected;
        current.ReviewNote = note?.Trim() ?? string.Empty;
        current.ReviewedByUserId = founder.Id;
        current.ReviewedAtUtc = DateTime.UtcNow;
        current.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(current);
        await AddAuditAsync(founder.Id,
            approved ? "ApproveTraineeEvaluation" : "RejectTraineeEvaluation",
            nameof(TraineeEvaluation), evaluationId, current.ReviewNote);
        return current;
    }

    private void CacheOnlineEvaluations(IEnumerable<TraineeEvaluationRow> rows)
    {
        foreach (var row in rows)
        {
            Online.Upsert(Online.TraineeEvaluations, row.Evaluation, item => item.Id == row.Evaluation.Id);
        }
    }

    private static IReadOnlyList<TraineeEvaluationRow> WithPrevious(
        IReadOnlyList<TraineeEvaluationRow> rows)
    {
        return rows.Select((row, index) => row with
        {
            Previous = rows
                .Skip(index + 1)
                .FirstOrDefault(previous => previous.Evaluation.TraineeUserId == row.Evaluation.TraineeUserId)
                ?.Evaluation
        }).ToList();
    }

    private static TraineeEvaluation ToEvaluation(CloudTraineeEvaluationSnapshot source) => new()
    {
        Id = source.Id,
        ClassId = source.ClassId,
        TraineeUserId = source.TraineeUserId,
        CoachUserId = source.CoachUserId,
        EvaluationType = source.EvaluationType,
        Title = source.Title,
        EvaluationDateUtc = source.EvaluationDate == default
            ? DateTime.UtcNow.Date
            : source.EvaluationDate.UtcDateTime,
        OverallScore = source.OverallScore,
        TechnicalScore = source.TechnicalScore,
        TacticalScore = source.TacticalScore,
        PhysicalScore = source.PhysicalScore,
        AttitudeScore = source.AttitudeScore,
        Strengths = source.Strengths,
        Improvements = source.Improvements,
        Notes = source.Notes,
        Status = source.Status,
        ReviewNote = source.ReviewNote,
        ReviewedByUserId = source.ReviewedByUserId,
        ReviewedAtUtc = source.ReviewedAt?.UtcDateTime,
        CreatedAtUtc = source.CreatedAt == default ? DateTime.UtcNow : source.CreatedAt.UtcDateTime,
        UpdatedAtUtc = source.UpdatedAt == default ? DateTime.UtcNow : source.UpdatedAt.UtcDateTime
    };

    private static void CopyEvaluationFields(TraineeEvaluation target, TraineeEvaluation source)
    {
        target.EvaluationType = source.EvaluationType;
        target.Title = source.Title?.Trim() ?? string.Empty;
        target.EvaluationDateUtc = source.EvaluationDateUtc == default ? DateTime.UtcNow : source.EvaluationDateUtc.ToUniversalTime();
        target.OverallScore = source.OverallScore;
        target.TechnicalScore = Math.Clamp(source.TechnicalScore, 0, 5);
        target.TacticalScore = Math.Clamp(source.TacticalScore, 0, 5);
        target.PhysicalScore = Math.Clamp(source.PhysicalScore, 0, 5);
        target.AttitudeScore = Math.Clamp(source.AttitudeScore, 0, 5);
        target.Strengths = source.Strengths?.Trim() ?? string.Empty;
        target.Improvements = source.Improvements?.Trim() ?? string.Empty;
        target.Notes = source.Notes?.Trim() ?? string.Empty;
    }

    private static string EvaluationTypeKey(TraineeEvaluationType type) => type switch
    {
        TraineeEvaluationType.TournamentMatch => "tournament_match",
        _ => "periodic"
    };
}
