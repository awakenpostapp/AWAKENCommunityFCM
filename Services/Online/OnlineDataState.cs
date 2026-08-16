using CommunityFootballClubManager.Models;

namespace CommunityFootballClubManager.Services.Online;

/// <summary>
/// Volatile, in-memory view of the currently authenticated Cloud tenant.
/// D1 remains the source of truth; this projection is cleared on logout.
/// </summary>
public sealed class OnlineDataState
{
    private readonly object _gate = new();

    public bool IsLoaded { get; private set; }
    public string TenantId { get; private set; } = string.Empty;
    public UserAccount? CurrentUser { get; private set; }
    public PersonProfile? CurrentProfile { get; private set; }
    public ClubProfile? Club { get; private set; }
    public long SyncVersion { get; private set; }

    public List<UserAccount> Users { get; } = [];
    public List<PersonProfile> Profiles { get; } = [];
    public List<Venue> Venues { get; } = [];
    public List<TrainingClass> Classes { get; } = [];
    public List<ClassCoachAssignment> ClassCoaches { get; } = [];
    public List<ClassEnrollment> ClassEnrollments { get; } = [];
    public List<TrainingSession> TrainingSessions { get; } = [];
    public List<SessionCoachAssignment> SessionCoaches { get; } = [];
    public List<CoachCheckIn> CoachCheckIns { get; } = [];
    public List<AttendanceRecord> AttendanceRecords { get; } = [];
    public List<TuitionInvoice> TuitionInvoices { get; } = [];
    public List<TuitionInvoice> Invoices => TuitionInvoices;
    public List<PaymentProof> PaymentProofs { get; } = [];
    public List<Receipt> Receipts { get; } = [];
    public List<CoachSalary> CoachSalaries { get; } = [];
    /// <summary>
    /// Evaluations are loaded through their dedicated, role-scoped endpoint.
    /// They remain in the same volatile tenant projection, but are not part
    /// of the broad operational snapshot to keep login payloads small.
    /// </summary>
    public List<TraineeEvaluation> TraineeEvaluations { get; } = [];
    public List<AppNotification> Notifications { get; } = [];
    public List<AuditLog> AuditLogs { get; } = [];

    public void Replace(CloudSnapshotEntityCollections snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            TenantId = snapshot.TenantId ?? string.Empty;
            CurrentUser = snapshot.CurrentUser;
            CurrentProfile = snapshot.CurrentProfile;
            Club = snapshot.Club ?? snapshot.ActiveClub;
            SyncVersion = snapshot.SyncVersion;
            Replace(Users, snapshot.Users);
            Replace(Profiles, snapshot.Profiles);
            Replace(Venues, snapshot.Venues);
            Replace(Classes, snapshot.Classes);
            Replace(ClassCoaches, snapshot.ClassCoaches);
            Replace(ClassEnrollments, snapshot.ClassEnrollments);
            Replace(TrainingSessions, snapshot.TrainingSessions);
            Replace(SessionCoaches, snapshot.SessionCoaches);
            Replace(CoachCheckIns, snapshot.CoachCheckIns);
            Replace(AttendanceRecords, snapshot.AttendanceRecords);
            Replace(TuitionInvoices, snapshot.TuitionInvoices);
            Replace(PaymentProofs, snapshot.PaymentProofs);
            Replace(Receipts, snapshot.Receipts);
            Replace(CoachSalaries, snapshot.CoachSalaries);
            Replace(Notifications, snapshot.Notifications);
            Replace(AuditLogs, snapshot.AuditLogs);
            IsLoaded = true;
        }
    }

    public void SetIdentity(UserAccount user, PersonProfile? profile, ClubProfile? club)
    {
        lock (_gate)
        {
            CurrentUser = user;
            CurrentProfile = profile;
            Club = club;
            TenantId = user.TenantId;
        }
    }

    public void MarkFresh(long syncVersion)
    {
        lock (_gate)
        {
            SyncVersion = syncVersion;
            IsLoaded = true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            IsLoaded = false;
            TenantId = string.Empty;
            CurrentUser = null;
            CurrentProfile = null;
            Club = null;
            SyncVersion = 0;
            Users.Clear();
            Profiles.Clear();
            Venues.Clear();
            Classes.Clear();
            ClassCoaches.Clear();
            ClassEnrollments.Clear();
            TrainingSessions.Clear();
            SessionCoaches.Clear();
            CoachCheckIns.Clear();
            AttendanceRecords.Clear();
            TuitionInvoices.Clear();
            PaymentProofs.Clear();
            Receipts.Clear();
            CoachSalaries.Clear();
            TraineeEvaluations.Clear();
            Notifications.Clear();
            AuditLogs.Clear();
        }
    }

    /// <summary>
    /// Marks the volatile projection stale without discarding the authenticated
    /// identity.  Online writes already update the affected in-memory rows; a
    /// subsequent read can now pull one authoritative snapshot when needed,
    /// instead of every mutation starting a background full refresh.
    /// </summary>
    public void InvalidateData()
    {
        lock (_gate)
        {
            // Keep the last tenant-scoped projection visible while the next
            // authoritative D1 snapshot is fetched. Clearing every list here
            // caused tab switches to render an empty page and wait on a full
            // network round-trip after every mutation.
            IsLoaded = false;
        }
    }

    public UserAccount? User(string id) => Users.FirstOrDefault(item => item.Id == id)
        ?? (CurrentUser?.Id == id ? CurrentUser : null);
    public PersonProfile? Profile(string id) =>
        (CurrentProfile?.UserId == id ? CurrentProfile : null)
        ?? Profiles.FirstOrDefault(item => item.UserId == id);
    public Venue? Venue(string id) => Venues.FirstOrDefault(item => item.Id == id);
    public TrainingClass? Class(string id) => Classes.FirstOrDefault(item => item.Id == id);
    public TrainingSession? Session(string id) => TrainingSessions.FirstOrDefault(item => item.Id == id);
    public CoachCheckIn? CheckIn(string sessionId, string coachId) =>
        CoachCheckIns.FirstOrDefault(item => item.SessionId == sessionId && item.CoachUserId == coachId);

    public void Upsert<T>(List<T> items, T item, Func<T, bool> match)
    {
        lock (_gate)
        {
            var index = items.FindIndex(existing => match(existing));
            if (index >= 0) items[index] = item;
            else items.Add(item);
        }
    }

    public void Remove<T>(List<T> items, Func<T, bool> match)
    {
        lock (_gate) items.RemoveAll(item => match(item));
    }

    private static void Replace<T>(List<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        target.AddRange(source);
    }
}
