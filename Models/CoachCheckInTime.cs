namespace CommunityFootballClubManager.Models;

public static class CoachCheckInTime
{
    public const int CheckInOpenLeadMinutes = 60;
    public const int CheckInLockAfterEndMinutes = 120;
    public const string AutoAbsentReviewNote = "AUTO_ABSENT_NO_CHECKIN";
    public const string FounderSubstitutedCoachReviewNote = "FOUNDER_SUBSTITUTED_COACH";
    /// <summary>
    /// Marker used when a Founder backfills a completed historical lesson.
    /// These rows have no selfies because the Coach was not using the app yet,
    /// but they are still payable teaching sessions.
    /// </summary>
    public const string FounderManualTaughtMarker = "Founder ghi nhận buổi học cũ; Coach đã dạy";

    /// <summary>
    /// Safety cap for an open teaching session.  A missing checkout must not
    /// leave a live timer (or a trainee roster grant) open forever.  The cap
    /// only closes the timer/roster; it never creates salary and it never
    /// counts as a valid checkout until the Coach submits the checkout selfie.
    /// </summary>
    public const long MaxOpenDurationSeconds = 8 * 60 * 60;

    public static long ElapsedSeconds(CoachCheckIn checkIn, DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(checkIn);
        if (checkIn.DurationSeconds > 0)
        {
            return checkIn.DurationSeconds;
        }

        var end = checkIn.CheckedOutAtUtc ?? nowUtc ?? DateTime.UtcNow;
        return Math.Max(0, (long)Math.Floor((end - checkIn.CheckedInAtUtc).TotalSeconds));
    }

    public static DateTime SafetyCloseAtUtc(CoachCheckIn checkIn) =>
        checkIn.CheckedInAtUtc.ToUniversalTime().AddSeconds(MaxOpenDurationSeconds);

    public static bool IsSafetyClosed(CoachCheckIn checkIn) =>
        checkIn.CheckedOutAtUtc is not null
        && string.IsNullOrWhiteSpace(checkIn.CheckOutSelfiePath)
        && !IsFounderManualTaught(checkIn);

    public static bool HasCoachCheckout(CoachCheckIn checkIn) =>
        checkIn.CheckedOutAtUtc is not null
        && (!string.IsNullOrWhiteSpace(checkIn.CheckOutSelfiePath)
            || IsFounderManualTaught(checkIn));

    public static bool IsFounderManualTaught(CoachCheckIn checkIn) =>
        checkIn.ReviewNote.Contains(
            FounderManualTaughtMarker,
            StringComparison.OrdinalIgnoreCase);

    public static bool IsAutoAbsent(CoachCheckIn checkIn) =>
        string.Equals(checkIn.ReviewNote, AutoAbsentReviewNote, StringComparison.Ordinal);

    public static bool IsFounderSubstitution(CoachCheckIn checkIn) =>
        string.Equals(checkIn.ReviewNote, FounderSubstitutedCoachReviewNote, StringComparison.Ordinal);

    public static DateTime ScheduledStartLocal(TrainingClass trainingClass, DateTime sessionDate) =>
        DateTime.SpecifyKind(
            sessionDate.Date.AddMinutes(Math.Clamp(trainingClass.StartTimeMinutes, 0, 1439)),
            DateTimeKind.Local);

    public static DateTime ScheduledEndLocal(TrainingClass trainingClass, DateTime sessionDate) =>
        DateTime.SpecifyKind(
            sessionDate.Date.AddMinutes(Math.Clamp(trainingClass.EndTimeMinutes, 1, 1440)),
            DateTimeKind.Local);

    public static DateTime CheckInOpensLocal(TrainingClass trainingClass, DateTime sessionDate) =>
        ScheduledStartLocal(trainingClass, sessionDate).AddMinutes(-CheckInOpenLeadMinutes);

    public static DateTime CheckInLocksLocal(TrainingClass trainingClass, DateTime sessionDate) =>
        ScheduledEndLocal(trainingClass, sessionDate).AddMinutes(CheckInLockAfterEndMinutes);

    public static bool IsCheckInWindowOpen(
        TrainingClass trainingClass,
        DateTime sessionDate,
        DateTime? nowUtc = null)
    {
        var nowLocal = (nowUtc ?? DateTime.UtcNow).ToLocalTime();
        return nowLocal >= CheckInOpensLocal(trainingClass, sessionDate)
               && nowLocal < CheckInLocksLocal(trainingClass, sessionDate);
    }

    public static bool IsCheckInWindowTooEarly(
        TrainingClass trainingClass,
        DateTime sessionDate,
        DateTime? nowUtc = null) =>
        (nowUtc ?? DateTime.UtcNow).ToLocalTime() < CheckInOpensLocal(trainingClass, sessionDate);

    public static bool IsCheckInWindowLocked(
        TrainingClass trainingClass,
        DateTime sessionDate,
        DateTime? nowUtc = null) =>
        (nowUtc ?? DateTime.UtcNow).ToLocalTime() >= CheckInLocksLocal(trainingClass, sessionDate);

    public static string CheckInWindowText(TrainingClass trainingClass, DateTime sessionDate) =>
        $"Mở check-in {CheckInOpensLocal(trainingClass, sessionDate):HH:mm} · khóa {CheckInLocksLocal(trainingClass, sessionDate):HH:mm}";

    public static string FormatDuration(long seconds)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    public static string Range(CoachCheckIn checkIn)
    {
        var checkOut = checkIn.CheckedOutAtUtc is { } value
            ? $"{value.ToLocalTime():HH:mm}"
            : "đang dạy";
        return $"{checkIn.CheckedInAtUtc.ToLocalTime():HH:mm} → {checkOut}";
    }
}
