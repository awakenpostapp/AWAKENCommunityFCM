using SQLite;

namespace CommunityFootballClubManager.Models;

public static class EntityId
{
    public static string New() => Guid.NewGuid().ToString("N");
}

[Table("UserAccounts")]
public sealed class UserAccount
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [MaxLength(80)]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Cloudflare D1 tenant that owns this cached identity.  The mobile
    /// SQLite database is a cache, but keeping the tenant boundary here lets
    /// all member queries reject stale identities from another team.
    /// </summary>
    [Indexed, MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [Indexed(Unique = true), MaxLength(80)]
    public string UsernameNormalized { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public int PasswordIterations { get; set; } = 210_000;
    public UserRole Role { get; set; }

    [Indexed, MaxLength(200)]
    public string EmailNormalized { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    // A supported trainee is exempt from tuition. This is intentionally stored
    // on the account so the exemption remains available to enrollment, invoice
    // and reminder workflows without depending on editable profile fields.
    public bool IsTuitionSupported { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("PersonProfiles")]
public sealed class PersonProfile
{
    [PrimaryKey, MaxLength(32)]
    public string UserId { get; set; } = string.Empty;

    [Indexed, MaxLength(180)]
    public string FullName { get; set; } = string.Empty;

    public string PhotoPath { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>
    /// Stable catalog key for a Coach's teaching position.  Keep the key
    /// language-neutral so it can be stored unchanged in SQLite and Cloudflare
    /// D1 while the label remains editable/localizable in the client.
    /// </summary>
    public string CoachPosition { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public double HeightCm { get; set; }
    public double WeightKg { get; set; }
    public string GuardianName { get; set; } = string.Empty;
    public string GuardianPhone { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("ExternalAccountLinks")]
public sealed class ExternalAccountLink
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed(Name = "IX_ExternalUserProvider", Order = 1, Unique = true), MaxLength(32)]
    public string UserId { get; set; } = string.Empty;

    [Indexed(Name = "IX_ExternalProviderSubject", Order = 1, Unique = true)]
    [Indexed(Name = "IX_ExternalUserProvider", Order = 2, Unique = true)]
    public ExternalAuthProvider Provider { get; set; }

    [Indexed(Name = "IX_ExternalProviderSubject", Order = 2, Unique = true), MaxLength(200)]
    public string ExternalSubjectNormalized { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("ClubProfile")]
public sealed class ClubProfile
{
    [PrimaryKey]
    public int Id { get; set; } = 1;

    public string TeamName { get; set; } = "Community Football Club";
    public string FounderName { get; set; } = string.Empty;
    public string FounderPhotoPath { get; set; } = string.Empty;
    public string LogoPath { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankBin { get; set; } = string.Empty;
    public string BankAccountNumber { get; set; } = string.Empty;
    public string BankAccountName { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("Venues")]
public sealed class Venue
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed, MaxLength(180)]
    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("TrainingClasses")]
public sealed class TrainingClass
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed, MaxLength(180)]
    public string Name { get; set; } = string.Empty;

    [Indexed, MaxLength(32)]
    public string VenueId { get; set; } = string.Empty;

    [Indexed, MaxLength(32)]
    public string ManagerUserId { get; set; } = string.Empty;

    public string ScheduleDays { get; set; } = string.Empty;
    /// <summary>First date from which the recurring class schedule is visible.</summary>
    public DateTime StartDate { get; set; } = DateTime.Today;
    public int StartTimeMinutes { get; set; } = 17 * 60;
    public int EndTimeMinutes { get; set; } = 18 * 60 + 30;
    /// <summary>
    /// Number of attended sessions represented by the class tuition amount.
    /// The actual invoice is prorated from completed attendance, so a month
    /// with an extra scheduled week is billed correctly.
    /// </summary>
    public int TuitionSessionCount { get; set; } = 4;
    public long DefaultFeeVnd { get; set; }
    /// <summary>
    /// Founder-controlled gate for the trainee evaluation workflow. Coaches
    /// may only submit or edit an evaluation while this request is open.
    /// </summary>
    public bool EvaluationRequestOpen { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("ClassCoachAssignments")]
public sealed class ClassCoachAssignment
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed(Name = "IX_ClassCoach", Order = 1, Unique = true), MaxLength(32)]
    public string ClassId { get; set; } = string.Empty;

    [Indexed(Name = "IX_ClassCoach", Order = 2, Unique = true), MaxLength(32)]
    public string CoachUserId { get; set; } = string.Empty;

    public long SalaryPerSessionVnd { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("ClassEnrollments")]
public sealed class ClassEnrollment
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed(Name = "IX_ClassTrainee", Order = 1, Unique = true), MaxLength(32)]
    public string ClassId { get; set; } = string.Empty;

    [Indexed(Name = "IX_ClassTrainee", Order = 2, Unique = true), MaxLength(32)]
    public string TraineeUserId { get; set; } = string.Empty;

    public long MonthlyFeeVnd { get; set; }
    /// <summary>
    /// Fee for one tuition cycle. MonthlyFeeVnd is kept for offline database
    /// compatibility with older Demo versions.
    /// </summary>
    public long CycleFeeVnd { get; set; }
    public bool IsTrial { get; set; }
    public int TrialSessionCount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime EnrolledAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("TrainingSessions")]
public sealed class TrainingSession
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed(Name = "IX_ClassDate", Order = 1, Unique = true), MaxLength(32)]
    public string ClassId { get; set; } = string.Empty;

    [Indexed(Name = "IX_ClassDate", Order = 2, Unique = true)]
    public DateTime SessionDate { get; set; }

    public SessionStatus Status { get; set; }
    public string SubmittedByUserId { get; set; } = string.Empty;
    public DateTime? SubmittedAtUtc { get; set; }
    public string OverrideReason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("SessionCoachAssignments")]
