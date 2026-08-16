using CommunityFootballClubManager.Models;

namespace CommunityFootballClubManager.Services.Online;

/// <summary>
/// Local entities represented by a cloud snapshot. Tenant identifiers on the
/// transport rows are also copied to cached UserAccount identities so the
/// client can enforce the active team boundary when rendering local data.
/// </summary>
public sealed class CloudSnapshotEntityCollections
{
    public string Cursor { get; init; } = string.Empty;
    public long SyncVersion { get; init; }
    public DateTimeOffset ServerTimeUtc { get; init; }
    public UserRole? Role { get; init; }
    public string? TenantId { get; init; }
    public string? ClubTenantId { get; init; }
    public string? ActiveClubTenantId { get; init; }
    public UserAccount? CurrentUser { get; init; }
    public PersonProfile? CurrentProfile { get; init; }
    public ClubProfile? Club { get; init; }
    public ClubProfile? ActiveClub { get; init; }
    public IReadOnlyList<UserAccount> Users { get; init; } = [];
    public IReadOnlyList<PersonProfile> Profiles { get; init; } = [];
    public IReadOnlyList<ExternalAccountLink> ExternalAccountLinks { get; init; } = [];
    public IReadOnlyList<Venue> Venues { get; init; } = [];
    public IReadOnlyList<TrainingClass> Classes { get; init; } = [];
    public IReadOnlyList<ClassCoachAssignment> ClassCoaches { get; init; } = [];
    public IReadOnlyList<ClassEnrollment> ClassEnrollments { get; init; } = [];
    public IReadOnlyList<TrainingSession> TrainingSessions { get; init; } = [];
    public IReadOnlyList<SessionCoachAssignment> SessionCoaches { get; init; } = [];
    public IReadOnlyList<CoachCheckIn> CoachCheckIns { get; init; } = [];
    public IReadOnlyList<AttendanceRecord> AttendanceRecords { get; init; } = [];
    public IReadOnlyList<TuitionInvoice> TuitionInvoices { get; init; } = [];
    public IReadOnlyList<PaymentProof> PaymentProofs { get; init; } = [];
    public IReadOnlyList<Receipt> Receipts { get; init; } = [];
    public IReadOnlyList<CoachSalary> CoachSalaries { get; init; } = [];
    public IReadOnlyList<AppNotification> Notifications { get; init; } = [];
    public IReadOnlyList<AuditLog> AuditLogs { get; init; } = [];
}

/// <summary>
/// Converts the existing single-device entities to and from the online API
/// contract without ever putting local password verifiers in a snapshot.
/// </summary>
public static class CloudSnapshotMapper
{
    public static CloudUserSnapshot ToCloud(
        UserAccount source,
        string? tenantId = null,
        string? email = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudUserSnapshot
        {
            Id = Text(source.Id),
            TenantId = Tenant(tenantId),
            Username = Text(source.Username),
            Email = string.IsNullOrWhiteSpace(email)
                ? Text(source.EmailNormalized)
                : email.Trim(),
            Role = source.Role,
            IsActive = source.IsActive,
            IsTuitionSupported = source.IsTuitionSupported,
            MustChangePassword = source.MustChangePassword,
            CreatedAt = ToUtc(source.CreatedAtUtc),
            UpdatedAt = ToUtc(source.UpdatedAtUtc)
        };
    }

    public static UserAccount ToEntity(CloudUserSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new UserAccount
        {
            Id = Text(source.Id),
            Username = Text(source.Username),
            TenantId = Tenant(source.TenantId) ?? string.Empty,
            UsernameNormalized = Normalize(source.Username),
            // Password verifiers are server-only. A cache importer must merge
            // an existing verifier explicitly if legacy offline login remains enabled.
            PasswordHash = string.Empty,
            PasswordSalt = string.Empty,
            Role = source.Role,
            EmailNormalized = Normalize(source.Email),
            IsActive = source.IsActive,
            IsTuitionSupported = source.IsTuitionSupported,
            MustChangePassword = source.MustChangePassword,
            FailedLoginCount = 0,
            LockoutUntilUtc = null,
            CreatedAtUtc = FromUtc(source.CreatedAt),
            UpdatedAtUtc = FromUtc(source.UpdatedAt)
        };
    }

