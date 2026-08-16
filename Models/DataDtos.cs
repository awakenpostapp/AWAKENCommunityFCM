namespace CommunityFootballClubManager.Models;

public sealed record LoginResult(bool Succeeded, string Message, UserAccount? User = null);

public sealed record MemberRow(UserAccount Account, PersonProfile Profile)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Profile.FullName) ? Account.Username : Profile.FullName;

    // Admin-only metadata returned by the online Founder management endpoint.
    // Null means the row came from the legacy local cache, where the old schema
    // did not distinguish pending approval from disabled accounts.
    public string? FounderApprovalStatus { get; init; }
    public string? FounderTenantStatus { get; init; }
    public string? TeamName { get; init; }
}

public sealed record ClassRow(
    TrainingClass Class,
    Venue? Venue,
    IReadOnlyList<MemberRow> Coaches,
    IReadOnlyList<MemberRow> Trainees)
{
    public string ScheduleText =>
        $"{DomainText.Weekdays(Class.ScheduleDays)} · {DomainText.TimeRange(Class.StartTimeMinutes, Class.EndTimeMinutes)}";
    public string CoachNames => Coaches.Count == 0
        ? "ChÆ°a phÃ¢n cÃ´ng Coach"
        : string.Join(", ", Coaches.Select(item =>
            $"{item.DisplayName} · {CoachPositionCatalog.Label(item.Profile.CoachPosition)}"));
}

public sealed class AttendanceRosterItem
{
    public required string TraineeUserId { get; init; }
    public required string TraineeName { get; init; }
    public string PhotoPath { get; init; } = string.Empty;
    public AttendanceStatus Status { get; set; }
    public AttendanceRecord? ExistingRecord { get; set; }
}

public sealed record AttendanceHistoryRow(
    DateTime SessionDate,
    string ClassName,
    AttendanceStatus Status,
    DateTime RecordedAtUtc);

public sealed record MemberAttendanceSummary(
    UserRole Role,
    int AttendedCount,
    int AbsentCount,
    int LateCount,
    int ExcusedCount,
    int SubmittedSessionCount,
    int PendingCheckInCount = 0);

public sealed record CoachCheckInRow(
    CoachCheckIn CheckIn,
    string CoachName,
    string CoachPosition = "");

public sealed record CoachCheckInReviewRow(
    CoachCheckIn CheckIn,
    string CoachName,
    string ClassName,
    DateTime SessionDate,
    string CoachPosition = "");

/// <summary>
/// A Coach check-in shown to the Founder in chronological history.  This is
/// deliberately separate from the review row, because approved and rejected
/// check-ins need to remain visible after they leave the approval queue.
/// </summary>
public sealed record CoachCheckInHistoryRow(
    CoachCheckIn CheckIn,
    string CoachName,
    string CoachPhotoPath,
    string ClassName,
    DateTime SessionDate,
    string CoachPosition = "");

/// <summary>
/// A submitted trainee attendance record that the Founder can inspect by
/// attendance category and session date.
/// </summary>
public sealed record FounderTraineeAttendanceHistoryRow(
    DateTime SessionDate,
    string ClassName,
    string TraineeName,
    string TraineePhotoPath,
    AttendanceStatus Status,
    DateTime RecordedAtUtc);

public sealed record InvoiceRow(
    TuitionInvoice Invoice,
    string TraineeName,
    string ClassName,
    PaymentProof? LatestProof,
    Receipt? Receipt)
{
    public TuitionCycleProgress Progress { get; init; } = new(0, 0, false, false);
}

public sealed record TuitionCycleProgress(
    int AttendedSessions,
    int PlannedSessions,
    bool IsComplete,
    bool NeedsPaymentWarning);

public sealed record SalaryRow(
    CoachSalary Salary,
    string CoachName,
    string ClassName,
    string CoachPosition = "");

public sealed record TraineeEvaluationRow(
    TraineeEvaluation Evaluation,
    string TraineeName,
    string CoachName,
    string ClassName,
    string CoachPosition = "")
{
    public TraineeEvaluation? Previous { get; init; }
}

/// <summary>Minimal trainee details used by the Coach evaluation roster.</summary>
public sealed record TraineeEvaluationRosterRow(
    string TraineeUserId,
    string FullName,
    DateTime? DateOfBirth,
    double HeightCm,
    double WeightKg);

public sealed record DashboardMetrics(
    int ActiveClasses,
    int ActiveCoaches,
    int ActiveTrainees,
    int PendingTuitionProofs,
    int UnpaidTuition,
    int OverdueSalaries,
    int PendingCoachCheckOuts = 0);