public sealed class SessionCoachAssignment
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed(Name = "IX_SessionCoachAssignment", Order = 1, Unique = true), MaxLength(32)]
    public string SessionId { get; set; } = string.Empty;

    [Indexed(Name = "IX_SessionCoachAssignment", Order = 2, Unique = true)]
    [Indexed(Name = "IX_SessionCoachAssignment_Coach")]
    [MaxLength(32)]
    public string CoachUserId { get; set; } = string.Empty;

    public DateTime SnapshottedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("CoachCheckIns")]
public sealed class CoachCheckIn
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed(Name = "IX_SessionCoach", Order = 1, Unique = true), MaxLength(32)]
    public string SessionId { get; set; } = string.Empty;

    [Indexed(Name = "IX_SessionCoach", Order = 2, Unique = true), MaxLength(32)]
    public string CoachUserId { get; set; } = string.Empty;

    public string SelfiePath { get; set; } = string.Empty;
    public string CheckOutSelfiePath { get; set; } = string.Empty;
    public long SalaryPerSessionVndSnapshot { get; set; } = 0;
    public DateTime CheckedInAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CheckedOutAtUtc { get; set; }
    /// <summary>
    /// Elapsed teaching time captured at check-out. An open check-in keeps
    /// this at zero; the UI derives a live value from CheckedInAtUtc until
    /// the Coach checks out.
    /// </summary>
    public long DurationSeconds { get; set; }
    public CoachCheckInApprovalStatus ApprovalStatus { get; set; } = CoachCheckInApprovalStatus.Pending;
    public string ReviewedByUserId { get; set; } = string.Empty;
    public DateTime? ReviewedAtUtc { get; set; }
    public string ReviewNote { get; set; } = string.Empty;
}

[Table("AttendanceRecords")]
public sealed class AttendanceRecord
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed(Name = "IX_SessionTrainee", Order = 1, Unique = true), MaxLength(32)]
    public string SessionId { get; set; } = string.Empty;

    [Indexed(Name = "IX_SessionTrainee", Order = 2, Unique = true), MaxLength(32)]
    public string TraineeUserId { get; set; } = string.Empty;

    public AttendanceStatus Status { get; set; }
    public string RecordedByUserId { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
    public string Notes { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
}

[Table("TuitionInvoices")]
public sealed class TuitionInvoice
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed(Name = "IX_EnrollmentPeriod", Order = 1, Unique = true), MaxLength(32)]
    public string EnrollmentId { get; set; } = string.Empty;

    [Indexed(Name = "IX_EnrollmentPeriod", Order = 2, Unique = true), MaxLength(7)]
    public string Period { get; set; } = string.Empty;

    [Indexed, MaxLength(32)]
    public string TraineeUserId { get; set; } = string.Empty;

    [Indexed, MaxLength(32)]
    public string ClassId { get; set; } = string.Empty;

    public long AmountVnd { get; set; }
    /// <summary>Number of cycles covered by this payment request.</summary>
    public int CycleCount { get; set; } = 1;
    /// <summary>Unit fee for one cycle, before multiplying by CycleCount.</summary>
    public long CycleFeeVnd { get; set; }
    /// <summary>Sequential cycle number for the enrollment.</summary>
    public int CycleNumber { get; set; }
    public int AttendedSessionCount { get; set; }
    public int PlannedSessionCount { get; set; }
    public long AmountPerSessionVnd { get; set; }
    public DateTime DueDate { get; set; }
    public InvoiceStatus Status { get; set; }
    public string PaymentContent { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("PaymentProofs")]