    public static CloudProfileSnapshot ToCloud(PersonProfile source, string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudProfileSnapshot
        {
            UserId = Text(source.UserId),
            TenantId = Tenant(tenantId),
            FullName = Text(source.FullName),
            PhotoObjectKey = Text(source.PhotoPath),
            Phone = Text(source.Phone),
            Email = Text(source.Email),
            CoachPosition = Text(source.CoachPosition),
            DateOfBirth = source.DateOfBirth is { } value
                ? DateOnly.FromDateTime(value)
                : null,
            HeightCm = source.HeightCm,
            WeightKg = source.WeightKg,
            GuardianName = Text(source.GuardianName),
            GuardianPhone = Text(source.GuardianPhone),
            UpdatedAt = ToUtc(source.UpdatedAtUtc)
        };
    }

    public static PersonProfile ToEntity(CloudProfileSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new PersonProfile
        {
            UserId = Text(source.UserId),
            FullName = Text(source.FullName),
            PhotoPath = Text(source.PhotoObjectKey),
            Phone = Text(source.Phone),
            Email = Text(source.Email),
            CoachPosition = Text(source.CoachPosition),
            DateOfBirth = source.DateOfBirth is { } value
                ? FromDate(value)
                : null,
            HeightCm = source.HeightCm,
            WeightKg = source.WeightKg,
            GuardianName = Text(source.GuardianName),
            GuardianPhone = Text(source.GuardianPhone),
            UpdatedAtUtc = FromUtc(source.UpdatedAt)
        };
    }

    public static CloudExternalAccountLinkSnapshot ToCloud(ExternalAccountLink source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudExternalAccountLinkSnapshot
        {
            Id = Text(source.Id),
            UserId = Text(source.UserId),
            Provider = source.Provider,
            ExternalSubject = Text(source.ExternalSubjectNormalized),
            Email = Text(source.Email),
            DisplayName = Text(source.DisplayName),
            LinkedAt = ToUtc(source.LinkedAtUtc),
            UpdatedAt = ToUtc(source.UpdatedAtUtc)
        };
    }

    public static ExternalAccountLink ToEntity(CloudExternalAccountLinkSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ExternalAccountLink
        {
            Id = Text(source.Id),
            UserId = Text(source.UserId),
            Provider = source.Provider,
            ExternalSubjectNormalized = Text(source.ExternalSubject),
            Email = Text(source.Email),
            DisplayName = Text(source.DisplayName),
            LinkedAtUtc = FromUtc(source.LinkedAt),
            UpdatedAtUtc = FromUtc(source.UpdatedAt)
        };
    }

    public static CloudClubSnapshot ToCloud(ClubProfile source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudClubSnapshot
        {
            TenantId = RequiredTenant(tenantId),
            TeamName = Text(source.TeamName),
            LogoObjectKey = Text(source.LogoPath),
            Phone = Text(source.Phone),
            Email = Text(source.Email),
            BankName = Text(source.BankName),
            BankBin = Text(source.BankBin),
            BankAccountNumber = Text(source.BankAccountNumber),
            BankAccountName = Text(source.BankAccountName),
            UpdatedAt = ToUtc(source.UpdatedAtUtc)
        };
    }

    public static ClubProfile ToEntity(CloudClubSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ClubProfile
        {
            Id = 1,
            TeamName = Text(source.TeamName),
            FounderName = string.Empty,
            FounderPhotoPath = string.Empty,
            LogoPath = Text(source.LogoObjectKey),
            Phone = Text(source.Phone),
            Email = Text(source.Email),
            BankName = Text(source.BankName),
            BankBin = Text(source.BankBin),
            BankAccountNumber = Text(source.BankAccountNumber),
            BankAccountName = Text(source.BankAccountName),
            UpdatedAtUtc = FromUtc(source.UpdatedAt)
        };
    }

