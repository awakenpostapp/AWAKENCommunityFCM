using CommunityFootballClubManager.Models;

namespace CommunityFootballClubManager.Services.Online;

public sealed class CloudUploadResponse
{
    public string Id { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long ByteSize { get; init; }
}

public sealed class CloudBinaryResponse
{
    public required byte[] Bytes { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";
}

public sealed class CloudCheckInResponse
{
    public string Id { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CheckedInAt { get; init; }
}

public sealed class CloudTuitionReviewResponse
{
    public string ProofId { get; init; } = string.Empty;
    public string InvoiceId { get; init; } = string.Empty;
    public bool Accepted { get; init; }
    public CloudReceiptResponse? Receipt { get; init; }
}

public sealed class CloudReceiptResponse
{
    public string Id { get; init; } = string.Empty;
    public string InvoiceId { get; init; } = string.Empty;
    public string ReceiptNumber { get; init; } = string.Empty;
    public string TeamNameSnapshot { get; init; } = string.Empty;
    public string TraineeNameSnapshot { get; init; } = string.Empty;
    public string ClassNameSnapshot { get; init; } = string.Empty;
    public string CycleSnapshot { get; init; } = string.Empty;
    public long AmountVndSnapshot { get; init; }
    public string ConfirmedByNameSnapshot { get; init; } = string.Empty;
    public DateTimeOffset ConfirmedAt { get; init; }
    public string PdfObjectKey { get; init; } = string.Empty;
}

public sealed class CloudTraineeEvaluationSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string ClassId { get; init; } = string.Empty;
    public string TraineeUserId { get; init; } = string.Empty;
    public string CoachUserId { get; init; } = string.Empty;
    public TraineeEvaluationType EvaluationType { get; init; }
    public string Title { get; init; } = string.Empty;
    public DateTimeOffset EvaluationDate { get; init; }
    public int OverallScore { get; init; }
    public int TechnicalScore { get; init; }
    public int TacticalScore { get; init; }
    public int PhysicalScore { get; init; }
    public int AttitudeScore { get; init; }
    public string Strengths { get; init; } = string.Empty;
    public string Improvements { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public TraineeEvaluationStatus Status { get; init; }
    public string ReviewNote { get; init; } = string.Empty;
    public string ReviewedByUserId { get; init; } = string.Empty;
    public DateTimeOffset? ReviewedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string TraineeName { get; init; } = string.Empty;
    public string CoachName { get; init; } = string.Empty;
    public string CoachPosition { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
}

public sealed class CloudTraineeEvaluationResponse
{
    public CloudTraineeEvaluationSnapshot? Evaluation { get; init; }
}

public sealed class CloudEvaluationRequestResponse
{
    public bool EvaluationRequestOpen { get; init; }
}

public sealed class CloudTraineeEvaluationListResponse
{
    public IReadOnlyList<CloudTraineeEvaluationSnapshot> Evaluations { get; init; } = [];
    public bool EvaluationRequestOpen { get; init; }
}

public sealed class CloudTraineeEvaluationRosterTrainee
{
    public string UserId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    public double HeightCm { get; init; }
    public double WeightKg { get; init; }
}

public sealed class CloudTraineeEvaluationRosterResponse
{
    public string ClassId { get; init; } = string.Empty;
    public bool EvaluationRequestOpen { get; init; }
    public IReadOnlyList<CloudTraineeEvaluationRosterTrainee> Trainees { get; init; } = [];
}

public sealed class CloudAchievementBadge
{
    public string Id { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public AchievementCategory Category { get; init; }
    public string AssetKey { get; init; } = string.Empty;
    public AchievementDisplaySize DisplaySize { get; init; } = AchievementDisplaySize.Medium;
    public int Points { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class CloudAchievementSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string TraineeUserId { get; init; } = string.Empty;
    public string TraineeName { get; init; } = string.Empty;
    public string BadgeId { get; init; } = string.Empty;
    public string BadgeKey { get; init; } = string.Empty;
    public string BadgeName { get; init; } = string.Empty;
    public string BadgeAssetKey { get; init; } = string.Empty;
    public AchievementDisplaySize BadgeDisplaySize { get; init; } = AchievementDisplaySize.Medium;
    public string ClassId { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public AchievementCategory Category { get; init; }
    public string Title { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public DateTimeOffset AwardedForDate { get; init; }
    public int Points { get; init; }
    public AchievementStatus Status { get; init; }
    public string CreatedByUserId { get; init; } = string.Empty;
    public string CoachName { get; init; } = string.Empty;
    public string ReviewedByUserId { get; init; } = string.Empty;
    public DateTimeOffset? ReviewedAt { get; init; }
    public string ReviewNote { get; init; } = string.Empty;
    public DateTimeOffset VisibleUntil { get; init; }
    public DateTimeOffset? RemovedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CloudAchievementResponse
{
    public CloudAchievementSnapshot? Achievement { get; init; }
}

public sealed class CloudAchievementListResponse
{
    public IReadOnlyList<CloudAchievementSnapshot> Achievements { get; init; } = [];
    public int TotalPoints { get; init; }
    public int PendingCount { get; init; }
}

public sealed class CloudAchievementBadgeListResponse
{
    public IReadOnlyList<CloudAchievementBadge> Badges { get; init; } = [];
}

public sealed record CloudAchievementCreateRequest(
    string TraineeUserId,
    string BadgeId,
    AchievementCategory Category,
    string? ClassId,
    string? Title,
    string? EventName,
    string? Reason,
    // The Worker validates this value as a date key (YYYY-MM-DD), not an
    // ISO timestamp. Keeping the wire DTO as a string prevents DateTime's
    // default JSON formatter from sending a value the API rejects.
    string? AwardedForDate);

public sealed record CloudAchievementReviewRequest(
    bool Approved,
    string? ReviewNote);
