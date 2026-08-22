using CommunityFootballClubManager.Models;
using System.Text.Json.Serialization;

namespace CommunityFootballClubManager.Services.Online;

public sealed class CloudUserSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string? TenantId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public UserRole Role { get; init; }
    public bool IsActive { get; init; } = true;
    public bool IsTuitionSupported { get; init; }
    public bool MustChangePassword { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CloudProfileSnapshot
{
    public string UserId { get; init; } = string.Empty;
    public string? TenantId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string PhotoObjectKey { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string CoachPosition { get; init; } = string.Empty;
    public DateOnly? DateOfBirth { get; init; }
    public double HeightCm { get; init; }
    public double WeightKg { get; init; }
    public string GuardianName { get; init; } = string.Empty;
    public string GuardianPhone { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CloudExternalAccountLinkSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public ExternalAuthProvider Provider { get; init; }
    public string ExternalSubject { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public DateTimeOffset LinkedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CloudClubSnapshot
{
    public string TenantId { get; init; } = string.Empty;
    public string TeamName { get; init; } = string.Empty;
    public string LogoObjectKey { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string BankBin { get; init; } = string.Empty;
    public string BankAccountNumber { get; init; } = string.Empty;
    public string BankAccountName { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CloudVenueSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CloudTrainingClassSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string VenueId { get; init; } = string.Empty;
    public string ManagerUserId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ScheduleDays { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; } = DateOnly.MinValue;
    public int StartTimeMinutes { get; init; }
    public int EndTimeMinutes { get; init; }
    public int TuitionSessionCount { get; init; } = 4;
    public long DefaultCycleFeeVnd { get; init; }
    public bool EvaluationRequestOpen { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CloudClassCoachAssignmentSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string ClassId { get; init; } = string.Empty;
    public string CoachUserId { get; init; } = string.Empty;
    public long SalaryPerSessionVnd { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset AssignedAt { get; init; }
}

public sealed class CloudClassEnrollmentSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string ClassId { get; init; } = string.Empty;
    public string TraineeUserId { get; init; } = string.Empty;
    public long CycleFeeVnd { get; init; }
    public bool IsTrial { get; init; }
    public int TrialSessionCount { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset EnrolledAt { get; init; }
}

public sealed class CloudTrainingSessionSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string ClassId { get; init; } = string.Empty;
    public DateOnly SessionDate { get; init; }
    public SessionStatus Status { get; init; }
    public string SubmittedByUserId { get; init; } = string.Empty;
    public DateTimeOffset? SubmittedAt { get; init; }
    public string OverrideReason { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CloudSessionCoachAssignmentSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string CoachUserId { get; init; } = string.Empty;
    public DateTimeOffset SnapshottedAt { get; init; }
}

public sealed class CloudCoachCheckInSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string CoachUserId { get; init; } = string.Empty;
    public string CheckInSelfieObjectKey { get; init; } = string.Empty;
    public string CheckOutSelfieObjectKey { get; init; } = string.Empty;
    public long SalaryPerSessionVndSnapshot { get; init; }
    public DateTimeOffset CheckedInAt { get; init; }
    public DateTimeOffset? CheckedOutAt { get; init; }
    public long DurationSeconds { get; init; }
    public CoachCheckInApprovalStatus ApprovalStatus { get; init; }
    public string ReviewedByUserId { get; init; } = string.Empty;
    public DateTimeOffset? ReviewedAt { get; init; }
    public string ReviewNote { get; init; } = string.Empty;
}

public sealed class CloudAttendanceRecordSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public string TraineeUserId { get; init; } = string.Empty;
    public AttendanceStatus Status { get; init; }
    public string RecordedByUserId { get; init; } = string.Empty;
    public DateTimeOffset RecordedAt { get; init; }
    public string Notes { get; init; } = string.Empty;
    public int Revision { get; init; } = 1;
}

public enum CloudInvoiceStatus
{
    Pending,
    ProofSubmitted,
    Paid,
    Rejected,
    Overdue,
    Waived
}

public sealed class CloudTuitionInvoiceSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string EnrollmentId { get; init; } = string.Empty;
    public string TraineeUserId { get; init; } = string.Empty;
    public string ClassId { get; init; } = string.Empty;
    public int CycleNumber { get; init; }
    public int CycleCount { get; init; } = 1;
    public long CycleFeeVnd { get; init; }
    public long AmountVnd { get; init; }
    public int AttendedSessionCount { get; init; }
    public int PlannedSessionCount { get; init; }
    public DateOnly DueDate { get; init; }
    public CloudInvoiceStatus Status { get; init; }
    public string PaymentContent { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public enum CloudPaymentProofReviewStatus
{
    Pending,
    Accepted,
    Rejected
}

public sealed class CloudPaymentProofSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string InvoiceId { get; init; } = string.Empty;
    public string ImageObjectKey { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; init; }
    public string ReviewedByUserId { get; init; } = string.Empty;
    public DateTimeOffset? ReviewedAt { get; init; }
    public CloudPaymentProofReviewStatus ReviewStatus { get; init; }
}

public sealed class CloudReceiptSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
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

public sealed class CloudCoachSalarySnapshot
{
    public string Id { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string CoachUserId { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public long AmountVnd { get; init; }
    public DateOnly DueDate { get; init; }
    public SalaryStatus Status { get; init; }
    public DateTimeOffset? PaidAt { get; init; }
    public string PaidByUserId { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class CloudNotificationSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string? TenantId { get; init; }
    public string RecipientUserId { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string RelatedEntityId { get; init; } = string.Empty;
    public bool IsRead { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class CloudAuditLogSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string? TenantId { get; init; }
    public string ActorUserId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;
    public string DetailsJson { get; init; } = "{}";
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Server-authoritative snapshot used to seed or rebuild the device cache.
/// The backend may omit collections that the authenticated role cannot read.
/// </summary>
public sealed class CloudDataSnapshot
{
    public string Cursor { get; init; } = string.Empty;
    public bool Unchanged { get; init; }
    public long SyncVersion { get; init; }
    [JsonPropertyName("serverTime")]
    public DateTimeOffset ServerTimeUtc { get; init; }
    public UserRole? Role { get; init; }
    public CloudUserSnapshot? CurrentUser { get; init; }
    public CloudProfileSnapshot? CurrentProfile { get; init; }
    public CloudClubSnapshot? Club { get; init; }
    public CloudClubSnapshot? ActiveClub { get; init; }
    public IReadOnlyList<CloudUserSnapshot> Users { get; init; } = [];
    public IReadOnlyList<CloudProfileSnapshot> Profiles { get; init; } = [];
    public IReadOnlyList<CloudVenueSnapshot> Venues { get; init; } = [];
    public IReadOnlyList<CloudTrainingClassSnapshot> Classes { get; init; } = [];
    public IReadOnlyList<CloudClassCoachAssignmentSnapshot> ClassCoaches { get; init; } = [];
    public IReadOnlyList<CloudClassEnrollmentSnapshot> ClassEnrollments { get; init; } = [];
    public IReadOnlyList<CloudTrainingSessionSnapshot> TrainingSessions { get; init; } = [];
    public IReadOnlyList<CloudSessionCoachAssignmentSnapshot> SessionCoaches { get; init; } = [];
    public IReadOnlyList<CloudCoachCheckInSnapshot> CoachCheckIns { get; init; } = [];
    public IReadOnlyList<CloudAttendanceRecordSnapshot> AttendanceRecords { get; init; } = [];
    public IReadOnlyList<CloudTuitionInvoiceSnapshot> TuitionInvoices { get; init; } = [];
    public IReadOnlyList<CloudPaymentProofSnapshot> PaymentProofs { get; init; } = [];
    public IReadOnlyList<CloudReceiptSnapshot> Receipts { get; init; } = [];
    public IReadOnlyList<CloudCoachSalarySnapshot> CoachSalaries { get; init; } = [];
    public IReadOnlyList<CloudNotificationSnapshot> Notifications { get; init; } = [];
    public IReadOnlyList<CloudAuditLogSnapshot> AuditLogs { get; init; } = [];
}

public sealed class CloudSnapshotApplyResponse
{
    public bool Applied { get; init; }
    [JsonPropertyName("serverTime")]
    public DateTimeOffset ServerTimeUtc { get; init; }
    public long SyncVersion { get; init; }
}