    public static CloudVenueSnapshot ToCloud(Venue source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudVenueSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            Name = Text(source.Name),
            Address = Text(source.Address),
            Notes = Text(source.Notes),
            IsActive = source.IsActive,
            CreatedAt = ToUtc(source.CreatedAtUtc),
            UpdatedAt = ToUtc(source.UpdatedAtUtc)
        };
    }

    public static Venue ToEntity(CloudVenueSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Venue
        {
            Id = Text(source.Id),
            Name = Text(source.Name),
            Address = Text(source.Address),
            Notes = Text(source.Notes),
            IsActive = source.IsActive,
            CreatedAtUtc = FromUtc(source.CreatedAt),
            UpdatedAtUtc = FromUtc(source.UpdatedAt)
        };
    }

    public static CloudTrainingClassSnapshot ToCloud(TrainingClass source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudTrainingClassSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            VenueId = Text(source.VenueId),
            Name = Text(source.Name),
            ScheduleDays = Text(source.ScheduleDays),
            StartDate = DateOnly.FromDateTime(source.StartDate.Date),
            StartTimeMinutes = source.StartTimeMinutes,
            EndTimeMinutes = source.EndTimeMinutes,
            TuitionSessionCount = source.TuitionSessionCount,
            DefaultCycleFeeVnd = source.DefaultFeeVnd,
            EvaluationRequestOpen = source.EvaluationRequestOpen,
            IsActive = source.IsActive,
            CreatedAt = ToUtc(source.CreatedAtUtc),
            UpdatedAt = ToUtc(source.UpdatedAtUtc)
        };
    }

    public static TrainingClass ToEntity(CloudTrainingClassSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new TrainingClass
        {
            Id = Text(source.Id),
            VenueId = Text(source.VenueId),
            Name = Text(source.Name),
            ScheduleDays = Text(source.ScheduleDays),
            StartDate = source.StartDate == default
                ? DateTime.Today
                : FromDate(source.StartDate),
            StartTimeMinutes = source.StartTimeMinutes,
            EndTimeMinutes = source.EndTimeMinutes,
            TuitionSessionCount = source.TuitionSessionCount,
            DefaultFeeVnd = source.DefaultCycleFeeVnd,
            EvaluationRequestOpen = source.EvaluationRequestOpen,
            IsActive = source.IsActive,
            CreatedAtUtc = FromUtc(source.CreatedAt),
            UpdatedAtUtc = FromUtc(source.UpdatedAt)
        };
    }

    public static CloudClassCoachAssignmentSnapshot ToCloud(
        ClassCoachAssignment source,
        string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudClassCoachAssignmentSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            ClassId = Text(source.ClassId),
            CoachUserId = Text(source.CoachUserId),
            SalaryPerSessionVnd = source.SalaryPerSessionVnd,
            IsActive = source.IsActive,
            AssignedAt = ToUtc(source.AssignedAtUtc)
        };
    }

    public static ClassCoachAssignment ToEntity(CloudClassCoachAssignmentSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ClassCoachAssignment
        {
            Id = Text(source.Id),
            ClassId = Text(source.ClassId),
            CoachUserId = Text(source.CoachUserId),
            SalaryPerSessionVnd = source.SalaryPerSessionVnd,
            IsActive = source.IsActive,
            AssignedAtUtc = FromUtc(source.AssignedAt)
        };
    }

    public static CloudClassEnrollmentSnapshot ToCloud(ClassEnrollment source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudClassEnrollmentSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            ClassId = Text(source.ClassId),
            TraineeUserId = Text(source.TraineeUserId),
            CycleFeeVnd = source.CycleFeeVnd > 0
                ? source.CycleFeeVnd
                : source.MonthlyFeeVnd,
            IsTrial = source.IsTrial,
            TrialSessionCount = source.TrialSessionCount,
            IsActive = source.IsActive,
            EnrolledAt = ToUtc(source.EnrolledAtUtc)
        };
    }

    public static ClassEnrollment ToEntity(CloudClassEnrollmentSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ClassEnrollment
        {
            Id = Text(source.Id),
            ClassId = Text(source.ClassId),
            TraineeUserId = Text(source.TraineeUserId),
            MonthlyFeeVnd = source.CycleFeeVnd,
            CycleFeeVnd = source.CycleFeeVnd,
            IsTrial = source.IsTrial,
            TrialSessionCount = source.TrialSessionCount,
            IsActive = source.IsActive,
            EnrolledAtUtc = FromUtc(source.EnrolledAt)
        };
    }

    public static CloudTrainingSessionSnapshot ToCloud(TrainingSession source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudTrainingSessionSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            ClassId = Text(source.ClassId),
            SessionDate = DateOnly.FromDateTime(source.SessionDate),
            Status = source.Status,
            SubmittedByUserId = Text(source.SubmittedByUserId),
            SubmittedAt = ToUtc(source.SubmittedAtUtc),
            OverrideReason = Text(source.OverrideReason),
            CreatedAt = ToUtc(source.CreatedAtUtc),
            UpdatedAt = ToUtc(source.UpdatedAtUtc)
        };
    }

    public static TrainingSession ToEntity(CloudTrainingSessionSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new TrainingSession
        {
            Id = Text(source.Id),
            ClassId = Text(source.ClassId),
            SessionDate = FromDate(source.SessionDate),
            Status = source.Status,
            SubmittedByUserId = Text(source.SubmittedByUserId),
            SubmittedAtUtc = FromUtc(source.SubmittedAt),
            OverrideReason = Text(source.OverrideReason),
            CreatedAtUtc = FromUtc(source.CreatedAt),
            UpdatedAtUtc = FromUtc(source.UpdatedAt)
        };
    }

    public static CloudSessionCoachAssignmentSnapshot ToCloud(
        SessionCoachAssignment source,
        string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudSessionCoachAssignmentSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            SessionId = Text(source.SessionId),
            CoachUserId = Text(source.CoachUserId),
            SnapshottedAt = ToUtc(source.SnapshottedAtUtc)
        };
    }

    public static SessionCoachAssignment ToEntity(CloudSessionCoachAssignmentSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new SessionCoachAssignment
        {
            Id = Text(source.Id),
            SessionId = Text(source.SessionId),
            CoachUserId = Text(source.CoachUserId),
            SnapshottedAtUtc = FromUtc(source.SnapshottedAt)
        };
    }

    public static CloudCoachCheckInSnapshot ToCloud(CoachCheckIn source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudCoachCheckInSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            SessionId = Text(source.SessionId),
            CoachUserId = Text(source.CoachUserId),
            CheckInSelfieObjectKey = Text(source.SelfiePath),
            CheckOutSelfieObjectKey = Text(source.CheckOutSelfiePath),
            SalaryPerSessionVndSnapshot = source.SalaryPerSessionVndSnapshot,
            CheckedInAt = ToUtc(source.CheckedInAtUtc),
            CheckedOutAt = ToUtc(source.CheckedOutAtUtc),
            DurationSeconds = source.DurationSeconds,
            ApprovalStatus = source.ApprovalStatus,
            ReviewedByUserId = Text(source.ReviewedByUserId),
            ReviewedAt = ToUtc(source.ReviewedAtUtc),
            ReviewNote = Text(source.ReviewNote)
        };
    }

    public static CoachCheckIn ToEntity(CloudCoachCheckInSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CoachCheckIn
        {
            Id = Text(source.Id),
            SessionId = Text(source.SessionId),
            CoachUserId = Text(source.CoachUserId),
            SelfiePath = Text(source.CheckInSelfieObjectKey),
            CheckOutSelfiePath = Text(source.CheckOutSelfieObjectKey),
            SalaryPerSessionVndSnapshot = source.SalaryPerSessionVndSnapshot,
            CheckedInAtUtc = FromUtc(source.CheckedInAt),
            CheckedOutAtUtc = FromUtc(source.CheckedOutAt),
            DurationSeconds = source.DurationSeconds,
            ApprovalStatus = source.ApprovalStatus,
            ReviewedByUserId = Text(source.ReviewedByUserId),
            ReviewedAtUtc = FromUtc(source.ReviewedAt),
            ReviewNote = Text(source.ReviewNote)
        };
    }

    public static CloudAttendanceRecordSnapshot ToCloud(AttendanceRecord source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudAttendanceRecordSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            SessionId = Text(source.SessionId),
            TraineeUserId = Text(source.TraineeUserId),
            Status = source.Status,
            RecordedByUserId = Text(source.RecordedByUserId),
            RecordedAt = ToUtc(source.RecordedAtUtc),
            Notes = Text(source.Notes),
            Revision = source.Revision
        };
    }

    public static AttendanceRecord ToEntity(CloudAttendanceRecordSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AttendanceRecord
        {
            Id = Text(source.Id),
            SessionId = Text(source.SessionId),
            TraineeUserId = Text(source.TraineeUserId),
            Status = source.Status,
            RecordedByUserId = Text(source.RecordedByUserId),
            RecordedAtUtc = FromUtc(source.RecordedAt),
            Notes = Text(source.Notes),
            Revision = source.Revision
        };
    }

    public static CloudTuitionInvoiceSnapshot ToCloud(TuitionInvoice source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudTuitionInvoiceSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            EnrollmentId = Text(source.EnrollmentId),
            TraineeUserId = Text(source.TraineeUserId),
            ClassId = Text(source.ClassId),
            CycleNumber = source.CycleNumber,
            CycleCount = source.CycleCount,
            CycleFeeVnd = source.CycleFeeVnd,
            AmountVnd = source.AmountVnd,
            AttendedSessionCount = source.AttendedSessionCount,
            PlannedSessionCount = source.PlannedSessionCount,
            DueDate = DateOnly.FromDateTime(source.DueDate),
            Status = ToCloudInvoiceStatus(source.Status),
            PaymentContent = Text(source.PaymentContent),
            CreatedAt = ToUtc(source.CreatedAtUtc),
            UpdatedAt = ToUtc(source.UpdatedAtUtc)
        };
    }

    public static TuitionInvoice ToEntity(CloudTuitionInvoiceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new TuitionInvoice
        {
            Id = Text(source.Id),
            EnrollmentId = Text(source.EnrollmentId),
            Period = source.CycleNumber > 0
                ? $"C{source.CycleNumber:000000}"
                : string.Empty,
            TraineeUserId = Text(source.TraineeUserId),
            ClassId = Text(source.ClassId),
            AmountVnd = source.AmountVnd,
            CycleCount = source.CycleCount,
            CycleFeeVnd = source.CycleFeeVnd,
            CycleNumber = source.CycleNumber,
            AttendedSessionCount = source.AttendedSessionCount,
            PlannedSessionCount = source.PlannedSessionCount,
            AmountPerSessionVnd = PerSessionAmount(source.AmountVnd, source.PlannedSessionCount),
            DueDate = FromDate(source.DueDate),
            Status = FromCloudInvoiceStatus(source.Status),
            PaymentContent = Text(source.PaymentContent),
            CreatedAtUtc = FromUtc(source.CreatedAt),
            UpdatedAtUtc = FromUtc(source.UpdatedAt)
        };
    }

    public static CloudPaymentProofSnapshot ToCloud(PaymentProof source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudPaymentProofSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            InvoiceId = Text(source.InvoiceId),
            ImageObjectKey = Text(source.ImagePath),
            Note = Text(source.Note),
            SubmittedAt = ToUtc(source.SubmittedAtUtc),
            ReviewedByUserId = Text(source.ReviewedByUserId),
            ReviewedAt = ToUtc(source.ReviewedAtUtc),
            ReviewStatus = source.IsAccepted
                ? CloudPaymentProofReviewStatus.Accepted
                : source.ReviewedAtUtc is null
                    ? CloudPaymentProofReviewStatus.Pending
                    : CloudPaymentProofReviewStatus.Rejected
        };
    }

    public static PaymentProof ToEntity(CloudPaymentProofSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new PaymentProof
        {
            Id = Text(source.Id),
            InvoiceId = Text(source.InvoiceId),
            ImagePath = Text(source.ImageObjectKey),
            Note = Text(source.Note),
            SubmittedAtUtc = FromUtc(source.SubmittedAt),
            ReviewedByUserId = Text(source.ReviewedByUserId),
            ReviewedAtUtc = FromUtc(source.ReviewedAt),
            IsAccepted = source.ReviewStatus == CloudPaymentProofReviewStatus.Accepted
        };
    }

    public static CloudReceiptSnapshot ToCloud(Receipt source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudReceiptSnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            InvoiceId = Text(source.InvoiceId),
            ReceiptNumber = Text(source.ReceiptNumber),
            TeamNameSnapshot = Text(source.TeamNameSnapshot),
            TraineeNameSnapshot = Text(source.TraineeNameSnapshot),
            ClassNameSnapshot = Text(source.ClassNameSnapshot),
            CycleSnapshot = Text(source.PeriodSnapshot),
            AmountVndSnapshot = source.AmountVndSnapshot,
            ConfirmedByNameSnapshot = Text(source.ConfirmedByNameSnapshot),
            ConfirmedAt = ToUtc(source.ConfirmedAtUtc),
            PdfObjectKey = Text(source.PdfPath)
        };
    }

    public static Receipt ToEntity(CloudReceiptSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Receipt
        {
            Id = Text(source.Id),
            InvoiceId = Text(source.InvoiceId),
            ReceiptNumber = Text(source.ReceiptNumber),
            TeamNameSnapshot = Text(source.TeamNameSnapshot),
            TraineeNameSnapshot = Text(source.TraineeNameSnapshot),
            ClassNameSnapshot = Text(source.ClassNameSnapshot),
            PeriodSnapshot = Text(source.CycleSnapshot),
            AmountVndSnapshot = source.AmountVndSnapshot,
            ConfirmedByNameSnapshot = Text(source.ConfirmedByNameSnapshot),
            ConfirmedAtUtc = FromUtc(source.ConfirmedAt),
            PdfPath = Text(source.PdfObjectKey)
        };
    }

    public static CloudCoachSalarySnapshot ToCloud(CoachSalary source, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudCoachSalarySnapshot
        {
            Id = Text(source.Id),
            TenantId = RequiredTenant(tenantId),
            CoachUserId = Text(source.CoachUserId),
            Period = Text(source.Period),
            AmountVnd = source.AmountVnd,
            DueDate = DateOnly.FromDateTime(source.DueDate),
            Status = source.Status,
            PaidAt = ToUtc(source.PaidAtUtc),
            PaidByUserId = Text(source.PaidByUserId),
            Notes = Text(source.Notes),
            UpdatedAt = ToUtc(source.UpdatedAtUtc)
        };
    }

    public static CoachSalary ToEntity(CloudCoachSalarySnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CoachSalary
        {
            Id = Text(source.Id),
            CoachUserId = Text(source.CoachUserId),
            Period = Text(source.Period),
            AmountVnd = source.AmountVnd,
            DueDate = FromDate(source.DueDate),
            Status = source.Status,
            PaidAtUtc = FromUtc(source.PaidAt),
            PaidByUserId = Text(source.PaidByUserId),
            Notes = Text(source.Notes),
            UpdatedAtUtc = FromUtc(source.UpdatedAt)
        };
    }

    public static CloudNotificationSnapshot ToCloud(AppNotification source, string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudNotificationSnapshot
        {
            Id = Text(source.Id),
            TenantId = Tenant(tenantId),
            RecipientUserId = Text(source.RecipientUserId),
            Kind = source.Kind.ToString(),
            Title = Text(source.Title),
            Message = Text(source.Message),
            RelatedEntityId = Text(source.RelatedEntityId),
            IsRead = source.IsRead,
            CreatedAt = ToUtc(source.CreatedAtUtc)
        };
    }

    public static AppNotification ToEntity(CloudNotificationSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AppNotification
        {
            Id = Text(source.Id),
            RecipientUserId = Text(source.RecipientUserId),
            Kind = ParseNotificationKind(source.Kind),
            Title = Text(source.Title),
            Message = Text(source.Message),
            RelatedEntityId = Text(source.RelatedEntityId),
            IsRead = source.IsRead,
            CreatedAtUtc = FromUtc(source.CreatedAt)
        };
    }

    public static CloudAuditLogSnapshot ToCloud(AuditLog source, string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new CloudAuditLogSnapshot
        {
            Id = Text(source.Id),
            TenantId = Tenant(tenantId),
            ActorUserId = Text(source.ActorUserId),
            Action = Text(source.Action),
            EntityType = Text(source.EntityType),
            EntityId = Text(source.EntityId),
            DetailsJson = Text(source.Details),
            CreatedAt = ToUtc(source.CreatedAtUtc)
        };
    }

    public static AuditLog ToEntity(CloudAuditLogSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AuditLog
        {
            Id = Text(source.Id),
            ActorUserId = Text(source.ActorUserId),
            Action = Text(source.Action),
            EntityType = Text(source.EntityType),
            EntityId = Text(source.EntityId),
            Details = Text(source.DetailsJson),
            CreatedAtUtc = FromUtc(source.CreatedAt)
        };
    }

    /// <summary>
    /// Exports all collections supported by CloudDataSnapshot. The current API
    /// contract has no ExternalAccountLinks collection, so links remain available
    /// through the individual mapper but cannot be embedded in this object.
    /// </summary>
    public static CloudDataSnapshot Export(CloudSnapshotEntityCollections source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var tenantId = Tenant(source.TenantId);
        var profiles = source.Profiles
            .GroupBy(item => item.UserId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var roles = source.Users
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Role, StringComparer.Ordinal);

        string? UserTenant(UserAccount user) =>
            user.Role == UserRole.Admin ? null : tenantId;

        string? ProfileTenant(PersonProfile profile) =>
            roles.GetValueOrDefault(profile.UserId) == UserRole.Admin ? null : tenantId;

        var currentUserEmail = source.CurrentProfile?.Email;
        return new CloudDataSnapshot
        {
            Cursor = Text(source.Cursor),
            SyncVersion = source.SyncVersion,
            ServerTimeUtc = source.ServerTimeUtc == default
                ? DateTimeOffset.UtcNow
                : source.ServerTimeUtc.ToUniversalTime(),
            Role = source.Role ?? source.CurrentUser?.Role,
            CurrentUser = source.CurrentUser is null
                ? null
                : ToCloud(source.CurrentUser, UserTenant(source.CurrentUser), currentUserEmail),
            CurrentProfile = source.CurrentProfile is null
                ? null
                : ToCloud(
                    source.CurrentProfile,
                    source.CurrentUser?.Role == UserRole.Admin ? null : tenantId),
            Club = source.Club is null
                ? null
                : ToCloud(source.Club, RequiredTenant(source.ClubTenantId ?? tenantId)),
            ActiveClub = source.ActiveClub is null
                ? null
                : ToCloud(source.ActiveClub, RequiredTenant(source.ActiveClubTenantId ?? tenantId)),
            Users = source.Users
                .Select(item => ToCloud(
                    item,
                    UserTenant(item),
                    profiles.GetValueOrDefault(item.Id)?.Email))
                .ToArray(),
            Profiles = source.Profiles
                .Select(item => ToCloud(item, ProfileTenant(item)))
                .ToArray(),
            Venues = source.Venues.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            Classes = source.Classes.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            ClassCoaches = source.ClassCoaches.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            ClassEnrollments = source.ClassEnrollments.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            TrainingSessions = source.TrainingSessions.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            SessionCoaches = source.SessionCoaches.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            CoachCheckIns = source.CoachCheckIns.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            AttendanceRecords = source.AttendanceRecords.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            TuitionInvoices = source.TuitionInvoices.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            PaymentProofs = source.PaymentProofs.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            Receipts = source.Receipts.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            CoachSalaries = source.CoachSalaries.Select(item => ToCloud(item, RequiredTenant(tenantId))).ToArray(),
            Notifications = source.Notifications.Select(item => ToCloud(item, tenantId)).ToArray(),
            AuditLogs = source.AuditLogs.Select(item => ToCloud(item, tenantId)).ToArray()
        };
    }

    public static CloudSnapshotEntityCollections Import(CloudDataSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var tenantId = Tenant(
            source.ActiveClub?.TenantId
            ?? source.Club?.TenantId
            ?? source.CurrentUser?.TenantId
            ?? source.Users.Select(item => item.TenantId)
                .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)));

        return new CloudSnapshotEntityCollections
        {
            Cursor = Text(source.Cursor),
            SyncVersion = source.SyncVersion,
            ServerTimeUtc = source.ServerTimeUtc.ToUniversalTime(),
            Role = source.Role ?? source.CurrentUser?.Role,
            TenantId = tenantId,
            ClubTenantId = Tenant(source.Club?.TenantId),
            ActiveClubTenantId = Tenant(source.ActiveClub?.TenantId),
            CurrentUser = source.CurrentUser is null ? null : ToEntity(source.CurrentUser),
            CurrentProfile = source.CurrentProfile is null ? null : ToEntity(source.CurrentProfile),
            Club = source.Club is null ? null : ToEntity(source.Club),
            ActiveClub = source.ActiveClub is null ? null : ToEntity(source.ActiveClub),
            Users = source.Users.Select(ToEntity).ToArray(),
            Profiles = source.Profiles.Select(ToEntity).ToArray(),
            // CloudDataSnapshot currently has no ExternalAccountLinks property.
            ExternalAccountLinks = [],
            Venues = source.Venues.Select(ToEntity).ToArray(),
            Classes = source.Classes.Select(ToEntity).ToArray(),
            ClassCoaches = source.ClassCoaches.Select(ToEntity).ToArray(),
            ClassEnrollments = source.ClassEnrollments.Select(ToEntity).ToArray(),
            TrainingSessions = source.TrainingSessions.Select(ToEntity).ToArray(),
            SessionCoaches = source.SessionCoaches.Select(ToEntity).ToArray(),
            CoachCheckIns = source.CoachCheckIns.Select(ToEntity).ToArray(),
            AttendanceRecords = source.AttendanceRecords.Select(ToEntity).ToArray(),
            TuitionInvoices = source.TuitionInvoices.Select(ToEntity).ToArray(),
            PaymentProofs = source.PaymentProofs.Select(ToEntity).ToArray(),
            Receipts = source.Receipts.Select(ToEntity).ToArray(),
            CoachSalaries = source.CoachSalaries.Select(ToEntity).ToArray(),
            Notifications = source.Notifications.Select(ToEntity).ToArray(),
            AuditLogs = source.AuditLogs.Select(ToEntity).ToArray()
        };
    }

    public static CloudInvoiceStatus ToCloudInvoiceStatus(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Pending => CloudInvoiceStatus.Pending,
        InvoiceStatus.ProofSubmitted => CloudInvoiceStatus.ProofSubmitted,
        InvoiceStatus.Paid => CloudInvoiceStatus.Paid,
        InvoiceStatus.Rejected => CloudInvoiceStatus.Rejected,
        InvoiceStatus.Overdue => CloudInvoiceStatus.Overdue,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown local invoice status.")
    };

    public static InvoiceStatus FromCloudInvoiceStatus(CloudInvoiceStatus status) => status switch
    {
        CloudInvoiceStatus.Pending => InvoiceStatus.Pending,
        CloudInvoiceStatus.ProofSubmitted => InvoiceStatus.ProofSubmitted,
        CloudInvoiceStatus.Paid => InvoiceStatus.Paid,
        CloudInvoiceStatus.Rejected => InvoiceStatus.Rejected,
        CloudInvoiceStatus.Overdue => InvoiceStatus.Overdue,
        // The offline schema has no Waived value. Paid is the only closed state
        // that prevents reminders while preserving read-only receipt behavior.
        CloudInvoiceStatus.Waived => InvoiceStatus.Paid,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown cloud invoice status.")
    };

    private static NotificationKind ParseNotificationKind(string? value)
    {
        var normalized = value?.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        if (Enum.TryParse<NotificationKind>(normalized, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        return NotificationKind.System;
    }

    private static long PerSessionAmount(long amountVnd, int plannedSessionCount) =>
        plannedSessionCount <= 0
            ? 0
            : (long)Math.Round(
                amountVnd / (decimal)plannedSessionCount,
                MidpointRounding.AwayFromZero);

    private static string Text(string? value) => value ?? string.Empty;

    private static string Normalize(string? value) =>
        Text(value).Trim().ToUpperInvariant();

    private static string? Tenant(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequiredTenant(string? value) =>
        Tenant(value)
        ?? throw new InvalidOperationException(
            "TenantId is required when mapping club-scoped entities.");

    private static DateTimeOffset ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value),
        DateTimeKind.Local => new DateTimeOffset(value).ToUniversalTime(),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
    };

    private static DateTimeOffset? ToUtc(DateTime? value) =>
        value is null ? null : ToUtc(value.Value);

    private static DateTime FromUtc(DateTimeOffset value) =>
        value.ToUniversalTime().UtcDateTime;

    private static DateTime? FromUtc(DateTimeOffset? value) =>
        value is null ? null : FromUtc(value.Value);

    private static DateTime FromDate(DateOnly value) =>
        DateTime.SpecifyKind(value.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
}