public sealed class PaymentProof
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed, MaxLength(32)]
    public string InvoiceId { get; set; } = string.Empty;

    public string ImagePath { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public string ReviewedByUserId { get; set; } = string.Empty;
    public DateTime? ReviewedAtUtc { get; set; }
    public bool IsAccepted { get; set; }
}

[Table("Receipts")]
public sealed class Receipt
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed(Unique = true), MaxLength(32)]
    public string InvoiceId { get; set; } = string.Empty;

    [Indexed(Unique = true), MaxLength(40)]
    public string ReceiptNumber { get; set; } = string.Empty;

    public string TeamNameSnapshot { get; set; } = string.Empty;
    public string TraineeNameSnapshot { get; set; } = string.Empty;
    public string ClassNameSnapshot { get; set; } = string.Empty;
    public string PeriodSnapshot { get; set; } = string.Empty;
    public long AmountVndSnapshot { get; set; }
    public string ConfirmedByNameSnapshot { get; set; } = string.Empty;
    public DateTime ConfirmedAtUtc { get; set; } = DateTime.UtcNow;
    public string PdfPath { get; set; } = string.Empty;
}

[Table("CoachSalaries")]
public sealed class CoachSalary
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed(Name = "IX_CoachPeriod", Order = 1, Unique = true), MaxLength(32)]
    public string CoachUserId { get; set; } = string.Empty;

    [Indexed(Name = "IX_CoachPeriod", Order = 2, Unique = true), MaxLength(7)]
    public string Period { get; set; } = string.Empty;

    public long AmountVnd { get; set; }
    public DateTime DueDate { get; set; }
    public SalaryStatus Status { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public string PaidByUserId { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("TraineeEvaluations")]
public sealed class TraineeEvaluation
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed, MaxLength(32)]
    public string ClassId { get; set; } = string.Empty;

    [Indexed, MaxLength(32)]
    public string TraineeUserId { get; set; } = string.Empty;

    [Indexed, MaxLength(32)]
    public string CoachUserId { get; set; } = string.Empty;

    public TraineeEvaluationType EvaluationType { get; set; } = TraineeEvaluationType.Periodic;
    public string Title { get; set; } = string.Empty;
    public DateTime EvaluationDateUtc { get; set; } = DateTime.UtcNow;
    public int OverallScore { get; set; }
    public int TechnicalScore { get; set; }
    public int TacticalScore { get; set; }
    public int PhysicalScore { get; set; }
    public int AttitudeScore { get; set; }
    public string Strengths { get; set; } = string.Empty;
    public string Improvements { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public TraineeEvaluationStatus Status { get; set; } = TraineeEvaluationStatus.Pending;
    public string ReviewNote { get; set; } = string.Empty;
    public string ReviewedByUserId { get; set; } = string.Empty;
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("AchievementBadges")]
public sealed class AchievementBadge
{
    [PrimaryKey, MaxLength(64)]
    public string Id { get; set; } = string.Empty;

    [Indexed(Unique = true), MaxLength(80)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(180)]
    public string Name { get; set; } = string.Empty;

    public AchievementCategory Category { get; set; }
    public string AssetKey { get; set; } = string.Empty;
    public AchievementDisplaySize DisplaySize { get; set; } = AchievementDisplaySize.Medium;
    public int Points { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("TraineeAchievements")]
public sealed class TraineeAchievement
{
    [PrimaryKey, MaxLength(64)]
    public string Id { get; set; } = EntityId.New();

    [Indexed, MaxLength(64)]
    public string TenantId { get; set; } = string.Empty;

    [Indexed, MaxLength(64)]
    public string TraineeUserId { get; set; } = string.Empty;

    [Indexed, MaxLength(64)]
    public string BadgeId { get; set; } = string.Empty;

    [Indexed, MaxLength(64)]
    public string ClassId { get; set; } = string.Empty;

    public AchievementCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime AwardedForDateUtc { get; set; } = DateTime.UtcNow.Date;
    public int Points { get; set; }
    public AchievementStatus Status { get; set; } = AchievementStatus.Pending;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string ReviewedByUserId { get; set; } = string.Empty;
    public DateTime? ReviewedAtUtc { get; set; }
    public string ReviewNote { get; set; } = string.Empty;
    public DateTime VisibleUntilUtc { get; set; } = DateTime.UtcNow.AddDays(30);
    public DateTime? RemovedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("AppNotifications")]
public sealed class AppNotification
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = EntityId.New();

    [Indexed, MaxLength(32)]
    public string RecipientUserId { get; set; } = string.Empty;

    public NotificationKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RelatedEntityId { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

[Table("AuditLogs")]
public sealed class AuditLog
{
    [PrimaryKey, MaxLength(32)]
    public string Id { get; set; } = Models.EntityId.New();

    [Indexed, MaxLength(32)]
    public string ActorUserId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
