namespace CommunityFootballClubManager.Models;

public enum UserRole
{
    Founder = 1,
    Coach = 2,
    Trainee = 3,
    Admin = 4,
    CoFounder = 5,
    Manager = 6
}

public enum ExternalAuthProvider
{
    Google = 1
}

public sealed record CoachPositionOption(string Key, string Label);

/// <summary>
/// The supported Coach positions.  Persist the key, not the Vietnamese label,
/// so existing accounts remain stable if the UI is translated later.
/// </summary>
public static class CoachPositionCatalog
{
    public const string HeadCoachManager = "head_coach_manager";
    public const string GoalkeepingCoach = "goalkeeping_coach";
    public const string FitnessCoach = "fitness_coach";
    public const string TechnicalCoach = "technical_coach";
    public const string TacticalCoach = "tactical_coach";
    public const string RehabilitationConditioningCoach = "rehabilitation_conditioning_coach";
    public const string PerformanceCoach = "performance_coach";

    public static IReadOnlyList<CoachPositionOption> Options { get; } =
    [
        new(HeadCoachManager, "Huấn luyện viên trưởng (Head Coach / Manager)"),
        new(GoalkeepingCoach, "HLV Thủ môn (Goalkeeping Coach)"),
        new(FitnessCoach, "HLV Thể lực (Fitness Coach)"),
        new(TechnicalCoach, "HLV Kỹ thuật (Technical Coach)"),
        new(TacticalCoach, "HLV Chiến thuật (Tactical Coach)"),
        new(RehabilitationConditioningCoach, "HLV Phục hồi / Thể chất (Rehabilitation / Conditioning Coach)"),
        new(PerformanceCoach, "HLV Phân tích (Performance Coach)")
    ];

    public static bool IsValid(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && Options.Any(option => string.Equals(option.Key, key.Trim(), StringComparison.Ordinal));

    public static string Label(string? key) =>
        Options.FirstOrDefault(option => string.Equals(option.Key, key?.Trim(), StringComparison.Ordinal))?.Label
        ?? "Chưa chọn vị trí dạy";
}

public enum SessionStatus
{
    Draft = 0,
    Submitted = 1,
    Locked = 2
}

public enum AttendanceStatus
{
    Unmarked = 0,
    Present = 1,
    Late = 2,
    Absent = 3,
    Excused = 4
}

public enum InvoiceStatus
{
    Pending = 0,
    ProofSubmitted = 1,
    Paid = 2,
    Rejected = 3,
    Overdue = 4
}

public enum SalaryStatus
{
    Pending = 0,
    Paid = 1
}

public enum CoachCheckInApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

/// <summary>Reason/context for a trainee evaluation.</summary>
public enum TraineeEvaluationType
{
    Periodic = 1,
    TournamentMatch = 2
}

/// <summary>
/// Coach can revise a pending/rejected evaluation.  Founder approval is the
/// immutable point in the history and cannot be edited afterwards.
/// </summary>
public enum TraineeEvaluationStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum NotificationKind
{
    Announcement = 0,
    TuitionProofSubmitted = 1,
    TuitionConfirmed = 2,
    TuitionRejected = 3,
    TuitionReminder = 4,
    SalaryReminder = 5,
    AttendanceUpdated = 6,
    System = 7,
    CoachCheckIn = 8,
    CoachCheckInReviewed = 9,
    EvaluationRequestOpened = 10,
    EvaluationSubmitted = 11,
    EvaluationApproved = 12,
    EvaluationRejected = 13,
    EvaluationClassCompleted = 14
}

public static class DomainText
{
    public const string SupportedTraineeLabel = "Cầu Thủ Học Viên Được Hỗ Trợ";
    public const string SupportedTraineeTuitionLabel = "Cầu Thủ Học Viên Được Hỗ Trợ · Miễn Học Phí";

    public static string ExternalProvider(ExternalAuthProvider provider) => provider switch
    {
        ExternalAuthProvider.Google => "Google",
        _ => "Tài khoản ngoài"
    };

    public static string Role(UserRole role) => role switch
    {
        UserRole.Founder => "Sáng lập & Điều hành",
        UserRole.CoFounder => "Đồng Sáng Lập",
        UserRole.Manager => "Quản Lý",
        UserRole.Coach => "Huấn luyện viên",
        UserRole.Trainee => "Cầu thủ học viên",
        UserRole.Admin => "Quản trị hệ thống",
        _ => "Không xác định"
    };

    public static string Attendance(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => "Có mặt",
        AttendanceStatus.Late => "Đi trễ",
        AttendanceStatus.Absent => "Vắng",
        AttendanceStatus.Excused => "Có phép",
        _ => "Chưa ghi nhận"
    };

    public static string Invoice(InvoiceStatus status) => status switch
    {
        InvoiceStatus.ProofSubmitted => "Chờ xác nhận",
        InvoiceStatus.Paid => "Đã đóng",
        InvoiceStatus.Rejected => "Cần tải lại bill",
        InvoiceStatus.Overdue => "Quá hạn",
        _ => "Chưa đóng"
    };

    public static string Salary(SalaryStatus status) =>
        status == SalaryStatus.Paid ? "Đã thanh toán" : "Chưa thanh toán";

    public static string CoachCheckInApproval(CoachCheckInApprovalStatus status) => status switch
    {
        CoachCheckInApprovalStatus.Approved => "Đã xác nhận",
        CoachCheckInApprovalStatus.Rejected => "Đã từ chối",
        _ => "Chờ duyệt check-in"
    };

    public static string EvaluationType(TraineeEvaluationType type) => type switch
    {
        TraineeEvaluationType.TournamentMatch => "Sau trận đấu / giải",
        _ => "Đánh giá định kỳ"
    };

    public static string EvaluationStatus(TraineeEvaluationStatus status) => status switch
    {
        TraineeEvaluationStatus.Approved => "Đã Founder xác nhận · Không thể sửa",
        TraineeEvaluationStatus.Rejected => "Cần chỉnh sửa và gửi lại",
        _ => "Chờ Founder xác nhận"
    };

    public static string Weekdays(string values)
    {
        if (string.IsNullOrWhiteSpace(values))
        {
            return "Chưa chọn ngày";
        }

        var labels = new Dictionary<int, string>
        {
            [(int)DayOfWeek.Monday] = "T2",
            [(int)DayOfWeek.Tuesday] = "T3",
            [(int)DayOfWeek.Wednesday] = "T4",
            [(int)DayOfWeek.Thursday] = "T5",
            [(int)DayOfWeek.Friday] = "T6",
            [(int)DayOfWeek.Saturday] = "T7",
            [(int)DayOfWeek.Sunday] = "CN"
        };

        return string.Join(", ", values
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var day) && labels.TryGetValue(day, out var label)
                ? label
                : value));
    }

    public static string TimeRange(int startMinutes, int endMinutes) =>
        $"{startMinutes / 60:00}:{startMinutes % 60:00} – {endMinutes / 60:00}:{endMinutes % 60:00}";

    public static string Period(string period)
    {
        if (DateTime.TryParseExact(
                $"{period}-01",
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed))
        {
            return $"Tháng {parsed:MM/yyyy}";
        }

        return period;
    }

    public static string TuitionCycle(TuitionInvoice invoice) =>
        invoice.CycleNumber > 0
            ? $"Chu kỳ {invoice.CycleNumber}"
            : Period(invoice.Period);

    public static string TuitionPrepaidCycles(TuitionInvoice invoice) =>
        $"{Math.Max(1, invoice.CycleCount)} chu kỳ";
}
