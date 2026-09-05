using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services.Online;

namespace CommunityFootballClubManager.UiHarness;

// This handler has no inner network handler: even unexpected requests cannot
// leave this test application. Production does not compile tools/**.
public sealed class FixtureBackend(OnlineDataState state) : HttpMessageHandler
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
    private CloudSnapshotEntityCollections _snapshot = new();
    private string _mode = "normal";
    public int LastSavedRosterCount { get; private set; }
    public bool LastSavedSubmit { get; private set; }
    private readonly List<CloudAchievementSnapshot> _awards = [];

    public void Reset(UserRole role, string mode)
    {
        _mode = mode;
        var users = new List<UserAccount>();
        var profiles = new List<PersonProfile>();
        foreach (var accountRole in new[] { UserRole.Founder, UserRole.CoFounder, UserRole.Manager, UserRole.Coach })
        {
            users.Add(new UserAccount { Id = accountRole.ToString(), TenantId = "fixture-team", Role = accountRole, Username = accountRole.ToString(), MustChangePassword = false });
            profiles.Add(new PersonProfile { UserId = accountRole.ToString(), FullName = accountRole == UserRole.Coach ? "Trần Hoàng Nam" : "AWAKEN " + accountRole });
        }
        var names = new[] { "Nguyễn Minh Anh", "Trần Gia Bảo", "Lê Hoàng Nam", "Phạm Quốc Huy", "Vũ Đức Anh", "Đỗ Minh Khang" };
        for (var i = 0; i < 18; i++)
        {
            users.Add(new UserAccount { Id = "trainee-" + i, TenantId = "fixture-team", Role = UserRole.Trainee, Username = "trainee" + i, MustChangePassword = false });
            profiles.Add(new PersonProfile { UserId = "trainee-" + i, FullName = i < names.Length ? names[i] : "Học viên " + (i + 1), DateOfBirth = new DateTime(2014, 5, 20) });
        }
        var classes = new[]
        {
            new TrainingClass { Id = "u12", Name = "Lớp U12 · Cơ bản", VenueId = "pitch", ManagerUserId = "Manager", ScheduleDays = "0,1,2,3,4,5,6", StartDate = DateTime.Today.AddMonths(-1), StartTimeMinutes = 17 * 60, EndTimeMinutes = 18 * 60 + 30 },
            new TrainingClass { Id = "u15", Name = "Lớp U15 · Nâng cao", VenueId = "pitch", ManagerUserId = "Manager", ScheduleDays = "0,1,2,3,4,5,6", StartDate = DateTime.Today.AddMonths(-1), StartTimeMinutes = 18 * 60 + 30, EndTimeMinutes = 20 * 60 }
        };
        var current = users.Single(user => role == UserRole.Trainee ? user.Id == "trainee-0" : user.Role == role);
        _snapshot = new CloudSnapshotEntityCollections
        {
            TenantId = "fixture-team", SyncVersion = 1, CurrentUser = current, CurrentProfile = profiles.Single(p => p.UserId == current.Id),
            Club = new ClubProfile { TeamName = "AWAKEN Community FCM" }, Users = users, Profiles = profiles,
            Venues = [new Venue { Id = "pitch", Name = "Sân bóng AWAKEN" }], Classes = classes,
            ClassCoaches = classes.Select(c => new ClassCoachAssignment { ClassId = c.Id, CoachUserId = "Coach", SalaryPerSessionVnd = 200000 }).ToList(),
            ClassEnrollments = users.Where(u => u.Role == UserRole.Trainee).Select(u => new ClassEnrollment { ClassId = "u12", TraineeUserId = u.Id, EnrolledAtUtc = DateTime.UtcNow.AddMonths(-1) }).ToList(),
            TrainingSessions = [new TrainingSession { Id = "session", ClassId = "u12", SessionDate = DateTime.Today, Status = SessionStatus.Draft }],
            SessionCoaches = [new SessionCoachAssignment { SessionId = "session", CoachUserId = "Coach" }],
            CoachCheckIns = [new CoachCheckIn { SessionId = "session", CoachUserId = "Coach", CheckedInAtUtc = DateTime.UtcNow.AddMinutes(-5) }],
            AttendanceRecords = users.Where(u => u.Role == UserRole.Trainee).Select(u => new AttendanceRecord { SessionId = "session", TraineeUserId = u.Id, Status = u.Id == "trainee-1" ? AttendanceStatus.Absent : AttendanceStatus.Present }).ToList(),
            TuitionInvoices = [new TuitionInvoice { Id = "invoice", TraineeUserId = "trainee-0", ClassId = "u12", AmountVnd = 600000, Status = InvoiceStatus.ProofSubmitted }]
        };
        state.Replace(_snapshot);
        _awards.Clear();
        var awards = new[] { ("tien_bo", "Tiến bộ", 20), ("tich_cuc", "Tích cực", 15), ("no_luc_xuat_sac", "Nỗ lực xuất sắc", 30), ("huy_chuong_vang", "Huy chương vàng", 150), ("huy_chuong_bac", "Huy chương bạc", 100), ("huy_chuong_dong", "Huy chương đồng", 60), ("the_vang", "Thẻ vàng", -10) };
        for (var i = 0; i < awards.Length; i++)
        {
            var (key, name, points) = awards[i];
            _awards.Add(new CloudAchievementSnapshot
            {
                Id = "award-" + i, TenantId = "fixture-team", TraineeUserId = "trainee-0", TraineeName = names[0],
                BadgeId = key, BadgeKey = key, BadgeName = name, BadgeAssetKey = key, Points = points,
                Category = AchievementCategory.WeeklyClassRanking, ClassId = "u12", ClassName = classes[0].Name,
                AwardedForDate = DateTimeOffset.UtcNow.AddDays(i < 3 ? -i : -60),
                VisibleUntil = DateTimeOffset.UtcNow.AddDays(i < 3 ? 27 : -30),
                Status = i < 3 ? AchievementStatus.Approved : AchievementStatus.Expired,
                Reason = "Ghi nhận nỗ lực trong buổi tập.", CoachName = "Trần Hoàng Nam"
            });
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Host != "ui-fixture.invalid") throw new InvalidOperationException("Non-fixture host blocked");
        var path = request.RequestUri.AbsolutePath;
        object body;
        var status = HttpStatusCode.OK;
        if (path.EndsWith("achievement-badges"))
            body = new { badges = _awards.Select(a => new CloudAchievementBadge { Id = a.BadgeId, Key = a.BadgeKey, AssetKey = a.BadgeAssetKey, Name = a.BadgeName, Points = a.Points, Category = a.Category }).ToArray() };
        else if (path.EndsWith("achievements"))
        {
            if (_mode == "error") { status = HttpStatusCode.ServiceUnavailable; body = new { error = new { code = "fixture_error", message = "Kết nối kiểm thử đang tạm ngắt." } }; _mode = "normal"; }
            else body = new { achievements = _mode == "empty" ? [] : _awards.ToArray(), totalPoints = _mode == "empty" ? 0 : 365, pendingCount = 0 };
        }
        else if (path.Contains("/attendance/") && request.Method == HttpMethod.Put)
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            LastSavedRosterCount = doc.RootElement.GetProperty("records").GetArrayLength();
            LastSavedSubmit = doc.RootElement.GetProperty("submit").GetBoolean();
            global::Android.Util.Log.Info("FCM-UI-QA", $"attendance records={LastSavedRosterCount} submit={LastSavedSubmit}");
            foreach (var record in doc.RootElement.GetProperty("records").EnumerateArray())
                global::Android.Util.Log.Info("FCM-UI-QA", $"{record.GetProperty("traineeUserId").GetString()}={record.GetProperty("status").GetString()}");
            body = new { ok = true };
        }
        else if (path.EndsWith("snapshot")) body = new { unchanged = true, syncVersion = 1 };
        else { status = HttpStatusCode.NotFound; body = new { error = new { code = "unhandled_fixture", message = "Fixture has no route: " + path } }; }
        global::Android.Util.Log.Info("FCM-UI-QA", $"{request.Method} {path} {(int)status}");
        return new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body, _json), Encoding.UTF8, "application/json") };
    }
}
