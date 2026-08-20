using System.Globalization;
using System.Net;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services.Online;
using SQLite;

namespace CommunityFootballClubManager.Services;

public sealed partial class AppDatabase
{
    private const string DatabaseFileName = "community_football_club.db3";
    public const string NewAccountDefaultPassword = "12345678";
    private readonly PasswordService _passwordService;
    private readonly CloudApiClient _cloudApi;
    private readonly CloudTokenStore _cloudTokens;
    private readonly CloudBackendOptions _cloudOptions;
    private readonly OnlineDataState _onlineState;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private readonly SemaphoreSlim _onlineSnapshotLock = new(1, 1);
    private int _cloudProjectionRefreshQueued;
    private SQLiteAsyncConnection? _database;
    private bool _initialized;

    public AppDatabase(
        PasswordService passwordService,
        CloudApiClient cloudApi,
        CloudTokenStore cloudTokens,
        CloudBackendOptions cloudOptions,
        OnlineDataState onlineState)
    {
        _passwordService = passwordService;
        _cloudApi = cloudApi;
        _cloudTokens = cloudTokens;
        _cloudOptions = cloudOptions;
        _onlineState = onlineState;
    }

    private bool IsOnline => _cloudOptions.IsConfigured;

    private OnlineDataState Online => IsOnline
        ? _onlineState
        : throw new InvalidOperationException("Online state is not available in offline mode.");

    private void SetOnlineIdentity(
        CloudUserSnapshot userSnapshot,
        CloudProfileSnapshot? profileSnapshot,
        CloudClubSnapshot? clubSnapshot)
    {
        var user = CloudSnapshotMapper.ToEntity(userSnapshot);
        var profile = profileSnapshot is null
            ? new PersonProfile { UserId = user.Id, FullName = user.Username }
            : CloudSnapshotMapper.ToEntity(profileSnapshot);
        var club = clubSnapshot is null ? null : CloudSnapshotMapper.ToEntity(clubSnapshot);
        Online.SetIdentity(user, profile, club);
    }

    private async Task EnsureOnlineSnapshotAsync()
    {
        if (!IsOnline || Online.IsLoaded)
        {
            return;
        }

        await _onlineSnapshotLock.WaitAsync();
        try
        {
            // Several pages can request the projection together during app
            // startup.  Only one request is allowed to cross the network; the
            // other callers reuse the result that was just installed.
            if (Online.IsLoaded)
            {
                return;
            }

            var wireSnapshot = await _cloudApi.GetSnapshotAsync(Online.SyncVersion > 0
                ? Online.SyncVersion
                : null);
            if (wireSnapshot.Unchanged)
            {
                Online.MarkFresh(wireSnapshot.SyncVersion);
                return;
            }
            var imported = CloudSnapshotMapper.Import(wireSnapshot);
            if (string.IsNullOrWhiteSpace(imported.TenantId)
                || (!string.IsNullOrWhiteSpace(Online.TenantId)
                    && !string.Equals(imported.TenantId, Online.TenantId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Snapshot không thuộc đúng đội bóng đang đăng nhập.");
            }

            Online.Replace(imported);
        }
        finally
        {
            _onlineSnapshotLock.Release();
        }
    }

    private async Task ReloadOnlineSnapshotAsync()
    {
        if (!IsOnline)
            return;
        await _onlineSnapshotLock.WaitAsync();
        try
        {
            var wireSnapshot = await _cloudApi.GetSnapshotAsync();
            Online.Replace(CloudSnapshotMapper.Import(wireSnapshot));
        }
        finally
        {
            _onlineSnapshotLock.Release();
        }
    }

    private async Task<UserAccount> RequireOnlineUserAsync(string userId)
    {
        if (Online.CurrentUser is { } currentUser && currentUser.Id == userId)
        {
            return currentUser;
        }

        await EnsureOnlineSnapshotAsync();
        return Online.User(userId)
               ?? throw new InvalidOperationException("Không tìm thấy tài khoản online hiện tại.");
    }

    private async Task<UserAccount> RequireOnlineRoleAsync(string userId, UserRole role)
    {
        var user = await RequireOnlineUserAsync(userId);
        if (user.Role != role)
        {
            throw new UnauthorizedAccessException("Tài khoản không có quyền thực hiện thao tác này.");
        }

        return user;
    }

    private static MemberRow ToMember(OnlineDataState state, UserAccount user)
    {
        return new MemberRow(
            user,
            state.Profile(user.Id) ?? new PersonProfile { UserId = user.Id, FullName = user.Username });
    }

    private SQLiteAsyncConnection Database =>
        IsOnline
            ? throw new InvalidOperationException("SQLite đã được tắt trong chế độ online.")
            : _database ?? throw new InvalidOperationException("Database chưa được khởi tạo.");

    public string DatabasePath =>
        Path.Combine(FileSystem.AppDataDirectory, DatabaseFileName);

    public async Task InitializeAsync()
    {
        // Online builds use D1 as the sole source of truth. Do not create,
        // migrate, seed or hydrate the legacy SQLite cache in that mode.
        if (IsOnline)
        {
            _initialized = true;
            return;
        }

        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            _database = new SQLiteAsyncConnection(
                DatabasePath,
                SQLiteOpenFlags.ReadWrite
                | SQLiteOpenFlags.Create
                | SQLiteOpenFlags.SharedCache);

            var hadCheckInApprovalStatus = await Database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('CoachCheckIns') WHERE name = 'ApprovalStatus'") > 0;
            var hadCheckInDuration = await Database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('CoachCheckIns') WHERE name = 'DurationSeconds'") > 0;
            var hadTuitionSupportColumn = await Database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('UserAccounts') WHERE name = 'IsTuitionSupported'") > 0;
            var hadCoachPositionColumn = await Database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM pragma_table_info('PersonProfiles') WHERE name = 'CoachPosition'") > 0;

            await Database.CreateTableAsync<UserAccount>();
            await Database.CreateTableAsync<PersonProfile>();
            await Database.CreateTableAsync<ExternalAccountLink>();
            await Database.CreateTableAsync<ClubProfile>();
            await Database.CreateTableAsync<Venue>();
            await Database.CreateTableAsync<TrainingClass>();
            await Database.CreateTableAsync<ClassCoachAssignment>();
            await Database.CreateTableAsync<ClassEnrollment>();
            await Database.CreateTableAsync<TrainingSession>();
            await Database.CreateTableAsync<SessionCoachAssignment>();
            await Database.CreateTableAsync<CoachCheckIn>();
            await Database.CreateTableAsync<AttendanceRecord>();
            await Database.CreateTableAsync<TuitionInvoice>();
            await Database.CreateTableAsync<PaymentProof>();
            await Database.CreateTableAsync<Receipt>();
            await Database.CreateTableAsync<CoachSalary>();
            await Database.CreateTableAsync<TraineeEvaluation>();
            await Database.CreateTableAsync<AppNotification>();
            await Database.CreateTableAsync<AuditLog>();

            if (!hadCoachPositionColumn && await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('PersonProfiles') WHERE name = 'CoachPosition'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE PersonProfiles ADD COLUMN CoachPosition TEXT NOT NULL DEFAULT ''");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('TrainingClasses') WHERE name = 'TuitionSessionCount'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE TrainingClasses ADD COLUMN TuitionSessionCount INTEGER NOT NULL DEFAULT 4");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('TrainingClasses') WHERE name = 'StartDate'") == 0)
            {
                var migrationDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                await Database.ExecuteAsync(
                    $"ALTER TABLE TrainingClasses ADD COLUMN StartDate TEXT NOT NULL DEFAULT '{migrationDate}'");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('TrainingClasses') WHERE name = 'EvaluationRequestOpen'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE TrainingClasses ADD COLUMN EvaluationRequestOpen INTEGER NOT NULL DEFAULT 0");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('UserAccounts') WHERE name = 'TenantId'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE UserAccounts ADD COLUMN TenantId TEXT NOT NULL DEFAULT ''");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('ClassEnrollments') WHERE name = 'CycleFeeVnd'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE ClassEnrollments ADD COLUMN CycleFeeVnd INTEGER NOT NULL DEFAULT 0");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('ClassEnrollments') WHERE name = 'IsTrial'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE ClassEnrollments ADD COLUMN IsTrial INTEGER NOT NULL DEFAULT 0");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('ClassEnrollments') WHERE name = 'TrialSessionCount'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE ClassEnrollments ADD COLUMN TrialSessionCount INTEGER NOT NULL DEFAULT 0");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('TuitionInvoices') WHERE name = 'CycleCount'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE TuitionInvoices ADD COLUMN CycleCount INTEGER NOT NULL DEFAULT 1");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('TuitionInvoices') WHERE name = 'CycleFeeVnd'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE TuitionInvoices ADD COLUMN CycleFeeVnd INTEGER NOT NULL DEFAULT 0");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('TuitionInvoices') WHERE name = 'CycleNumber'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE TuitionInvoices ADD COLUMN CycleNumber INTEGER NOT NULL DEFAULT 0");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('TuitionInvoices') WHERE name = 'AttendedSessionCount'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE TuitionInvoices ADD COLUMN AttendedSessionCount INTEGER NOT NULL DEFAULT 0");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('TuitionInvoices') WHERE name = 'PlannedSessionCount'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE TuitionInvoices ADD COLUMN PlannedSessionCount INTEGER NOT NULL DEFAULT 0");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('TuitionInvoices') WHERE name = 'AmountPerSessionVnd'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE TuitionInvoices ADD COLUMN AmountPerSessionVnd INTEGER NOT NULL DEFAULT 0");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('CoachCheckIns') WHERE name = 'CheckOutSelfiePath'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE CoachCheckIns ADD COLUMN CheckOutSelfiePath TEXT NOT NULL DEFAULT ''");
            }

            if (await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('CoachCheckIns') WHERE name = 'CheckedOutAtUtc'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE CoachCheckIns ADD COLUMN CheckedOutAtUtc TEXT NULL");
            }

            if (!hadCheckInDuration && await Database.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('CoachCheckIns') WHERE name = 'DurationSeconds'") == 0)
            {
                await Database.ExecuteAsync(
                    "ALTER TABLE CoachCheckIns ADD COLUMN DurationSeconds INTEGER NOT NULL DEFAULT 0");
            }

            // Existing installations receive the new nullable SQLite column through
            // CreateTableAsync's migration. Normalize legacy rows before materializing
            // them into the non-nullable long property.
            await Database.ExecuteAsync(
                "UPDATE ClassCoachAssignments SET SalaryPerSessionVnd = 0 "
                + "WHERE SalaryPerSessionVnd IS NULL");
            await Database.ExecuteAsync(
                "UPDATE ClassEnrollments SET CycleFeeVnd = MonthlyFeeVnd "
                + "WHERE (CycleFeeVnd IS NULL OR CycleFeeVnd = 0) AND MonthlyFeeVnd > 0");
            await Database.ExecuteAsync(
                "UPDATE CoachCheckIns SET SalaryPerSessionVndSnapshot = 0 "
                + "WHERE SalaryPerSessionVndSnapshot IS NULL");
            await Database.ExecuteAsync(
                "UPDATE CoachCheckIns SET DurationSeconds = CAST(MAX(0, (julianday(CheckedOutAtUtc) - julianday(CheckedInAtUtc)) * 86400) AS INTEGER) "
                + "WHERE DurationSeconds = 0 AND CheckedOutAtUtc IS NOT NULL");
            if (!hadTuitionSupportColumn)
            {
                // Existing accounts keep their original tuition behavior after the
                // schema migration. Only accounts explicitly created as supported
                // are exempt from invoices and reminders.
                await Database.ExecuteAsync(
                    "UPDATE UserAccounts SET IsTuitionSupported = 0 "
                    + "WHERE IsTuitionSupported IS NULL");
            }
            if (!hadCheckInApprovalStatus)
            {
                // Check-in created by older app versions was accepted immediately.
                // Preserve that behavior during migration; only new uploads wait for review.
                await Database.ExecuteAsync(
                    "UPDATE CoachCheckIns SET ApprovalStatus = ?, ReviewedAtUtc = CheckedInAtUtc",
                    (int)CoachCheckInApprovalStatus.Approved);
            }

            await SeedAsync();
            await BackfillSessionCoachAssignmentsAsync();
            await EnsureMissedCoachCheckInsAsync(DateTime.Today);
            await EnsureSalaryRowsForExistingCheckInsAsync();
            await EnsureRecurringDataAsync(DateTime.Today);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private async Task SeedAsync()
    {
        var admin = await Database.Table<UserAccount>()
            .Where(item => item.UsernameNormalized == "ADMIN")
            .FirstOrDefaultAsync();

        // Admin is provisioned only by the Worker's secret-protected setup
        // endpoint. Never place a reusable Admin credential or verifier in the
        // mobile source. Existing installations keep their row solely for data
        // migration, but Admin authentication is always online below.
        if (admin is not null)
        {
            if (admin.Role != UserRole.Admin)
            {
                // Demo versions before 2.0 seeded `admin` as a Founder. Keep
                // the same credentials but migrate this reserved account to
                // the new system-admin role so it cannot access team data.
                admin.Role = UserRole.Admin;
                admin.UpdatedAtUtc = DateTime.UtcNow;
                await Database.UpdateAsync(admin);
            }

            if (admin.MustChangePassword)
            {
                // Account quản trị demo được phép vào thẳng ứng dụng, kể cả trên
                // database đã tạo bởi phiên bản cũ từng bắt đổi mật khẩu lần đầu.
                admin.MustChangePassword = false;
                admin.UpdatedAtUtc = DateTime.UtcNow;
                await Database.UpdateAsync(admin);
            }

            var adminProfile = await Database.FindAsync<PersonProfile>(admin.Id);
            if (adminProfile is not null
                && (string.IsNullOrWhiteSpace(adminProfile.FullName)
                    || adminProfile.FullName.Contains("Sáng lập", StringComparison.OrdinalIgnoreCase)))
            {
                adminProfile.FullName = "Quản trị hệ thống";
                adminProfile.UpdatedAtUtc = DateTime.UtcNow;
                await Database.UpdateAsync(adminProfile);
            }
        }

        var club = await Database.FindAsync<ClubProfile>(1);
        if (club is null)
        {
            await Database.InsertAsync(new ClubProfile
            {
                Id = 1,
                TeamName = "Community Football Club",
                FounderName = "Sáng lập & Điều hành",
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
    }

    public async Task<LoginResult> AuthenticateAsync(string username, string password)
    {
        if (IsOnline)
        {
            try
            {
                await _cloudTokens.ClearAsync();
                var response = await _cloudApi.LoginAsync(new CloudLoginRequest(
                    username.Trim(),
                    password,
                    CurrentDeviceName()));
                var cloudUser = response.User
                                 ?? throw new InvalidOperationException(
                                     "Máy chủ không trả về thông tin tài khoản.");
                SetOnlineIdentity(
                    cloudUser,
                    response.Profile,
                    response.ActiveClub ?? response.Club);
                // Do not block login on a full tenant projection. The first
                // screen loads it lazily from D1, while the identity/session
                // response above is enough to route the user immediately.
                Online.Clear();
                SetOnlineIdentity(
                    cloudUser,
                    response.Profile,
                    response.ActiveClub ?? response.Club);
                return new LoginResult(
                    true,
                    "Đăng nhập thành công.",
                    Online.CurrentUser);
            }
            catch (ApiException exception)
            {
                await _cloudTokens.ClearAsync();
                return new LoginResult(false, exception.Message);
            }
            catch (Exception exception) when (IsCloudUnavailable(exception))
            {
                await _cloudTokens.ClearAsync();
                return new LoginResult(
                    false,
                    "Không thể kết nối Cloudflare. Vui lòng kiểm tra mạng và thử lại.");
            }
        }

        await InitializeAsync();

        var normalized = Normalize(username);
        var user = await Database.Table<UserAccount>()
            .Where(item => item.UsernameNormalized == normalized)
            .FirstOrDefaultAsync();

        if (user is null)
        {
            return new LoginResult(false, "Username hoặc mật khẩu không đúng.");
        }

        if (user.Role == UserRole.Admin)
        {
            return new LoginResult(
                false,
                "Tài khoản Admin bắt buộc kết nối máy chủ để đăng nhập.");
        }

        if (!user.IsActive)
        {
            return new LoginResult(false, "Tài khoản đang bị khóa. Vui lòng liên hệ người điều hành.");
        }

        if (user.LockoutUntilUtc is { } lockout && lockout > DateTime.UtcNow)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((lockout - DateTime.UtcNow).TotalMinutes));
            return new LoginResult(false, $"Tài khoản tạm khóa. Thử lại sau {minutes} phút.");
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash)
            || string.IsNullOrWhiteSpace(user.PasswordSalt)
            || !_passwordService.Verify(
                password,
                user.PasswordHash,
                user.PasswordSalt,
                user.PasswordIterations))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= 5)
            {
                user.LockoutUntilUtc = DateTime.UtcNow.AddMinutes(10);
                user.FailedLoginCount = 0;
            }

            user.UpdatedAtUtc = DateTime.UtcNow;
            await Database.UpdateAsync(user);
            return new LoginResult(false, "Username hoặc mật khẩu không đúng.");
        }

        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(user);
        await EnsureRecurringDataAsync(DateTime.Today);
        return new LoginResult(true, "Đăng nhập thành công.", user);
    }

    public async Task<LoginResult> RestoreSessionAsync(string userId)
    {
        if (IsOnline)
        {
            var cloudSession = await _cloudTokens.LoadRefreshSessionAsync();
            if (cloudSession is null)
            {
                return new LoginResult(false, "Phiên đăng nhập không còn hợp lệ.");
            }

            try
            {
                var response = await _cloudApi.RefreshSessionAsync();
                var cloudUser = response.User
                                 ?? throw new InvalidOperationException(
                                     "Máy chủ không trả về thông tin tài khoản.");
                SetOnlineIdentity(
                    cloudUser,
                    response.Profile,
                    response.ActiveClub ?? response.Club);
                Online.Clear();
                SetOnlineIdentity(
                    cloudUser,
                    response.Profile,
                    response.ActiveClub ?? response.Club);
                return new LoginResult(true, "Đã khôi phục phiên đăng nhập online.", Online.CurrentUser);
            }
            catch (ApiException exception)
            {
                await _cloudTokens.ClearAsync();
                return new LoginResult(false, exception.Message);
            }
            catch (Exception exception) when (IsCloudUnavailable(exception))
            {
                return new LoginResult(false, "Không thể xác thực phiên online lúc này.");
            }
        }

        await InitializeAsync();

        var user = await Database.FindAsync<UserAccount>(userId);
        if (user is null
            || !user.IsActive
            || user.Role == UserRole.Admin
            || string.IsNullOrWhiteSpace(user.PasswordHash)
            || string.IsNullOrWhiteSpace(user.PasswordSalt))
        {
            return new LoginResult(false, "Phiên đăng nhập không còn hợp lệ.");
        }

        // When the production Worker is configured, do not silently restore a
        // legacy local-password session. Such a session cannot perform online
        // actions (including Google OAuth binding) and would make the app look
        // connected while it is actually offline.
        if (_cloudOptions.IsConfigured)
        {
            return new LoginResult(
                false,
                "Phiên cũ đang ở chế độ offline. Vui lòng đăng nhập lại bằng account online.");
        }

        await EnsureRecurringDataAsync(DateTime.Today);
        return new LoginResult(true, "Đã khôi phục phiên đăng nhập.", user);
    }

    private static void EnsureGoogleProvider(ExternalAuthProvider provider)
    {
        if (provider != ExternalAuthProvider.Google)
        {
            throw new NotSupportedException("Ứng dụng hiện chỉ hỗ trợ đăng nhập và Bind Account bằng Google.");
        }
    }

    public async Task<LoginResult> AuthenticateExternalAsync(
        ExternalAuthProvider provider,
        string externalEmail)
    {
        EnsureGoogleProvider(provider);
        if (IsOnline)
        {
            return new LoginResult(
                false,
                "Đăng nhập Google cần dùng luồng OAuth xác thực trực tiếp.");
        }

        await InitializeAsync();
        var normalized = NormalizeEmail(externalEmail);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new LoginResult(false, "Vui lòng nhập email đã liên kết.");
        }

        var link = await Database.Table<ExternalAccountLink>()
            .Where(item => item.Provider == provider
                           && item.ExternalSubjectNormalized == normalized)
            .FirstOrDefaultAsync();
        if (link is null)
        {
            return new LoginResult(
                false,
                $"Email này chưa được Bind Account với {DomainText.ExternalProvider(provider)}.");
        }

        var user = await Database.FindAsync<UserAccount>(link.UserId);
        if (user is null || !user.IsActive)
        {
            return new LoginResult(false, "Tài khoản đang bị khóa hoặc không còn tồn tại.");
        }

        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(user);
        await EnsureRecurringDataAsync(DateTime.Today);
        return new LoginResult(true, "Đăng nhập thành công.", user);
    }

    public async Task<LoginResult> AuthenticateExternalOAuthAsync(
        ExternalAuthProvider provider,
        string authorizationTicket,
        string redirectUri)
    {
        EnsureGoogleProvider(provider);
        await InitializeAsync();
        if (!_cloudOptions.IsConfigured)
        {
            return new LoginResult(false, "Đăng nhập OAuth cần kết nối backend online.");
        }

        try
        {
            var response = await _cloudApi.ExchangeOAuthCodeAsync(
                new CloudOAuthExchangeRequest(
                    provider,
                    authorizationTicket,
                    string.Empty,
                    redirectUri,
                    CurrentDeviceName()));
            var cloudUser = response.User
                            ?? throw new InvalidOperationException(
                                "Máy chủ không trả về thông tin tài khoản.");
            SetOnlineIdentity(
                cloudUser,
                response.Profile,
                response.ActiveClub ?? response.Club);
            Online.Clear();
            SetOnlineIdentity(
                cloudUser,
                response.Profile,
                response.ActiveClub ?? response.Club);
            return new LoginResult(true, "Đăng nhập OAuth thành công.", Online.CurrentUser);
        }
        catch (ApiException exception)
        {
            await _cloudTokens.ClearAsync();
            return new LoginResult(false, exception.Message);
        }
    }

    public async Task BindExternalOAuthAsync(
        string actorUserId,
        ExternalAuthProvider provider,
        string authorizationTicket,
        string redirectUri)
    {
        EnsureGoogleProvider(provider);
        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            var onlineLinkResponse = await _cloudApi.LinkOAuthAsync(
                new CloudOAuthExchangeRequest(
                    provider,
                    authorizationTicket,
                    string.Empty,
                    redirectUri,
                    CurrentDeviceName()));
            return;
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        var hasCloudSession = await HasCloudSessionForAsync(actorUserId);
        if (!_cloudOptions.IsConfigured || !hasCloudSession)
        {
            throw new InvalidOperationException(
                "Account chưa có phiên Cloudflare hợp lệ. Nếu đây là Founder vừa được Admin duyệt, hãy Đăng xuất rồi đăng nhập lại online trước khi Bind Google.");
        }

        var response = await _cloudApi.LinkOAuthAsync(
            new CloudOAuthExchangeRequest(
                provider,
                authorizationTicket,
                string.Empty,
                redirectUri,
                CurrentDeviceName()));
        await BindExternalAccountAsync(
            actorUserId,
            response.Provider,
            response.ExternalSubject,
            response.DisplayName,
            response.Email,
            response.ExternalSubject);
    }

    public async Task<IReadOnlyList<ExternalAccountLink>> GetExternalAccountLinksAsync(
        string actorUserId)
    {
        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            var response = await _cloudApi.GetOAuthLinksAsync();
            return response.Links
                .Where(item => item.Provider == ExternalAuthProvider.Google)
                .Select(item => new ExternalAccountLink
                {
                    Id = item.Id,
                    UserId = actorUserId,
                    Provider = item.Provider,
                    ExternalSubjectNormalized = item.ExternalSubject,
                    Email = item.Email,
                    DisplayName = item.DisplayName,
                    LinkedAtUtc = item.LinkedAtUtc?.UtcDateTime ?? DateTime.UtcNow,
                    UpdatedAtUtc = item.UpdatedAtUtc?.UtcDateTime ?? DateTime.UtcNow
                })
                .ToList();
        }

        await InitializeAsync();
        await RequireUserAsync(actorUserId);
        return (await Database.Table<ExternalAccountLink>()
                .Where(item => item.UserId == actorUserId)
                .ToListAsync())
            .OrderBy(item => item.Provider)
            .ToList();
    }

    public async Task BindExternalAccountAsync(
        string actorUserId,
        ExternalAuthProvider provider,
        string externalEmail,
        string displayName,
        string? emailOverride = null,
        string? subjectOverride = null)
    {
        EnsureGoogleProvider(provider);
        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            if (string.IsNullOrWhiteSpace(subjectOverride))
            {
                throw new InvalidOperationException(
                    "Bind Google phải hoàn tất qua OAuth xác thực trực tiếp.");
            }

            await _cloudApi.PostAsync(
                "auth/oauth/exchange",
                new CloudOAuthExchangeRequest(
                    provider,
                    subjectOverride,
                    string.Empty,
                    string.Empty,
                    CurrentDeviceName()),
                EntityId.New());
            return;
        }

        await InitializeAsync();
        await RequireUserAsync(actorUserId);
        var email = (emailOverride ?? externalEmail).Trim();
        var hasSubjectOverride = !string.IsNullOrWhiteSpace(subjectOverride);
        var normalized = hasSubjectOverride
            ? subjectOverride!.Trim()
            : NormalizeEmail(email);
        var invalidEmail = !hasSubjectOverride
            && (!normalized.Contains('@')
                || normalized.StartsWith('@')
                || normalized.EndsWith('@'));
        if (string.IsNullOrWhiteSpace(normalized) || invalidEmail)
        {
            throw new InvalidOperationException("Vui lòng nhập email Google hợp lệ.");
        }

        var conflicting = await Database.Table<ExternalAccountLink>()
            .Where(item => item.Provider == provider
                           && item.ExternalSubjectNormalized == normalized)
            .FirstOrDefaultAsync();
        if (conflicting is not null && conflicting.UserId != actorUserId)
        {
            throw new InvalidOperationException(
                $"Email {DomainText.ExternalProvider(provider)} này đã liên kết với account khác.");
        }

        var link = await Database.Table<ExternalAccountLink>()
            .Where(item => item.UserId == actorUserId && item.Provider == provider)
            .FirstOrDefaultAsync();
        if (link is null)
        {
            link = new ExternalAccountLink
            {
                UserId = actorUserId,
                Provider = provider,
                LinkedAtUtc = DateTime.UtcNow
            };
        }

        link.ExternalSubjectNormalized = normalized;
        link.Email = email;
        link.DisplayName = displayName.Trim();
        link.UpdatedAtUtc = DateTime.UtcNow;
        await Database.InsertOrReplaceAsync(link);
        await AddAuditAsync(
            actorUserId,
            "BindExternalAccount",
            nameof(ExternalAccountLink),
            link.Id,
            DomainText.ExternalProvider(provider));
    }

    public async Task UnbindExternalAccountAsync(
        string actorUserId,
        ExternalAuthProvider provider)
    {
        EnsureGoogleProvider(provider);
        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            await _cloudApi.DeleteAsync(
                $"auth/oauth/links/{Uri.EscapeDataString(provider.ToString().ToLowerInvariant())}",
                Guid.NewGuid().ToString("N"));
            return;
        }

        await InitializeAsync();
        await RequireUserAsync(actorUserId);
        var link = await Database.Table<ExternalAccountLink>()
            .Where(item => item.UserId == actorUserId && item.Provider == provider)
            .FirstOrDefaultAsync();
        if (link is null)
        {
            return;
        }

        await Database.DeleteAsync(link);
        await AddAuditAsync(
            actorUserId,
            "UnbindExternalAccount",
            nameof(ExternalAccountLink),
            link.Id,
            DomainText.ExternalProvider(provider));
    }

    public async Task<PersonProfile> GetProfileAsync(string userId)
    {
        if (IsOnline)
        {
            if (Online.CurrentProfile is { } currentProfile && currentProfile.UserId == userId)
            {
                await MaterializeProfileImageAsync(userId, currentProfile);
                return currentProfile;
            }
            await EnsureOnlineSnapshotAsync();
            var profile = Online.Profile(userId)
                          ?? new PersonProfile { UserId = userId };
            await MaterializeProfileImageAsync(
                Online.CurrentUser?.Id ?? userId,
                profile);
            return profile;
        }

        await InitializeAsync();
        return await Database.FindAsync<PersonProfile>(userId)
               ?? new PersonProfile { UserId = userId };
    }


    public async Task<UserAccount?> GetUserAsync(string userId)
    {
        if (IsOnline)
        {
            if (Online.CurrentUser is { } currentUser && currentUser.Id == userId)
            {
                return currentUser;
            }
            await EnsureOnlineSnapshotAsync();
            return Online.User(userId);
        }

        await InitializeAsync();
        return await Database.FindAsync<UserAccount>(userId);
    }

    public async Task<MemberRow> GetFounderAsync(string actorUserId)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            var onlineFounder = Online.Users
                .Where(item => item.Role == UserRole.Founder
                               && item.IsActive
                               && item.TenantId == onlineActor.TenantId)
                .OrderBy(item => item.CreatedAtUtc)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Không tìm thấy account Founder.");
            var member = ToMember(Online, onlineFounder);
            await MaterializeMemberImagesAsync(onlineActor.Id, [member]);
            return member;
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        var founder = (await Database.Table<UserAccount>().ToListAsync())
            .Where(item => item.Role == UserRole.Founder
                           && item.IsActive
                           && (string.IsNullOrWhiteSpace(actor.TenantId)
                               || item.TenantId == actor.TenantId))
            .OrderBy(item => item.CreatedAtUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Không tìm thấy account Founder.");
        var profile = await Database.FindAsync<PersonProfile>(founder.Id)
                      ?? new PersonProfile { UserId = founder.Id };
        return new MemberRow(founder, profile);
    }

    public async Task<IReadOnlyList<MemberRow>> GetMembersAsync(
        string actorUserId,
        UserRole? role = null,
        bool includeInactive = false)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            var onlineAllowedIds = GetVisibleMemberIdsOnline(onlineActor);
            var members = Online.Users
                .Where(item => item.TenantId == onlineActor.TenantId
                               && (includeInactive || item.IsActive)
                               && (role is null || item.Role == role.Value)
                               && onlineAllowedIds.Contains(item.Id))
                .Select(item => ToMember(Online, item))
                .OrderBy(item => item.DisplayName)
                .ToList();
            await MaterializeMemberImagesAsync(onlineActor.Id, members);
            return members;
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        var users = await Database.Table<UserAccount>().ToListAsync();
        if (!string.IsNullOrWhiteSpace(actor.TenantId))
        {
            users = users.Where(item => item.TenantId == actor.TenantId).ToList();
        }
        if (!includeInactive)
        {
            users = users.Where(item => item.IsActive).ToList();
        }

        if (role is not null)
        {
            users = users.Where(item => item.Role == role.Value).ToList();
        }

        var allowedIds = await GetVisibleMemberIdsAsync(actor);
        users = users.Where(item => allowedIds.Contains(item.Id)).ToList();

        var profiles = await Database.Table<PersonProfile>().ToListAsync();
        var profileMap = profiles.ToDictionary(item => item.UserId);

        return users
            .Select(user => new MemberRow(
                user,
                profileMap.GetValueOrDefault(user.Id)
                ?? new PersonProfile { UserId = user.Id }))
            .OrderBy(item => item.DisplayName)
            .ToList();
    }

    private async Task<HashSet<string>> GetVisibleMemberIdsAsync(UserAccount actor)
    {
        var result = new HashSet<string> { actor.Id };
        if (RoleCapabilities.IsFounderLike(actor.Role) || actor.Role == UserRole.Manager)
        {
            var all = await Database.Table<UserAccount>().ToListAsync();
            if (!string.IsNullOrWhiteSpace(actor.TenantId))
            {
                all = all.Where(item => item.TenantId == actor.TenantId).ToList();
            }
            result.UnionWith(all.Select(item => item.Id));
            return result;
        }

        var classIds = actor.Role == UserRole.Coach
            ? (await Database.Table<ClassCoachAssignment>()
                .Where(item => item.CoachUserId == actor.Id && item.IsActive)
                .ToListAsync())
                .Select(item => item.ClassId)
                .ToHashSet()
            : (await Database.Table<ClassEnrollment>()
                .Where(item => item.TraineeUserId == actor.Id && item.IsActive)
                .ToListAsync())
                .Select(item => item.ClassId)
                .ToHashSet();

        var coaches = await Database.Table<ClassCoachAssignment>()
            .Where(item => item.IsActive)
            .ToListAsync();
        var trainees = await Database.Table<ClassEnrollment>()
            .Where(item => item.IsActive)
            .ToListAsync();

        result.UnionWith(coaches.Where(item => classIds.Contains(item.ClassId))
            .Select(item => item.CoachUserId));
        result.UnionWith(trainees.Where(item => classIds.Contains(item.ClassId))
            .Select(item => item.TraineeUserId));
        return result;
    }

    private HashSet<string> GetVisibleMemberIdsOnline(UserAccount actor)
    {
        var result = new HashSet<string> { actor.Id };
        if (RoleCapabilities.IsFounderLike(actor.Role) || actor.Role == UserRole.Manager)
        {
            result.UnionWith(Online.Users
                .Where(item => item.TenantId == actor.TenantId)
                .Select(item => item.Id));
            return result;
        }

        var classIds = actor.Role == UserRole.Coach
            ? Online.ClassCoaches
                .Where(item => item.CoachUserId == actor.Id && item.IsActive)
                .Select(item => item.ClassId)
                .ToHashSet()
            : Online.ClassEnrollments
                .Where(item => item.TraineeUserId == actor.Id && item.IsActive)
                .Select(item => item.ClassId)
                .ToHashSet();
        result.UnionWith(Online.ClassCoaches
            .Where(item => item.IsActive && classIds.Contains(item.ClassId))
            .Select(item => item.CoachUserId));
        result.UnionWith(Online.ClassEnrollments
            .Where(item => item.IsActive && classIds.Contains(item.ClassId))
            .Select(item => item.TraineeUserId));
        return result;
    }

    public async Task<UserAccount> CreateUserAsync(
        string actorUserId,
        UserRole role,
        string username,
        string fullName,
        string email,
        string phone,
        bool isTuitionSupported = false,
        string coachPosition = "",
        string guardianName = "",
        string guardianPhone = "")
    {
        coachPosition = role == UserRole.Coach && CoachPositionCatalog.IsValid(coachPosition)
            ? coachPosition.Trim()
            : string.Empty;
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanCreateMember(onlineActor.Role, role))
            {
                throw new UnauthorizedAccessException("Tài khoản không có quyền tạo loại account này.");
            }
            try
            {
                var response = await _cloudApi.PostAsync<CloudCreateUserRequest, CloudAuthResponse>(
                    "users",
                    new CloudCreateUserRequest(
                        username.Trim(),
                        fullName.Trim(),
                        email.Trim(),
                        RoleCapabilities.ToWireRole(role),
                        role == UserRole.Trainee && isTuitionSupported,
                        phone.Trim(),
                        guardianName.Trim(),
                        guardianPhone.Trim(),
                        coachPosition),
                    idempotencyKey: EntityId.New());
                var createdSnapshot = response.User
                                      ?? throw new InvalidOperationException(
                                          "Máy chủ không trả về account vừa tạo.");
                var created = CloudSnapshotMapper.ToEntity(createdSnapshot);
                var onlineProfile = response.Profile is null
                    ? new PersonProfile { UserId = created.Id, FullName = fullName.Trim(), Email = email.Trim(), Phone = phone.Trim() }
                    : CloudSnapshotMapper.ToEntity(response.Profile);
                Online.Upsert(Online.Users, created, item => item.Id == created.Id);
                Online.Upsert(Online.Profiles, onlineProfile, item => item.UserId == onlineProfile.UserId);
                return created;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanCreateMember(actor.Role, role))
        {
            throw new UnauthorizedAccessException("Tài khoản không có quyền tạo loại account này.");
        }

        if (_cloudOptions.IsConfigured
            && !await HasCloudSessionForAsync(actorUserId))
        {
            throw new InvalidOperationException(
                "Account quản lý phải đăng nhập online trước khi tạo account.");
        }

        if (IsCloudBackedAccount(actor) || await HasCloudSessionForAsync(actorUserId))
        {
            try
            {
                var response = await _cloudApi.PostAsync<CloudCreateUserRequest, CloudAuthResponse>(
                    "users",
                    new CloudCreateUserRequest(
                        username.Trim(),
                        fullName.Trim(),
                        email.Trim(),
                        RoleCapabilities.ToWireRole(role),
                        role == UserRole.Trainee && isTuitionSupported,
                        phone.Trim(),
                        guardianName.Trim(),
                        guardianPhone.Trim(),
                        coachPosition),
                    idempotencyKey: EntityId.New());
                return await CacheCloudIdentityAsync(response);
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        var usernameNormalized = Normalize(username);
        if (usernameNormalized.Length < 3)
        {
            throw new InvalidOperationException("Username cần có ít nhất 3 ký tự.");
        }

        if (await Database.Table<UserAccount>()
                .Where(item => item.UsernameNormalized == usernameNormalized)
                .CountAsync() > 0)
        {
            throw new InvalidOperationException("Username đã tồn tại.");
        }

        var digest = _passwordService.Hash(NewAccountDefaultPassword);
        var user = new UserAccount
        {
            Id = EntityId.New(),
            Username = username.Trim(),
            UsernameNormalized = usernameNormalized,
            PasswordHash = digest.Hash,
            PasswordSalt = digest.Salt,
            PasswordIterations = digest.Iterations,
            Role = role,
            EmailNormalized = NormalizeEmail(email),
            IsActive = true,
            IsTuitionSupported = role == UserRole.Trainee && isTuitionSupported,
            MustChangePassword = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var profile = new PersonProfile
        {
            UserId = user.Id,
            FullName = fullName.Trim(),
            Email = email.Trim(),
            Phone = phone.Trim(),
            GuardianName = guardianName.Trim(),
            GuardianPhone = guardianPhone.Trim(),
            CoachPosition = coachPosition,
            UpdatedAtUtc = DateTime.UtcNow
        };
        await Database.RunInTransactionAsync(connection =>
        {
            connection.Insert(user);
            connection.Insert(profile);
        });
        await AddAuditAsync(
            actorUserId,
            "CreateUser",
            nameof(UserAccount),
            user.Id,
            user.IsTuitionSupported
                ? $"{DomainText.Role(role)} · {DomainText.SupportedTraineeLabel}"
                : DomainText.Role(role));
        return user;
    }

    public async Task<UserAccount> RegisterFounderAsync(
        string username,
        string fullName,
        string email,
        string password)
    {
        if (IsOnline)
        {
            _cloudOptions.EnsureConfigured();
            try
            {
                var response = await _cloudApi.RegisterFounderAsync(
                    new CloudFounderRegistrationRequest(
                        username.Trim(),
                        fullName.Trim(),
                        email.Trim(),
                        password,
                        "Community Football Club",
                        CurrentDeviceName()));
                var cloudUser = response.User
                                 ?? throw new InvalidOperationException(
                                     "Máy chủ không trả về account Founder vừa tạo.");
                var user = CloudSnapshotMapper.ToEntity(cloudUser);
                var profile = response.Profile is null
                    ? new PersonProfile { UserId = user.Id, FullName = fullName.Trim(), Email = email.Trim() }
                    : CloudSnapshotMapper.ToEntity(response.Profile);
                Online.Clear();
                Online.SetIdentity(user, profile, response.ActiveClub is null
                    ? null
                    : CloudSnapshotMapper.ToEntity(response.ActiveClub));
                await _cloudApi.ClearLocalSessionAsync();
                return user;
            }
            catch (ApiException exception) when (!IsCloudUnavailable(exception))
            {
                throw CloudOperationException(exception);
            }
            catch (Exception exception) when (IsCloudUnavailable(exception))
            {
                await _cloudTokens.ClearAsync();
                throw new InvalidOperationException(
                    "Không thể kết nối Cloudflare. Đăng ký Founder yêu cầu backend online.",
                    exception);
            }
        }

        await InitializeAsync();
        // Public Founder registration is intentionally a Cloudflare-only
        // operation.  It creates a pending approval record in the same D1
        // workflow used by the Admin Founder editor; never create a local
        // SQLite account here, otherwise OAuth binding cannot identify the
        // account on the server.
        _cloudOptions.EnsureConfigured();
        try
        {
            var response = await _cloudApi.RegisterFounderAsync(
                new CloudFounderRegistrationRequest(
                    username.Trim(),
                    fullName.Trim(),
                    email.Trim(),
                    password,
                    "Community Football Club",
                    CurrentDeviceName()));
            var cloudUser = await CacheCloudIdentityAsync(response);

            // The public registration endpoint does not issue a login session.
            // Clear only local tokens so a previous user session is never
            // revoked as a side effect of creating a pending Founder.
            await _cloudApi.ClearLocalSessionAsync();
            return cloudUser;
        }
        catch (ApiException exception) when (!IsCloudUnavailable(exception))
        {
            throw CloudOperationException(exception);
        }
        catch (Exception exception) when (IsCloudUnavailable(exception))
        {
            await _cloudTokens.ClearAsync();
            throw new InvalidOperationException(
                "Không thể kết nối Cloudflare. Đăng ký Founder yêu cầu backend online.",
                exception);
        }
    }

    public async Task<UserAccount> CreateFounderByAdminAsync(
        string actorUserId,
        string username,
        string fullName,
        string email,
        string password)
    {
        if (IsOnline)
        {
            await RequireOnlineRoleAsync(actorUserId, UserRole.Admin);
            try
            {
                var response = await _cloudApi.PostAsync<CloudFounderRegistrationRequest, CloudAuthResponse>(
                    "admin/founders",
                    new CloudFounderRegistrationRequest(
                        username.Trim(),
                        fullName.Trim(),
                        email.Trim(),
                        null,
                        "Community Football Club",
                        CurrentDeviceName()),
                    idempotencyKey: EntityId.New());
                var createdSnapshot = response.User
                                      ?? throw new InvalidOperationException(
                                          "Máy chủ không trả về Founder vừa tạo.");
                return CloudSnapshotMapper.ToEntity(createdSnapshot);
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireRoleAsync(actorUserId, UserRole.Admin);
        if (_cloudOptions.IsConfigured
            && !await HasCloudSessionForAsync(actorUserId))
        {
            throw new InvalidOperationException(
                "Account Admin phải đăng nhập online trước khi tạo account Founder.");
        }

        if (IsCloudBackedAccount(actor) || await HasCloudSessionForAsync(actorUserId))
        {
            try
            {
                var response = await _cloudApi.PostAsync<CloudFounderRegistrationRequest, CloudAuthResponse>(
                    "admin/founders",
                    new CloudFounderRegistrationRequest(
                        username.Trim(),
                        fullName.Trim(),
                        email.Trim(),
                        null,
                        "Community Football Club",
                        CurrentDeviceName()),
                    idempotencyKey: EntityId.New());
                return await CacheCloudIdentityAsync(response, updateClub: false);
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        var user = await CreateFounderAccountAsync(
            username,
            fullName,
            email,
            NewAccountDefaultPassword,
            mustChangePassword: true);
        await AddAuditAsync(
            actorUserId,
            "AdminCreateFounder",
            nameof(UserAccount),
            user.Id,
            user.Username);
        return user;
    }

    private async Task<UserAccount> CreateFounderAccountAsync(
        string username,
        string fullName,
        string email,
        string password,
        bool mustChangePassword,
        bool isActive = true)
    {
        var usernameNormalized = Normalize(username);
        if (usernameNormalized.Length < 3)
        {
            throw new InvalidOperationException("Username cần có ít nhất 3 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException("Vui lòng nhập tên Sáng lập & Điều hành.");
        }

        var passwordError = PasswordService.Validate(password);
        if (!string.IsNullOrEmpty(passwordError))
        {
            throw new InvalidOperationException(passwordError);
        }

        if (await Database.Table<UserAccount>()
                .Where(item => item.UsernameNormalized == usernameNormalized)
                .CountAsync() > 0)
        {
            throw new InvalidOperationException("Username đã tồn tại.");
        }

        var digest = _passwordService.Hash(password);
        var user = new UserAccount
        {
            Id = EntityId.New(),
            Username = username.Trim(),
            UsernameNormalized = usernameNormalized,
            PasswordHash = digest.Hash,
            PasswordSalt = digest.Salt,
            PasswordIterations = digest.Iterations,
            Role = UserRole.Founder,
            EmailNormalized = NormalizeEmail(email),
            IsActive = isActive,
            MustChangePassword = mustChangePassword,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var profile = new PersonProfile
        {
            UserId = user.Id,
            FullName = fullName.Trim(),
            Email = email.Trim(),
            UpdatedAtUtc = DateTime.UtcNow
        };
        await Database.RunInTransactionAsync(connection =>
        {
            connection.Insert(user);
            connection.Insert(profile);
        });
        return user;
    }

    public async Task<IReadOnlyList<MemberRow>> GetFounderAccountsAsync(
        string actorUserId,
        bool includeInactive = true)
    {
        if (IsOnline)
        {
            await RequireOnlineRoleAsync(actorUserId, UserRole.Admin);
            try
            {
                var response = await _cloudApi.GetAsync<CloudFounderListResponse>("admin/founders");
                return response.Founders
                    .Where(founder => includeInactive || founder.IsActive)
                    .Select(founder =>
                    {
                        var user = CloudSnapshotMapper.ToEntity(founder.ToUser());
                        var profile = new PersonProfile
                        {
                            UserId = user.Id,
                            FullName = founder.FullName,
                            Email = founder.Email,
                            UpdatedAtUtc = founder.UpdatedAt.UtcDateTime
                        };
                        return new MemberRow(user, profile)
                        {
                            FounderApprovalStatus = founder.FounderStatus,
                            FounderTenantStatus = founder.TenantStatus,
                            TeamName = founder.TeamName
                        };
                    })
                    .OrderBy(item => item.DisplayName)
                    .ToList();
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireRoleAsync(actorUserId, UserRole.Admin);
        if (IsCloudBackedAccount(actor) || await HasCloudSessionForAsync(actorUserId))
        {
            try
            {
                var response = await _cloudApi.GetAsync<CloudFounderListResponse>("admin/founders");
                var rows = new List<MemberRow>(response.Founders.Count);
                foreach (var founder in response.Founders)
                {
                    var profile = new CloudProfileSnapshot
                    {
                        UserId = founder.Id,
                        FullName = founder.FullName,
                        Email = founder.Email,
                        UpdatedAt = founder.UpdatedAt
                    };
                    var cached = await CacheCloudIdentityAsync(
                        founder.ToUser(),
                        profile,
                        club: null,
                        updateClub: false,
                        profileIsComplete: false);
                    if (includeInactive || cached.IsActive)
                    {
                        rows.Add(new MemberRow(cached, await GetProfileAsync(cached.Id))
                        {
                            FounderApprovalStatus = founder.FounderStatus,
                            FounderTenantStatus = founder.TenantStatus,
                            TeamName = founder.TeamName
                        });
                    }
                }

                return rows.OrderBy(item => item.DisplayName).ToList();
            }
            catch (Exception exception) when (IsCloudUnavailable(exception))
            {
                // Read-only admin lists may safely use the last public cache.
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        var users = await Database.Table<UserAccount>()
            .Where(item => item.Role == UserRole.Founder)
            .ToListAsync();
        if (!includeInactive)
        {
            users = users.Where(item => item.IsActive).ToList();
        }

        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);
        var localClub = await Database.FindAsync<ClubProfile>(1);
        return users
            .Select(item => new MemberRow(
                item,
                profiles.GetValueOrDefault(item.Id)
                ?? new PersonProfile { UserId = item.Id })
            {
                TeamName = localClub?.TeamName
            })
            .OrderBy(item => item.DisplayName)
            .ToList();
    }

    public async Task SetFounderActiveByAdminAsync(
        string actorUserId,
        string targetUserId,
        bool isActive)
    {
        if (IsOnline)
        {
            await RequireOnlineRoleAsync(actorUserId, UserRole.Admin);
            try
            {
                await _cloudApi.PatchFounderStatusAsync(targetUserId, isActive);
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireRoleAsync(actorUserId, UserRole.Admin);
        if (IsCloudBackedAccount(actor) || await HasCloudSessionForAsync(actorUserId))
        {
            try
            {
                // The Worker performs the authoritative status change and
                // writes the Admin audit entry in the same online workflow.
                // Do not call AddAuditAsync afterwards: an Admin has no
                // tenant, so /v1/audit would fail with tenant_required and
                // incorrectly surface an error after a successful update.
                await _cloudApi.PatchFounderStatusAsync(targetUserId, isActive);
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }

            // The Founder list is normally refreshed from D1 immediately. If
            // a projection is present, update it opportunistically; an Admin
            // action must not fail merely because that Founder is absent from
            // the local SQLite cache.
            var cachedTarget = await Database.FindAsync<UserAccount>(targetUserId);
            if (cachedTarget is not null)
            {
                if (cachedTarget.Role != UserRole.Founder)
                {
                    throw new InvalidOperationException("Admin chỉ được quản trị account Founder.");
                }

                cachedTarget.IsActive = isActive;
                cachedTarget.UpdatedAtUtc = DateTime.UtcNow;
                await Database.UpdateAsync(cachedTarget);
            }

            return;
        }

        var target = await Database.FindAsync<UserAccount>(targetUserId)
                     ?? throw new InvalidOperationException("Không tìm thấy account Founder.");
        if (target.Role != UserRole.Founder)
        {
            throw new InvalidOperationException("Admin chỉ được quản trị account Founder.");
        }

        target.IsActive = isActive;
        target.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(target);
        await AddAuditAsync(
            actorUserId,
            isActive ? "AdminApproveFounder" : "AdminSuspendFounder",
            nameof(UserAccount),
            target.Id,
            target.Username);
    }

    public async Task ResetFounderPasswordByAdminAsync(
        string actorUserId,
        string targetUserId,
        string newPassword)
    {
        if (IsOnline)
        {
            await RequireOnlineRoleAsync(actorUserId, UserRole.Admin);
            try
            {
                await _cloudApi.PatchAsync(
                    $"admin/founders/{Uri.EscapeDataString(targetUserId)}/password",
                    new CloudAdminPasswordResetRequest(newPassword),
                    idempotencyKey: EntityId.New());
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireRoleAsync(actorUserId, UserRole.Admin);
        var target = await Database.FindAsync<UserAccount>(targetUserId)
                     ?? throw new InvalidOperationException("Không tìm thấy account Founder.");
        if (target.Role != UserRole.Founder)
        {
            throw new InvalidOperationException("Admin chỉ được quản trị account Founder.");
        }

        if (IsCloudBackedAccount(actor) || await HasCloudSessionForAsync(actorUserId))
        {
            try
            {
                await _cloudApi.PatchAsync(
                    $"admin/founders/{Uri.EscapeDataString(targetUserId)}/password",
                    new CloudAdminPasswordResetRequest(newPassword),
                    idempotencyKey: EntityId.New());
                target.MustChangePassword = true;
                target.UpdatedAtUtc = DateTime.UtcNow;
                target.PasswordHash = string.Empty;
                target.PasswordSalt = string.Empty;
                target.PasswordIterations = 0;
                await Database.UpdateAsync(target);
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await ApplyNewPasswordAsync(target, newPassword, mustChangePassword: true);
        await AddAuditAsync(
            actorUserId,
            "AdminResetFounderPassword",
            nameof(UserAccount),
            target.Id,
            target.Username);
    }

    public async Task DeleteFounderAccountAsync(
        string actorUserId,
        string targetUserId)
    {
        if (IsOnline)
        {
            await RequireOnlineRoleAsync(actorUserId, UserRole.Admin);
            try
            {
                await _cloudApi.DeleteAsync(
                    $"admin/founders/{Uri.EscapeDataString(targetUserId)}",
                    idempotencyKey: EntityId.New());
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireRoleAsync(actorUserId, UserRole.Admin);
        var target = await Database.FindAsync<UserAccount>(targetUserId)
                     ?? throw new InvalidOperationException("Không tìm thấy account Founder.");
        if (target.Role != UserRole.Founder)
        {
            throw new InvalidOperationException("Admin chỉ được xóa account Founder.");
        }

        if (IsCloudBackedAccount(actor) || await HasCloudSessionForAsync(actorUserId))
        {
            try
            {
                await _cloudApi.DeleteAsync(
                    $"admin/founders/{Uri.EscapeDataString(targetUserId)}",
                    idempotencyKey: EntityId.New());
                await Database.RunInTransactionAsync(connection =>
                {
                    connection.Execute("DELETE FROM ExternalAccountLinks WHERE UserId = ?", target.Id);
                    connection.Execute("DELETE FROM PersonProfiles WHERE UserId = ?", target.Id);
                    connection.Delete(target);
                });
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await Database.RunInTransactionAsync(connection =>
        {
            connection.Execute("DELETE FROM ExternalAccountLinks WHERE UserId = ?", target.Id);
            connection.Execute("DELETE FROM PersonProfiles WHERE UserId = ?", target.Id);
            connection.Delete(target);
        });
        await AddAuditAsync(
            actorUserId,
            "AdminDeleteFounder",
            nameof(UserAccount),
            target.Id,
            target.Username);
    }

    public async Task SetUserActiveAsync(string actorUserId, string targetUserId, bool isActive)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền thay đổi trạng thái account.");
            try
            {
                await _cloudApi.PatchAsync(
                    $"users/{Uri.EscapeDataString(targetUserId)}/status",
                    new CloudUserStatusRequest(isActive),
                    idempotencyKey: EntityId.New());
                if (Online.User(targetUserId) is { } onlineTarget)
                {
                    onlineTarget.IsActive = isActive;
                }
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(actor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền thay đổi trạng thái account.");
        var target = await Database.FindAsync<UserAccount>(targetUserId)
                     ?? throw new InvalidOperationException("Không tìm thấy account.");
        if (target.Role == UserRole.Founder)
        {
            throw new InvalidOperationException("Không thể khóa tài khoản Founder.");
        }

        if (IsCloudBackedAccount(actor) || await HasCloudSessionForAsync(actorUserId))
        {
            try
            {
                await _cloudApi.PatchAsync(
                    $"users/{Uri.EscapeDataString(targetUserId)}/status",
                    new CloudUserStatusRequest(isActive),
                    idempotencyKey: EntityId.New());
                target.IsActive = isActive;
                target.UpdatedAtUtc = DateTime.UtcNow;
                await Database.UpdateAsync(target);
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        target.IsActive = isActive;
        target.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(target);
        await AddAuditAsync(
            actorUserId,
            isActive ? "ActivateUser" : "DeactivateUser",
            nameof(UserAccount),
            target.Id,
            target.Username);
    }

    public async Task ResetPasswordByFounderAsync(
        string actorUserId,
        string targetUserId,
        string newPassword)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền đặt lại mật khẩu.");
            try
            {
                await _cloudApi.PatchAsync(
                    $"users/{Uri.EscapeDataString(targetUserId)}/password",
                    new CloudAdminPasswordResetRequest(newPassword),
                    idempotencyKey: EntityId.New());
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(actor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền đặt lại mật khẩu.");
        var target = await Database.FindAsync<UserAccount>(targetUserId)
                     ?? throw new InvalidOperationException("Không tìm thấy account.");
        if (target.Role == UserRole.Founder)
        {
            throw new InvalidOperationException("Founder cần đổi mật khẩu trong hồ sơ cá nhân.");
        }

        if (IsCloudBackedAccount(actor) || await HasCloudSessionForAsync(actorUserId))
        {
            try
            {
                await _cloudApi.PatchAsync(
                    $"users/{Uri.EscapeDataString(targetUserId)}/password",
                    new CloudAdminPasswordResetRequest(newPassword),
                    idempotencyKey: EntityId.New());
                target.MustChangePassword = true;
                target.PasswordHash = string.Empty;
                target.PasswordSalt = string.Empty;
                target.PasswordIterations = 0;
                target.UpdatedAtUtc = DateTime.UtcNow;
                await Database.UpdateAsync(target);
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await ApplyNewPasswordAsync(target, newPassword, mustChangePassword: true);
        await AddAuditAsync(actorUserId, "ResetPassword", nameof(UserAccount), target.Id, target.Username);
    }

    public async Task ResetPasswordByEmailAsync(
        string username,
        string email,
        string newPassword)
    {
        if (IsOnline)
        {
            throw new InvalidOperationException(
                "Đặt lại mật khẩu online cần dùng luồng xác minh email của máy chủ.");
        }

        await InitializeAsync();
        var normalizedUsername = Normalize(username);
        var user = await Database.Table<UserAccount>()
            .Where(item => item.UsernameNormalized == normalizedUsername)
            .FirstOrDefaultAsync();
        if (user is not null
            && (string.IsNullOrWhiteSpace(user.PasswordHash)
                || string.IsNullOrWhiteSpace(user.PasswordSalt)))
        {
            throw new InvalidOperationException(
                "Đặt lại mật khẩu online bằng email chưa được máy chủ hỗ trợ. "
                + "Vui lòng liên hệ Admin hoặc Sáng lập & Điều hành để được đặt lại mật khẩu.");
        }

        if (user is null
            || user.Role == UserRole.Admin
            || string.IsNullOrWhiteSpace(user.EmailNormalized)
            || user.EmailNormalized != NormalizeEmail(email))
        {
            throw new InvalidOperationException("Username và email không khớp.");
        }

        await ApplyNewPasswordAsync(user, newPassword, mustChangePassword: false);
        await AddAuditAsync(user.Id, "OfflineEmailPasswordReset", nameof(UserAccount), user.Id, "Self-service offline");
    }

    public async Task ChangePasswordAsync(
        string actorUserId,
        string currentPassword,
        string newPassword)
    {
        if (IsOnline)
        {
            var onlineUser = await RequireOnlineUserAsync(actorUserId);
            var passwordError = PasswordService.Validate(newPassword);
            if (!string.IsNullOrEmpty(passwordError))
            {
                throw new InvalidOperationException(passwordError);
            }

            try
            {
                await _cloudApi.PatchAsync(
                    "auth/password",
                    new CloudChangePasswordRequest(currentPassword, newPassword),
                    idempotencyKey: EntityId.New());
                await _cloudApi.ClearLocalSessionAsync();
                var response = await _cloudApi.LoginAsync(new CloudLoginRequest(
                    onlineUser.Username,
                    newPassword,
                    CurrentDeviceName()));
                var cloudUser = response.User
                                 ?? throw new InvalidOperationException(
                                     "Máy chủ không trả về thông tin tài khoản.");
                Online.Clear();
                SetOnlineIdentity(cloudUser, response.Profile, response.ActiveClub ?? response.Club);
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var user = await RequireUserAsync(actorUserId);
        if (IsCloudBackedAccount(user) || await HasCloudSessionForAsync(actorUserId))
        {
            var passwordError = PasswordService.Validate(newPassword);
            if (!string.IsNullOrEmpty(passwordError))
            {
                throw new InvalidOperationException(passwordError);
            }

            var passwordWasChanged = false;
            try
            {
                await _cloudApi.PatchAsync(
                    "auth/password",
                    new CloudChangePasswordRequest(currentPassword, newPassword),
                    idempotencyKey: EntityId.New());
                passwordWasChanged = true;

                // The Worker revokes every session after a password change.
                // Re-authenticate immediately so the user can continue without
                // persisting either the old or new password on the device.
                await _cloudApi.ClearLocalSessionAsync();
                var response = await _cloudApi.LoginAsync(new CloudLoginRequest(
                    user.Username,
                    newPassword,
                    CurrentDeviceName()));
                await CacheCloudIdentityAsync(response);
                return;
            }
            catch (ApiException exception) when (!passwordWasChanged)
            {
                throw CloudOperationException(exception);
            }
            catch (Exception exception) when (passwordWasChanged)
            {
                await _cloudApi.ClearLocalSessionAsync();
                throw new InvalidOperationException(
                    "Mật khẩu đã được đổi trên máy chủ nhưng chưa thể tạo lại phiên đăng nhập. "
                    + "Vui lòng đăng nhập lại bằng mật khẩu mới.",
                    exception);
            }
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash)
            || string.IsNullOrWhiteSpace(user.PasswordSalt))
        {
            throw new InvalidOperationException(
                "Cần kết nối máy chủ và đăng nhập lại trước khi đổi mật khẩu.");
        }

        if (!_passwordService.Verify(
                currentPassword,
                user.PasswordHash,
                user.PasswordSalt,
                user.PasswordIterations))
        {
            throw new InvalidOperationException("Mật khẩu hiện tại không đúng.");
        }

        await ApplyNewPasswordAsync(user, newPassword, mustChangePassword: false);
        await AddAuditAsync(actorUserId, "ChangePassword", nameof(UserAccount), actorUserId, "Self-service");
    }

    private async Task ApplyNewPasswordAsync(
        UserAccount user,
        string newPassword,
        bool mustChangePassword)
    {
        var passwordError = PasswordService.Validate(newPassword);
        if (!string.IsNullOrEmpty(passwordError))
        {
            throw new InvalidOperationException(passwordError);
        }

        var digest = _passwordService.Hash(newPassword);
        user.PasswordHash = digest.Hash;
        user.PasswordSalt = digest.Salt;
        user.PasswordIterations = digest.Iterations;
        user.MustChangePassword = mustChangePassword;
        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(user);
    }

    public async Task SaveProfileAsync(string actorUserId, PersonProfile profile)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            var onlineTarget = Online.User(profile.UserId)
                         ?? throw new InvalidOperationException("Không tìm thấy account.");
            if (onlineActor.Id != onlineTarget.Id
                && !RoleCapabilities.CanEditMemberProfile(onlineActor.Role, onlineTarget.Role))
            {
                throw new UnauthorizedAccessException("Bạn không có quyền sửa hồ sơ này.");
            }
            if (onlineActor.Role == UserRole.Coach && onlineTarget.Id != onlineActor.Id)
            {
                throw new UnauthorizedAccessException("Coach chỉ được sửa hồ sơ của mình.");
            }
            profile.FullName = profile.FullName.Trim();
            profile.Email = profile.Email.Trim();
            profile.Phone = profile.Phone.Trim();
            profile.CoachPosition = onlineTarget.Role == UserRole.Coach
                && CoachPositionCatalog.IsValid(profile.CoachPosition)
                ? profile.CoachPosition.Trim()
                : string.Empty;
            try
            {
                string? photoUploadId = null;
                if (IsPendingProfileImage(profile.PhotoPath))
                {
                    photoUploadId = (await _cloudApi.UploadFileAsync(
                        profile.PhotoPath,
                        "avatar")).Id;
                }
                var response = await _cloudApi.PatchAsync<object, CloudProfileResponse>(
                    $"users/{Uri.EscapeDataString(profile.UserId)}/profile",
                    new
                    {
                        fullName = profile.FullName,
                        phone = profile.Phone,
                        email = profile.Email,
                        dateOfBirth = profile.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        heightCm = profile.HeightCm,
                        weightKg = profile.WeightKg,
                        guardianName = profile.GuardianName,
                        guardianPhone = profile.GuardianPhone,
                        coachPosition = profile.CoachPosition,
                        photoUploadId
                    },
                    idempotencyKey: EntityId.New());
                var saved = response.Profile is null
                    ? profile
                    : CloudSnapshotMapper.ToEntity(response.Profile);
                if (photoUploadId is not null && File.Exists(profile.PhotoPath))
                {
                    saved.PhotoPath = profile.PhotoPath;
                }
                Online.Upsert(Online.Profiles, saved, item => item.UserId == saved.UserId);
                onlineTarget.EmailNormalized = NormalizeEmail(saved.Email);
                if (onlineActor.Id == saved.UserId && Online.CurrentUser is { } currentUser)
                {
                    Online.SetIdentity(currentUser, saved, Online.Club);
                }
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        var target = await Database.FindAsync<UserAccount>(profile.UserId)
                     ?? throw new InvalidOperationException("Không tìm thấy account.");
        if (actor.Id != target.Id
            && !RoleCapabilities.CanEditMemberProfile(actor.Role, target.Role))
        {
            throw new UnauthorizedAccessException("Bạn không có quyền sửa hồ sơ này.");
        }

        if (actor.Role == UserRole.Coach && target.Id != actor.Id)
        {
            throw new UnauthorizedAccessException("Coach chỉ được sửa hồ sơ của mình.");
        }

        if (profile.DateOfBirth is { } dateOfBirth)
        {
            if (target.Role != UserRole.Trainee)
            {
                throw new InvalidOperationException(
                    "Ngày tháng năm sinh chỉ áp dụng cho Cầu Thủ Học Viên.");
            }

            dateOfBirth = dateOfBirth.Date;
            if (dateOfBirth > DateTime.Today)
            {
                throw new InvalidOperationException(
                    "Ngày tháng năm sinh không được sau ngày hiện tại.");
            }

            if (dateOfBirth < new DateTime(1900, 1, 1))
            {
                throw new InvalidOperationException(
                    "Ngày tháng năm sinh phải từ năm 1900 trở về sau.");
            }

            profile.DateOfBirth = dateOfBirth;
        }

        profile.FullName = profile.FullName.Trim();
        profile.Email = profile.Email.Trim();
        profile.Phone = profile.Phone.Trim();
        profile.CoachPosition = target.Role == UserRole.Coach
            && CoachPositionCatalog.IsValid(profile.CoachPosition)
            ? profile.CoachPosition.Trim()
            : string.Empty;

        if (_cloudOptions.IsConfigured && await HasCloudSessionForAsync(actorUserId))
        {
            try
            {
                await _cloudApi.PatchAsync(
                    $"users/{Uri.EscapeDataString(profile.UserId)}/profile",
                    new
                    {
                        fullName = profile.FullName,
                        phone = profile.Phone,
                        email = profile.Email,
                        dateOfBirth = profile.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        heightCm = profile.HeightCm,
                        weightKg = profile.WeightKg,
                        guardianName = profile.GuardianName,
                        guardianPhone = profile.GuardianPhone,
                        coachPosition = profile.CoachPosition,
                        photoUploadId = IsPendingProfileImage(profile.PhotoPath)
                            ? (await _cloudApi.UploadFileAsync(profile.PhotoPath, "avatar")).Id
                            : null
                    },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        profile.UpdatedAtUtc = DateTime.UtcNow;
        await Database.InsertOrReplaceAsync(profile);

        target.EmailNormalized = NormalizeEmail(profile.Email);
        target.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(target);
        await AddAuditAsync(actorUserId, "UpdateProfile", nameof(PersonProfile), profile.UserId, profile.FullName);
    }

    public async Task SetTuitionSupportAsync(
        string actorUserId,
        string targetUserId,
        bool isSupported)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền cập nhật trạng thái hỗ trợ học phí.");
            await EnsureOnlineSnapshotAsync();
            var onlineTarget = Online.User(targetUserId)
                         ?? throw new InvalidOperationException("Không tìm thấy account.");
            if (onlineTarget.Role != UserRole.Trainee)
            {
                throw new InvalidOperationException("Chỉ Cầu Thủ Học Viên mới có trạng thái được hỗ trợ.");
            }
            try
            {
                await _cloudApi.PatchAsync(
                    $"users/{Uri.EscapeDataString(targetUserId)}/tuition-support",
                    new { isSupported },
                    idempotencyKey: EntityId.New());
                onlineTarget.IsTuitionSupported = isSupported;
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var supportActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(supportActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền cập nhật trạng thái hỗ trợ học phí.");
        var target = await Database.FindAsync<UserAccount>(targetUserId)
                     ?? throw new InvalidOperationException("Không tìm thấy account.");
        if (target.Role != UserRole.Trainee)
        {
            throw new InvalidOperationException(
                "Chỉ Cầu Thủ Học Viên mới có trạng thái được hỗ trợ.");
        }

        if (_cloudOptions.IsConfigured)
        {
            await EnsureCloudWriteReadyAsync(actorUserId);
            try
            {
                await _cloudApi.PatchAsync(
                    $"users/{Uri.EscapeDataString(targetUserId)}/tuition-support",
                    new { isSupported },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        target.IsTuitionSupported = isSupported;
        target.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(target);
        await AddAuditAsync(
            actorUserId,
            "UpdateTuitionSupport",
            nameof(UserAccount),
            target.Id,
            isSupported ? DomainText.SupportedTraineeLabel : "Đã tắt hỗ trợ học phí");
        QueueCloudProjectionRefresh();
    }

    public async Task<ClubProfile> GetClubAsync()
    {
        if (IsOnline)
        {
            if (Online.Club is { } currentClub)
            {
                if (Online.CurrentUser is { } currentUser)
                {
                    await MaterializeClubLogoAsync(currentUser.Id, currentClub);
                }
                return currentClub;
            }
            try
            {
                var response = await _cloudApi.GetAsync<CloudClubResponse>("club");
                var club = response.Club is null
                    ? new ClubProfile { Id = 1 }
                    : CloudSnapshotMapper.ToEntity(response.Club);
                if (Online.CurrentUser is { } currentUser)
                {
                    Online.SetIdentity(currentUser, Online.CurrentProfile, club);
                    await MaterializeClubLogoAsync(currentUser.Id, club);
                }
                return club;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        return await Database.FindAsync<ClubProfile>(1)
               ?? throw new InvalidOperationException("Không tìm thấy thông tin đội.");
    }

    public async Task SaveClubAsync(string actorUserId, ClubProfile club)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền chỉnh sửa thông tin đội.");
            club.Id = 1;
            club.UpdatedAtUtc = DateTime.UtcNow;
            try
            {
                string? logoUploadId = null;
                if (IsPendingClubLogo(club.LogoPath))
                {
                    logoUploadId = (await _cloudApi.UploadFileAsync(
                        club.LogoPath,
                        "club_logo")).Id;
                }
                var response = await _cloudApi.PatchAsync<object, CloudClubResponse>(
                    "club",
                    new
                    {
                        teamName = club.TeamName.Trim(),
                        phone = club.Phone.Trim(),
                        email = club.Email.Trim(),
                        bankName = club.BankName.Trim(),
                        bankBin = club.BankBin.Trim(),
                        bankAccountNumber = club.BankAccountNumber.Trim(),
                        bankAccountName = club.BankAccountName.Trim(),
                        logoUploadId
                    },
                    idempotencyKey: EntityId.New());
                var saved = response.Club is null ? club : CloudSnapshotMapper.ToEntity(response.Club);
                if (logoUploadId is not null && File.Exists(club.LogoPath))
                {
                    saved.LogoPath = club.LogoPath;
                }
                Online.SetIdentity(Online.CurrentUser!, Online.CurrentProfile, saved);
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var clubActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(clubActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền chỉnh sửa thông tin đội.");
        var hasBankDetails = !string.IsNullOrWhiteSpace(club.BankBin)
                             || !string.IsNullOrWhiteSpace(club.BankAccountNumber);
        if (hasBankDetails)
        {
            var bankBin = new string(club.BankBin.Where(char.IsDigit).ToArray());
            var accountNumber = new string(club.BankAccountNumber.Where(char.IsDigit).ToArray());
            if (bankBin.Length != 6)
            {
                throw new InvalidOperationException("Bank BIN phải gồm đúng 6 chữ số.");
            }

            if (accountNumber.Length is < 6 or > 20)
            {
                throw new InvalidOperationException("Số tài khoản phải gồm từ 6 đến 20 chữ số.");
            }

            club.BankBin = bankBin;
            club.BankAccountNumber = accountNumber;
        }

        club.Id = 1;
        club.UpdatedAtUtc = DateTime.UtcNow;

        if (_cloudOptions.IsConfigured && await HasCloudSessionForAsync(actorUserId))
        {
            try
            {
                await _cloudApi.PatchAsync(
                    "club",
                    new
                    {
                        teamName = club.TeamName.Trim(),
                        phone = club.Phone.Trim(),
                        email = club.Email.Trim(),
                        bankName = club.BankName.Trim(),
                        bankBin = club.BankBin.Trim(),
                        bankAccountNumber = club.BankAccountNumber.Trim(),
                        bankAccountName = club.BankAccountName.Trim()
                    },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await Database.InsertOrReplaceAsync(club);
        await AddAuditAsync(actorUserId, "UpdateClub", nameof(ClubProfile), "1", club.TeamName);
    }

    public async Task<IReadOnlyList<Venue>> GetVenuesAsync(bool includeInactive = false)
    {
        if (IsOnline)
        {
            await EnsureOnlineSnapshotAsync();
            return Online.Venues
                .Where(item => includeInactive || item.IsActive)
                .OrderBy(item => item.Name)
                .ToList();
        }

        await InitializeAsync();
        var venues = await Database.Table<Venue>().ToListAsync();
        return venues
            .Where(item => includeInactive || item.IsActive)
            .OrderBy(item => item.Name)
            .ToList();
    }

    public async Task SaveVenueAsync(string actorUserId, Venue venue)
    {
        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(actor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền quản lý sân.");
            await EnsureOnlineSnapshotAsync();
            if (string.IsNullOrWhiteSpace(venue.Name))
            {
                throw new InvalidOperationException("Vui lòng nhập tên sân.");
            }
            venue.Name = venue.Name.Trim();
            venue.Address = venue.Address.Trim();
            venue.Notes = venue.Notes.Trim();
            venue.UpdatedAtUtc = DateTime.UtcNow;
            if (Online.Venue(venue.Id) is null)
            {
                venue.CreatedAtUtc = DateTime.UtcNow;
            }
            await PushOnlineDeltaAsync(actor, venues: new[] { venue });
            Online.Upsert(Online.Venues, venue, item => item.Id == venue.Id);
            return;
        }

        await InitializeAsync();
        var venueActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(venueActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền quản lý sân.");
        await EnsureCloudWriteReadyAsync(actorUserId);
        if (string.IsNullOrWhiteSpace(venue.Name))
        {
            throw new InvalidOperationException("Vui lòng nhập tên sân.");
        }

        venue.Name = venue.Name.Trim();
        venue.Address = venue.Address.Trim();
        venue.Notes = venue.Notes.Trim();
        venue.UpdatedAtUtc = DateTime.UtcNow;
        if (await Database.FindAsync<Venue>(venue.Id) is null)
        {
            venue.CreatedAtUtc = DateTime.UtcNow;
            await Database.InsertAsync(venue);
        }
        else
        {
            await Database.UpdateAsync(venue);
        }

        await AddAuditAsync(actorUserId, "SaveVenue", nameof(Venue), venue.Id, venue.Name);
        await PushCloudMutationAsync(actorUserId, venues: new[] { venue });
    }

    public async Task SetVenueActiveAsync(string actorUserId, string venueId, bool isActive)
    {
        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(actor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền quản lý sân.");
            await EnsureOnlineSnapshotAsync();
            var onlineVenue = Online.Venue(venueId)
                        ?? throw new InvalidOperationException("Không tìm thấy sân.");
            onlineVenue.IsActive = isActive;
            onlineVenue.UpdatedAtUtc = DateTime.UtcNow;
            await PushOnlineDeltaAsync(actor, venues: new[] { onlineVenue });
            return;
        }

        await InitializeAsync();
        var venueActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(venueActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền quản lý sân.");
        await EnsureCloudWriteReadyAsync(actorUserId);
        var venue = await Database.FindAsync<Venue>(venueId)
                    ?? throw new InvalidOperationException("Không tìm thấy sân.");
        venue.IsActive = isActive;
        venue.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(venue);
        await AddAuditAsync(actorUserId, "SetVenueActive", nameof(Venue), venueId, isActive.ToString());
        await PushCloudMutationAsync(actorUserId, venues: new[] { venue });
    }

    public async Task<IReadOnlyList<ClassRow>> GetClassesAsync(
        string actorUserId,
        bool refreshOnline = false)
    {
        if (IsOnline)
        {
            if (refreshOnline)
            {
                // Evaluation requests can be opened by Founder while this
                // process is already running.  Refresh only the evaluation
                // entry point so the newly granted roster is visible without
                // making every class-tab navigation pay for a network call.
                await ReloadOnlineSnapshotAsync();
            }
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            var onlineClasses = (RoleCapabilities.IsFounderLike(onlineActor.Role)
                    ? Online.Classes
                    : Online.Classes.Where(item => item.IsActive))
                .ToList();
            if (onlineActor.Role == UserRole.Coach)
            {
                var ids = Online.ClassCoaches
                    .Where(item => item.CoachUserId == onlineActor.Id && item.IsActive)
                    .Select(item => item.ClassId)
                    .ToHashSet();
                onlineClasses = onlineClasses.Where(item => ids.Contains(item.Id)).ToList();
            }
            else if (onlineActor.Role == UserRole.Trainee)
            {
                var ids = Online.ClassEnrollments
                    .Where(item => item.TraineeUserId == onlineActor.Id && item.IsActive)
                    .Select(item => item.ClassId)
                    .ToHashSet();
                onlineClasses = onlineClasses.Where(item => ids.Contains(item.Id)).ToList();
            }

            var classRows = onlineClasses.OrderBy(item => item.Name)
                .Select(item => new ClassRow(
                    item,
                    Online.Venue(item.VenueId),
                    Online.ClassCoaches
                        .Where(link => link.ClassId == item.Id && link.IsActive)
                        .Select(link => Online.User(link.CoachUserId))
                        .Where(user => user is not null)
                        .Select(user => ToMember(Online, user!))
                        .ToList(),
                    Online.ClassEnrollments
                        .Where(link => link.ClassId == item.Id && link.IsActive)
                        .Select(link => Online.User(link.TraineeUserId))
                        .Where(user => user is not null)
                        .Select(user => ToMember(Online, user!))
                        .ToList()))
                .ToList();
            await MaterializeMemberImagesAsync(
                onlineActor.Id,
                classRows.SelectMany(item => item.Coaches.Concat(item.Trainees)));
            return classRows;
        }

        await InitializeAsync();
        await EnsureMissedCoachCheckInsAsync(DateTime.Today);
        var actor = await RequireUserAsync(actorUserId);
        var classes = await Database.Table<TrainingClass>()
            .ToListAsync();
        if (!RoleCapabilities.IsFounderLike(actor.Role))
        {
            classes = classes.Where(item => item.IsActive).ToList();
        }

        if (actor.Role == UserRole.Coach)
        {
            var ids = (await Database.Table<ClassCoachAssignment>()
                    .Where(item => item.CoachUserId == actor.Id && item.IsActive)
                    .ToListAsync())
                .Select(item => item.ClassId)
                .ToHashSet();
            classes = classes.Where(item => ids.Contains(item.Id)).ToList();
        }
        else if (actor.Role == UserRole.Trainee)
        {
            var ids = (await Database.Table<ClassEnrollment>()
                    .Where(item => item.TraineeUserId == actor.Id && item.IsActive)
                    .ToListAsync())
                .Select(item => item.ClassId)
                .ToHashSet();
            classes = classes.Where(item => ids.Contains(item.Id)).ToList();
        }

        var venues = (await Database.Table<Venue>().ToListAsync())
            .ToDictionary(item => item.Id);
        var users = (await Database.Table<UserAccount>().ToListAsync())
            .ToDictionary(item => item.Id);
        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);
        var assignments = await Database.Table<ClassCoachAssignment>()
            .Where(item => item.IsActive)
            .ToListAsync();
        var enrollments = await Database.Table<ClassEnrollment>()
            .Where(item => item.IsActive)
            .ToListAsync();

        MemberRow? Member(string userId)
        {
            if (!users.TryGetValue(userId, out var user))
            {
                return null;
            }

            return new MemberRow(
                user,
                profiles.GetValueOrDefault(userId)
                ?? new PersonProfile { UserId = userId });
        }

        return classes
            .OrderBy(item => item.Name)
            .Select(item => new ClassRow(
                item,
                venues.GetValueOrDefault(item.VenueId),
                assignments.Where(link => link.ClassId == item.Id)
                    .Select(link => Member(link.CoachUserId))
                    .OfType<MemberRow>()
                    .ToList(),
                enrollments.Where(link => link.ClassId == item.Id)
                    .Select(link => Member(link.TraineeUserId))
                    .OfType<MemberRow>()
                    .ToList()))
            .ToList();
    }

    public async Task SaveClassAsync(
        string actorUserId,
        TrainingClass trainingClass,
        IReadOnlyDictionary<string, long> coachRates,
        IReadOnlyDictionary<string, long> traineeFees,
        IReadOnlyDictionary<string, int>? traineeTrialSessions = null)
    {
        var trialSessions = traineeTrialSessions ?? new Dictionary<string, int>();
        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanCreateClasses(actor.Role))
            {
                throw new UnauthorizedAccessException("Tài khoản không có quyền tạo lớp học.");
            }
            await EnsureOnlineSnapshotAsync();
            if (string.IsNullOrWhiteSpace(trainingClass.Name))
                throw new InvalidOperationException("Vui lòng nhập tên lớp.");
            if (string.IsNullOrWhiteSpace(trainingClass.VenueId))
                throw new InvalidOperationException("Vui lòng chọn sân.");
            if (string.IsNullOrWhiteSpace(trainingClass.ScheduleDays))
                throw new InvalidOperationException("Vui lòng chọn ít nhất một ngày học.");
            if (trainingClass.EndTimeMinutes <= trainingClass.StartTimeMinutes)
                throw new InvalidOperationException("Giờ kết thúc phải sau giờ bắt đầu.");

            var onlineTraineeAccounts = Online.Users
                .Where(item => item.Role == UserRole.Trainee)
                .ToDictionary(item => item.Id);
            if (traineeFees.Keys.Any(id => !onlineTraineeAccounts.ContainsKey(id)))
                throw new InvalidOperationException("Có học viên không hợp lệ trong danh sách lớp.");
            var effectiveFees = traineeFees.ToDictionary(
                pair => pair.Key,
                pair => onlineTraineeAccounts[pair.Key].IsTuitionSupported ? 0 : pair.Value);
            if (effectiveFees.Any(pair => pair.Value < 0
                                          || (!onlineTraineeAccounts[pair.Key].IsTuitionSupported
                                              && pair.Value <= 0)))
                throw new InvalidOperationException("Học phí của học viên phải lớn hơn 0.");
            if (coachRates.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
                throw new InvalidOperationException("Mức lương mỗi buổi của Coach không được âm.");

            trainingClass.Name = trainingClass.Name.Trim();
            trainingClass.UpdatedAtUtc = DateTime.UtcNow;
            var onlineCurrentCoaches = Online.ClassCoaches
                .Where(item => item.ClassId == trainingClass.Id)
                .ToList();
            foreach (var link in onlineCurrentCoaches)
            {
                link.IsActive = coachRates.ContainsKey(link.CoachUserId);
                if (coachRates.TryGetValue(link.CoachUserId, out var rate))
                    link.SalaryPerSessionVnd = rate;
            }
            var newCoaches = coachRates
                .Where(pair => onlineCurrentCoaches.All(item => item.CoachUserId != pair.Key))
                .Select(pair => new ClassCoachAssignment
                {
                    Id = EntityId.New(),
                    ClassId = trainingClass.Id,
                    CoachUserId = pair.Key,
                    SalaryPerSessionVnd = pair.Value,
                    IsActive = true,
                    AssignedAtUtc = DateTime.UtcNow
                })
                .ToList();
            var onlineCurrentEnrollments = Online.ClassEnrollments
                .Where(item => item.ClassId == trainingClass.Id)
                .ToList();
            foreach (var enrollment in onlineCurrentEnrollments)
            {
                enrollment.IsActive = effectiveFees.ContainsKey(enrollment.TraineeUserId);
                if (effectiveFees.TryGetValue(enrollment.TraineeUserId, out var fee))
                {
                    enrollment.MonthlyFeeVnd = fee;
                    enrollment.CycleFeeVnd = fee;
                    var trialCount = trialSessions.GetValueOrDefault(enrollment.TraineeUserId);
                    enrollment.IsTrial = !onlineTraineeAccounts[enrollment.TraineeUserId].IsTuitionSupported
                                         && trialCount > 0;
                    enrollment.TrialSessionCount = enrollment.IsTrial
                        ? Math.Clamp(trialCount, 1, 5)
                        : 0;
                }
            }
            var onlineNewEnrollments = effectiveFees
                .Where(pair => onlineCurrentEnrollments.All(item => item.TraineeUserId != pair.Key))
                .Select(pair => new ClassEnrollment
                {
                    Id = EntityId.New(),
                    ClassId = trainingClass.Id,
                    TraineeUserId = pair.Key,
                    MonthlyFeeVnd = pair.Value,
                    CycleFeeVnd = pair.Value,
                    IsTrial = !onlineTraineeAccounts[pair.Key].IsTuitionSupported
                              && trialSessions.GetValueOrDefault(pair.Key) > 0,
                    TrialSessionCount = !onlineTraineeAccounts[pair.Key].IsTuitionSupported
                                         ? Math.Clamp(trialSessions.GetValueOrDefault(pair.Key), 0, 5)
                                         : 0,
                    IsActive = true,
                    EnrolledAtUtc = DateTime.UtcNow
                })
                .ToList();

            await PushOnlineDeltaAsync(
                actor,
                classes: new[] { trainingClass },
                classCoaches: onlineCurrentCoaches.Concat(newCoaches),
                classEnrollments: onlineCurrentEnrollments.Concat(onlineNewEnrollments));
            Online.Upsert(Online.Classes, trainingClass, item => item.Id == trainingClass.Id);
            foreach (var item in onlineCurrentCoaches.Concat(newCoaches))
                Online.Upsert(Online.ClassCoaches, item, value => value.Id == item.Id);
            foreach (var item in onlineCurrentEnrollments.Concat(onlineNewEnrollments))
                Online.Upsert(Online.ClassEnrollments, item, value => value.Id == item.Id);
            return;
        }

        await InitializeAsync();
        var offlineActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanCreateClasses(offlineActor.Role))
        {
            throw new UnauthorizedAccessException("Tài khoản không có quyền tạo lớp học.");
        }
        await EnsureCloudWriteReadyAsync(actorUserId);
        if (string.IsNullOrWhiteSpace(trainingClass.Name))
        {
            throw new InvalidOperationException("Vui lòng nhập tên lớp.");
        }

        if (string.IsNullOrWhiteSpace(trainingClass.VenueId))
        {
            throw new InvalidOperationException("Vui lòng chọn sân.");
        }

        if (string.IsNullOrWhiteSpace(trainingClass.ScheduleDays))
        {
            throw new InvalidOperationException("Vui lòng chọn ít nhất một ngày học.");
        }

        if (trainingClass.EndTimeMinutes <= trainingClass.StartTimeMinutes)
        {
            throw new InvalidOperationException("Giờ kết thúc phải sau giờ bắt đầu.");
        }

        var traineeAccounts = (await Database.Table<UserAccount>()
                .Where(item => item.Role == UserRole.Trainee)
                .ToListAsync())
            .ToDictionary(item => item.Id);
        if (traineeFees.Keys.Any(id => !traineeAccounts.ContainsKey(id)))
        {
            throw new InvalidOperationException("Có học viên không hợp lệ trong danh sách lớp.");
        }

        // Tuition support always takes precedence over the class default fee.
        // Normalizing it here keeps the rule intact even when another caller
        // invokes SaveClassAsync without going through the screen UI.
        var effectiveTraineeFees = traineeFees.ToDictionary(
            pair => pair.Key,
            pair => traineeAccounts[pair.Key].IsTuitionSupported ? 0 : pair.Value);
        if (effectiveTraineeFees.Any(pair => pair.Value < 0)
            || effectiveTraineeFees.Any(pair =>
                !traineeAccounts[pair.Key].IsTuitionSupported && pair.Value <= 0))
        {
            throw new InvalidOperationException("Học phí của học viên phải lớn hơn 0.");
        }

        if (coachRates.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
        {
            throw new InvalidOperationException("Mức lương mỗi buổi của Coach không được âm.");
        }

        trainingClass.Name = trainingClass.Name.Trim();
        trainingClass.UpdatedAtUtc = DateTime.UtcNow;
        var existing = await Database.FindAsync<TrainingClass>(trainingClass.Id);
        if (existing is null)
        {
            trainingClass.CreatedAtUtc = DateTime.UtcNow;
        }

        var currentCoaches = await Database.Table<ClassCoachAssignment>()
            .Where(item => item.ClassId == trainingClass.Id)
            .ToListAsync();
        foreach (var link in currentCoaches)
        {
            link.IsActive = coachRates.ContainsKey(link.CoachUserId);
            if (coachRates.TryGetValue(link.CoachUserId, out var rate))
            {
                link.SalaryPerSessionVnd = rate;
            }
        }

        var newCoachLinks = new List<ClassCoachAssignment>();
        foreach (var pair in coachRates)
        {
            if (currentCoaches.All(item => item.CoachUserId != pair.Key))
            {
                newCoachLinks.Add(new ClassCoachAssignment
                {
                    ClassId = trainingClass.Id,
                    CoachUserId = pair.Key,
                    SalaryPerSessionVnd = pair.Value,
                    IsActive = true
                });
            }
        }

        var currentEnrollments = await Database.Table<ClassEnrollment>()
            .Where(item => item.ClassId == trainingClass.Id)
            .ToListAsync();
        foreach (var enrollment in currentEnrollments)
        {
            enrollment.IsActive = effectiveTraineeFees.ContainsKey(enrollment.TraineeUserId);
            if (effectiveTraineeFees.TryGetValue(enrollment.TraineeUserId, out var fee))
            {
                enrollment.MonthlyFeeVnd = fee;
                enrollment.CycleFeeVnd = fee;
                var trialCount = trialSessions.GetValueOrDefault(enrollment.TraineeUserId);
                enrollment.IsTrial = !traineeAccounts[enrollment.TraineeUserId].IsTuitionSupported
                                     && trialCount > 0;
                enrollment.TrialSessionCount = enrollment.IsTrial
                    ? Math.Clamp(trialCount, 1, 5)
                    : 0;
            }
        }

        var newEnrollments = new List<ClassEnrollment>();
        foreach (var pair in effectiveTraineeFees)
        {
            if (currentEnrollments.All(item => item.TraineeUserId != pair.Key))
            {
                newEnrollments.Add(new ClassEnrollment
                {
                    ClassId = trainingClass.Id,
                    TraineeUserId = pair.Key,
                    MonthlyFeeVnd = pair.Value,
                    CycleFeeVnd = pair.Value,
                    IsTrial = !traineeAccounts[pair.Key].IsTuitionSupported
                              && trialSessions.GetValueOrDefault(pair.Key) > 0,
                    TrialSessionCount = !traineeAccounts[pair.Key].IsTuitionSupported
                                         ? Math.Clamp(trialSessions.GetValueOrDefault(pair.Key), 0, 5)
                                         : 0,
                    IsActive = true
                });
            }
        }

        await Database.RunInTransactionAsync(connection =>
        {
            if (existing is null)
            {
                connection.Insert(trainingClass);
            }
            else
            {
                connection.Update(trainingClass);
            }

            foreach (var link in currentCoaches)
            {
                connection.Update(link);
            }

            foreach (var link in newCoachLinks)
            {
                connection.Insert(link);
            }

            foreach (var enrollment in currentEnrollments)
            {
                connection.Update(enrollment);
            }

            foreach (var enrollment in newEnrollments)
            {
                connection.Insert(enrollment);
            }
        });

        await AddAuditAsync(actorUserId, "SaveClass", nameof(TrainingClass), trainingClass.Id, trainingClass.Name);
        await EnsureRecurringDataAsync(DateTime.Today);
        await PushCloudMutationAsync(
            actorUserId,
            classes: new[] { trainingClass },
            classCoaches: currentCoaches.Concat(newCoachLinks),
            classEnrollments: currentEnrollments.Concat(newEnrollments));
    }

    public async Task SetClassActiveAsync(string actorUserId, string classId, bool isActive)
    {
        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(actor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền bật/tắt lớp học.");
            await EnsureOnlineSnapshotAsync();
            var onlineTrainingClass = Online.Class(classId)
                                ?? throw new InvalidOperationException("Không tìm thấy lớp.");
            onlineTrainingClass.IsActive = isActive;
            onlineTrainingClass.UpdatedAtUtc = DateTime.UtcNow;
            await PushOnlineDeltaAsync(actor, classes: new[] { onlineTrainingClass });
            return;
        }

        await InitializeAsync();
        var classStatusActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(classStatusActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền bật/tắt lớp học.");
        await EnsureCloudWriteReadyAsync(actorUserId);
        var trainingClass = await Database.FindAsync<TrainingClass>(classId)
                            ?? throw new InvalidOperationException("Không tìm thấy lớp.");
        trainingClass.IsActive = isActive;
        trainingClass.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(trainingClass);
        await AddAuditAsync(actorUserId, "SetClassActive", nameof(TrainingClass), classId, isActive.ToString());
        await PushCloudMutationAsync(actorUserId, classes: new[] { trainingClass });
    }

    public async Task DeleteClassAsync(string actorUserId, string classId)
    {
        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(actor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền xóa lớp học.");
            await EnsureOnlineSnapshotAsync();
            var onlineClass = Online.Class(classId)
                ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
            try
            {
                await _cloudApi.DeleteAsync(
                    $"classes/{Uri.EscapeDataString(classId)}",
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }

            Online.Remove(Online.Classes, item => item.Id == classId);
            Online.Remove(Online.ClassCoaches, item => item.ClassId == classId);
            Online.Remove(Online.ClassEnrollments, item => item.ClassId == classId);
            var sessionIds = Online.TrainingSessions
                .Where(item => item.ClassId == classId)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            Online.Remove(Online.TrainingSessions, item => item.ClassId == classId);
            Online.Remove(Online.SessionCoaches, item => sessionIds.Contains(item.SessionId));
            Online.Remove(Online.CoachCheckIns, item => sessionIds.Contains(item.SessionId));
            Online.Remove(Online.AttendanceRecords, item => sessionIds.Contains(item.SessionId));
            var invoiceIds = Online.TuitionInvoices
                .Where(item => item.ClassId == classId)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            Online.Remove(Online.TuitionInvoices, item => item.ClassId == classId);
            Online.Remove(Online.PaymentProofs, item => invoiceIds.Contains(item.InvoiceId));
            Online.Remove(Online.Receipts, item => invoiceIds.Contains(item.InvoiceId));
            return;
        }

        await InitializeAsync();
        var deleteActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(deleteActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền xóa lớp học.");
        var trainingClass = await Database.FindAsync<TrainingClass>(classId)
            ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
        var classSessionIds = (await Database.Table<TrainingSession>()
                .Where(item => item.ClassId == classId)
                .ToListAsync())
            .Select(item => item.Id)
            .ToArray();
        var classInvoiceIds = (await Database.Table<TuitionInvoice>()
                .Where(item => item.ClassId == classId)
                .ToListAsync())
            .Select(item => item.Id)
            .ToArray();

        await Database.RunInTransactionAsync(connection =>
        {
            if (classInvoiceIds.Length > 0)
            {
                var invoiceArgs = string.Join(",", classInvoiceIds.Select(_ => "?"));
                connection.Execute(
                    $"DELETE FROM PaymentProofs WHERE InvoiceId IN ({invoiceArgs})",
                    classInvoiceIds.Cast<object>().ToArray());
                connection.Execute(
                    $"DELETE FROM Receipts WHERE InvoiceId IN ({invoiceArgs})",
                    classInvoiceIds.Cast<object>().ToArray());
                connection.Execute(
                    $"DELETE FROM TuitionInvoices WHERE Id IN ({invoiceArgs})",
                    classInvoiceIds.Cast<object>().ToArray());
            }

            if (classSessionIds.Length > 0)
            {
                var sessionArgs = string.Join(",", classSessionIds.Select(_ => "?"));
                connection.Execute(
                    $"DELETE FROM AttendanceRecords WHERE SessionId IN ({sessionArgs})",
                    classSessionIds.Cast<object>().ToArray());
                connection.Execute(
                    $"DELETE FROM CoachCheckIns WHERE SessionId IN ({sessionArgs})",
                    classSessionIds.Cast<object>().ToArray());
                connection.Execute(
                    $"DELETE FROM SessionCoachAssignments WHERE SessionId IN ({sessionArgs})",
                    classSessionIds.Cast<object>().ToArray());
                connection.Execute(
                    $"DELETE FROM TrainingSessions WHERE Id IN ({sessionArgs})",
                    classSessionIds.Cast<object>().ToArray());
            }

            connection.Execute("DELETE FROM ClassEnrollments WHERE ClassId = ?", classId);
            connection.Execute("DELETE FROM ClassCoachAssignments WHERE ClassId = ?", classId);
            connection.Delete(trainingClass);
        });
        await AddAuditAsync(
            actorUserId,
            "DeleteClass",
            nameof(TrainingClass),
            classId,
            trainingClass.Name);
    }

    public async Task<IReadOnlyList<ClassCoachAssignment>> GetClassCoachesAsync(string classId)
    {
        if (IsOnline)
        {
            await EnsureOnlineSnapshotAsync();
            return Online.ClassCoaches
                .Where(item => item.ClassId == classId && item.IsActive)
                .ToList();
        }

        await InitializeAsync();
        return await Database.Table<ClassCoachAssignment>()
            .Where(item => item.ClassId == classId && item.IsActive)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<ClassEnrollment>> GetClassEnrollmentsAsync(string classId)
    {
        if (IsOnline)
        {
            await EnsureOnlineSnapshotAsync();
            return Online.ClassEnrollments
                .Where(item => item.ClassId == classId && item.IsActive)
                .ToList();
        }

        await InitializeAsync();
        return await Database.Table<ClassEnrollment>()
            .Where(item => item.ClassId == classId && item.IsActive)
            .ToListAsync();
    }

    private async Task EnsureSessionCoachAssignmentsAsync(
        TrainingSession session,
        bool onlyWhenEmpty = false)
    {
        var activeAssignments = await Database.Table<ClassCoachAssignment>()
            .Where(item => item.ClassId == session.ClassId && item.IsActive)
            .ToListAsync();
        if (activeAssignments.Count == 0)
        {
            return;
        }

        var existingCoachIds = (await Database.Table<SessionCoachAssignment>()
                .Where(item => item.SessionId == session.Id)
                .ToListAsync())
            .Select(item => item.CoachUserId)
            .ToHashSet();
        if (onlyWhenEmpty && existingCoachIds.Count > 0)
        {
            return;
        }

        var snapshottedAtUtc = DateTime.UtcNow;
        var missing = activeAssignments
            .Where(item => !existingCoachIds.Contains(item.CoachUserId))
            .Select(item => new SessionCoachAssignment
            {
                SessionId = session.Id,
                CoachUserId = item.CoachUserId,
                SnapshottedAtUtc = snapshottedAtUtc
            })
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        await Database.RunInTransactionAsync(connection =>
        {
            foreach (var item in missing)
            {
                connection.Insert(item, "OR IGNORE");
            }
        });
    }

    private static string MediaCacheStem(string entityId, string sourceKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(sourceKey ?? string.Empty);
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(bytes))[..12];
        return $"{entityId}_{hash}";
    }

    private static string? FindCachedMediaPath(
        string category,
        string entityId,
        string sourceKey)
    {
        var directory = Path.Combine(FileSystem.AppDataDirectory, "media", category);
        if (!Directory.Exists(directory))
            return null;

        var stem = MediaCacheStem(entityId, sourceKey);
        foreach (var extension in new[] { ".jpg", ".png", ".webp" })
        {
            var candidate = Path.Combine(directory, stem + extension);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string CreateCachedMediaPath(
        string category,
        string entityId,
        string sourceKey,
        string contentType)
    {
        var extension = contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var directory = Path.Combine(FileSystem.AppDataDirectory, "media", category);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, MediaCacheStem(entityId, sourceKey) + extension);
    }

    private static bool IsPendingProfileImage(string? path) =>
        IsPendingMediaFile(path, "profiles");

    private static bool IsPendingClubLogo(string? path) =>
        // MediaService removes non-alphanumeric characters from the category
        // name, so "club_logo" is persisted under AppData/media/clublogo.
        IsPendingMediaFile(path, "clublogo");

    private static bool IsPendingMediaFile(string? path, string category)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var expectedDirectory = Path.GetFullPath(
            Path.Combine(FileSystem.AppDataDirectory, "media", category))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(expectedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private async Task MaterializeProfileImageAsync(
        string actorUserId,
        PersonProfile profile)
    {
        if (!IsOnline
            || string.IsNullOrWhiteSpace(profile.PhotoPath)
            || File.Exists(profile.PhotoPath))
        {
            return;
        }

        var sourceKey = profile.PhotoPath;
        var cachedPath = FindCachedMediaPath("avatar", profile.UserId, sourceKey);
        if (cachedPath is not null)
        {
            profile.PhotoPath = cachedPath;
            return;
        }

        try
        {
            await RequireOnlineUserAsync(actorUserId);
            var remote = await _cloudApi.DownloadFileAsync(
                $"users/{Uri.EscapeDataString(profile.UserId)}/avatar");
            var localPath = CreateCachedMediaPath(
                "avatar",
                profile.UserId,
                sourceKey,
                remote.ContentType);
            await File.WriteAllBytesAsync(localPath, remote.Bytes);
            profile.PhotoPath = localPath;
        }
        catch (ApiException)
        {
            // A missing R2 object must not prevent the profile/member page from
            // loading; UiKit.Avatar will render its normal placeholder.
        }
    }

    private async Task MaterializeClubLogoAsync(
        string actorUserId,
        ClubProfile club)
    {
        if (!IsOnline
            || string.IsNullOrWhiteSpace(club.LogoPath)
            || File.Exists(club.LogoPath))
        {
            return;
        }

        var sourceKey = club.LogoPath;
        var cachedPath = FindCachedMediaPath("club-logo", "club", sourceKey);
        if (cachedPath is not null)
        {
            club.LogoPath = cachedPath;
            return;
        }

        try
        {
            await RequireOnlineUserAsync(actorUserId);
            var remote = await _cloudApi.DownloadFileAsync("club/logo");
            var localPath = CreateCachedMediaPath(
                "club-logo",
                "club",
                sourceKey,
                remote.ContentType);
            await File.WriteAllBytesAsync(localPath, remote.Bytes);
            club.LogoPath = localPath;
        }
        catch (ApiException)
        {
            // Keep the team page usable if the private logo object was removed.
        }
    }

    private async Task MaterializeMemberImagesAsync(
        string actorUserId,
        IEnumerable<MemberRow> members)
    {
        foreach (var member in members)
        {
            await MaterializeProfileImageAsync(actorUserId, member.Profile);
        }
    }

    private async Task<IReadOnlyList<AttendanceRosterItem>> MaterializeAttendanceImagesAsync(
        string actorUserId,
        IEnumerable<AttendanceRosterItem> rows)
    {
        var materialized = new List<AttendanceRosterItem>();
        foreach (var row in rows)
        {
            var profile = Online.Profile(row.TraineeUserId);
            if (profile is null)
            {
                materialized.Add(row);
                continue;
            }

            await MaterializeProfileImageAsync(actorUserId, profile);
            materialized.Add(new AttendanceRosterItem
            {
                TraineeUserId = row.TraineeUserId,
                TraineeName = row.TraineeName,
                PhotoPath = profile.PhotoPath,
                Status = row.Status,
                ExistingRecord = row.ExistingRecord
            });
        }

        return materialized;
    }

    /// <summary>
    /// Materializes scheduled sessions for the recent local calendar and
    /// records an explicit, locked absence when a Coach never checked in by
    /// two hours after the class end.  The marker is stored in ReviewNote so
    /// this remains compatible with existing SQLite/D1 schemas.
    /// </summary>
    private async Task EnsureMissedCoachCheckInsAsync(DateTime localDate)
    {
        if (_cloudOptions.IsConfigured)
        {
            return;
        }

        var classes = await Database.Table<TrainingClass>()
            .Where(item => item.IsActive)
            .ToListAsync();
        var assignments = await Database.Table<ClassCoachAssignment>()
            .Where(item => item.IsActive)
            .ToListAsync();
        if (classes.Count == 0 || assignments.Count == 0)
        {
            return;
        }

        var sessions = await Database.Table<TrainingSession>().ToListAsync();
        var nowLocal = DateTime.Now;
        // A short lookback lets the app close missed classes after a day or
        // two offline, without generating an unbounded history on first run.
        for (var offset = -14; offset <= 0; offset++)
        {
            var date = localDate.Date.AddDays(offset);
            foreach (var trainingClass in classes)
            {
                if (date.Date < trainingClass.StartDate.Date)
                {
                    continue;
                }

                var scheduled = trainingClass.ScheduleDays
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Any(value => int.TryParse(value, out var day)
                                  && day == (int)date.DayOfWeek);
                if (!scheduled)
                {
                    continue;
                }

                var classAssignments = assignments
                    .Where(item => item.ClassId == trainingClass.Id
                                   && item.AssignedAtUtc.ToLocalTime().Date <= date)
                    .ToList();
                if (classAssignments.Count == 0)
                {
                    continue;
                }

                var session = sessions.FirstOrDefault(item =>
                    item.ClassId == trainingClass.Id && item.SessionDate == date);
                if (session is null)
                {
                    session = new TrainingSession
                    {
                        Id = EntityId.New(),
                        ClassId = trainingClass.Id,
                        SessionDate = date,
                        Status = SessionStatus.Draft,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    };
                    await Database.InsertAsync(session);
                    sessions.Add(session);
                }

                await EnsureSessionCoachAssignmentsAsync(session);
                if (nowLocal < CoachCheckInTime.CheckInLocksLocal(trainingClass, date))
                {
                    continue;
                }

                var existing = (await Database.Table<CoachCheckIn>()
                        .Where(item => item.SessionId == session.Id)
                        .ToListAsync())
                    .ToDictionary(item => item.CoachUserId);
                var closeAtUtc = CoachCheckInTime.CheckInLocksLocal(trainingClass, date)
                    .ToUniversalTime();
                foreach (var assignment in classAssignments)
                {
                    if (existing.ContainsKey(assignment.CoachUserId))
                    {
                        continue;
                    }

                    var absent = new CoachCheckIn
                    {
                        Id = EntityId.New(),
                        SessionId = session.Id,
                        CoachUserId = assignment.CoachUserId,
                        SelfiePath = string.Empty,
                        CheckOutSelfiePath = string.Empty,
                        SalaryPerSessionVndSnapshot = Math.Max(0, assignment.SalaryPerSessionVnd),
                        CheckedInAtUtc = closeAtUtc,
                        CheckedOutAtUtc = closeAtUtc,
                        DurationSeconds = 0,
                        ApprovalStatus = CoachCheckInApprovalStatus.Rejected,
                        ReviewNote = CoachCheckInTime.AutoAbsentReviewNote
                    };
                    await Database.InsertAsync(absent);
                    existing[assignment.CoachUserId] = absent;
                }
            }
        }
    }

    private async Task BackfillSessionCoachAssignmentsAsync()
    {
        var today = DateTime.Today;
        var submittedSessions = await Database.Table<TrainingSession>()
            .Where(item => item.Status == SessionStatus.Submitted
                           && item.SessionDate <= today)
            .ToListAsync();
        if (submittedSessions.Count == 0)
        {
            return;
        }

        var existingSnapshots = await Database.Table<SessionCoachAssignment>()
            .ToListAsync();
        var snapshottedSessionIds = existingSnapshots
            .Select(item => item.SessionId)
            .ToHashSet();
        var existingSnapshotKeys = existingSnapshots
            .Select(item => $"{item.SessionId}\u001F{item.CoachUserId}")
            .ToHashSet(StringComparer.Ordinal);
        var activeAssignments = await Database.Table<ClassCoachAssignment>()
            .Where(item => item.IsActive)
            .ToListAsync();
        var submittedSessionIds = submittedSessions
            .Select(item => item.Id)
            .ToHashSet();
        var checkIns = (await Database.Table<CoachCheckIn>().ToListAsync())
            .Where(item => submittedSessionIds.Contains(item.SessionId))
            .ToList();
        var snapshottedAtUtc = DateTime.UtcNow;
        var checkedInSnapshots = checkIns
            .Where(item => !existingSnapshotKeys.Contains(
                $"{item.SessionId}\u001F{item.CoachUserId}"))
            .Select(item => new SessionCoachAssignment
            {
                SessionId = item.SessionId,
                CoachUserId = item.CoachUserId,
                SnapshottedAtUtc = snapshottedAtUtc
            });
        var inferredSnapshots = submittedSessions
            .Where(session => !snapshottedSessionIds.Contains(session.Id))
            .SelectMany(session => activeAssignments
                .Where(assignment =>
                    assignment.ClassId == session.ClassId
                    && session.SessionDate >= assignment.AssignedAtUtc.Date)
                .Select(assignment => new SessionCoachAssignment
                {
                    SessionId = session.Id,
                    CoachUserId = assignment.CoachUserId,
                    SnapshottedAtUtc = snapshottedAtUtc
                }));
        var missing = checkedInSnapshots
            .Concat(inferredSnapshots)
            .GroupBy(
                item => $"{item.SessionId}\u001F{item.CoachUserId}",
                StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        await Database.RunInTransactionAsync(connection =>
        {
            foreach (var item in missing)
            {
                connection.Insert(item, "OR IGNORE");
            }
        });
    }

    public async Task<TrainingSession> GetOrCreateSessionAsync(
        string actorUserId,
        string classId,
        DateTime sessionDate)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            var onlineTrainingClass = Online.Class(classId)
                                ?? throw new InvalidOperationException("Không tìm thấy lớp.");
            EnsureOnlineClassAccess(onlineActor, classId);
            var onlineDate = sessionDate.Date;
            if (onlineDate > DateTime.Today)
                throw new InvalidOperationException("Không thể tạo điểm danh cho ngày tương lai.");
            if (onlineDate < onlineTrainingClass.StartDate.Date)
                throw new InvalidOperationException("Session date is before the class start date.");
            var onlineScheduledDays = onlineTrainingClass.ScheduleDays
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, out var day) ? day : -1)
                .ToHashSet();
            if (!onlineScheduledDays.Contains((int)onlineDate.DayOfWeek))
                throw new InvalidOperationException("Ngày đã chọn không thuộc lịch cố định của lớp.");
            var onlineExisting = Online.TrainingSessions
                .FirstOrDefault(item => item.ClassId == classId && item.SessionDate == onlineDate);
            if (onlineExisting is not null) return onlineExisting;

            var onlineSession = new TrainingSession
            {
                Id = EntityId.New(),
                ClassId = classId,
                SessionDate = onlineDate,
                Status = SessionStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            var onlineAssignments = Online.ClassCoaches
                .Where(item => item.ClassId == classId && item.IsActive)
                .Select(item => new SessionCoachAssignment
                {
                    Id = EntityId.New(),
                    SessionId = onlineSession.Id,
                    CoachUserId = item.CoachUserId,
                    SnapshottedAtUtc = DateTime.UtcNow
                })
                .ToList();
            await PushOnlineDeltaAsync(onlineActor, trainingSessions: new[] { onlineSession }, sessionCoaches: onlineAssignments);
            Online.TrainingSessions.Add(onlineSession);
            Online.SessionCoaches.AddRange(onlineAssignments);
            return onlineSession;
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        await EnsureClassAccessAsync(actor, classId, writeAttendance: true);
        var date = sessionDate.Date;
        if (date > DateTime.Today)
        {
            throw new InvalidOperationException("Không thể tạo điểm danh cho ngày tương lai.");
        }

        var trainingClass = await Database.FindAsync<TrainingClass>(classId)
                            ?? throw new InvalidOperationException("Không tìm thấy lớp.");
        if (date < trainingClass.StartDate.Date)
        {
            throw new InvalidOperationException(
                "Session date is before the class start date.");
        }

        var scheduledDays = trainingClass.ScheduleDays
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var day) ? day : -1)
            .ToHashSet();
        if (!scheduledDays.Contains((int)date.DayOfWeek))
        {
            throw new InvalidOperationException("Ngày đã chọn không thuộc lịch cố định của lớp.");
        }

        var session = await Database.Table<TrainingSession>()
            .Where(item => item.ClassId == classId && item.SessionDate == date)
            .FirstOrDefaultAsync();

        if (session is not null)
        {
            if (session.Status is SessionStatus.Draft or SessionStatus.Submitted)
            {
                await EnsureSessionCoachAssignmentsAsync(
                    session,
                    onlyWhenEmpty: session.Status == SessionStatus.Submitted);
            }

            return session;
        }

        session = new TrainingSession
        {
            Id = EntityId.New(),
            ClassId = classId,
            SessionDate = date,
            Status = SessionStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        await Database.InsertAsync(session);
        await EnsureSessionCoachAssignmentsAsync(session);
        return session;
    }

    public async Task<IReadOnlyList<TrainingSession>> GetSessionsForClassAsync(
        string actorUserId,
        string classId,
        int limit = 30)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            EnsureOnlineClassAccess(onlineActor, classId);
            return Online.TrainingSessions
                .Where(item => item.ClassId == classId)
                .OrderByDescending(item => item.SessionDate)
                .Take(limit)
                .ToList();
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        await EnsureClassAccessAsync(actor, classId, writeAttendance: false);
        var sessions = await Database.Table<TrainingSession>()
            .Where(item => item.ClassId == classId)
            .ToListAsync();
        return sessions
            .OrderByDescending(item => item.SessionDate)
            .Take(limit)
            .ToList();
    }

    public async Task<int> GetCompletedAttendanceCountAsync(
        string actorUserId,
        string classId,
        string traineeUserId)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền xem điểm danh.");
            await EnsureOnlineSnapshotAsync();
            var sessionIds = Online.TrainingSessions
                .Where(item => item.ClassId == classId
                               && item.Status is SessionStatus.Submitted or SessionStatus.Locked)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            return Online.AttendanceRecords.Count(item =>
                item.TraineeUserId == traineeUserId
                && sessionIds.Contains(item.SessionId)
                && item.Status != AttendanceStatus.Unmarked);
        }

        await InitializeAsync();
        var attendanceActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(attendanceActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem điểm danh.");
        var sessions = await Database.Table<TrainingSession>()
            .Where(item => item.ClassId == classId
                           && (item.Status == SessionStatus.Submitted
                               || item.Status == SessionStatus.Locked))
            .ToListAsync();
        var sessionIdsLocal = sessions.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        return (await Database.Table<AttendanceRecord>()
                .Where(item => item.TraineeUserId == traineeUserId)
                .ToListAsync())
            .Count(item => sessionIdsLocal.Contains(item.SessionId)
                           && item.Status != AttendanceStatus.Unmarked);
    }

    /// <summary>
    /// Returns the number of distinct sessions in a class for which a Coach
    /// completed check-out.  A Founder substitution is deliberately excluded:
    /// the class was delivered, but no Coach taught that session.  The query
    /// is tenant-scoped through the same access checks used by class details,
    /// so Founder/Coach/Trainee see only classes they are allowed to inspect.
    /// </summary>
    public async Task<int> GetClassTaughtSessionCountAsync(
        string actorUserId,
        string classId)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            EnsureOnlineClassAccess(onlineActor, classId);

            var sessionIds = Online.TrainingSessions
                .Where(item => item.ClassId == classId)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            return Online.CoachCheckIns
                .Where(item => sessionIds.Contains(item.SessionId)
                               && CoachCheckInTime.HasCoachCheckout(item)
                               && !CoachCheckInTime.IsFounderSubstitution(item)
                               && (onlineActor.Role != UserRole.Coach
                                   || item.CoachUserId == onlineActor.Id))
                .Select(item => item.SessionId)
                .Distinct(StringComparer.Ordinal)
                .Count();
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        await EnsureClassAccessAsync(actor, classId, writeAttendance: false);
        var sessionIdsLocal = (await Database.Table<TrainingSession>()
                .Where(item => item.ClassId == classId)
                .ToListAsync())
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var checkIns = await Database.Table<CoachCheckIn>().ToListAsync();
        return checkIns
            .Where(item => sessionIdsLocal.Contains(item.SessionId)
                           && CoachCheckInTime.HasCoachCheckout(item)
                           && !CoachCheckInTime.IsFounderSubstitution(item)
                           && (actor.Role != UserRole.Coach
                               || item.CoachUserId == actor.Id))
            .Select(item => item.SessionId)
            .Distinct(StringComparer.Ordinal)
            .Count();
    }

    public async Task<IReadOnlyList<AttendanceRosterItem>> GetAttendanceRosterAsync(
        string actorUserId,
        string sessionId,
        bool historicalSnapshot = false)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            var onlineSession = Online.Session(sessionId)
                          ?? throw new InvalidOperationException("Không tìm thấy buổi học.");
            EnsureOnlineClassAccess(onlineActor, onlineSession.ClassId);
            if (onlineActor.Role == UserRole.Coach)
            {
                var onlineCheckIn = Online.CheckIn(onlineSession.Id, onlineActor.Id);
                if (onlineCheckIn is null || onlineCheckIn.CheckedOutAtUtc is not null)
                    throw new InvalidOperationException("Vui lòng chụp selfie check-in trước khi xem danh sách học viên.");
            }
            var onlineRecords = Online.AttendanceRecords
                .Where(item => item.SessionId == sessionId)
                .ToDictionary(item => item.TraineeUserId);
            var onlineEnrollments = Online.ClassEnrollments
                .Where(item => item.ClassId == onlineSession.ClassId
                               && item.IsActive
                                && item.EnrolledAtUtc <= onlineSession.CreatedAtUtc)
                .ToList();
            // A historical/locked session must use the attendance records
            // captured for that session, not today's current enrollment.  A
            // trainee added later must never appear in an older class history.
            var useOnlineSessionRecords = (historicalSnapshot
                                           || onlineSession.Status is SessionStatus.Submitted or SessionStatus.Locked)
                                          && !CoachCheckInTime.IsFounderNoAttendance(onlineSession);
            var onlineIds = useOnlineSessionRecords
                ? onlineRecords.Keys.ToHashSet()
                : onlineEnrollments.Select(item => item.TraineeUserId)
                    .Concat(onlineRecords.Keys)
                    .ToHashSet();
            if (onlineActor.Role == UserRole.Trainee) onlineIds.IntersectWith([onlineActor.Id]);
            var onlineRoster = onlineIds
                .Select(id =>
                {
                    onlineRecords.TryGetValue(id, out var record);
                    var profile = Online.Profile(id);
                    return new AttendanceRosterItem
                    {
                        TraineeUserId = id,
                        TraineeName = string.IsNullOrWhiteSpace(profile?.FullName) ? "Học viên" : profile.FullName,
                        PhotoPath = profile?.PhotoPath ?? string.Empty,
                        Status = record?.Status ?? AttendanceStatus.Unmarked,
                        ExistingRecord = record
                    };
                })
                .OrderBy(item => item.TraineeName)
                .ToList();
            return await MaterializeAttendanceImagesAsync(onlineActor.Id, onlineRoster);
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        var session = await Database.FindAsync<TrainingSession>(sessionId)
                      ?? throw new InvalidOperationException("Không tìm thấy buổi học.");
        await EnsureClassAccessAsync(actor, session.ClassId, writeAttendance: actor.Role != UserRole.Trainee);
        await EnsureCoachSessionIsOpenAsync(actor, session);

        var enrollments = await Database.Table<ClassEnrollment>()
            .Where(item => item.ClassId == session.ClassId
                           && item.IsActive
                           && item.EnrolledAtUtc <= session.CreatedAtUtc)
            .ToListAsync();
        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);
        var records = await Database.Table<AttendanceRecord>()
            .Where(item => item.SessionId == sessionId)
            .ToListAsync();
        var recordMap = records.ToDictionary(item => item.TraineeUserId);

        var useSessionRecords = (historicalSnapshot
                                 || session.Status is SessionStatus.Submitted or SessionStatus.Locked)
                                && !CoachCheckInTime.IsFounderNoAttendance(session);
        var traineeIds = useSessionRecords
            ? records.Select(item => item.TraineeUserId).ToHashSet()
            : enrollments.Select(item => item.TraineeUserId)
                .Concat(records.Select(item => item.TraineeUserId))
                .ToHashSet();

        if (actor.Role == UserRole.Trainee)
        {
            traineeIds.IntersectWith([actor.Id]);
        }

        return traineeIds
            .Select(traineeUserId =>
            {
                recordMap.TryGetValue(traineeUserId, out var record);
                var profile = profiles.GetValueOrDefault(traineeUserId);
                return new AttendanceRosterItem
                {
                    TraineeUserId = traineeUserId,
                    TraineeName = string.IsNullOrWhiteSpace(profile?.FullName)
                        ? "Học viên"
                        : profile.FullName,
                    PhotoPath = profile?.PhotoPath ?? string.Empty,
                    Status = record?.Status ?? AttendanceStatus.Unmarked,
                    ExistingRecord = record
                };
            })
            .OrderBy(item => item.TraineeName)
            .ToList();
    }

    private static string FounderSubstitutionReason(string reason)
    {
        var trimmed = reason.Trim();
        const string suffix = "Coach không dạy; Founder điểm danh thay Coach";
        return trimmed.Contains(suffix, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed} · {suffix}";
    }

    private static string FounderNoAttendanceReason(string reason)
    {
        var trimmed = reason.Trim();
        return trimmed.Contains(
                   CoachCheckInTime.FounderNoAttendanceMarker,
                   StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed} · {CoachCheckInTime.FounderNoAttendanceMarker}";
    }

    private static string FounderManualTaughtReason(string reason)
    {
        var trimmed = reason.Trim();
        const string suffix = "Founder ghi nhận buổi học cũ; Coach đã dạy";
        return trimmed.Contains(suffix, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed} · {suffix}";
    }

    public async Task SaveAttendanceAsync(
        string actorUserId,
        string sessionId,
        IEnumerable<AttendanceRosterItem> roster,
        bool submit,
        string overrideReason,
        bool founderCoachTaughtManually = false,
        bool founderNoAttendance = false)
    {
        if (founderCoachTaughtManually && founderNoAttendance)
        {
            throw new InvalidOperationException("Chỉ được chọn một trạng thái dạy cho buổi học cũ.");
        }

        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            if (onlineActor.Role is not (UserRole.Founder or UserRole.CoFounder or UserRole.Coach))
                throw new UnauthorizedAccessException("Tài khoản này không có quyền điểm danh.");
            var onlineSession = Online.Session(sessionId)
                          ?? throw new InvalidOperationException("Không tìm thấy buổi học.");
            EnsureOnlineClassAccess(onlineActor, onlineSession.ClassId);
            if (onlineActor.Role == UserRole.Coach)
            {
                var open = Online.CheckIn(sessionId, onlineActor.Id);
                if (open is null || open.CheckedOutAtUtc is not null)
                    throw new InvalidOperationException("Vui lòng chụp selfie check-in trước khi điểm danh.");
            }
            if (RoleCapabilities.IsFounderLike(onlineActor.Role) && submit && string.IsNullOrWhiteSpace(overrideReason))
                throw new InvalidOperationException("Founder cần nhập lý do khi điểm danh thay.");
            var storedOverrideReason = RoleCapabilities.IsFounderLike(onlineActor.Role) && submit
                ? founderNoAttendance
                    ? FounderNoAttendanceReason(overrideReason)
                    : founderCoachTaughtManually
                        ? FounderManualTaughtReason(overrideReason)
                        : FounderSubstitutionReason(overrideReason)
                : RoleCapabilities.IsFounderLike(onlineActor.Role)
                    ? overrideReason.Trim()
                    : string.Empty;
            var onlineItems = roster.ToList();
            if (submit && !founderNoAttendance
                && onlineItems.Any(item => item.Status == AttendanceStatus.Unmarked))
                throw new InvalidOperationException("Vui lòng ghi nhận trạng thái cho tất cả học viên.");
            var onlineRecords = new List<AttendanceRecord>();
            foreach (var item in founderNoAttendance ? [] : onlineItems)
            {
                var record = Online.AttendanceRecords.FirstOrDefault(value =>
                    value.SessionId == sessionId && value.TraineeUserId == item.TraineeUserId);
                if (record is null)
                {
                    record = new AttendanceRecord
                    {
                        Id = EntityId.New(),
                        SessionId = sessionId,
                        TraineeUserId = item.TraineeUserId,
                        Revision = 1
                    };
                }
                else if (record.Status != item.Status)
                {
                    record.Revision++;
                }
                record.Status = item.Status;
                record.RecordedByUserId = actorUserId;
                record.RecordedAtUtc = DateTime.UtcNow;
                onlineRecords.Add(record);
            }
            try
            {
                await _cloudApi.PutAsync<object>(
                    $"attendance/{Uri.EscapeDataString(sessionId)}",
                    new
                    {
                        records = onlineRecords.Select(record => new
                        {
                            id = record.Id,
                            sessionId = record.SessionId,
                            traineeUserId = record.TraineeUserId,
                            status = record.Status.ToString().ToLowerInvariant(),
                            recordedByUserId = record.RecordedByUserId,
                            recordedAt = record.RecordedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                            notes = record.Notes,
                            revision = record.Revision
                        }).ToArray(),
                        submit,
                        // The Worker owns the canonical historical marker. Do
                        // not send the already-suffixed local display text or
                        // each reload would append the marker again.
                        overrideReason = RoleCapabilities.IsFounderLike(onlineActor.Role) && submit
                            ? overrideReason.Trim()
                            : storedOverrideReason,
                        coachTaughtManually = founderCoachTaughtManually,
                        founderNoAttendance
                    },
                    EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
            if (founderNoAttendance)
            {
                await ReloadOnlineSnapshotAsync();
                return;
            }

            foreach (var record in onlineRecords)
                Online.Upsert(Online.AttendanceRecords, record, value => value.Id == record.Id);
            onlineSession.Status = submit ? SessionStatus.Submitted : SessionStatus.Draft;
            onlineSession.SubmittedByUserId = submit ? actorUserId : onlineSession.SubmittedByUserId;
            onlineSession.SubmittedAtUtc = submit ? DateTime.UtcNow : onlineSession.SubmittedAtUtc;
            onlineSession.OverrideReason = RoleCapabilities.IsFounderLike(onlineActor.Role)
                ? storedOverrideReason
                : onlineSession.OverrideReason;
            onlineSession.UpdatedAtUtc = DateTime.UtcNow;
            if (submit && RoleCapabilities.IsFounderLike(onlineActor.Role) && !founderCoachTaughtManually)
            {
                var substitutionAt = DateTime.UtcNow;
                foreach (var assignment in Online.ClassCoaches
                             .Where(item => item.ClassId == onlineSession.ClassId && item.IsActive))
                {
                    var existingCheckIn = Online.CoachCheckIns.FirstOrDefault(item =>
                        item.SessionId == sessionId && item.CoachUserId == assignment.CoachUserId);
                    if (existingCheckIn is not null && !string.IsNullOrWhiteSpace(existingCheckIn.SelfiePath))
                    {
                        continue;
                    }

                    var substituted = existingCheckIn ?? new CoachCheckIn
                    {
                        Id = EntityId.New(),
                        SessionId = sessionId,
                        CoachUserId = assignment.CoachUserId
                    };
                    substituted.SelfiePath = string.Empty;
                    substituted.CheckOutSelfiePath = string.Empty;
                    substituted.SalaryPerSessionVndSnapshot = assignment.SalaryPerSessionVnd;
                    substituted.CheckedInAtUtc = substitutionAt;
                    substituted.CheckedOutAtUtc = substitutionAt;
                    substituted.DurationSeconds = 0;
                    substituted.ApprovalStatus = CoachCheckInApprovalStatus.Approved;
                    substituted.ReviewedByUserId = onlineActor.Id;
                    substituted.ReviewedAtUtc = substitutionAt;
                    substituted.ReviewNote = CoachCheckInTime.FounderSubstitutedCoachReviewNote;
                    Online.Upsert(Online.CoachCheckIns, substituted, value => value.Id == substituted.Id);
                }
            }
            else if (submit && RoleCapabilities.IsFounderLike(onlineActor.Role))
            {
                // The Worker derives historical Coach rows/salary in the same
                // attendance transaction. Reload both modes so a transition
                // from manual to Founder substitution also removes stale pay.
                await ReloadOnlineSnapshotAsync();
            }
            return;
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        if (actor.Role is not (UserRole.Founder or UserRole.CoFounder or UserRole.Coach))
        {
            throw new UnauthorizedAccessException("Tài khoản này không có quyền điểm danh.");
        }

        var session = await Database.FindAsync<TrainingSession>(sessionId)
                      ?? throw new InvalidOperationException("Không tìm thấy buổi học.");
        await EnsureClassAccessAsync(actor, session.ClassId, writeAttendance: true);
        await EnsureCoachSessionIsOpenAsync(actor, session);
        if (RoleCapabilities.IsFounderLike(actor.Role) && string.IsNullOrWhiteSpace(overrideReason))
        {
            throw new InvalidOperationException("Founder cần nhập lý do khi điểm danh thay.");
        }
        var storedLocalOverrideReason = RoleCapabilities.IsFounderLike(actor.Role) && submit
            ? founderNoAttendance
                ? FounderNoAttendanceReason(overrideReason)
                : founderCoachTaughtManually
                    ? FounderManualTaughtReason(overrideReason)
                    : FounderSubstitutionReason(overrideReason)
            : RoleCapabilities.IsFounderLike(actor.Role)
                ? overrideReason.Trim()
                : session.OverrideReason;

        if (session.Status == SessionStatus.Submitted && !submit)
        {
            throw new InvalidOperationException("Buổi học đã hoàn tất, không thể chuyển lại thành bản nháp.");
        }

        if (submit && session.Status == SessionStatus.Draft)
        {
            await EnsureSessionCoachAssignmentsAsync(session);
        }

        var items = roster.ToList();
        if (submit && !founderNoAttendance
            && items.Any(item => item.Status == AttendanceStatus.Unmarked))
        {
            throw new InvalidOperationException("Vui lòng ghi nhận trạng thái cho tất cả học viên.");
        }

        if (submit && founderNoAttendance)
        {
            var existingCheckIns = await Database.Table<CoachCheckIn>()
                .Where(item => item.SessionId == session.Id)
                .ToListAsync();
            if (existingCheckIns.Any(item => !string.IsNullOrWhiteSpace(item.SelfiePath)
                                             || !string.IsNullOrWhiteSpace(item.CheckOutSelfiePath)))
            {
                throw new InvalidOperationException(
                    "Không thể chọn Coach không dạy vì buổi này đã có ảnh check-in/check-out thật.");
            }

            var oldRecords = await Database.Table<AttendanceRecord>()
                .Where(item => item.SessionId == session.Id)
                .ToListAsync();
            var removableCheckIns = existingCheckIns
                .Where(item => CoachCheckInTime.IsFounderManualTaught(item)
                               || CoachCheckInTime.IsFounderSubstitution(item)
                               || string.IsNullOrWhiteSpace(item.SelfiePath))
                .ToList();

            session.Status = SessionStatus.Draft;
            session.SubmittedByUserId = string.Empty;
            session.SubmittedAtUtc = null;
            session.OverrideReason = storedLocalOverrideReason;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await Database.RunInTransactionAsync(connection =>
            {
                foreach (var record in oldRecords)
                {
                    connection.Delete(record);
                }

                foreach (var checkIn in removableCheckIns)
                {
                    connection.Delete(checkIn);
                }

                connection.Update(session);
            });

            var pendingSalaries = await Database.Table<CoachSalary>()
                .Where(item => item.Status == SalaryStatus.Pending)
                .ToListAsync();
            foreach (var salary in pendingSalaries)
            {
                await RecomputePendingSalaryAsync(salary);
            }

            if (_cloudOptions.IsConfigured)
            {
                await _cloudApi.PutAsync<object>(
                    $"attendance/{Uri.EscapeDataString(session.Id)}",
                    new
                    {
                        records = Array.Empty<object>(),
                        submit = true,
                        overrideReason,
                        coachTaughtManually = false,
                        founderNoAttendance = true
                    },
                    EntityId.New());
            }

            await AddAuditAsync(
                actorUserId,
                "SubmitAttendance",
                nameof(TrainingSession),
                session.Id,
                storedLocalOverrideReason,
                writeCloud: !_cloudOptions.IsConfigured);
            QueueCloudProjectionRefresh();
            return;
        }

        var recordsToInsert = new List<AttendanceRecord>();
        var recordsToUpdate = new List<AttendanceRecord>();
        var substitutedCheckInsToInsert = new List<CoachCheckIn>();
        var substitutedCheckInsToUpdate = new List<CoachCheckIn>();
        var manuallyTaughtCoachIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var record = item.ExistingRecord
                         ?? await Database.Table<AttendanceRecord>()
                             .Where(value => value.SessionId == sessionId
                                             && value.TraineeUserId == item.TraineeUserId)
                             .FirstOrDefaultAsync();
            if (record is null)
            {
                record = new AttendanceRecord
                {
                    Id = EntityId.New(),
                    SessionId = sessionId,
                    TraineeUserId = item.TraineeUserId,
                    Status = item.Status,
                    RecordedByUserId = actorUserId,
                    RecordedAtUtc = DateTime.UtcNow,
                    Revision = 1
                };
                recordsToInsert.Add(record);
            }
            else
            {
                if (actor.Role == UserRole.Coach
                    && session.Status == SessionStatus.Submitted
                    && record.Status != item.Status
                    && !(record.Status == AttendanceStatus.Absent
                         && item.Status == AttendanceStatus.Late))
                {
                    throw new InvalidOperationException(
                        "Sau khi hoàn tất, Coach chỉ được sửa học viên từ Vắng thành Đi trễ.");
                }

                if (record.Status != item.Status)
                {
                    record.Revision++;
                }

                record.Status = item.Status;
                record.RecordedByUserId = actorUserId;
                record.RecordedAtUtc = DateTime.UtcNow;
                recordsToUpdate.Add(record);
            }
        }

        if (submit && RoleCapabilities.IsFounderLike(actor.Role) && !founderCoachTaughtManually)
        {
            var assignments = await Database.Table<ClassCoachAssignment>()
                .Where(item => item.ClassId == session.ClassId && item.IsActive)
                .ToListAsync();
            var existingCheckIns = (await Database.Table<CoachCheckIn>()
                    .Where(item => item.SessionId == session.Id)
                    .ToListAsync())
                .ToDictionary(item => item.CoachUserId);
            var substitutedAt = DateTime.UtcNow;
            foreach (var assignment in assignments)
            {
                existingCheckIns.TryGetValue(assignment.CoachUserId, out var checkIn);
                if (checkIn is not null && !string.IsNullOrWhiteSpace(checkIn.SelfiePath))
                {
                    continue;
                }

                checkIn ??= new CoachCheckIn
                {
                    Id = EntityId.New(),
                    SessionId = session.Id,
                    CoachUserId = assignment.CoachUserId
                };
                checkIn.SelfiePath = string.Empty;
                checkIn.CheckOutSelfiePath = string.Empty;
                checkIn.SalaryPerSessionVndSnapshot = assignment.SalaryPerSessionVnd;
                checkIn.CheckedInAtUtc = substitutedAt;
                checkIn.CheckedOutAtUtc = substitutedAt;
                checkIn.DurationSeconds = 0;
                checkIn.ApprovalStatus = CoachCheckInApprovalStatus.Approved;
                checkIn.ReviewedByUserId = actor.Id;
                checkIn.ReviewedAtUtc = substitutedAt;
                checkIn.ReviewNote = CoachCheckInTime.FounderSubstitutedCoachReviewNote;
                if (existingCheckIns.ContainsKey(assignment.CoachUserId))
                {
                    substitutedCheckInsToUpdate.Add(checkIn);
                }
                else
                {
                    substitutedCheckInsToInsert.Add(checkIn);
                }
            }
        }
        else if (submit && RoleCapabilities.IsFounderLike(actor.Role) && founderCoachTaughtManually)
        {
            // Historical classes can be entered after the app goes live. A
            // Founder explicitly choosing “Đã dạy (ghi nhận thủ công)” must
            // create an approved, payable Coach teaching row even though no
            // selfie exists for that old lesson.
            var assignments = await Database.Table<ClassCoachAssignment>()
                .Where(item => item.ClassId == session.ClassId && item.IsActive)
                .ToListAsync();
            var existingCheckIns = (await Database.Table<CoachCheckIn>()
                    .Where(item => item.SessionId == session.Id)
                    .ToListAsync())
                .ToDictionary(item => item.CoachUserId);
            var recordedAt = DateTime.UtcNow;
            foreach (var assignment in assignments)
            {
                existingCheckIns.TryGetValue(assignment.CoachUserId, out var checkIn);
                var alreadyPayable = checkIn is not null
                                      && checkIn.ApprovalStatus == CoachCheckInApprovalStatus.Approved
                                      && CoachCheckInTime.HasCoachCheckout(checkIn);
                manuallyTaughtCoachIds.Add(assignment.CoachUserId);
                if (alreadyPayable)
                {
                    continue;
                }

                checkIn ??= new CoachCheckIn
                {
                    Id = EntityId.New(),
                    SessionId = session.Id,
                    CoachUserId = assignment.CoachUserId
                };
                checkIn.SalaryPerSessionVndSnapshot = Math.Max(
                    0,
                    assignment.SalaryPerSessionVnd);
                checkIn.CheckedInAtUtc = checkIn.CheckedInAtUtc == default
                    ? recordedAt
                    : checkIn.CheckedInAtUtc;
                checkIn.CheckedOutAtUtc ??= checkIn.CheckedInAtUtc;
                checkIn.DurationSeconds = Math.Max(
                    0,
                    checkIn.DurationSeconds);
                checkIn.ApprovalStatus = CoachCheckInApprovalStatus.Approved;
                checkIn.ReviewedByUserId = actor.Id;
                checkIn.ReviewedAtUtc = recordedAt;
                checkIn.ReviewNote = storedLocalOverrideReason;
                if (existingCheckIns.ContainsKey(assignment.CoachUserId))
                {
                    substitutedCheckInsToUpdate.Add(checkIn);
                }
                else
                {
                    substitutedCheckInsToInsert.Add(checkIn);
                }
            }
        }

        session.Status = submit ? SessionStatus.Submitted : SessionStatus.Draft;
        session.SubmittedByUserId = submit ? actorUserId : session.SubmittedByUserId;
        session.SubmittedAtUtc = submit ? DateTime.UtcNow : session.SubmittedAtUtc;
        session.OverrideReason = storedLocalOverrideReason;
        session.UpdatedAtUtc = DateTime.UtcNow;
        await Database.RunInTransactionAsync(connection =>
        {
            foreach (var record in recordsToInsert)
            {
                connection.Insert(record);
            }

            foreach (var record in recordsToUpdate)
            {
                connection.Update(record);
            }

            foreach (var checkIn in substitutedCheckInsToInsert)
            {
                connection.Insert(checkIn);
            }

            foreach (var checkIn in substitutedCheckInsToUpdate)
            {
                connection.Update(checkIn);
            }

            connection.Update(session);
        });

        foreach (var coachUserId in manuallyTaughtCoachIds)
        {
            await EnsureCoachSalaryForPeriodAsync(coachUserId, session.SessionDate);
        }

        if (submit && RoleCapabilities.IsFounderLike(actor.Role) && !founderCoachTaughtManually)
        {
            // Switching a previously manual historical session to Founder
            // substitution must remove its pending salary contribution. The
            // salary total is derived from payable check-ins, so recomputing
            // pending rows is safer than subtracting a guessed rate.
            var pendingSalaries = await Database.Table<CoachSalary>()
                .Where(item => item.Status == SalaryStatus.Pending)
                .ToListAsync();
            foreach (var salary in pendingSalaries)
            {
                await RecomputePendingSalaryAsync(salary);
            }
        }

        if (_cloudOptions.IsConfigured)
        {
            var cloudRecords = recordsToInsert
                .Concat(recordsToUpdate)
                .Select(record => new
                {
                    id = record.Id,
                    sessionId = record.SessionId,
                    traineeUserId = record.TraineeUserId,
                    status = record.Status.ToString().ToLowerInvariant(),
                    recordedByUserId = record.RecordedByUserId,
                    recordedAt = record.RecordedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    notes = record.Notes,
                    revision = record.Revision
                })
                .ToArray();
            if (cloudRecords.Length > 0)
            {
                await _cloudApi.PutAsync<object>(
                    $"attendance/{Uri.EscapeDataString(session.Id)}",
                    new
                    {
                        records = cloudRecords,
                        submit,
                        overrideReason = RoleCapabilities.IsFounderLike(actor.Role)
                            ? overrideReason.Trim()
                            : string.Empty,
                        coachTaughtManually = founderCoachTaughtManually,
                        founderNoAttendance = false
                    },
                    EntityId.New());
            }
        }

        if (submit && !_cloudOptions.IsConfigured)
        {
            foreach (var item in items)
            {
                await AddNotificationAsync(
                    item.TraineeUserId,
                    NotificationKind.AttendanceUpdated,
                    "Điểm danh đã được cập nhật",
                    $"{session.SessionDate:dd/MM/yyyy}: {DomainText.Attendance(item.Status)}.",
                    session.Id);
            }
        }

        await AddAuditAsync(
            actorUserId,
            submit ? "SubmitAttendance" : "SaveAttendanceDraft",
            nameof(TrainingSession),
            session.Id,
            RoleCapabilities.IsFounderLike(actor.Role) ? storedLocalOverrideReason : string.Empty,
            writeCloud: !_cloudOptions.IsConfigured);
        QueueCloudProjectionRefresh();
    }

    private async Task EnsureCoachSessionIsOpenAsync(
        UserAccount actor,
        TrainingSession session)
    {
        if (actor.Role != UserRole.Coach)
        {
            return;
        }

        var checkIn = await Database.Table<CoachCheckIn>()
            .Where(item => item.SessionId == session.Id && item.CoachUserId == actor.Id)
            .FirstOrDefaultAsync();
        checkIn = await AutoCloseStaleCoachCheckInAsync(checkIn);
        if (checkIn is null)
        {
            throw new InvalidOperationException(
                "Vui lòng chụp selfie check-in trước khi xem danh sách học viên.");
        }

        if (checkIn.ApprovalStatus == CoachCheckInApprovalStatus.Rejected)
        {
            throw new InvalidOperationException(
                "Check-in bị từ chối, vui lòng chụp lại selfie trước khi xem danh sách học viên.");
        }

        if (checkIn.CheckedOutAtUtc is not null)
        {
            throw new InvalidOperationException(
                "Buổi học đã check-out, danh sách học viên không còn mở.");
        }
    }

    /// <summary>
    /// Safety net for a Coach who leaves the app without checking out. The
    /// session is marked as closed at an eight-hour cap so the timer and
    /// trainee roster cannot remain open indefinitely. Because the checkout
    /// selfie is intentionally empty, this state is not eligible for Founder
    /// approval or salary; the Coach must still submit a real checkout selfie.
    /// </summary>
    private async Task<CoachCheckIn?> AutoCloseStaleCoachCheckInAsync(
        CoachCheckIn? checkIn,
        DateTime? nowUtc = null)
    {
        if (checkIn is null
            || checkIn.CheckedOutAtUtc is not null
            || checkIn.CheckedInAtUtc.ToUniversalTime().AddSeconds(
                CoachCheckInTime.MaxOpenDurationSeconds) > (nowUtc ?? DateTime.UtcNow))
        {
            return checkIn;
        }

        var closeAt = checkIn.CheckedInAtUtc.ToUniversalTime().AddSeconds(
            CoachCheckInTime.MaxOpenDurationSeconds);
        checkIn.CheckedOutAtUtc = closeAt;
        checkIn.DurationSeconds = CoachCheckInTime.MaxOpenDurationSeconds;
        checkIn.CheckOutSelfiePath = string.Empty;
        await Database.UpdateAsync(checkIn);
        return checkIn;
    }

    private async Task AutoCloseStaleCoachCheckInsAsync(IEnumerable<CoachCheckIn> checkIns)
    {
        foreach (var checkIn in checkIns)
        {
            await AutoCloseStaleCoachCheckInAsync(checkIn);
        }
    }

    public async Task<CoachCheckIn> SaveCoachCheckInAsync(
        string actorUserId,
        string sessionId,
        string selfiePath)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineRoleAsync(actorUserId, UserRole.Coach);
            await EnsureOnlineSnapshotAsync();
            var onlineSession = Online.Session(sessionId)
                          ?? throw new InvalidOperationException("Không tìm thấy buổi học.");
            EnsureOnlineClassAccess(onlineActor, onlineSession.ClassId);
            var onlineTrainingClass = Online.Class(onlineSession.ClassId)
                                ?? throw new InvalidOperationException("Không tìm thấy lớp.");
            var trainingClass = onlineTrainingClass;
            var session = onlineSession;
            if (CoachCheckInTime.IsCheckInWindowTooEarly(onlineTrainingClass, onlineSession.SessionDate))
                throw new InvalidOperationException(
                    $"Check-in chỉ mở từ {CoachCheckInTime.CheckInOpensLocal(trainingClass, session.SessionDate):HH:mm}.");
            if (CoachCheckInTime.IsCheckInWindowLocked(onlineTrainingClass, onlineSession.SessionDate))
                throw new InvalidOperationException("Đã quá 2 giờ sau khi lớp kết thúc, Coach không thể check-in.");
            if (string.IsNullOrWhiteSpace(selfiePath) || !File.Exists(selfiePath))
                throw new InvalidOperationException("Không tìm thấy hình selfie.");
            try
            {
                var upload = await _cloudApi.UploadFileAsync(selfiePath, "checkin_selfie");
                var remote = await _cloudApi.PostAsync<object, CloudCheckInResponse>(
                    "check-ins",
                    new
                    {
                        sessionId,
                         classId = onlineSession.ClassId,
                         sessionDate = onlineSession.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        uploadId = upload.Id
                    },
                    EntityId.New());
                var onlineAssignment = Online.ClassCoaches.FirstOrDefault(item =>
                    item.ClassId == onlineSession.ClassId && item.CoachUserId == onlineActor.Id && item.IsActive);
                var onlineCheckIn = Online.CheckIn(sessionId, onlineActor.Id) ?? new CoachCheckIn
                {
                    Id = remote.Id,
                    SessionId = sessionId,
                    CoachUserId = onlineActor.Id,
                    SalaryPerSessionVndSnapshot = onlineAssignment?.SalaryPerSessionVnd ?? 0
                };
                onlineCheckIn.Id = string.IsNullOrWhiteSpace(remote.Id) ? onlineCheckIn.Id : remote.Id;
                onlineCheckIn.SelfiePath = selfiePath;
                onlineCheckIn.CheckOutSelfiePath = string.Empty;
                onlineCheckIn.CheckedInAtUtc = remote.CheckedInAt.UtcDateTime;
                onlineCheckIn.CheckedOutAtUtc = null;
                onlineCheckIn.DurationSeconds = 0;
                onlineCheckIn.ApprovalStatus = CoachCheckInApprovalStatus.Pending;
                onlineCheckIn.ReviewedByUserId = string.Empty;
                onlineCheckIn.ReviewedAtUtc = null;
                onlineCheckIn.ReviewNote = string.Empty;
                Online.Upsert(Online.CoachCheckIns, onlineCheckIn,
                    item => item.SessionId == sessionId && item.CoachUserId == onlineActor.Id);
                return onlineCheckIn;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireRoleAsync(actorUserId, UserRole.Coach);
        var offlineSession = await Database.FindAsync<TrainingSession>(sessionId)
                      ?? throw new InvalidOperationException("Không tìm thấy buổi học.");
        await EnsureClassAccessAsync(actor, offlineSession.ClassId, writeAttendance: true);
        var offlineTrainingClass = await Database.FindAsync<TrainingClass>(offlineSession.ClassId)
                            ?? throw new InvalidOperationException("Không tìm thấy lớp.");
        if (CoachCheckInTime.IsCheckInWindowTooEarly(offlineTrainingClass, offlineSession.SessionDate))
        {
            throw new InvalidOperationException(
                $"Check-in chỉ mở từ {CoachCheckInTime.CheckInOpensLocal(offlineTrainingClass, offlineSession.SessionDate):HH:mm} "
                + $"({CoachCheckInTime.CheckInOpenLeadMinutes} phút trước giờ học).");
        }
        if (CoachCheckInTime.IsCheckInWindowLocked(offlineTrainingClass, offlineSession.SessionDate))
        {
            throw new InvalidOperationException(
                "Đã quá 2 giờ sau khi lớp kết thúc. Coach được ghi nhận vắng check-in và không thể check-in.");
        }
        if (string.IsNullOrWhiteSpace(selfiePath) || !File.Exists(selfiePath))
        {
            throw new InvalidOperationException("Không tìm thấy hình selfie.");
        }

        var assignment = await Database.Table<ClassCoachAssignment>()
            .Where(item => item.ClassId == offlineSession.ClassId
                           && item.CoachUserId == actorUserId
                           && item.IsActive)
            .FirstOrDefaultAsync()
                         ?? throw new InvalidOperationException(
                             "Coach không còn được phân công vào lớp này.");
        await EnsureSessionCoachAssignmentsAsync(offlineSession);
        var checkIn = await Database.Table<CoachCheckIn>()
            .Where(item => item.SessionId == sessionId && item.CoachUserId == actorUserId)
            .FirstOrDefaultAsync();
        if (checkIn is null)
        {
            checkIn = new CoachCheckIn
            {
                Id = EntityId.New(),
                SessionId = sessionId,
                CoachUserId = actorUserId,
                SelfiePath = selfiePath,
                SalaryPerSessionVndSnapshot = Math.Max(
                    0,
                    assignment.SalaryPerSessionVnd),
                CheckedInAtUtc = DateTime.UtcNow,
                DurationSeconds = 0,
                ApprovalStatus = CoachCheckInApprovalStatus.Pending
            };
            await Database.InsertAsync(checkIn);
        }
        else
        {
            if (CoachCheckInTime.IsAutoAbsent(checkIn))
            {
                throw new InvalidOperationException(
                    "Coach đã được ghi nhận vắng check-in và ca đã bị khóa.");
            }
            checkIn.SelfiePath = selfiePath;
            checkIn.CheckOutSelfiePath = string.Empty;
            checkIn.CheckedInAtUtc = DateTime.UtcNow;
            checkIn.CheckedOutAtUtc = null;
            checkIn.DurationSeconds = 0;
            checkIn.ApprovalStatus = CoachCheckInApprovalStatus.Pending;
            checkIn.ReviewedByUserId = string.Empty;
            checkIn.ReviewedAtUtc = null;
            checkIn.ReviewNote = string.Empty;
            await Database.UpdateAsync(checkIn);
        }

        if (_cloudOptions.IsConfigured)
        {
            var upload = await _cloudApi.UploadFileAsync(selfiePath, "checkin_selfie");
            var remote = await _cloudApi.PostAsync<object, CloudCheckInResponse>(
                "check-ins",
                new
                {
                    sessionId,
                     classId = offlineSession.ClassId,
                     sessionDate = offlineSession.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    uploadId = upload.Id
                },
                EntityId.New());
            if (!string.IsNullOrWhiteSpace(remote.Id)
                && !string.Equals(remote.Id, checkIn.Id, StringComparison.Ordinal))
            {
                await Database.DeleteAsync(checkIn);
                checkIn.Id = remote.Id;
                await Database.InsertAsync(checkIn);
            }
        }

        var coachProfile = await GetProfileAsync(actorUserId);
        var founders = await Database.Table<UserAccount>()
            .Where(item => item.IsActive
                           && (item.Role == UserRole.Founder
                               || item.Role == UserRole.CoFounder
                               || item.Role == UserRole.Manager))
            .ToListAsync();
        foreach (var founder in founders)
        {
            #if false
            await AddNotificationAsync(
                founder.Id,
                NotificationKind.CoachCheckIn,
                "Check-in đang chờ duyệt",
                $"{coachProfile.FullName} đã gửi selfie lớp {trainingClass?.Name ?? "lớp học"} lúc {checkIn.CheckedInAtUtc.ToLocalTime():HH:mm}. Vui lòng kiểm tra và xác nhận.",
                checkIn.Id,
                writeCloud: !_cloudOptions.IsConfigured);
            #endif
            await AddNotificationAsync(
                founder.Id,
                NotificationKind.CoachCheckIn,
                "Coach check-in",
                $"{coachProfile.FullName} sent a check-in selfie for {offlineTrainingClass?.Name ?? "class"} at {checkIn.CheckedInAtUtc.ToLocalTime():HH:mm}.",
                checkIn.Id,
                writeCloud: false);
        }

        await AddAuditAsync(actorUserId, "CoachCheckIn", nameof(CoachCheckIn), checkIn.Id, sessionId,
            writeCloud: !_cloudOptions.IsConfigured);
        QueueCloudProjectionRefresh();
        return checkIn;
    }

    public async Task<CoachCheckIn> SaveCoachCheckOutAsync(
        string actorUserId,
        string sessionId,
        string selfiePath)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineRoleAsync(actorUserId, UserRole.Coach);
            await EnsureOnlineSnapshotAsync();
            var onlineSession = Online.Session(sessionId)
                          ?? throw new InvalidOperationException("Không tìm thấy buổi học.");
            EnsureOnlineClassAccess(onlineActor, onlineSession.ClassId);
            if (string.IsNullOrWhiteSpace(selfiePath) || !File.Exists(selfiePath))
                throw new InvalidOperationException("Không tìm thấy hình selfie check-out.");
            var current = Online.CheckIn(sessionId, onlineActor.Id)
                          ?? throw new InvalidOperationException("Vui lòng chụp selfie check-in trước khi check-out.");
            try
            {
                var upload = await _cloudApi.UploadFileAsync(selfiePath, "checkout_selfie");
                await _cloudApi.PostAsync(
                    "check-outs",
                    new { sessionId, uploadId = upload.Id },
                    EntityId.New());
                current.CheckOutSelfiePath = selfiePath;
                current.CheckedOutAtUtc = DateTime.UtcNow;
                current.DurationSeconds = Math.Max(
                    0,
                    (long)Math.Floor((current.CheckedOutAtUtc.Value - current.CheckedInAtUtc).TotalSeconds));
                current.ApprovalStatus = CoachCheckInApprovalStatus.Pending;
                onlineSession.Status = SessionStatus.Submitted;
                onlineSession.SubmittedByUserId = onlineActor.Id;
                onlineSession.SubmittedAtUtc = DateTime.UtcNow;
                return current;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireRoleAsync(actorUserId, UserRole.Coach);
        var session = await Database.FindAsync<TrainingSession>(sessionId)
                      ?? throw new InvalidOperationException("Không tìm thấy buổi học.");
        await EnsureClassAccessAsync(actor, session.ClassId, writeAttendance: true);
        if (string.IsNullOrWhiteSpace(selfiePath) || !File.Exists(selfiePath))
        {
            throw new InvalidOperationException("Không tìm thấy hình selfie check-out.");
        }

        var checkIn = await Database.Table<CoachCheckIn>()
            .Where(item => item.SessionId == sessionId && item.CoachUserId == actorUserId)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                "Vui lòng chụp selfie check-in trước khi check-out.");
        var safetyClosed = CoachCheckInTime.IsSafetyClosed(checkIn);
        if (checkIn.CheckedOutAtUtc is not null && !safetyClosed)
        {
            throw new InvalidOperationException("Buổi học này đã check-out.");
        }
        if (checkIn.ApprovalStatus == CoachCheckInApprovalStatus.Rejected)
        {
            throw new InvalidOperationException(
                "Check-in bị từ chối, vui lòng chụp lại selfie trước khi check-out.");
        }

        checkIn.CheckOutSelfiePath = selfiePath;
        checkIn.CheckedOutAtUtc = DateTime.UtcNow;
        checkIn.DurationSeconds = safetyClosed
            ? Math.Max(1, checkIn.DurationSeconds)
            : Math.Min(
                CoachCheckInTime.MaxOpenDurationSeconds,
                Math.Max(
                    0,
                    (long)Math.Floor((checkIn.CheckedOutAtUtc.Value - checkIn.CheckedInAtUtc).TotalSeconds)));
        await Database.UpdateAsync(checkIn);

        if (_cloudOptions.IsConfigured)
        {
            var upload = await _cloudApi.UploadFileAsync(selfiePath, "checkout_selfie");
            await _cloudApi.PostAsync(
                "check-outs",
                new { sessionId, uploadId = upload.Id },
                EntityId.New());
        }

        var coachProfile = await GetProfileAsync(actorUserId);
        var trainingClass = await Database.FindAsync<TrainingClass>(session.ClassId);
        var founders = await Database.Table<UserAccount>()
            .Where(item => item.IsActive
                           && (item.Role == UserRole.Founder
                               || item.Role == UserRole.CoFounder
                               || item.Role == UserRole.Manager))
            .ToListAsync();
        foreach (var founder in founders)
        {
            await AddNotificationAsync(
                founder.Id,
                NotificationKind.CoachCheckIn,
                "Check-out lớp học",
                $"{coachProfile.FullName} đã gửi selfie check-out lớp {trainingClass?.Name ?? "lớp học"} lúc {checkIn.CheckedOutAtUtc.Value.ToLocalTime():HH:mm}.",
                checkIn.Id,
                writeCloud: !_cloudOptions.IsConfigured);
        }

        await AddAuditAsync(actorUserId, "CoachCheckOut", nameof(CoachCheckIn), checkIn.Id, sessionId,
            writeCloud: !_cloudOptions.IsConfigured);
        QueueCloudProjectionRefresh();
        return checkIn;
    }

    public async Task<CoachCheckIn?> GetCoachCheckInAsync(string sessionId, string coachUserId)
    {
        if (IsOnline)
        {
            await EnsureOnlineSnapshotAsync();
            return Online.CheckIn(sessionId, coachUserId);
        }

        await InitializeAsync();
        var checkIn = await Database.Table<CoachCheckIn>()
            .Where(item => item.SessionId == sessionId && item.CoachUserId == coachUserId)
            .FirstOrDefaultAsync();
        return await AutoCloseStaleCoachCheckInAsync(checkIn);
    }

    public async Task<IReadOnlyList<CoachCheckInRow>> GetCoachCheckInsForSessionAsync(
        string actorUserId,
        string sessionId)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            var onlineSession = Online.Session(sessionId)
                ?? throw new InvalidOperationException("Không tìm thấy buổi học.");
            EnsureOnlineClassAccess(onlineActor, onlineSession.ClassId);
            var sessionCoachIds = Online.CoachCheckIns
                .Where(item => item.SessionId == sessionId)
                .Select(item => item.CoachUserId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            foreach (var coachId in sessionCoachIds)
            {
                if (Online.Profile(coachId) is { } coachProfile)
                {
                    await MaterializeProfileImageAsync(onlineActor.Id, coachProfile);
                }
            }
            return Online.CoachCheckIns
                .Where(item => item.SessionId == sessionId)
                .Select(item => new CoachCheckInRow(
                    item,
                    Online.Profile(item.CoachUserId)?.FullName ?? "Huấn luyện viên",
                    Online.Profile(item.CoachUserId)?.CoachPosition ?? string.Empty))
                .OrderBy(item => item.CoachName)
                .ToList();
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        var session = await Database.FindAsync<TrainingSession>(sessionId)
                      ?? throw new InvalidOperationException("Không tìm thấy buổi học.");
        await EnsureClassAccessAsync(actor, session.ClassId, writeAttendance: true);
        var checkIns = await Database.Table<CoachCheckIn>()
            .Where(item => item.SessionId == sessionId)
            .ToListAsync();
        await AutoCloseStaleCoachCheckInsAsync(checkIns);
        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);
        return checkIns
            .Select(item => new CoachCheckInRow(
                item,
                profiles.GetValueOrDefault(item.CoachUserId)?.FullName ?? "Huấn luyện viên",
                profiles.GetValueOrDefault(item.CoachUserId)?.CoachPosition ?? string.Empty))
            .OrderBy(item => item.CoachName)
            .ToList();
    }

    public async Task<IReadOnlyList<CoachCheckInReviewRow>> GetPendingCoachCheckInsAsync(
        string actorUserId,
        string? coachUserId = null)
    {
        if (IsOnline)
        {
            var onlineApprovalActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanApproveOperations(onlineApprovalActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền xem điểm danh.");
            await EnsureOnlineSnapshotAsync();
            var onlineSessions = Online.TrainingSessions.ToDictionary(item => item.Id);
            var onlineProfiles = Online.Profiles.ToDictionary(item => item.UserId);
            var onlineClasses = Online.Classes.ToDictionary(item => item.Id);
            var pendingRows = Online.CoachCheckIns
                .Where(item => item.ApprovalStatus == CoachCheckInApprovalStatus.Pending
                               && CoachCheckInTime.HasCoachCheckout(item)
                               && (string.IsNullOrWhiteSpace(coachUserId) || item.CoachUserId == coachUserId))
                .Where(item => onlineSessions.ContainsKey(item.SessionId))
                .Select(item =>
                {
                    var session = onlineSessions[item.SessionId];
                    return new CoachCheckInReviewRow(
                        item,
                        onlineProfiles.GetValueOrDefault(item.CoachUserId)?.FullName ?? "Huấn luyện viên",
                        onlineClasses.GetValueOrDefault(session.ClassId)?.Name ?? "Lớp học",
                        session.SessionDate,
                        onlineProfiles.GetValueOrDefault(item.CoachUserId)?.CoachPosition ?? string.Empty);
                })
                .OrderByDescending(item => item.CheckIn.CheckedInAtUtc)
                .ToList();
            foreach (var row in pendingRows)
            {
                if (onlineProfiles.GetValueOrDefault(row.CheckIn.CoachUserId) is { } profile)
                {
                    await MaterializeProfileImageAsync(actorUserId, profile);
                }
            }
            return pendingRows;
        }

        await InitializeAsync();
        var attendanceActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanApproveOperations(attendanceActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem điểm danh.");
        var checkIns = await Database.Table<CoachCheckIn>().ToListAsync();
        await AutoCloseStaleCoachCheckInsAsync(checkIns);
        checkIns = checkIns
            .Where(item => item.ApprovalStatus == CoachCheckInApprovalStatus.Pending
                           && CoachCheckInTime.HasCoachCheckout(item)
                           && (string.IsNullOrWhiteSpace(coachUserId)
                               || item.CoachUserId == coachUserId))
            .ToList();
        if (checkIns.Count == 0)
        {
            return [];
        }

        var sessions = (await Database.Table<TrainingSession>().ToListAsync())
            .ToDictionary(item => item.Id);
        var classes = (await Database.Table<TrainingClass>().ToListAsync())
            .ToDictionary(item => item.Id);
        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);

        return checkIns
            .Where(item => sessions.ContainsKey(item.SessionId))
            .Select(item =>
            {
                var session = sessions[item.SessionId];
                return new CoachCheckInReviewRow(
                    item,
                    profiles.GetValueOrDefault(item.CoachUserId)?.FullName ?? "Huấn luyện viên",
                    classes.GetValueOrDefault(session.ClassId)?.Name ?? "Lớp học",
                    session.SessionDate,
                    profiles.GetValueOrDefault(item.CoachUserId)?.CoachPosition ?? string.Empty);
            })
            .OrderByDescending(item => item.CheckIn.CheckedInAtUtc)
            .ToList();
    }

    /// <summary>
    /// Returns every Coach check-in that is available to the Founder, including
    /// check-ins that have already been approved or rejected.  The approval
    /// queue intentionally only contains pending entries, so this separate
    /// history query preserves the daily audit trail.
    /// </summary>
    public async Task<IReadOnlyList<CoachCheckInHistoryRow>> GetCoachCheckInHistoryAsync(
        string actorUserId,
        CoachCheckInApprovalStatus? approvalStatus = null)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (onlineActor.Role is not (UserRole.Founder or UserRole.CoFounder or UserRole.Manager or UserRole.Coach))
                throw new UnauthorizedAccessException("Tài khoản không có quyền xem lịch sử dạy học.");
            await EnsureOnlineSnapshotAsync();
            var onlineHistorySessions = Online.TrainingSessions.ToDictionary(item => item.Id);
            var onlineHistoryProfiles = Online.Profiles.ToDictionary(item => item.UserId);
            var onlineHistoryClasses = Online.Classes.ToDictionary(item => item.Id);
            foreach (var profile in onlineHistoryProfiles.Values)
            {
                await MaterializeProfileImageAsync(onlineActor.Id, profile);
            }
            var historyRows = Online.CoachCheckIns
                .Where(item => onlineActor.Role != UserRole.Coach || item.CoachUserId == onlineActor.Id)
                .Where(item => approvalStatus is null || item.ApprovalStatus == approvalStatus.Value)
                .Where(item => onlineHistorySessions.ContainsKey(item.SessionId))
                .Select(item =>
                {
                    var session = onlineHistorySessions[item.SessionId];
                    onlineHistoryProfiles.TryGetValue(item.CoachUserId, out var profile);
                    return new CoachCheckInHistoryRow(
                        item,
                        profile?.FullName ?? "Huấn luyện viên",
                        profile?.PhotoPath ?? string.Empty,
                        onlineHistoryClasses.GetValueOrDefault(session.ClassId)?.Name ?? "Lớp học",
                        session.SessionDate,
                        profile?.CoachPosition ?? string.Empty);
                })
                .OrderByDescending(item => item.SessionDate)
                .ThenByDescending(item => item.CheckIn.CheckedInAtUtc)
                .ToList();
            return historyRows;
        }

        await InitializeAsync();
        await EnsureMissedCoachCheckInsAsync(DateTime.Today);
        var actor = await RequireUserAsync(actorUserId);
        if (actor.Role is not (UserRole.Founder or UserRole.CoFounder or UserRole.Manager or UserRole.Coach))
        {
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem lịch sử dạy học.");
        }

        var checkIns = await Database.Table<CoachCheckIn>().ToListAsync();
        await AutoCloseStaleCoachCheckInsAsync(checkIns);
        if (actor.Role == UserRole.Coach)
        {
            checkIns = checkIns
                .Where(item => item.CoachUserId == actorUserId)
                .ToList();
        }
        if (approvalStatus is not null)
        {
            checkIns = checkIns
                .Where(item => item.ApprovalStatus == approvalStatus.Value)
                .ToList();
        }

        if (checkIns.Count == 0)
        {
            return [];
        }

        var sessions = (await Database.Table<TrainingSession>().ToListAsync())
            .ToDictionary(item => item.Id);
        var classes = (await Database.Table<TrainingClass>().ToListAsync())
            .ToDictionary(item => item.Id);
        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);

        return checkIns
            .Where(item => sessions.ContainsKey(item.SessionId))
            .Select(item =>
            {
                var session = sessions[item.SessionId];
                var profile = profiles.GetValueOrDefault(item.CoachUserId);
                return new CoachCheckInHistoryRow(
                    item,
                    profile?.FullName ?? "Huấn luyện viên",
                    profile?.PhotoPath ?? string.Empty,
                    classes.GetValueOrDefault(session.ClassId)?.Name ?? "Lớp học",
                    session.SessionDate,
                    profile?.CoachPosition ?? string.Empty);
            })
            .OrderByDescending(item => item.SessionDate)
            .ThenByDescending(item => item.CheckIn.CheckedInAtUtc)
            .ToList();
    }

    public async Task ReviewCoachCheckInAsync(
        string actorUserId,
        string checkInId,
        bool approve,
        string reviewNote = "")
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanApproveOperations(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền duyệt check-in.");
            await EnsureOnlineSnapshotAsync();
            var onlineCheckIn = Online.CoachCheckIns.FirstOrDefault(item => item.Id == checkInId)
                ?? throw new InvalidOperationException("Không tìm thấy check-in.");
            if (onlineCheckIn.ApprovalStatus != CoachCheckInApprovalStatus.Pending)
                throw new InvalidOperationException("Check-in này đã được xử lý trước đó.");
            if (!CoachCheckInTime.HasCoachCheckout(onlineCheckIn))
                throw new InvalidOperationException("Coach phải chụp selfie check-out thì Founder mới có thể xác nhận và tính lương.");
            try
            {
                await _cloudApi.PatchAsync(
                    $"check-ins/{Uri.EscapeDataString(checkInId)}/review",
                    new { status = approve ? "approved" : "rejected", note = reviewNote.Trim() },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
            onlineCheckIn.ApprovalStatus = approve ? CoachCheckInApprovalStatus.Approved : CoachCheckInApprovalStatus.Rejected;
            onlineCheckIn.ReviewedByUserId = actorUserId;
            onlineCheckIn.ReviewedAtUtc = DateTime.UtcNow;
            onlineCheckIn.ReviewNote = reviewNote.Trim();
            if (!approve && Online.Session(onlineCheckIn.SessionId) is { } onlineSession)
            {
                onlineSession.Status = SessionStatus.Draft;
                onlineSession.SubmittedByUserId = string.Empty;
                onlineSession.SubmittedAtUtc = null;
            }
            return;
        }

        await InitializeAsync();
        var reviewActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanApproveOperations(reviewActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền duyệt check-in.");
        var checkIn = await Database.FindAsync<CoachCheckIn>(checkInId)
                      ?? throw new InvalidOperationException("Không tìm thấy check-in.");
        if (checkIn.ApprovalStatus != CoachCheckInApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Check-in này đã được xử lý trước đó.");
        }
        if (!CoachCheckInTime.HasCoachCheckout(checkIn))
        {
            throw new InvalidOperationException(
                "Coach phải chụp selfie check-out thì Founder mới có thể xác nhận và tính lương.");
        }

        var session = await Database.FindAsync<TrainingSession>(checkIn.SessionId)
                      ?? throw new InvalidOperationException("Không tìm thấy buổi học của check-in.");
        var period = session.SessionDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var existingSalary = await Database.Table<CoachSalary>()
            .Where(item => item.CoachUserId == checkIn.CoachUserId && item.Period == period)
            .FirstOrDefaultAsync();
        if (existingSalary?.Status == SalaryStatus.Paid)
        {
            throw new InvalidOperationException(
                "Kỳ lương này đã được đánh dấu thanh toán nên không thể thay đổi check-in.");
        }

        if (_cloudOptions.IsConfigured)
        {
            try
            {
                await _cloudApi.PatchAsync(
                    $"check-ins/{Uri.EscapeDataString(checkIn.Id)}/review",
                    new { status = approve ? "approved" : "rejected", note = reviewNote.Trim() },
                    idempotencyKey: EntityId.New());
                // The Worker is authoritative for the review.  Mirror the
                // accepted/rejected state locally so the next screen render
                // does not need to download the full tenant snapshot.
                checkIn.ApprovalStatus = approve
                    ? CoachCheckInApprovalStatus.Approved
                    : CoachCheckInApprovalStatus.Rejected;
                checkIn.ReviewedByUserId = actorUserId;
                checkIn.ReviewedAtUtc = DateTime.UtcNow;
                checkIn.ReviewNote = reviewNote.Trim();
                await Database.UpdateAsync(checkIn);
                if (!approve)
                {
                    session.Status = SessionStatus.Draft;
                    session.SubmittedByUserId = string.Empty;
                    session.SubmittedAtUtc = null;
                    session.UpdatedAtUtc = DateTime.UtcNow;
                    await Database.UpdateAsync(session);
                }
                if (approve)
                {
                    await EnsureCoachSalaryForPeriodAsync(checkIn.CoachUserId, session.SessionDate);
                }
                else if (existingSalary is not null)
                {
                    await RecomputePendingSalaryAsync(existingSalary);
                }
                QueueCloudProjectionRefresh();
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        checkIn.ApprovalStatus = approve
            ? CoachCheckInApprovalStatus.Approved
            : CoachCheckInApprovalStatus.Rejected;
        checkIn.ReviewedByUserId = actorUserId;
        checkIn.ReviewedAtUtc = DateTime.UtcNow;
        checkIn.ReviewNote = reviewNote.Trim();
        await Database.UpdateAsync(checkIn);

        if (!approve)
        {
            // A rejected check-in can be submitted again for the same class
            // session.  Re-open a session that had been submitted by
            // check-out so the next selfie also gets a fresh roster.
            session.Status = SessionStatus.Draft;
            session.SubmittedByUserId = string.Empty;
            session.SubmittedAtUtc = null;
            session.UpdatedAtUtc = DateTime.UtcNow;
            await Database.UpdateAsync(session);
        }

        if (approve)
        {
            await EnsureCoachSalaryForPeriodAsync(checkIn.CoachUserId, session.SessionDate);
        }
        else if (existingSalary is not null)
        {
            await RecomputePendingSalaryAsync(existingSalary);
        }

        await AddNotificationAsync(
            checkIn.CoachUserId,
            NotificationKind.CoachCheckInReviewed,
            approve ? "Check-in đã được xác nhận" : "Check-in bị từ chối",
            approve
                ? $"Check-in ngày {session.SessionDate:dd/MM/yyyy} đã được xác nhận và được tính lương."
                : $"Check-in ngày {session.SessionDate:dd/MM/yyyy} chưa được chấp nhận. {checkIn.ReviewNote}".Trim(),
            checkIn.Id);
        await AddAuditAsync(
            actorUserId,
            approve ? "ApproveCoachCheckIn" : "RejectCoachCheckIn",
            nameof(CoachCheckIn),
            checkIn.Id,
            checkIn.ReviewNote);
    }

    public async Task<IReadOnlyList<AttendanceHistoryRow>> GetAttendanceHistoryAsync(
        string actorUserId,
        string traineeUserId)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (onlineActor.Role == UserRole.Trainee && onlineActor.Id != traineeUserId)
                throw new UnauthorizedAccessException("Bạn chỉ được xem điểm danh của mình.");
            if (onlineActor.Role == UserRole.Coach)
            {
                await EnsureOnlineSnapshotAsync();
                if (!GetVisibleMemberIdsOnline(onlineActor).Contains(traineeUserId))
                    throw new UnauthorizedAccessException("Học viên không thuộc lớp được phân công.");
            }
            await EnsureOnlineSnapshotAsync();
            var onlineSessions = Online.TrainingSessions
                .Where(item => item.Status == SessionStatus.Submitted)
                .ToDictionary(item => item.Id);
            return Online.AttendanceRecords
                .Where(item => item.TraineeUserId == traineeUserId && onlineSessions.ContainsKey(item.SessionId))
                .Select(item =>
                {
                    var session = onlineSessions[item.SessionId];
                    return new AttendanceHistoryRow(
                        session.SessionDate,
                        Online.Class(session.ClassId)?.Name ?? "Lớp học",
                        item.Status,
                        item.RecordedAtUtc);
                })
                .OrderByDescending(item => item.SessionDate)
                .ToList();
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        if (actor.Role == UserRole.Trainee && actor.Id != traineeUserId)
        {
            throw new UnauthorizedAccessException("Bạn chỉ được xem điểm danh của mình.");
        }

        if (actor.Role == UserRole.Coach)
        {
            var visible = await GetVisibleMemberIdsAsync(actor);
            if (!visible.Contains(traineeUserId))
            {
                throw new UnauthorizedAccessException("Học viên không thuộc lớp được phân công.");
            }
        }

        var records = await Database.Table<AttendanceRecord>()
            .Where(item => item.TraineeUserId == traineeUserId)
            .ToListAsync();
        var sessions = (await Database.Table<TrainingSession>().ToListAsync())
            .Where(item => item.Status == SessionStatus.Submitted)
            .ToDictionary(item => item.Id);
        var classes = (await Database.Table<TrainingClass>().ToListAsync())
            .ToDictionary(item => item.Id);

        return records
            .Where(item => sessions.ContainsKey(item.SessionId))
            .Select(item =>
            {
                var session = sessions[item.SessionId];
                var className = classes.GetValueOrDefault(session.ClassId)?.Name ?? "Lớp học";
                return new AttendanceHistoryRow(
                    session.SessionDate,
                    className,
                    item.Status,
                    item.RecordedAtUtc);
            })
            .OrderByDescending(item => item.SessionDate)
            .ToList();
    }

    /// <summary>
    /// Returns submitted trainee attendance for a single category.  It is
    /// Founder-only so the attendance tab can show who attended or was absent
    /// on each historical date without exposing draft records.
    /// </summary>
    public async Task<IReadOnlyList<FounderTraineeAttendanceHistoryRow>>
        GetFounderTraineeAttendanceHistoryAsync(
            string actorUserId,
            AttendanceStatus status)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền xem lịch sử điểm danh.");
            if (status == AttendanceStatus.Unmarked)
                throw new InvalidOperationException("Không có lịch sử cho trạng thái chưa ghi nhận.");
            await EnsureOnlineSnapshotAsync();
            var sessions = Online.TrainingSessions
                .Where(item => item.Status == SessionStatus.Submitted)
                .ToDictionary(item => item.Id);
            // Attendance history rows carry the trainee avatar path.  In the
            // online projection that value is an R2 object key, so materialize
            // every referenced profile before constructing the rows; otherwise
            // the Avatar control receives a non-file key after a cold start.
            var historyTraineeIds = Online.AttendanceRecords
                .Where(item => item.Status == status && sessions.ContainsKey(item.SessionId))
                .Select(item => item.TraineeUserId)
                .Distinct(StringComparer.Ordinal);
            foreach (var traineeId in historyTraineeIds)
            {
                if (Online.Profile(traineeId) is { } profile)
                {
                    await MaterializeProfileImageAsync(actorUserId, profile);
                }
            }
            return Online.AttendanceRecords
                .Where(item => item.Status == status && sessions.ContainsKey(item.SessionId))
                .Select(item =>
                {
                    var session = sessions[item.SessionId];
                    var profile = Online.Profile(item.TraineeUserId);
                    return new FounderTraineeAttendanceHistoryRow(
                        session.SessionDate,
                        Online.Class(session.ClassId)?.Name ?? "Lớp học",
                        profile?.FullName ?? "Cầu thủ học viên",
                        profile?.PhotoPath ?? string.Empty,
                        item.Status,
                        item.RecordedAtUtc);
                })
                .OrderByDescending(item => item.SessionDate)
                .ThenBy(item => item.TraineeName)
                .ToList();
        }

        await InitializeAsync();
        var attendanceActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(attendanceActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem lịch sử điểm danh.");
        if (status == AttendanceStatus.Unmarked)
        {
            throw new InvalidOperationException(
                "Không có lịch sử cho trạng thái chưa ghi nhận.");
        }

        var records = (await Database.Table<AttendanceRecord>().ToListAsync())
            .Where(item => item.Status == status)
            .ToList();
        if (records.Count == 0)
        {
            return [];
        }

        var submittedSessions = (await Database.Table<TrainingSession>().ToListAsync())
            .Where(item => item.Status == SessionStatus.Submitted)
            .ToDictionary(item => item.Id);
        var classes = (await Database.Table<TrainingClass>().ToListAsync())
            .ToDictionary(item => item.Id);
        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);

        return records
            .Where(item => submittedSessions.ContainsKey(item.SessionId))
            .Select(item =>
            {
                var session = submittedSessions[item.SessionId];
                var profile = profiles.GetValueOrDefault(item.TraineeUserId);
                return new FounderTraineeAttendanceHistoryRow(
                    session.SessionDate,
                    classes.GetValueOrDefault(session.ClassId)?.Name ?? "Lớp học",
                    profile?.FullName ?? "Cầu thủ học viên",
                    profile?.PhotoPath ?? string.Empty,
                    item.Status,
                    item.RecordedAtUtc);
            })
            .OrderByDescending(item => item.SessionDate)
            .ThenBy(item => item.TraineeName)
            .ToList();
    }

    public async Task<MemberAttendanceSummary> GetMemberAttendanceSummaryAsync(
        string actorUserId,
        string memberUserId)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanApproveOperations(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền xem thống kê điểm danh.");
            await EnsureOnlineSnapshotAsync();
            var onlineMember = Online.User(memberUserId)
                ?? throw new InvalidOperationException("Không tìm thấy thành viên.");
            if (onlineMember.Role is not (UserRole.Coach or UserRole.Trainee))
                throw new InvalidOperationException("Chỉ có thể xem thống kê điểm danh của Coach hoặc Cầu Thủ Học Viên.");
            var submitted = Online.TrainingSessions
                .Where(item => item.Status == SessionStatus.Submitted)
                .ToList();
            var submittedIds = submitted.Select(item => item.Id).ToHashSet();
            if (onlineMember.Role == UserRole.Trainee)
            {
                var records = Online.AttendanceRecords
                    .Where(item => item.TraineeUserId == memberUserId && submittedIds.Contains(item.SessionId))
                    .ToList();
                var late = records.Count(item => item.Status == AttendanceStatus.Late);
                return new MemberAttendanceSummary(
                    onlineMember.Role,
                    records.Count(item => item.Status == AttendanceStatus.Present || item.Status == AttendanceStatus.Late),
                    records.Count(item => item.Status == AttendanceStatus.Absent),
                    late,
                    records.Count(item => item.Status == AttendanceStatus.Excused),
                    records.Count(item => item.Status != AttendanceStatus.Unmarked));
            }
            var expectedIds = Online.SessionCoaches
                .Where(item => item.CoachUserId == memberUserId && submittedIds.Contains(item.SessionId))
                .Select(item => item.SessionId)
                .ToHashSet();
            var allCheckIns = Online.CoachCheckIns.Where(item => item.CoachUserId == memberUserId).ToList();
            var onlineCoachCheckIns = allCheckIns.Where(item => expectedIds.Contains(item.SessionId)).ToList();
            var approved = onlineCoachCheckIns
                .Where(item => item.ApprovalStatus == CoachCheckInApprovalStatus.Approved && CoachCheckInTime.HasCoachCheckout(item))
                .Select(item => item.SessionId)
                .ToHashSet();
            var pendingAll = allCheckIns.Count(item => item.ApprovalStatus == CoachCheckInApprovalStatus.Pending && CoachCheckInTime.HasCoachCheckout(item));
            var pendingSubmitted = onlineCoachCheckIns.Count(item => item.ApprovalStatus == CoachCheckInApprovalStatus.Pending && CoachCheckInTime.HasCoachCheckout(item));
            return new MemberAttendanceSummary(
                onlineMember.Role,
                approved.Count,
                Math.Max(0, expectedIds.Count - approved.Count - pendingSubmitted),
                0,
                0,
                expectedIds.Count,
                pendingAll);
        }

        await InitializeAsync();
        var attendanceActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanApproveOperations(attendanceActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem thống kê điểm danh.");
        var member = await Database.FindAsync<UserAccount>(memberUserId)
                     ?? throw new InvalidOperationException("Không tìm thấy thành viên.");
        if (member.Role is not (UserRole.Coach or UserRole.Trainee))
        {
            throw new InvalidOperationException(
                "Chỉ có thể xem thống kê điểm danh của Coach hoặc Cầu Thủ Học Viên.");
        }

        var submittedSessions = (await Database.Table<TrainingSession>().ToListAsync())
            .Where(item => item.Status == SessionStatus.Submitted)
            .ToList();

        if (member.Role == UserRole.Trainee)
        {
            var submittedSessionIds = submittedSessions
                .Select(item => item.Id)
                .ToHashSet();
            var records = (await Database.Table<AttendanceRecord>()
                    .Where(item => item.TraineeUserId == memberUserId)
                    .ToListAsync())
                .Where(item => submittedSessionIds.Contains(item.SessionId))
                .ToList();
            var presentCount = records.Count(
                item => item.Status == AttendanceStatus.Present);
            var lateCount = records.Count(
                item => item.Status == AttendanceStatus.Late);
            var absentCount = records.Count(
                item => item.Status == AttendanceStatus.Absent);
            var excusedCount = records.Count(
                item => item.Status == AttendanceStatus.Excused);

            return new MemberAttendanceSummary(
                member.Role,
                presentCount + lateCount,
                absentCount,
                lateCount,
                excusedCount,
                records.Count(item => item.Status != AttendanceStatus.Unmarked));
        }

        var submittedCoachSessionIds = submittedSessions
            .Select(item => item.Id)
            .ToHashSet();
        var expectedSessionIds = (await Database.Table<SessionCoachAssignment>()
                .Where(item => item.CoachUserId == memberUserId)
                .ToListAsync())
            .Where(item => submittedCoachSessionIds.Contains(item.SessionId))
            .Select(item => item.SessionId)
            .ToHashSet();
        var allCoachCheckIns = await Database.Table<CoachCheckIn>()
            .Where(item => item.CoachUserId == memberUserId)
            .ToListAsync();
        var coachCheckIns = allCoachCheckIns
            .Where(item => expectedSessionIds.Contains(item.SessionId))
            .ToList();
        var checkedInSessionIds = coachCheckIns
            .Where(item => item.ApprovalStatus == CoachCheckInApprovalStatus.Approved
                           && CoachCheckInTime.HasCoachCheckout(item))
            .Select(item => item.SessionId)
            .ToHashSet();
        var pendingCheckInCount = allCoachCheckIns.Count(
            item => item.ApprovalStatus == CoachCheckInApprovalStatus.Pending
                    && CoachCheckInTime.HasCoachCheckout(item));
        var pendingSubmittedCount = coachCheckIns.Count(
            item => item.ApprovalStatus == CoachCheckInApprovalStatus.Pending
                    && CoachCheckInTime.HasCoachCheckout(item));

        return new MemberAttendanceSummary(
            member.Role,
            checkedInSessionIds.Count,
            Math.Max(0, expectedSessionIds.Count - checkedInSessionIds.Count - pendingSubmittedCount),
            0,
            0,
            expectedSessionIds.Count,
            pendingCheckInCount);
    }

    /// <summary>
    /// Returns the attendance counters for one trainee in one class.  The
    /// Founder class detail uses this compact summary so the roster shows the
    /// same submitted/locked sessions that drive tuition-cycle progress,
    /// without loading a long attendance history for every member.
    /// </summary>
    public async Task<MemberAttendanceSummary> GetClassTraineeAttendanceSummaryAsync(
        string actorUserId,
        string classId,
        string traineeUserId)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền xem điểm danh lớp.");
            await EnsureOnlineSnapshotAsync();
            var trainee = Online.User(traineeUserId)
                ?? throw new InvalidOperationException("Không tìm thấy học viên.");
            if (trainee.Role != UserRole.Trainee)
            {
                throw new InvalidOperationException("Chỉ có thể xem điểm danh của Cầu Thủ Học Viên.");
            }

            var isEnrolled = Online.ClassEnrollments.Any(item =>
                item.ClassId == classId
                && item.TraineeUserId == traineeUserId
                && item.IsActive);
            if (!isEnrolled)
            {
                return new MemberAttendanceSummary(UserRole.Trainee, 0, 0, 0, 0, 0);
            }

            var sessionIds = Online.TrainingSessions
                .Where(item => item.ClassId == classId
                               && item.Status is SessionStatus.Submitted or SessionStatus.Locked)
                .Select(item => item.Id)
                .ToHashSet();
            var records = Online.AttendanceRecords
                .Where(item => item.TraineeUserId == traineeUserId
                               && sessionIds.Contains(item.SessionId)
                               && item.Status != AttendanceStatus.Unmarked)
                .ToList();
            var late = records.Count(item => item.Status == AttendanceStatus.Late);
            return new MemberAttendanceSummary(
                UserRole.Trainee,
                records.Count(item => item.Status is AttendanceStatus.Present or AttendanceStatus.Late),
                records.Count(item => item.Status == AttendanceStatus.Absent),
                late,
                records.Count(item => item.Status == AttendanceStatus.Excused),
                records.Count);
        }

        await InitializeAsync();
        var attendanceActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(attendanceActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem điểm danh lớp.");
        var traineeAccount = await Database.FindAsync<UserAccount>(traineeUserId)
                             ?? throw new InvalidOperationException("Không tìm thấy học viên.");
        if (traineeAccount.Role != UserRole.Trainee)
        {
            throw new InvalidOperationException("Chỉ có thể xem điểm danh của Cầu Thủ Học Viên.");
        }

        var enrollment = await Database.Table<ClassEnrollment>()
            .Where(item => item.ClassId == classId
                           && item.TraineeUserId == traineeUserId
                           && item.IsActive)
            .FirstOrDefaultAsync();
        if (enrollment is null)
        {
            return new MemberAttendanceSummary(UserRole.Trainee, 0, 0, 0, 0, 0);
        }

        var sessionIdsLocal = (await Database.Table<TrainingSession>().ToListAsync())
            .Where(item => item.ClassId == classId
                           && item.Status is SessionStatus.Submitted or SessionStatus.Locked)
            .Select(item => item.Id)
            .ToHashSet();
        var recordsLocal = (await Database.Table<AttendanceRecord>()
                .Where(item => item.TraineeUserId == traineeUserId)
                .ToListAsync())
            .Where(item => sessionIdsLocal.Contains(item.SessionId)
                           && item.Status != AttendanceStatus.Unmarked)
            .ToList();
        var lateLocal = recordsLocal.Count(item => item.Status == AttendanceStatus.Late);
        return new MemberAttendanceSummary(
            UserRole.Trainee,
            recordsLocal.Count(item => item.Status is AttendanceStatus.Present or AttendanceStatus.Late),
            recordsLocal.Count(item => item.Status == AttendanceStatus.Absent),
            lateLocal,
            recordsLocal.Count(item => item.Status == AttendanceStatus.Excused),
            recordsLocal.Count);
    }

    private async Task<(int PlannedSessionCount, int AttendedSessionCount)> GetTuitionCycleProgressAsync(
        ClassEnrollment enrollment,
        int plannedPerCycle)
    {
        var sessions = await Database.Table<TrainingSession>()
            .Where(item => item.ClassId == enrollment.ClassId
                           && item.Status == SessionStatus.Submitted
                           && item.SessionDate >= enrollment.EnrolledAtUtc.Date)
            .ToListAsync();
        var sessionIds = sessions.Select(item => item.Id).ToHashSet();
        var records = await Database.Table<AttendanceRecord>()
            .Where(item => item.TraineeUserId == enrollment.TraineeUserId)
            .ToListAsync();
        // A completed class session counts toward the paid cycle even when
        // this trainee was marked Absent or Excused.  The session itself is
        // only Submitted after every roster entry has a recorded status, so
        // counting submitted sessions keeps cycle billing aligned with the
        // number of lessons delivered rather than attendance presence.
        var completedSessionIds = records
            .Where(item => sessionIds.Contains(item.SessionId)
                           && item.Status != AttendanceStatus.Unmarked)
            .Select(item => item.SessionId)
            .ToHashSet();
        var completed = sessions.Count(item => completedSessionIds.Contains(item.Id));
        return (Math.Max(1, plannedPerCycle), completed);
    }

    private static bool IsTuitionCycleInvoice(TuitionInvoice invoice) =>
        invoice.CycleNumber > 0
        || invoice.Period.StartsWith("C", StringComparison.OrdinalIgnoreCase);

    private static long CycleAmount(long cycleFeeVnd, int cycleCount) =>
        checked(Math.Max(0, cycleFeeVnd) * Math.Max(1, cycleCount));

    public async Task EnsureRecurringDataAsync(DateTime localDate)
    {
        // The Worker/D1 database is authoritative in online builds. Creating
        // invoices, reminders or salaries locally would introduce a second
        // truth that disappears at the next tenant snapshot refresh.
        if (_cloudOptions.IsConfigured)
        {
            return;
        }

        if (_database is null)
        {
            return;
        }

        var period = localDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var enrollments = await Database.Table<ClassEnrollment>()
            .Where(item => item.IsActive)
            .ToListAsync();
        var activeClasses = await Database.Table<TrainingClass>()
            .Where(item => item.IsActive)
            .ToListAsync();
        var activeClassIds = activeClasses.Select(item => item.Id).ToHashSet();
        var activeTrainees = await Database.Table<UserAccount>()
            .Where(item => item.Role == UserRole.Trainee && item.IsActive)
            .ToListAsync();
        var activeTraineeIds = activeTrainees
            .Select(item => item.Id)
            .ToHashSet();
        var supportedTraineeIds = (await Database.Table<UserAccount>()
                .Where(item => item.Role == UserRole.Trainee && item.IsTuitionSupported)
                .ToListAsync())
            .Select(item => item.Id)
            .ToHashSet();
        enrollments = enrollments
            .Where(item => activeClassIds.Contains(item.ClassId)
                           && activeTraineeIds.Contains(item.TraineeUserId)
                           && !supportedTraineeIds.Contains(item.TraineeUserId))
            .ToList();
        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);

        foreach (var enrollment in enrollments)
        {
            var trainingClass = activeClasses.FirstOrDefault(item => item.Id == enrollment.ClassId);
            if (trainingClass is null)
            {
                continue;
            }

            var plannedPerCycle = Math.Max(1, trainingClass.TuitionSessionCount);
            var cycleFee = enrollment.CycleFeeVnd > 0
                ? enrollment.CycleFeeVnd
                : enrollment.MonthlyFeeVnd > 0
                    ? enrollment.MonthlyFeeVnd
                    : trainingClass.DefaultFeeVnd;
            if (cycleFee <= 0)
            {
                continue;
            }

            var progress = await GetTuitionCycleProgressAsync(enrollment, plannedPerCycle);
            if (enrollment.IsTrial
                && progress.AttendedSessionCount >= Math.Clamp(enrollment.TrialSessionCount, 1, 5))
            {
                enrollment.IsTrial = false;
                enrollment.TrialSessionCount = 0;
                // The paid cycle starts after the trial lessons; keep trial
                // attendance in history but exclude it from tuition progress.
                enrollment.EnrolledAtUtc = DateTime.UtcNow;
                await Database.UpdateAsync(enrollment);
            }
            if (enrollment.IsTrial)
            {
                // Trial lessons are free and must not create a bill until the
                // configured trial count has been delivered.
                continue;
            }
            var allEnrollmentInvoices = (await Database.Table<TuitionInvoice>()
                    .Where(item => item.EnrollmentId == enrollment.Id)
                    .ToListAsync())
                .OrderBy(item => item.CycleNumber)
                .ThenBy(item => item.CreatedAtUtc)
                .ToList();
            var cycleInvoices = allEnrollmentInvoices
                .Where(IsTuitionCycleInvoice)
                .ToList();
            var deliveredBefore = 0;
            foreach (var paidInvoice in cycleInvoices
                         .Where(item => item.Status == InvoiceStatus.Paid)
                         .OrderBy(item => item.CycleNumber))
            {
                var plannedPaid = checked(plannedPerCycle * Math.Max(1, paidInvoice.CycleCount));
                var deliveredForInvoice = Math.Max(0, progress.AttendedSessionCount - deliveredBefore);
                if (deliveredForInvoice >= plannedPaid)
                {
                    var proofs = await Database.Table<PaymentProof>()
                        .Where(item => item.InvoiceId == paidInvoice.Id)
                        .ToListAsync();
                    foreach (var proof in proofs)
                    {
                        if (!string.IsNullOrWhiteSpace(proof.ImagePath)
                            && File.Exists(proof.ImagePath))
                        {
                            File.Delete(proof.ImagePath);
                        }
                        await Database.DeleteAsync(proof);
                    }
                }
                deliveredBefore += plannedPaid;
            }
            var name = profiles.GetValueOrDefault(enrollment.TraineeUserId)?.FullName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Hoc vien";
            }

            // Convert one legacy open monthly invoice into the first cycle so
            // existing offline data immediately follows the new rule.
            if (cycleInvoices.Count == 0)
            {
                var legacyOpen = allEnrollmentInvoices.FirstOrDefault(item =>
                    !IsTuitionCycleInvoice(item) && item.Status != InvoiceStatus.Paid);
                if (legacyOpen is not null)
                {
                    legacyOpen.Period = "C000001";
                    legacyOpen.CycleNumber = 1;
                    legacyOpen.CycleCount = 1;
                    legacyOpen.CycleFeeVnd = cycleFee;
                    legacyOpen.AmountVnd = cycleFee;
                    legacyOpen.PlannedSessionCount = plannedPerCycle;
                    legacyOpen.AttendedSessionCount = Math.Min(
                        progress.AttendedSessionCount,
                        plannedPerCycle);
                    legacyOpen.AmountPerSessionVnd = (long)Math.Round(
                        cycleFee / (decimal)plannedPerCycle,
                        MidpointRounding.AwayFromZero);
                    legacyOpen.DueDate = localDate.Date;
                    legacyOpen.PaymentContent = $"{name} dong hoc phi";
                    legacyOpen.UpdatedAtUtc = DateTime.UtcNow;
                    await Database.UpdateAsync(legacyOpen);
                    cycleInvoices.Add(legacyOpen);
                }
            }

            if (cycleInvoices.Count == 0)
            {
                var firstInvoice = new TuitionInvoice
                {
                    Id = EntityId.New(),
                    EnrollmentId = enrollment.Id,
                    TraineeUserId = enrollment.TraineeUserId,
                    ClassId = enrollment.ClassId,
                    Period = "C000001",
                    CycleNumber = 1,
                    CycleCount = 1,
                    CycleFeeVnd = cycleFee,
                    AmountVnd = cycleFee,
                    AttendedSessionCount = Math.Min(progress.AttendedSessionCount, plannedPerCycle),
                    PlannedSessionCount = plannedPerCycle,
                    AmountPerSessionVnd = (long)Math.Round(
                        cycleFee / (decimal)plannedPerCycle,
                        MidpointRounding.AwayFromZero),
                    DueDate = localDate.Date,
                    Status = InvoiceStatus.Pending,
                    PaymentContent = $"{name} dong hoc phi",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                await Database.InsertAsync(firstInvoice);
                cycleInvoices.Add(firstInvoice);
            }

            foreach (var invoice in cycleInvoices.Where(item =>
                         item.Status is InvoiceStatus.Pending
                             or InvoiceStatus.Overdue
                             or InvoiceStatus.Rejected))
            {
                var cycleCount = Math.Max(1, invoice.CycleCount);
                var planned = checked(plannedPerCycle * cycleCount);
                var amountPerSession = (long)Math.Round(
                    cycleFee / (decimal)plannedPerCycle,
                    MidpointRounding.AwayFromZero);
                var amount = CycleAmount(cycleFee, cycleCount);
                var attended = Math.Min(progress.AttendedSessionCount, planned);
                var changed = invoice.CycleFeeVnd != cycleFee
                              || invoice.AmountVnd != amount
                              || invoice.PlannedSessionCount != planned
                              || invoice.AttendedSessionCount != attended
                              || invoice.AmountPerSessionVnd != amountPerSession
                              || invoice.PaymentContent != $"{name} dong hoc phi";
                invoice.CycleFeeVnd = cycleFee;
                invoice.AmountVnd = amount;
                invoice.PlannedSessionCount = planned;
                invoice.AttendedSessionCount = attended;
                invoice.AmountPerSessionVnd = amountPerSession;
                invoice.PaymentContent = $"{name} dong hoc phi";
                if (changed)
                {
                    invoice.UpdatedAtUtc = DateTime.UtcNow;
                    await Database.UpdateAsync(invoice);
                }
            }

            var paidCycles = cycleInvoices
                .Where(item => item.Status == InvoiceStatus.Paid)
                .Sum(item => Math.Max(1, item.CycleCount));
            var hasOpenInvoice = cycleInvoices.Any(item => item.Status != InvoiceStatus.Paid);
            if (!hasOpenInvoice
                && paidCycles > 0
                && progress.AttendedSessionCount >= paidCycles * plannedPerCycle)
            {
                var nextCycleNumber = cycleInvoices.Max(item => Math.Max(1, item.CycleNumber)) + 1;
                var nextInvoice = new TuitionInvoice
                {
                    Id = EntityId.New(),
                    EnrollmentId = enrollment.Id,
                    TraineeUserId = enrollment.TraineeUserId,
                    ClassId = enrollment.ClassId,
                    Period = $"C{nextCycleNumber:000000}",
                    CycleNumber = nextCycleNumber,
                    CycleCount = 1,
                    CycleFeeVnd = cycleFee,
                    AmountVnd = cycleFee,
                    AttendedSessionCount = 0,
                    PlannedSessionCount = plannedPerCycle,
                    AmountPerSessionVnd = (long)Math.Round(
                        cycleFee / (decimal)plannedPerCycle,
                        MidpointRounding.AwayFromZero),
                    DueDate = localDate.Date,
                    Status = InvoiceStatus.Pending,
                    PaymentContent = $"{name} dong hoc phi",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                await Database.InsertAsync(nextInvoice);
                if (!await NotificationExistsAsync(
                        nextInvoice.TraineeUserId,
                        NotificationKind.TuitionReminder,
                        nextInvoice.Id))
                {
                    await AddNotificationAsync(
                        nextInvoice.TraineeUserId,
                        NotificationKind.TuitionReminder,
                        "Nhắc đóng học phí chu kỳ tiếp theo",
                        $"Bạn đã hoàn tất {paidCycles} chu kỳ đã đóng. Vui lòng thanh toán {DomainText.TuitionPrepaidCycles(nextInvoice)} tiếp theo.",
                        nextInvoice.Id);
                }
            }
        }

        var invoices = await Database.Table<TuitionInvoice>().ToListAsync();
        foreach (var invoice in invoices.Where(item =>
                     !supportedTraineeIds.Contains(item.TraineeUserId)
                     && item.Status == InvoiceStatus.Pending
                     && item.DueDate.Date < localDate.Date))
        {
            invoice.Status = InvoiceStatus.Overdue;
            invoice.UpdatedAtUtc = DateTime.UtcNow;
            await Database.UpdateAsync(invoice);
            if (!await NotificationExistsAsync(invoice.TraineeUserId, NotificationKind.TuitionReminder, invoice.Id))
            {
                await AddNotificationAsync(
                    invoice.TraineeUserId,
                    NotificationKind.TuitionReminder,
                    "Nhắc đóng học phí",
                    $"{DomainText.TuitionCycle(invoice)} đã quá hạn ngày {invoice.DueDate:dd/MM/yyyy}.",
                    invoice.Id);
            }
        }

        var coaches = await Database.Table<UserAccount>()
            .Where(item => item.Role == UserRole.Coach && item.IsActive)
            .ToListAsync();
        foreach (var coach in coaches)
        {
            var salary = await Database.Table<CoachSalary>()
                .Where(item => item.CoachUserId == coach.Id && item.Period == period)
                .FirstOrDefaultAsync();
            if (salary is null)
            {
                salary = new CoachSalary
                {
                    Id = EntityId.New(),
                    CoachUserId = coach.Id,
                    Period = period,
                    AmountVnd = await CalculateCoachSalaryAmountAsync(coach.Id, period),
                    DueDate = new DateTime(localDate.Year, localDate.Month, 10),
                    Status = SalaryStatus.Pending,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                await Database.InsertAsync(salary);
            }
            else
            {
                await RecomputePendingSalaryAsync(salary);
            }

            if (localDate.Day > 10 && salary.Status == SalaryStatus.Pending)
            {
                var founders = await Database.Table<UserAccount>()
                    .Where(item => item.IsActive
                                   && (item.Role == UserRole.Founder
                                       || item.Role == UserRole.CoFounder
                                       || item.Role == UserRole.Manager))
                    .ToListAsync();
                foreach (var founder in founders)
                {
                    if (!await NotificationExistsAsync(
                            founder.Id,
                            NotificationKind.SalaryReminder,
                            salary.Id))
                    {
                        var name = profiles.GetValueOrDefault(coach.Id)?.FullName ?? coach.Username;
                        await AddNotificationAsync(
                            founder.Id,
                            NotificationKind.SalaryReminder,
                            "Lương Coach chưa thanh toán",
                            $"Lương {name} {DomainText.Period(period).ToLowerInvariant()} chưa được đánh dấu đã thanh toán.",
                            salary.Id);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Calculates the current cycle progress from submitted lesson sessions,
    /// rather than trusting a stale invoice snapshot.  A completed session
    /// counts when the trainee has any final attendance status, including
    /// absent or excused, because the lesson was delivered.
    /// </summary>
    public async Task<TuitionCycleProgress> GetDisplayedTuitionProgressAsync(
        string actorUserId,
        string traineeUserId,
        string classId,
        TuitionInvoice? invoice = null)
    {
        if (IsOnline)
        {
            var actor = await RequireOnlineUserAsync(actorUserId);
            if (actor.Role == UserRole.Trainee && actor.Id != traineeUserId)
            {
                throw new UnauthorizedAccessException("Báº¡n chá»‰ Ä‘Æ°á»£c xem tiáº¿n Ä‘á»™ há»c phÃ­ cá»§a mÃ¬nh.");
            }

            await EnsureOnlineSnapshotAsync();
            var enrollment = Online.ClassEnrollments.FirstOrDefault(item =>
                item.ClassId == classId && item.TraineeUserId == traineeUserId && item.IsActive);
            var trainingClass = Online.Class(classId);
            if (enrollment is null || trainingClass is null)
            {
                return new TuitionCycleProgress(0, Math.Max(1, invoice?.PlannedSessionCount ?? 1), false, false);
            }

            var sessionIds = Online.TrainingSessions
                .Where(item => item.ClassId == classId
                               && item.Status is SessionStatus.Submitted or SessionStatus.Locked
                               && item.SessionDate.Date >= enrollment.EnrolledAtUtc.Date)
                .Select(item => item.Id)
                .ToHashSet();
            var completed = Online.AttendanceRecords
                .Where(item => item.TraineeUserId == traineeUserId
                               && sessionIds.Contains(item.SessionId)
                               && item.Status != AttendanceStatus.Unmarked)
                .Select(item => item.SessionId)
                .Distinct()
                .Count();
            var perCycle = Math.Max(1, trainingClass.TuitionSessionCount);
            if (enrollment.IsTrial)
            {
                var trialSessions = Math.Clamp(enrollment.TrialSessionCount, 1, 5);
                var attendedTrial = Math.Min(completed, trialSessions);
                return new TuitionCycleProgress(
                    attendedTrial,
                    trialSessions,
                    attendedTrial >= trialSessions,
                    false);
            }
            if (invoice is null)
            {
                var attended = Math.Min(completed, perCycle);
                return new TuitionCycleProgress(attended, perCycle, attended >= perCycle, completed >= 2);
            }

            var invoices = Online.Invoices
                .Where(item => item.EnrollmentId == enrollment.Id && item.CycleNumber > 0)
                .OrderBy(item => item.CycleNumber)
                .ToList();
            var previousCycles = invoices
                .Where(item => item.CycleNumber < invoice.CycleNumber)
                .Sum(item => Math.Max(1, item.CycleCount));
            var planned = invoice.PlannedSessionCount > 0
                ? invoice.PlannedSessionCount
                : perCycle * Math.Max(1, invoice.CycleCount);
            var attendedInCycle = Math.Clamp(completed - previousCycles * perCycle, 0, planned);
            var needsWarning = invoice.Status is not (InvoiceStatus.Paid)
                               && invoice.Status is not (InvoiceStatus.ProofSubmitted)
                               && attendedInCycle >= 2;
            return new TuitionCycleProgress(
                attendedInCycle,
                planned,
                attendedInCycle >= planned,
                needsWarning);
        }

        await InitializeAsync();
        var localActor = await RequireUserAsync(actorUserId);
        if (localActor.Role == UserRole.Trainee && localActor.Id != traineeUserId)
        {
            throw new UnauthorizedAccessException("Báº¡n chá»‰ Ä‘Æ°á»£c xem tiáº¿n Ä‘á»™ há»c phÃ­ cá»§a mÃ¬nh.");
        }

        var enrollmentLocal = await Database.Table<ClassEnrollment>()
            .Where(item => item.ClassId == classId
                           && item.TraineeUserId == traineeUserId
                           && item.IsActive)
            .FirstOrDefaultAsync();
        var classLocal = await Database.FindAsync<TrainingClass>(classId);
        if (enrollmentLocal is null || classLocal is null)
        {
            return new TuitionCycleProgress(0, Math.Max(1, invoice?.PlannedSessionCount ?? 1), false, false);
        }

        var sessionsLocal = (await Database.Table<TrainingSession>().ToListAsync())
            .Where(item => item.ClassId == classId
                           && item.Status is SessionStatus.Submitted or SessionStatus.Locked
                           && item.SessionDate.Date >= enrollmentLocal.EnrolledAtUtc.Date)
            .ToList();
        var sessionIdsLocal = sessionsLocal.Select(item => item.Id).ToHashSet();
        var completedLocal = (await Database.Table<AttendanceRecord>().ToListAsync())
            .Where(item => item.TraineeUserId == traineeUserId
                           && sessionIdsLocal.Contains(item.SessionId)
                           && item.Status != AttendanceStatus.Unmarked)
            .Select(item => item.SessionId)
            .Distinct()
            .Count();
        var perCycleLocal = Math.Max(1, classLocal.TuitionSessionCount);
        if (enrollmentLocal.IsTrial)
        {
            var trialSessions = Math.Clamp(enrollmentLocal.TrialSessionCount, 1, 5);
            var attendedTrial = Math.Min(completedLocal, trialSessions);
            return new TuitionCycleProgress(
                attendedTrial,
                trialSessions,
                attendedTrial >= trialSessions,
                false);
        }
        if (invoice is null)
        {
            var attended = Math.Min(completedLocal, perCycleLocal);
            return new TuitionCycleProgress(attended, perCycleLocal, attended >= perCycleLocal, completedLocal >= 2);
        }

        var localInvoices = (await Database.Table<TuitionInvoice>().ToListAsync())
            .Where(item => item.EnrollmentId == enrollmentLocal.Id && item.CycleNumber > 0)
            .OrderBy(item => item.CycleNumber)
            .ToList();
        var previousLocalCycles = localInvoices
            .Where(item => item.CycleNumber < invoice.CycleNumber)
            .Sum(item => Math.Max(1, item.CycleCount));
        var plannedLocal = invoice.PlannedSessionCount > 0
            ? invoice.PlannedSessionCount
            : perCycleLocal * Math.Max(1, invoice.CycleCount);
        var attendedLocal = Math.Clamp(completedLocal - previousLocalCycles * perCycleLocal, 0, plannedLocal);
        var warningLocal = invoice.Status is not (InvoiceStatus.Paid)
                           && invoice.Status is not (InvoiceStatus.ProofSubmitted)
                           && attendedLocal >= 2;
        return new TuitionCycleProgress(
            attendedLocal,
            plannedLocal,
            attendedLocal >= plannedLocal,
            warningLocal);
    }

    private Dictionary<string, TuitionCycleProgress> BuildOnlineInvoiceProgress(
        IReadOnlyCollection<TuitionInvoice> invoices)
    {
        var activeEnrollments = Online.ClassEnrollments
            .Where(item => item.IsActive)
            .GroupBy(item => (item.ClassId, item.TraineeUserId))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.EnrolledAtUtc).First());
        var classes = Online.Classes.ToDictionary(item => item.Id);
        var sessionsByClass = Online.TrainingSessions
            .Where(item => item.Status is SessionStatus.Submitted or SessionStatus.Locked)
            .GroupBy(item => item.ClassId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var completedSessionIdsByTrainee = Online.AttendanceRecords
            .Where(item => item.Status != AttendanceStatus.Unmarked)
            .GroupBy(item => item.TraineeUserId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.SessionId).ToHashSet());

        var completedByEnrollment = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var enrollment in activeEnrollments.Values)
        {
            if (!sessionsByClass.TryGetValue(enrollment.ClassId, out var sessions)
                || !completedSessionIdsByTrainee.TryGetValue(
                    enrollment.TraineeUserId,
                    out var completedSessionIds))
            {
                completedByEnrollment[enrollment.Id] = 0;
                continue;
            }

            completedByEnrollment[enrollment.Id] = sessions.Count(item =>
                item.SessionDate.Date >= enrollment.EnrolledAtUtc.Date
                && completedSessionIds.Contains(item.Id));
        }

        var previousCycles = new Dictionary<(string EnrollmentId, int CycleNumber), int>();
        foreach (var enrollmentInvoices in Online.Invoices
                     .Where(item => item.CycleNumber > 0)
                     .GroupBy(item => item.EnrollmentId))
        {
            var deliveredCycles = 0;
            foreach (var cycle in enrollmentInvoices
                         .GroupBy(item => item.CycleNumber)
                         .OrderBy(group => group.Key))
            {
                previousCycles[(enrollmentInvoices.Key, cycle.Key)] = deliveredCycles;
                deliveredCycles += cycle.Sum(item => Math.Max(1, item.CycleCount));
            }
        }

        var result = new Dictionary<string, TuitionCycleProgress>(StringComparer.Ordinal);
        foreach (var invoice in invoices)
        {
            if (!activeEnrollments.TryGetValue(
                    (invoice.ClassId, invoice.TraineeUserId),
                    out var enrollment)
                || !classes.TryGetValue(invoice.ClassId, out var trainingClass))
            {
                result[invoice.Id] = new TuitionCycleProgress(
                    0,
                    Math.Max(1, invoice.PlannedSessionCount),
                    false,
                    false);
                continue;
            }

            var completed = completedByEnrollment.GetValueOrDefault(enrollment.Id);
            var perCycle = Math.Max(1, trainingClass.TuitionSessionCount);
            if (enrollment.IsTrial)
            {
                var trialSessions = Math.Clamp(enrollment.TrialSessionCount, 1, 5);
                var attendedTrial = Math.Min(completed, trialSessions);
                result[invoice.Id] = new TuitionCycleProgress(
                    attendedTrial,
                    trialSessions,
                    attendedTrial >= trialSessions,
                    false);
                continue;
            }

            var planned = invoice.PlannedSessionCount > 0
                ? invoice.PlannedSessionCount
                : perCycle * Math.Max(1, invoice.CycleCount);
            var cyclesBefore = previousCycles.GetValueOrDefault(
                (enrollment.Id, invoice.CycleNumber));
            var attended = Math.Clamp(completed - cyclesBefore * perCycle, 0, planned);
            var needsWarning = invoice.Status is not InvoiceStatus.Paid
                               && invoice.Status is not InvoiceStatus.ProofSubmitted
                               && attended >= 2;
            result[invoice.Id] = new TuitionCycleProgress(
                attended,
                planned,
                attended >= planned,
                needsWarning);
        }

        return result;
    }

    public async Task<IReadOnlyList<InvoiceRow>> GetInvoicesAsync(
        string actorUserId,
        string? period = null)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (onlineActor.Role == UserRole.Coach)
                throw new UnauthorizedAccessException("Coach không có quyền xem học phí.");
            await EnsureOnlineSnapshotAsync();
            var supported = Online.Users.Where(item => item.IsTuitionSupported).Select(item => item.Id).ToHashSet();
            var onlineInvoices = Online.Invoices
                .Where(item => !supported.Contains(item.TraineeUserId)
                               && (onlineActor.Role != UserRole.Trainee || item.TraineeUserId == onlineActor.Id)
                               && (string.IsNullOrWhiteSpace(period) || item.Period == period))
                .ToList();
            var progressByInvoice = BuildOnlineInvoiceProgress(onlineInvoices);
            var onlineProfiles = Online.Profiles.ToDictionary(item => item.UserId);
            var onlineClasses = Online.Classes.ToDictionary(item => item.Id);
            var latestProofs = Online.PaymentProofs
                .GroupBy(item => item.InvoiceId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.SubmittedAtUtc).First());
            var onlineReceipts = Online.Receipts
                .GroupBy(item => item.InvoiceId)
                .ToDictionary(group => group.Key, group => group.First());
            var onlineRows = onlineInvoices.Select(invoice =>
            {
                var progress = progressByInvoice[invoice.Id];
                invoice.AttendedSessionCount = progress.AttendedSessions;
                var row = new InvoiceRow(
                    invoice,
                    onlineProfiles.GetValueOrDefault(invoice.TraineeUserId)?.FullName ?? "Học viên",
                    onlineClasses.GetValueOrDefault(invoice.ClassId)?.Name ?? "Lớp học",
                    latestProofs.GetValueOrDefault(invoice.Id),
                    onlineReceipts.GetValueOrDefault(invoice.Id))
                {
                    Progress = progress
                };
                return row;
            }).ToList();

            return onlineRows
                .OrderByDescending(item => item.Invoice.CycleNumber)
                .ThenByDescending(item => item.Invoice.Period)
                .ThenBy(item => item.TraineeName)
                .ToList();
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        if (actor.Role == UserRole.Coach)
        {
            throw new UnauthorizedAccessException("Coach không có quyền xem học phí.");
        }

        var supportedTraineeIds = await GetSupportedTraineeIdsAsync();
        var invoices = (await Database.Table<TuitionInvoice>().ToListAsync())
            .Where(item => !supportedTraineeIds.Contains(item.TraineeUserId))
            .ToList();
        if (actor.Role == UserRole.Trainee)
        {
            invoices = invoices.Where(item => item.TraineeUserId == actor.Id).ToList();
        }

        if (!string.IsNullOrWhiteSpace(period))
        {
            invoices = invoices.Where(item => item.Period == period).ToList();
        }

        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);
        var classes = (await Database.Table<TrainingClass>().ToListAsync())
            .ToDictionary(item => item.Id);
        var proofs = await Database.Table<PaymentProof>().ToListAsync();
        var receipts = await Database.Table<Receipt>().ToListAsync();

        var localRows = new List<InvoiceRow>();
        foreach (var invoice in invoices)
        {
            var row = new InvoiceRow(
                invoice,
                profiles.GetValueOrDefault(invoice.TraineeUserId)?.FullName ?? "Học viên",
                classes.GetValueOrDefault(invoice.ClassId)?.Name ?? "Lớp học",
                proofs.Where(item => item.InvoiceId == invoice.Id)
                    .OrderByDescending(item => item.SubmittedAtUtc)
                    .FirstOrDefault(),
                receipts.FirstOrDefault(item => item.InvoiceId == invoice.Id))
            {
                Progress = await GetDisplayedTuitionProgressAsync(
                    actorUserId,
                    invoice.TraineeUserId,
                    invoice.ClassId,
                    invoice)
            };
            invoice.AttendedSessionCount = row.Progress.AttendedSessions;
            localRows.Add(row);
        }

        return localRows
            .OrderByDescending(item => item.Invoice.CycleNumber)
            .ThenByDescending(item => item.Invoice.Period)
            .ThenBy(item => item.TraineeName)
            .ToList();
    }

    /// <summary>
    /// Materializes private R2 payment-proof objects into the device cache so
    /// the existing preview/save UI can use a normal local image path. Cloud
    /// snapshots intentionally contain only object keys and never device paths.
    /// </summary>
    public async Task EnsurePaymentProofImagesAsync(
        string actorUserId,
        IEnumerable<InvoiceRow> rows)
    {
        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            foreach (var row in rows)
            {
                if (row.LatestProof is null || File.Exists(row.LatestProof.ImagePath))
                    continue;
                var sourceKey = row.LatestProof.ImagePath;
                var cachedPath = FindCachedMediaPath(
                    "payment-proof",
                    row.LatestProof.Id,
                    sourceKey);
                if (cachedPath is not null)
                {
                    row.LatestProof.ImagePath = cachedPath;
                    continue;
                }
                try
                {
                    var remote = await _cloudApi.DownloadFileAsync($"tuition/proofs/{Uri.EscapeDataString(row.LatestProof.Id)}/image");
                    var localPath = CreateCachedMediaPath(
                        "payment-proof",
                        row.LatestProof.Id,
                        sourceKey,
                        remote.ContentType);
                    await File.WriteAllBytesAsync(localPath, remote.Bytes);
                    row.LatestProof.ImagePath = localPath;
                }
                catch (ApiException)
                {
                }
            }
            return;
        }

        await InitializeAsync();
        await RequireUserAsync(actorUserId);
        if (!_cloudOptions.IsConfigured || !await HasCloudSessionForAsync(actorUserId))
        {
            return;
        }

        foreach (var row in rows)
        {
            if (row.LatestProof is null || File.Exists(row.LatestProof.ImagePath))
            {
                continue;
            }

            try
            {
                var remote = await _cloudApi.DownloadFileAsync(
                    $"tuition/proofs/{Uri.EscapeDataString(row.LatestProof.Id)}/image");
                var extension = remote.ContentType.ToLowerInvariant() switch
                {
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    _ => ".jpg"
                };
                var directory = Path.Combine(
                    FileSystem.AppDataDirectory,
                    "media",
                    "payment-proof");
                Directory.CreateDirectory(directory);
                var localPath = Path.Combine(directory, row.LatestProof.Id + extension);
                await File.WriteAllBytesAsync(localPath, remote.Bytes);
                row.LatestProof.ImagePath = localPath;
                await Database.UpdateAsync(row.LatestProof);
            }
            catch (ApiException)
            {
                // A missing/expired R2 object must not prevent the Founder or
                // Trainee finance list from loading. The card simply omits the
                // preview until the object is available again.
            }
        }
    }

    /// <summary>
    /// Materializes private R2 Coach check-in selfies into the device cache.
    /// Cloud snapshots contain an object key in SelfiePath, not a Windows or
    /// Android file path, so Founder review/history pages must download the
    /// image through the tenant-authorized Worker endpoint before rendering.
    /// </summary>
    public async Task EnsureCoachCheckInSelfieImagesAsync(
        string actorUserId,
        IEnumerable<CoachCheckInReviewRow> rows)
    {
        await EnsureCoachCheckInSelfieImagesAsync(
            actorUserId,
            rows.Select(item => item.CheckIn));
    }

    public async Task EnsureCoachCheckInSelfieImagesAsync(
        string actorUserId,
        IEnumerable<CoachCheckInHistoryRow> rows)
    {
        await EnsureCoachCheckInSelfieImagesAsync(
            actorUserId,
            rows.Select(item => item.CheckIn));
    }

    private async Task EnsureCoachCheckInSelfieImagesAsync(
        string actorUserId,
        IEnumerable<CoachCheckIn> checkIns)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanApproveOperations(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền xem ảnh check-in.");
            foreach (var checkIn in checkIns)
            {
                if (!File.Exists(checkIn.SelfiePath) && !string.IsNullOrWhiteSpace(checkIn.SelfiePath))
                {
                    var sourceKey = checkIn.SelfiePath;
                    var cachedPath = FindCachedMediaPath(
                        "coach-check-in",
                        checkIn.Id,
                        sourceKey);
                    if (cachedPath is not null)
                    {
                        checkIn.SelfiePath = cachedPath;
                    }
                    else
                    {
                        try
                        {
                            var remote = await _cloudApi.DownloadFileAsync($"check-ins/{Uri.EscapeDataString(checkIn.Id)}/selfie");
                            var localPath = CreateCachedMediaPath(
                                "coach-check-in",
                                checkIn.Id,
                                sourceKey,
                                remote.ContentType);
                            await File.WriteAllBytesAsync(localPath, remote.Bytes);
                            checkIn.SelfiePath = localPath;
                        }
                        catch (ApiException)
                        {
                        }
                    }
                }
                if (!File.Exists(checkIn.CheckOutSelfiePath) && !string.IsNullOrWhiteSpace(checkIn.CheckOutSelfiePath))
                {
                    var sourceKey = checkIn.CheckOutSelfiePath;
                    var cachedPath = FindCachedMediaPath(
                        "coach-checkout",
                        checkIn.Id,
                        sourceKey);
                    if (cachedPath is not null)
                    {
                        checkIn.CheckOutSelfiePath = cachedPath;
                    }
                    else
                    {
                        try
                        {
                            var remote = await _cloudApi.DownloadFileAsync($"check-ins/{Uri.EscapeDataString(checkIn.Id)}/checkout-selfie");
                            var localPath = CreateCachedMediaPath(
                                "coach-checkout",
                                checkIn.Id,
                                sourceKey,
                                remote.ContentType);
                            await File.WriteAllBytesAsync(localPath, remote.Bytes);
                            checkIn.CheckOutSelfiePath = localPath;
                        }
                        catch (ApiException)
                        {
                        }
                    }
                }
            }
            return;
        }

        await InitializeAsync();
        var imageActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanApproveOperations(imageActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem ảnh check-in.");
        if (!_cloudOptions.IsConfigured || !await HasCloudSessionForAsync(actorUserId))
        {
            return;
        }

        foreach (var checkIn in checkIns)
        {
            if (!File.Exists(checkIn.SelfiePath)
                && !string.IsNullOrWhiteSpace(checkIn.SelfiePath))
            {
                try
                {
                    var remote = await _cloudApi.DownloadFileAsync(
                        $"check-ins/{Uri.EscapeDataString(checkIn.Id)}/selfie");
                    var extension = remote.ContentType.ToLowerInvariant() switch
                    {
                        "image/png" => ".png",
                        "image/webp" => ".webp",
                        _ => ".jpg"
                    };
                    var directory = Path.Combine(
                        FileSystem.AppDataDirectory,
                        "media",
                        "coach-check-in");
                    Directory.CreateDirectory(directory);
                    var localPath = Path.Combine(directory, checkIn.Id + extension);
                    await File.WriteAllBytesAsync(localPath, remote.Bytes);
                    checkIn.SelfiePath = localPath;
                    await Database.UpdateAsync(checkIn);
                }
                catch (ApiException)
                {
                    // A missing/expired R2 object must not prevent the approval
                    // queue from loading. The card keeps its missing-file state.
                }
            }

            if (!File.Exists(checkIn.CheckOutSelfiePath)
                && !string.IsNullOrWhiteSpace(checkIn.CheckOutSelfiePath))
            {
                try
                {
                    var remote = await _cloudApi.DownloadFileAsync(
                        $"check-ins/{Uri.EscapeDataString(checkIn.Id)}/checkout-selfie");
                    var extension = remote.ContentType.ToLowerInvariant() switch
                    {
                        "image/png" => ".png",
                        "image/webp" => ".webp",
                        _ => ".jpg"
                    };
                    var directory = Path.Combine(
                        FileSystem.AppDataDirectory,
                        "media",
                        "coach-checkout");
                    Directory.CreateDirectory(directory);
                    var localPath = Path.Combine(directory, checkIn.Id + extension);
                    await File.WriteAllBytesAsync(localPath, remote.Bytes);
                    checkIn.CheckOutSelfiePath = localPath;
                    await Database.UpdateAsync(checkIn);
                }
                catch (ApiException)
                {
                    // Keep the review card visible even when the checkout
                    // object was removed from private R2.
                }
            }
        }
    }

    private async Task<HashSet<string>> GetSupportedTraineeIdsAsync()
    {
        return (await Database.Table<UserAccount>()
                .Where(item => item.Role == UserRole.Trainee && item.IsTuitionSupported)
                .ToListAsync())
            .Select(item => item.Id)
            .ToHashSet();
    }

    public async Task SetInvoiceCycleCountAsync(
        string actorUserId,
        string invoiceId,
        int cycleCount)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineRoleAsync(actorUserId, UserRole.Trainee);
            if (cycleCount is < 1 or > 12)
                throw new InvalidOperationException("Số chu kỳ đóng trước phải từ 1 đến 12.");
            await EnsureOnlineSnapshotAsync();
            var onlineInvoice = Online.Invoices.FirstOrDefault(item => item.Id == invoiceId)
                ?? throw new InvalidOperationException("Không tìm thấy học phí.");
            if (onlineInvoice.TraineeUserId != onlineActor.Id)
                throw new UnauthorizedAccessException("Bạn không có quyền chỉnh khoản học phí này.");
            if (onlineActor.IsTuitionSupported)
                throw new InvalidOperationException($"Account {DomainText.SupportedTraineeLabel} được miễn học phí.");
            if (onlineInvoice.Status is InvoiceStatus.Paid or InvoiceStatus.ProofSubmitted)
                throw new InvalidOperationException("Khoản học phí đã gửi bill hoặc đã đóng không thể đổi số chu kỳ.");
            try
            {
                await _cloudApi.PatchAsync(
                    $"tuition/invoices/{Uri.EscapeDataString(invoiceId)}/cycles",
                    new { cycleCount },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
            onlineInvoice.CycleCount = cycleCount;
            onlineInvoice.AmountVnd = onlineInvoice.CycleFeeVnd * cycleCount;
            onlineInvoice.PlannedSessionCount = Math.Max(1, onlineInvoice.PlannedSessionCount * cycleCount);
            onlineInvoice.UpdatedAtUtc = DateTime.UtcNow;
            return;
        }

        await InitializeAsync();
        var actor = await RequireRoleAsync(actorUserId, UserRole.Trainee);
        if (cycleCount is < 1 or > 12)
        {
            throw new InvalidOperationException("Số chu kỳ đóng trước phải từ 1 đến 12.");
        }

        var invoice = await Database.FindAsync<TuitionInvoice>(invoiceId)
                      ?? throw new InvalidOperationException("Không tìm thấy học phí.");
        if (invoice.TraineeUserId != actor.Id)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền chỉnh khoản học phí này.");
        }

        if (actor.IsTuitionSupported)
        {
            throw new InvalidOperationException(
                $"Account {DomainText.SupportedTraineeLabel} được miễn học phí.");
        }

        if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.ProofSubmitted)
        {
            throw new InvalidOperationException(
                "Khoản học phí đã gửi bill hoặc đã đóng không thể đổi số chu kỳ.");
        }

        var trainingClass = await Database.FindAsync<TrainingClass>(invoice.ClassId)
                            ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
        var enrollment = await Database.FindAsync<ClassEnrollment>(invoice.EnrollmentId)
                         ?? throw new InvalidOperationException("Không tìm thấy phân công lớp.");
        var cycleFee = invoice.CycleFeeVnd > 0
            ? invoice.CycleFeeVnd
            : enrollment.CycleFeeVnd > 0
                ? enrollment.CycleFeeVnd
                : enrollment.MonthlyFeeVnd > 0
                    ? enrollment.MonthlyFeeVnd
                    : trainingClass.DefaultFeeVnd;
        if (cycleFee <= 0)
        {
            throw new InvalidOperationException("Lớp chưa có học phí cho một chu kỳ.");
        }

        var plannedPerCycle = Math.Max(1, trainingClass.TuitionSessionCount);
        invoice.CycleCount = cycleCount;
        invoice.CycleFeeVnd = cycleFee;
        invoice.AmountVnd = CycleAmount(cycleFee, cycleCount);
        invoice.PlannedSessionCount = checked(plannedPerCycle * cycleCount);
        invoice.AmountPerSessionVnd = (long)Math.Round(
            cycleFee / (decimal)plannedPerCycle,
            MidpointRounding.AwayFromZero);
        if (_cloudOptions.IsConfigured)
        {
            await EnsureCloudWriteReadyAsync(actorUserId);
            try
            {
                await _cloudApi.PatchAsync(
                    $"tuition/invoices/{Uri.EscapeDataString(invoice.Id)}/cycles",
                    new { cycleCount },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        invoice.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(invoice);
        await AddAuditAsync(
            actorUserId,
            "UpdateTuitionPrepaidCycles",
            nameof(TuitionInvoice),
            invoice.Id,
            $"{cycleCount} chu kỳ · {invoice.AmountVnd:N0} VNĐ");
    }

    /// <summary>
    /// Founder-side equivalent of the Trainee prepaid-cycle selector.  The
    /// Founder may prepare the same invoice before confirming a parent bank
    /// transfer, but can never change an already-paid or bill-submitted cycle.
    /// </summary>
    public async Task SetInvoiceCycleCountByFounderAsync(
        string actorUserId,
        string invoiceId,
        int cycleCount)
    {
        if (cycleCount is < 1 or > 12)
        {
            throw new InvalidOperationException("Số chu kỳ đóng trước phải từ 1 đến 12.");
        }

        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanApproveOperations(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền chuẩn bị học phí thay phụ huynh.");
            await EnsureOnlineSnapshotAsync();
            var invoice = Online.Invoices.FirstOrDefault(item => item.Id == invoiceId)
                          ?? throw new InvalidOperationException("Không tìm thấy học phí.");
            var trainee = Online.User(invoice.TraineeUserId);
            if (trainee?.IsTuitionSupported == true)
            {
                throw new InvalidOperationException(
                    $"{DomainText.SupportedTraineeLabel} được miễn học phí.");
            }

            if (invoice.Status is InvoiceStatus.Paid or InvoiceStatus.ProofSubmitted)
            {
                throw new InvalidOperationException(
                    "Khoản học phí đã gửi bill hoặc đã đóng không thể đổi số chu kỳ.");
            }

            var trainingClass = Online.Class(invoice.ClassId)
                                ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
            var enrollment = Online.ClassEnrollments.FirstOrDefault(item =>
                item.Id == invoice.EnrollmentId && item.IsActive)
                             ?? throw new InvalidOperationException("Không tìm thấy phân công lớp.");
            var cycleFee = invoice.CycleFeeVnd > 0
                ? invoice.CycleFeeVnd
                : enrollment.CycleFeeVnd > 0
                    ? enrollment.CycleFeeVnd
                    : enrollment.MonthlyFeeVnd > 0
                        ? enrollment.MonthlyFeeVnd
                        : trainingClass.DefaultFeeVnd;
            if (cycleFee <= 0)
            {
                throw new InvalidOperationException("Lớp chưa có học phí cho một chu kỳ.");
            }

            try
            {
                await _cloudApi.PatchAsync(
                    $"tuition/invoices/{Uri.EscapeDataString(invoiceId)}/cycles",
                    new { cycleCount },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }

            invoice.CycleCount = cycleCount;
            invoice.CycleFeeVnd = cycleFee;
            invoice.AmountVnd = CycleAmount(cycleFee, cycleCount);
            invoice.PlannedSessionCount = checked(
                Math.Max(1, trainingClass.TuitionSessionCount) * cycleCount);
            invoice.AmountPerSessionVnd = (long)Math.Round(
                cycleFee / (decimal)Math.Max(1, trainingClass.TuitionSessionCount),
                MidpointRounding.AwayFromZero);
            invoice.UpdatedAtUtc = DateTime.UtcNow;
            return;
        }

        await InitializeAsync();
        var cycleActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanApproveOperations(cycleActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền chuẩn bị học phí thay phụ huynh.");
        var localInvoice = await Database.FindAsync<TuitionInvoice>(invoiceId)
                           ?? throw new InvalidOperationException("Không tìm thấy học phí.");
        var localTrainee = await Database.FindAsync<UserAccount>(localInvoice.TraineeUserId);
        if (localTrainee?.IsTuitionSupported == true)
        {
            throw new InvalidOperationException(
                $"{DomainText.SupportedTraineeLabel} được miễn học phí.");
        }

        if (localInvoice.Status is InvoiceStatus.Paid or InvoiceStatus.ProofSubmitted)
        {
            throw new InvalidOperationException(
                "Khoản học phí đã gửi bill hoặc đã đóng không thể đổi số chu kỳ.");
        }

        var localClass = await Database.FindAsync<TrainingClass>(localInvoice.ClassId)
                         ?? throw new InvalidOperationException("Không tìm thấy lớp học.");
        var localEnrollment = await Database.FindAsync<ClassEnrollment>(localInvoice.EnrollmentId)
                              ?? throw new InvalidOperationException("Không tìm thấy phân công lớp.");
        var localCycleFee = localInvoice.CycleFeeVnd > 0
            ? localInvoice.CycleFeeVnd
            : localEnrollment.CycleFeeVnd > 0
                ? localEnrollment.CycleFeeVnd
                : localEnrollment.MonthlyFeeVnd > 0
                    ? localEnrollment.MonthlyFeeVnd
                    : localClass.DefaultFeeVnd;
        if (localCycleFee <= 0)
        {
            throw new InvalidOperationException("Lớp chưa có học phí cho một chu kỳ.");
        }

        localInvoice.CycleCount = cycleCount;
        localInvoice.CycleFeeVnd = localCycleFee;
        localInvoice.AmountVnd = CycleAmount(localCycleFee, cycleCount);
        localInvoice.PlannedSessionCount = checked(
            Math.Max(1, localClass.TuitionSessionCount) * cycleCount);
        localInvoice.AmountPerSessionVnd = (long)Math.Round(
            localCycleFee / (decimal)Math.Max(1, localClass.TuitionSessionCount),
            MidpointRounding.AwayFromZero);

        if (_cloudOptions.IsConfigured)
        {
            await EnsureCloudWriteReadyAsync(actorUserId);
            try
            {
                await _cloudApi.PatchAsync(
                    $"tuition/invoices/{Uri.EscapeDataString(invoiceId)}/cycles",
                    new { cycleCount },
                    idempotencyKey: EntityId.New());
                await RefreshCloudProjectionAsync();
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        localInvoice.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(localInvoice);
        await AddAuditAsync(
            actorUserId,
            "UpdateTuitionPrepaidCyclesByFounder",
            nameof(TuitionInvoice),
            localInvoice.Id,
            $"{cycleCount} chu kỳ · {localInvoice.AmountVnd:N0} VNĐ");
    }

    public async Task SubmitPaymentProofAsync(
        string actorUserId,
        string invoiceId,
        string imagePath,
        string note)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineRoleAsync(actorUserId, UserRole.Trainee);
            await EnsureOnlineSnapshotAsync();
            var onlineInvoice = Online.Invoices.FirstOrDefault(item => item.Id == invoiceId)
                ?? throw new InvalidOperationException("Không tìm thấy học phí.");
            if (onlineInvoice.TraineeUserId != onlineActor.Id)
                throw new UnauthorizedAccessException("Bạn không có quyền gửi bill cho học phí này.");
            if (onlineActor.IsTuitionSupported)
                throw new InvalidOperationException($"Account {DomainText.SupportedTraineeLabel} được miễn học phí và không cần gửi bill.");
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                throw new InvalidOperationException("Không tìm thấy hình bill.");
            try
            {
                var upload = await _cloudApi.UploadFileAsync(imagePath, "payment_proof");
                var remote = await _cloudApi.PostAsync<object, CloudUploadResponse>(
                    $"tuition/invoices/{Uri.EscapeDataString(invoiceId)}/proofs",
                    new { uploadId = upload.Id, note = note.Trim() },
                    EntityId.New());
                var onlineProofSubmit = new PaymentProof
                {
                    Id = remote.Id,
                    InvoiceId = invoiceId,
                    ImagePath = imagePath,
                    Note = note.Trim(),
                    SubmittedAtUtc = DateTime.UtcNow
                };
                Online.Upsert(Online.PaymentProofs, onlineProofSubmit, item => item.Id == onlineProofSubmit.Id);
                onlineInvoice.Status = InvoiceStatus.ProofSubmitted;
                onlineInvoice.UpdatedAtUtc = DateTime.UtcNow;
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var actor = await RequireRoleAsync(actorUserId, UserRole.Trainee);
        var invoice = await Database.FindAsync<TuitionInvoice>(invoiceId)
                      ?? throw new InvalidOperationException("Không tìm thấy học phí.");
        if (invoice.TraineeUserId != actor.Id)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền gửi bill cho học phí này.");
        }

        if (actor.IsTuitionSupported)
        {
            throw new InvalidOperationException(
                $"Account {DomainText.SupportedTraineeLabel} được miễn học phí và không cần gửi bill.");
        }

        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw new InvalidOperationException("Học phí này đã được xác nhận.");
        }

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            throw new InvalidOperationException("Không tìm thấy hình bill.");
        }

        var remoteProofId = string.Empty;
        if (_cloudOptions.IsConfigured)
        {
            await EnsureCloudWriteReadyAsync(actorUserId);
            try
            {
                var upload = await _cloudApi.UploadFileAsync(imagePath, "payment_proof");
                var remote = await _cloudApi.PostAsync<object, CloudUploadResponse>(
                    $"tuition/invoices/{Uri.EscapeDataString(invoice.Id)}/proofs",
                    new { uploadId = upload.Id, note = note.Trim() },
                    EntityId.New());
                remoteProofId = remote.Id;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        var proof = new PaymentProof
        {
            Id = string.IsNullOrWhiteSpace(remoteProofId) ? EntityId.New() : remoteProofId,
            InvoiceId = invoice.Id,
            ImagePath = imagePath,
            Note = note.Trim(),
            SubmittedAtUtc = DateTime.UtcNow
        };
        invoice.Status = InvoiceStatus.ProofSubmitted;
        invoice.UpdatedAtUtc = DateTime.UtcNow;
        await Database.RunInTransactionAsync(connection =>
        {
            connection.Insert(proof);
            connection.Update(invoice);
        });

        var profile = await GetProfileAsync(actor.Id);
        var founders = await Database.Table<UserAccount>()
            .Where(item => item.IsActive
                           && (item.Role == UserRole.Founder
                               || item.Role == UserRole.CoFounder
                               || item.Role == UserRole.Manager))
            .ToListAsync();
        foreach (var founder in founders)
        {
            await AddNotificationAsync(
                founder.Id,
                NotificationKind.TuitionProofSubmitted,
                "Có bill học phí mới",
                $"{profile.FullName} đã tải bill {DomainText.TuitionCycle(invoice).ToLowerInvariant()}.",
                invoice.Id,
                writeCloud: !_cloudOptions.IsConfigured);
        }

        await AddAuditAsync(actorUserId, "SubmitPaymentProof", nameof(TuitionInvoice), invoice.Id, proof.Id,
            writeCloud: !_cloudOptions.IsConfigured);
        QueueCloudProjectionRefresh();
    }

    public async Task<Receipt> ConfirmTuitionAsync(string actorUserId, string invoiceId)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanApproveOperations(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền duyệt bill học phí.");
            await EnsureOnlineSnapshotAsync();
            var onlineInvoice = Online.Invoices.FirstOrDefault(item => item.Id == invoiceId)
                ?? throw new InvalidOperationException("Không tìm thấy học phí.");
            var onlineTrainee = Online.User(onlineInvoice.TraineeUserId);
            if (onlineTrainee?.IsTuitionSupported == true)
                throw new InvalidOperationException($"{DomainText.SupportedTraineeLabel} được miễn học phí, không cần xác nhận bill.");
            if (Online.Receipts.FirstOrDefault(item => item.InvoiceId == invoiceId) is { } existing)
                return existing;
            var onlineProof = Online.PaymentProofs
                .Where(item => item.InvoiceId == invoiceId)
                .OrderByDescending(item => item.SubmittedAtUtc)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Học viên chưa tải bill.");
            try
            {
                await _cloudApi.PatchAsync(
                    $"tuition/proofs/{Uri.EscapeDataString(onlineProof.Id)}/review",
                    new { accepted = true },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
            await ReloadOnlineSnapshotAsync();
            return Online.Receipts.FirstOrDefault(item => item.InvoiceId == invoiceId)
                ?? throw new InvalidOperationException("Backend chưa tạo hóa đơn xác nhận.");
        }

        await InitializeAsync();
        var founder = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanApproveOperations(founder.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền duyệt bill học phí.");
        var invoice = await Database.FindAsync<TuitionInvoice>(invoiceId)
                      ?? throw new InvalidOperationException("Không tìm thấy học phí.");
        var trainee = await Database.FindAsync<UserAccount>(invoice.TraineeUserId);
        if (trainee?.IsTuitionSupported == true)
        {
            throw new InvalidOperationException(
                $"{DomainText.SupportedTraineeLabel} được miễn học phí, không cần xác nhận bill.");
        }
        var existingReceipt = await Database.Table<Receipt>()
            .Where(item => item.InvoiceId == invoice.Id)
            .FirstOrDefaultAsync();
        if (existingReceipt is not null)
        {
            return existingReceipt;
        }

        var proof = await Database.Table<PaymentProof>()
            .Where(item => item.InvoiceId == invoice.Id)
            .OrderByDescending(item => item.SubmittedAtUtc)
            .FirstOrDefaultAsync();
        if (proof is null)
        {
            throw new InvalidOperationException("Học viên chưa tải bill.");
        }

        if (_cloudOptions.IsConfigured)
        {
            await EnsureCloudWriteReadyAsync(actorUserId);
            try
            {
                await _cloudApi.PatchAsync(
                    $"tuition/proofs/{Uri.EscapeDataString(proof.Id)}/review",
                    new { accepted = true },
                    idempotencyKey: EntityId.New());
                await RefreshCloudProjectionAsync();
                return await Database.Table<Receipt>()
                           .Where(item => item.InvoiceId == invoice.Id)
                           .FirstOrDefaultAsync()
                       ?? throw new InvalidOperationException("Backend chÆ°a táº¡o hÃ³a Ä‘Æ¡n xÃ¡c nháº­n.");
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        proof.IsAccepted = true;
        proof.ReviewedByUserId = founder.Id;
        proof.ReviewedAtUtc = DateTime.UtcNow;
        invoice.Status = InvoiceStatus.Paid;
        invoice.UpdatedAtUtc = DateTime.UtcNow;

        var club = await GetClubAsync();
        var traineeProfile = await GetProfileAsync(invoice.TraineeUserId);
        var founderProfile = await GetProfileAsync(founder.Id);
        var trainingClass = await Database.FindAsync<TrainingClass>(invoice.ClassId);
        var receipt = new Receipt
        {
            Id = EntityId.New(),
            InvoiceId = invoice.Id,
            ReceiptNumber = $"CFC-{DateTime.Now:yyyyMMdd}-{invoice.Id[..6].ToUpperInvariant()}",
            TeamNameSnapshot = club.TeamName,
            TraineeNameSnapshot = traineeProfile.FullName,
            ClassNameSnapshot = trainingClass?.Name ?? "Lớp học",
            PeriodSnapshot = DomainText.TuitionCycle(invoice),
            AmountVndSnapshot = invoice.AmountVnd,
            ConfirmedByNameSnapshot = founderProfile.FullName,
            ConfirmedAtUtc = DateTime.UtcNow
        };
        await Database.RunInTransactionAsync(connection =>
        {
            connection.Update(proof);
            connection.Update(invoice);
            connection.Insert(receipt);
        });

        await AddNotificationAsync(
            invoice.TraineeUserId,
            NotificationKind.TuitionConfirmed,
            "Học phí đã được xác nhận",
            $"{DomainText.TuitionCycle(invoice)} đã được đánh dấu đã đóng. Bạn có thể xuất hóa đơn PDF.",
            invoice.Id,
            writeCloud: true);
        await AddAuditAsync(actorUserId, "ConfirmTuition", nameof(TuitionInvoice), invoice.Id, receipt.ReceiptNumber,
            writeCloud: true);
        return receipt;
    }

    /// <summary>
    /// Founder records a parent bank transfer directly. Unlike
    /// ConfirmTuitionAsync this flow intentionally does not require a payment
    /// proof image; it still uses the same receipt, notification, audit and
    /// online tenant checks as a normal bill confirmation.
    /// </summary>
    public async Task<Receipt> ConfirmTuitionByFounderAsync(
        string actorUserId,
        string invoiceId)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanApproveOperations(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền đóng học phí thay phụ huynh.");
            await EnsureOnlineSnapshotAsync();
            var onlineInvoice = Online.Invoices.FirstOrDefault(item => item.Id == invoiceId)
                ?? throw new InvalidOperationException("Không tìm thấy học phí.");
            if (Online.User(onlineInvoice.TraineeUserId)?.IsTuitionSupported == true)
            {
                throw new InvalidOperationException(
                    $"{DomainText.SupportedTraineeLabel} được miễn học phí, không cần xác nhận.");
            }

            if (Online.Receipts.FirstOrDefault(item => item.InvoiceId == invoiceId) is { } existing)
            {
                return existing;
            }

            try
            {
                await _cloudApi.PostAsync<object, object>(
                    $"tuition/invoices/{Uri.EscapeDataString(invoiceId)}/parent-confirm",
                    new { },
                    EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }

            await ReloadOnlineSnapshotAsync();
            return Online.Receipts.FirstOrDefault(item => item.InvoiceId == invoiceId)
                ?? throw new InvalidOperationException("Backend chưa tạo hóa đơn xác nhận.");
        }

        await InitializeAsync();
        var founder = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanApproveOperations(founder.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền đóng học phí thay phụ huynh.");
        var invoice = await Database.FindAsync<TuitionInvoice>(invoiceId)
                      ?? throw new InvalidOperationException("Không tìm thấy học phí.");
        var trainee = await Database.FindAsync<UserAccount>(invoice.TraineeUserId);
        if (trainee?.IsTuitionSupported == true)
        {
            throw new InvalidOperationException(
                $"{DomainText.SupportedTraineeLabel} được miễn học phí, không cần xác nhận.");
        }

        var existingReceipt = await Database.Table<Receipt>()
            .Where(item => item.InvoiceId == invoice.Id)
            .FirstOrDefaultAsync();
        if (existingReceipt is not null)
        {
            return existingReceipt;
        }

        if (_cloudOptions.IsConfigured)
        {
            await EnsureCloudWriteReadyAsync(actorUserId);
            try
            {
                await _cloudApi.PostAsync<object, object>(
                    $"tuition/invoices/{Uri.EscapeDataString(invoiceId)}/parent-confirm",
                    new { },
                    EntityId.New());
                await RefreshCloudProjectionAsync();
                return await Database.Table<Receipt>()
                           .Where(item => item.InvoiceId == invoice.Id)
                           .FirstOrDefaultAsync()
                       ?? throw new InvalidOperationException("Backend chưa tạo hóa đơn xác nhận.");
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        var club = await GetClubAsync();
        var traineeProfile = await GetProfileAsync(invoice.TraineeUserId);
        var founderProfile = await GetProfileAsync(founder.Id);
        var trainingClass = await Database.FindAsync<TrainingClass>(invoice.ClassId);
        invoice.Status = InvoiceStatus.Paid;
        invoice.UpdatedAtUtc = DateTime.UtcNow;
        var receipt = new Receipt
        {
            Id = EntityId.New(),
            InvoiceId = invoice.Id,
            ReceiptNumber = $"CFC-{DateTime.Now:yyyyMMdd}-{invoice.Id[..6].ToUpperInvariant()}",
            TeamNameSnapshot = club.TeamName,
            TraineeNameSnapshot = traineeProfile.FullName,
            ClassNameSnapshot = trainingClass?.Name ?? "Lớp học",
            PeriodSnapshot = DomainText.TuitionCycle(invoice),
            AmountVndSnapshot = invoice.AmountVnd,
            ConfirmedByNameSnapshot = founderProfile.FullName,
            ConfirmedAtUtc = DateTime.UtcNow
        };
        await Database.RunInTransactionAsync(connection =>
        {
            connection.Update(invoice);
            connection.Insert(receipt);
        });

        await AddNotificationAsync(
            invoice.TraineeUserId,
            NotificationKind.TuitionConfirmed,
            "Học phí đã được xác nhận",
            $"{DomainText.TuitionCycle(invoice)} đã được Founder xác nhận từ phụ huynh. Bạn có thể xuất hóa đơn PDF.",
            invoice.Id,
            writeCloud: false);
        await AddAuditAsync(
            actorUserId,
            "ConfirmTuitionByFounder",
            nameof(TuitionInvoice),
            invoice.Id,
            receipt.ReceiptNumber,
            writeCloud: false);
        return receipt;
    }

    public async Task RejectTuitionProofAsync(
        string actorUserId,
        string invoiceId,
        string reason)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanApproveOperations(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền từ chối bill học phí.");
            await EnsureOnlineSnapshotAsync();
            var onlineInvoice = Online.Invoices.FirstOrDefault(item => item.Id == invoiceId)
                ?? throw new InvalidOperationException("Không tìm thấy học phí.");
            if (Online.User(onlineInvoice.TraineeUserId)?.IsTuitionSupported == true)
                throw new InvalidOperationException($"{DomainText.SupportedTraineeLabel} được miễn học phí, không cần xử lý bill.");
            var onlineProof = Online.PaymentProofs
                .Where(item => item.InvoiceId == invoiceId)
                .OrderByDescending(item => item.SubmittedAtUtc)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("Không có bill để yêu cầu tải lại.");
            try
            {
                await _cloudApi.PatchAsync(
                    $"tuition/proofs/{Uri.EscapeDataString(onlineProof.Id)}/review",
                    new { accepted = false, note = reason.Trim() },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
            onlineProof.IsAccepted = false;
            onlineProof.ReviewedByUserId = actorUserId;
            onlineProof.ReviewedAtUtc = DateTime.UtcNow;
            onlineInvoice.Status = InvoiceStatus.Rejected;
            onlineInvoice.UpdatedAtUtc = DateTime.UtcNow;
            return;
        }

        await InitializeAsync();
        var founder = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanApproveOperations(founder.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền từ chối bill học phí.");
        var invoice = await Database.FindAsync<TuitionInvoice>(invoiceId)
                      ?? throw new InvalidOperationException("Không tìm thấy học phí.");
        var trainee = await Database.FindAsync<UserAccount>(invoice.TraineeUserId);
        if (trainee?.IsTuitionSupported == true)
        {
            throw new InvalidOperationException(
                $"{DomainText.SupportedTraineeLabel} được miễn học phí, không cần xử lý bill.");
        }
        var proof = await Database.Table<PaymentProof>()
            .Where(item => item.InvoiceId == invoice.Id)
            .OrderByDescending(item => item.SubmittedAtUtc)
            .FirstOrDefaultAsync();
        if (proof is null)
        {
            throw new InvalidOperationException("Không có bill để yêu cầu tải lại.");
        }

        var cloudReviewed = false;
        if (_cloudOptions.IsConfigured)
        {
            await EnsureCloudWriteReadyAsync(actorUserId);
            try
            {
                await _cloudApi.PatchAsync(
                    $"tuition/proofs/{Uri.EscapeDataString(proof.Id)}/review",
                    new { accepted = false, note = reason.Trim() },
                    idempotencyKey: EntityId.New());
                cloudReviewed = true;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        proof.IsAccepted = false;
        proof.ReviewedByUserId = founder.Id;
        proof.ReviewedAtUtc = DateTime.UtcNow;
        proof.Note = string.IsNullOrWhiteSpace(reason)
            ? proof.Note
            : $"{proof.Note}\nPhản hồi: {reason.Trim()}".Trim();
        invoice.Status = InvoiceStatus.Rejected;
        invoice.UpdatedAtUtc = DateTime.UtcNow;
        await Database.RunInTransactionAsync(connection =>
        {
            connection.Update(proof);
            connection.Update(invoice);
        });

        await AddNotificationAsync(
            invoice.TraineeUserId,
            NotificationKind.TuitionRejected,
            "Cần tải lại bill học phí",
            string.IsNullOrWhiteSpace(reason)
                ? "Bill chưa đủ thông tin. Vui lòng tải lại."
                : reason.Trim(),
            invoice.Id,
            writeCloud: !cloudReviewed);
        await AddAuditAsync(actorUserId, "RejectTuitionProof", nameof(TuitionInvoice), invoice.Id, reason,
            writeCloud: !cloudReviewed);
        QueueCloudProjectionRefresh();
    }

    public async Task UpdateReceiptPdfPathAsync(string receiptId, string pdfPath)
    {
        if (IsOnline)
        {
            await EnsureOnlineSnapshotAsync();
            var onlineReceipt = Online.Receipts.FirstOrDefault(item => item.Id == receiptId)
                ?? throw new InvalidOperationException("Không tìm thấy hóa đơn.");
            try
            {
                var upload = await _cloudApi.UploadFileAsync(pdfPath, "receipt");
                await _cloudApi.PatchAsync(
                    $"receipts/{Uri.EscapeDataString(receiptId)}/pdf",
                    new { uploadId = upload.Id },
                    idempotencyKey: EntityId.New());
                onlineReceipt.PdfPath = pdfPath;
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var receipt = await Database.FindAsync<Receipt>(receiptId)
                      ?? throw new InvalidOperationException("Không tìm thấy hóa đơn.");
        receipt.PdfPath = pdfPath;
        if (_cloudOptions.IsConfigured)
        {
            try
            {
                var upload = await _cloudApi.UploadFileAsync(pdfPath, "receipt");
                await _cloudApi.PatchAsync(
                    $"receipts/{Uri.EscapeDataString(receiptId)}/pdf",
                    new { uploadId = upload.Id },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }
        await Database.UpdateAsync(receipt);
        QueueCloudProjectionRefresh();
    }

    public async Task<IReadOnlyList<SalaryRow>> GetSalariesAsync(
        string actorUserId,
        string? period = null)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (onlineActor.Role == UserRole.Trainee)
                throw new UnauthorizedAccessException("Học viên không có quyền xem lương.");
            await EnsureOnlineSnapshotAsync();
            var onlineSalaries = Online.CoachSalaries
                .Where(item => (onlineActor.Role != UserRole.Coach || item.CoachUserId == onlineActor.Id)
                               && (string.IsNullOrWhiteSpace(period) || item.Period == period))
                .ToList();
            var classNames = Online.ClassCoaches
                .Where(item => item.IsActive)
                .GroupBy(item => item.CoachUserId)
                .ToDictionary(
                    group => group.Key,
                    group => string.Join(", ", group.Select(item => Online.Class(item.ClassId)?.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(name => name, StringComparer.Ordinal)));
            return onlineSalaries
                .Select(item => new SalaryRow(
                    item,
                    Online.Profile(item.CoachUserId)?.FullName ?? "Huấn luyện viên",
                    classNames.GetValueOrDefault(item.CoachUserId) ?? "Chưa phân lớp",
                    Online.Profile(item.CoachUserId)?.CoachPosition ?? string.Empty))
                .OrderByDescending(item => item.Salary.Period)
                .ThenBy(item => item.CoachName)
                .ToList();
        }

        await InitializeAsync();
        var actor = await RequireUserAsync(actorUserId);
        if (actor.Role == UserRole.Trainee)
        {
            throw new UnauthorizedAccessException("Học viên không có quyền xem lương.");
        }

        var salaries = await Database.Table<CoachSalary>().ToListAsync();
        if (actor.Role == UserRole.Coach)
        {
            salaries = salaries.Where(item => item.CoachUserId == actor.Id).ToList();
        }

        if (!string.IsNullOrWhiteSpace(period))
        {
            salaries = salaries.Where(item => item.Period == period).ToList();
        }

        foreach (var salary in salaries)
        {
            await RecomputePendingSalaryAsync(salary);
        }

        var profiles = (await Database.Table<PersonProfile>().ToListAsync())
            .ToDictionary(item => item.UserId);
        var classes = (await Database.Table<TrainingClass>().ToListAsync())
            .ToDictionary(item => item.Id);
        var classNamesByCoach = (await Database.Table<ClassCoachAssignment>()
                .Where(item => item.IsActive)
                .ToListAsync())
            .GroupBy(item => item.CoachUserId)
            .ToDictionary(
                group => group.Key,
                group => string.Join(", ", group
                    .Select(item => classes.GetValueOrDefault(item.ClassId)?.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)));
        return salaries
            .Select(item => new SalaryRow(
                item,
                profiles.GetValueOrDefault(item.CoachUserId)?.FullName ?? "Huấn luyện viên",
                classNamesByCoach.GetValueOrDefault(item.CoachUserId) ?? "Chưa phân lớp",
                profiles.GetValueOrDefault(item.CoachUserId)?.CoachPosition ?? string.Empty))
            .OrderByDescending(item => item.Salary.Period)
            .ThenBy(item => item.CoachName)
            .ToList();
    }

    public async Task SaveSalaryAsync(
        string actorUserId,
        string salaryId,
        bool isPaid,
        string notes)
    {
        if (IsOnline)
        {
            var onlineActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.CanApproveOperations(onlineActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền duyệt lương.");
            await EnsureOnlineSnapshotAsync();
            var onlineSalary = Online.CoachSalaries.FirstOrDefault(item => item.Id == salaryId)
                ?? throw new InvalidOperationException("Không tìm thấy kỳ lương.");
            if (onlineSalary.Status == SalaryStatus.Paid && !isPaid)
                throw new InvalidOperationException("Kỳ lương đã thanh toán là snapshot và không thể chuyển lại thành chưa thanh toán.");
            try
            {
                await _cloudApi.PatchAsync(
                    $"salaries/{Uri.EscapeDataString(onlineSalary.Id)}",
                    new { isPaid, notes = notes.Trim() },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
            onlineSalary.Status = isPaid ? SalaryStatus.Paid : SalaryStatus.Pending;
            onlineSalary.Notes = notes.Trim();
            onlineSalary.PaidAtUtc = isPaid ? DateTime.UtcNow : onlineSalary.PaidAtUtc;
            onlineSalary.PaidByUserId = isPaid ? actorUserId : onlineSalary.PaidByUserId;
            onlineSalary.UpdatedAtUtc = DateTime.UtcNow;
            // The Worker creates the next pending salary box atomically when
            // a salary is marked paid. Refresh the projection so the detail
            // screen can show that new box immediately.
            if (isPaid)
            {
                await ReloadOnlineSnapshotAsync();
            }
            return;
        }

        await InitializeAsync();
        var salaryActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanApproveOperations(salaryActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền duyệt lương.");
        var salary = await Database.FindAsync<CoachSalary>(salaryId)
                     ?? throw new InvalidOperationException("Không tìm thấy kỳ lương.");

        if (salary.Status == SalaryStatus.Paid && !isPaid)
        {
            throw new InvalidOperationException(
                "Kỳ lương đã thanh toán là snapshot và không thể chuyển lại thành chưa thanh toán.");
        }

        await RecomputePendingSalaryAsync(salary);
        var wasPaid = salary.Status == SalaryStatus.Paid;
        salary.Status = isPaid ? SalaryStatus.Paid : SalaryStatus.Pending;
        if (isPaid && !wasPaid)
        {
            salary.PaidAtUtc = DateTime.UtcNow;
            salary.PaidByUserId = actorUserId;
        }

        salary.Notes = notes.Trim();
        salary.UpdatedAtUtc = DateTime.UtcNow;
        if (_cloudOptions.IsConfigured)
        {
            try
            {
                await _cloudApi.PatchAsync(
                    $"salaries/{Uri.EscapeDataString(salary.Id)}",
                    new { isPaid, notes = salary.Notes },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }
        await Database.UpdateAsync(salary);
        if (isPaid && !wasPaid)
        {
            await EnsureCoachSalaryForPeriodAsync(
                salary.CoachUserId,
                NextSalaryPeriodStart(salary.Period));
        }
        await AddAuditAsync(
            actorUserId,
            isPaid
                ? wasPaid ? "UpdatePaidSalaryNotes" : "MarkSalaryPaid"
                : "UpdatePendingSalaryNotes",
            nameof(CoachSalary),
            salary.Id,
            salary.AmountVnd.ToString(CultureInfo.InvariantCulture));
        QueueCloudProjectionRefresh();
    }

    private static DateTime NextSalaryPeriodStart(string period)
    {
        return DateTime.TryParseExact(
                period,
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
            ? parsed.AddMonths(1)
            : new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1);
    }

    private async Task<CoachSalary> EnsureCoachSalaryForPeriodAsync(
        string coachUserId,
        DateTime sessionDate)
    {
        var period = sessionDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var salary = await Database.Table<CoachSalary>()
            .Where(item => item.CoachUserId == coachUserId && item.Period == period)
            .FirstOrDefaultAsync();
        if (salary is null)
        {
            salary = new CoachSalary
            {
                Id = EntityId.New(),
                CoachUserId = coachUserId,
                Period = period,
                DueDate = SalaryDueDate(DateTime.Now),
                Status = SalaryStatus.Pending,
                UpdatedAtUtc = DateTime.UtcNow
            };
            salary.AmountVnd = await CalculateCoachSalaryAmountAsync(coachUserId, period);
            await Database.InsertAsync(salary);
            return salary;
        }

        await RecomputePendingSalaryAsync(salary);
        return salary;
    }

    private async Task RecomputePendingSalaryAsync(CoachSalary salary)
    {
        if (salary.Status == SalaryStatus.Paid)
        {
            return;
        }

        var calculatedAmount = await CalculateCoachSalaryAmountAsync(
            salary.CoachUserId,
            salary.Period);
        if (salary.AmountVnd == calculatedAmount)
        {
            return;
        }

        salary.AmountVnd = calculatedAmount;
        salary.UpdatedAtUtc = DateTime.UtcNow;
        await Database.UpdateAsync(salary);
    }

    private async Task<long> CalculateCoachSalaryAmountAsync(
        string coachUserId,
        string period)
    {
        if (!DateTime.TryParseExact(
                $"{period}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var periodStart))
        {
            throw new InvalidOperationException("Kỳ lương không hợp lệ.");
        }

        var periodEnd = periodStart.AddMonths(1);
        var sessions = await Database.Table<TrainingSession>()
            .Where(item => item.SessionDate >= periodStart
                           && item.SessionDate < periodEnd)
            .ToListAsync();
        if (sessions.Count == 0)
        {
            return 0;
        }

        var sessionMap = sessions.ToDictionary(item => item.Id);
        var checkIns = await Database.Table<CoachCheckIn>()
            .Where(item => item.CoachUserId == coachUserId)
            .ToListAsync();
        var assignments = await Database.Table<ClassCoachAssignment>()
            .Where(item => item.CoachUserId == coachUserId)
            .ToListAsync();
        var rateByClass = assignments.ToDictionary(
            item => item.ClassId,
            item => Math.Max(0, item.SalaryPerSessionVnd));

        long total = 0;
        foreach (var checkIn in checkIns)
        {
            if (checkIn.ApprovalStatus != CoachCheckInApprovalStatus.Approved
                || !CoachCheckInTime.HasCoachCheckout(checkIn)
                || CoachCheckInTime.IsFounderSubstitution(checkIn)
                || !sessionMap.TryGetValue(checkIn.SessionId, out var session)
                || !rateByClass.TryGetValue(session.ClassId, out var currentRate))
            {
                continue;
            }

            var rate = checkIn.SalaryPerSessionVndSnapshot > 0
                ? checkIn.SalaryPerSessionVndSnapshot
                : currentRate;
            total = checked(total + rate);
        }

        return total;
    }

    private async Task EnsureSalaryRowsForExistingCheckInsAsync()
    {
        var checkIns = await Database.Table<CoachCheckIn>().ToListAsync();
        if (checkIns.Count == 0)
        {
            return;
        }

        var sessions = (await Database.Table<TrainingSession>().ToListAsync())
            .ToDictionary(item => item.Id);
        var assignmentKeys = (await Database.Table<ClassCoachAssignment>().ToListAsync())
            .Select(item => $"{item.CoachUserId}\u001F{item.ClassId}")
            .ToHashSet(StringComparer.Ordinal);
        var salaries = await Database.Table<CoachSalary>().ToListAsync();
        foreach (var salary in salaries.Where(item => item.Status == SalaryStatus.Pending))
        {
            await RecomputePendingSalaryAsync(salary);
        }
        var salaryKeys = salaries
            .Select(item => $"{item.CoachUserId}\u001F{item.Period}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var checkIn in checkIns.Where(
                     item => item.ApprovalStatus == CoachCheckInApprovalStatus.Approved
                             && CoachCheckInTime.HasCoachCheckout(item)))
        {
            if (!sessions.TryGetValue(checkIn.SessionId, out var session)
                || !assignmentKeys.Contains(
                    $"{checkIn.CoachUserId}\u001F{session.ClassId}"))
            {
                continue;
            }

            var period = session.SessionDate.ToString(
                "yyyy-MM",
                CultureInfo.InvariantCulture);
            var key = $"{checkIn.CoachUserId}\u001F{period}";
            if (!salaryKeys.Add(key))
            {
                continue;
            }

            var salary = new CoachSalary
            {
                Id = EntityId.New(),
                CoachUserId = checkIn.CoachUserId,
                Period = period,
                DueDate = SalaryDueDate(
                    (checkIn.ReviewedAtUtc ?? DateTime.UtcNow).ToLocalTime()),
                Status = SalaryStatus.Pending,
                UpdatedAtUtc = DateTime.UtcNow
            };
            salary.AmountVnd = await CalculateCoachSalaryAmountAsync(
                salary.CoachUserId,
                salary.Period);
            await Database.InsertAsync(salary);
        }
    }

    private static DateTime SalaryDueDate(DateTime confirmationLocal)
    {
        var local = confirmationLocal.Kind == DateTimeKind.Utc
            ? confirmationLocal.ToLocalTime()
            : confirmationLocal;
        var month = new DateTime(local.Year, local.Month, 1);
        return (local.Day > 10 ? month.AddMonths(1) : month).AddDays(9);
    }

    public async Task<IReadOnlyList<AppNotification>> GetNotificationsAsync(string actorUserId)
    {
        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            // Notifications are generated by Worker-side evaluation and
            // attendance mutations. Refresh this projection whenever the
            // page opens so another account's action appears immediately,
            // even when the current session already loaded its first snapshot.
            await ReloadOnlineSnapshotAsync();
            return Online.Notifications
                .Where(item => item.RecipientUserId == actorUserId)
                .OrderByDescending(item => item.CreatedAtUtc)
                .ToList();
        }

        await InitializeAsync();
        await RequireUserAsync(actorUserId);
        var notifications = await Database.Table<AppNotification>()
            .Where(item => item.RecipientUserId == actorUserId)
            .ToListAsync();
        return notifications.OrderByDescending(item => item.CreatedAtUtc).ToList();
    }

    public async Task MarkNotificationReadAsync(string actorUserId, string notificationId)
    {
        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            var onlineNotification = Online.Notifications.FirstOrDefault(item => item.Id == notificationId)
                ?? throw new InvalidOperationException("Không tìm thấy thông báo.");
            if (onlineNotification.RecipientUserId != actorUserId)
                throw new UnauthorizedAccessException("Bạn không có quyền đọc thông báo này.");
            try
            {
                await _cloudApi.PatchAsync(
                    $"notifications/{Uri.EscapeDataString(notificationId)}/read",
                    new { },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
            onlineNotification.IsRead = true;
            return;
        }

        await InitializeAsync();
        var notification = await Database.FindAsync<AppNotification>(notificationId)
                           ?? throw new InvalidOperationException("Không tìm thấy thông báo.");
        if (notification.RecipientUserId != actorUserId)
        {
            throw new UnauthorizedAccessException("Bạn không có quyền đọc thông báo này.");
        }

        notification.IsRead = true;
        if (_cloudOptions.IsConfigured)
        {
            try
            {
                await _cloudApi.PatchAsync(
                    $"notifications/{Uri.EscapeDataString(notificationId)}/read",
                    new { },
                    idempotencyKey: EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }
        await Database.UpdateAsync(notification);
        QueueCloudProjectionRefresh();
    }

    public async Task MarkAllNotificationsReadAsync(string actorUserId)
    {
        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            try
            {
                await _cloudApi.MarkAllNotificationsReadAsync();
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
            foreach (var notification in Online.Notifications.Where(item => item.RecipientUserId == actorUserId))
                notification.IsRead = true;
            return;
        }

        await InitializeAsync();
        await RequireUserAsync(actorUserId);
        await Database.ExecuteAsync(
            "UPDATE AppNotifications SET IsRead = 1 WHERE RecipientUserId = ?",
            actorUserId);
        if (_cloudOptions.IsConfigured)
        {
            try
            {
                await _cloudApi.MarkAllNotificationsReadAsync();
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }
    }

    public async Task DeleteAllNotificationsAsync(string actorUserId)
    {
        if (IsOnline)
        {
            await RequireOnlineUserAsync(actorUserId);
            await EnsureOnlineSnapshotAsync();
            try
            {
                await _cloudApi.DeleteAllNotificationsAsync();
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
            Online.Remove(Online.Notifications, item => item.RecipientUserId == actorUserId);
            return;
        }

        await InitializeAsync();
        await RequireUserAsync(actorUserId);
        await Database.ExecuteAsync(
            "DELETE FROM AppNotifications WHERE RecipientUserId = ?",
            actorUserId);
        if (_cloudOptions.IsConfigured)
        {
            try
            {
                await _cloudApi.DeleteAllNotificationsAsync();
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }
    }

    public async Task SendAnnouncementAsync(
        string actorUserId,
        string? traineeUserId,
        string title,
        string message,
        UserRole recipientRole = UserRole.Trainee)
    {
        if (recipientRole is not (UserRole.Coach or UserRole.Trainee))
        {
            throw new ArgumentOutOfRangeException(nameof(recipientRole));
        }

        if (IsOnline)
        {
            var announcementActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(announcementActor.Role))
                throw new UnauthorizedAccessException("Chỉ Sáng lập hoặc Đồng Sáng lập được gửi thông báo.");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
                throw new InvalidOperationException("Vui lòng nhập tiêu đề và nội dung.");
            try
            {
                await _cloudApi.PostAsync<object, object>(
                    "notifications/announcement",
                    new
                    {
                        traineeUserId,
                        recipientRole = recipientRole == UserRole.Coach ? "coach" : "trainee",
                        title = title.Trim(),
                        message = message.Trim()
                    },
                    EntityId.New());
                await ReloadOnlineSnapshotAsync();
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        await InitializeAsync();
        var announcementLocalActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(announcementLocalActor.Role))
            throw new UnauthorizedAccessException("Chỉ Sáng lập hoặc Đồng Sáng lập được gửi thông báo.");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            throw new InvalidOperationException("Vui lòng nhập tiêu đề và nội dung.");
        }

        var trainees = await Database.Table<UserAccount>()
            .Where(item => item.Role == recipientRole && item.IsActive)
            .ToListAsync();
        if (!string.IsNullOrWhiteSpace(traineeUserId))
        {
            trainees = trainees.Where(item => item.Id == traineeUserId).ToList();
        }

        if (trainees.Count == 0)
        {
            throw new InvalidOperationException("Không có học viên phù hợp để gửi.");
        }

        if (_cloudOptions.IsConfigured)
        {
            try
            {
                await _cloudApi.PostAsync<object, object>(
                    "notifications/announcement",
                    new
                    {
                        traineeUserId,
                        recipientRole = recipientRole == UserRole.Coach ? "coach" : "trainee",
                        title = title.Trim(),
                        message = message.Trim()
                    },
                    EntityId.New());
                return;
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        foreach (var trainee in trainees)
        {
            await AddNotificationAsync(
                trainee.Id,
                NotificationKind.Announcement,
                title.Trim(),
                message.Trim(),
                string.Empty);
        }

        await AddAuditAsync(
            actorUserId,
            "SendAnnouncement",
            nameof(AppNotification),
            traineeUserId ?? (recipientRole == UserRole.Coach ? "ALL_COACHES" : "ALL_TRAINEES"),
            title.Trim());
    }

    public async Task<DashboardMetrics> GetDashboardMetricsAsync(string actorUserId)
    {
        if (IsOnline)
        {
            var dashboardActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(dashboardActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền xem tổng quan Founder.");
            await EnsureOnlineSnapshotAsync();
            var supported = Online.Users.Where(item => item.IsTuitionSupported).Select(item => item.Id).ToHashSet();
            var onlineInvoices = Online.Invoices;
            return new DashboardMetrics(
                Online.Classes.Count(item => item.IsActive),
                Online.Users.Count(item => item.Role == UserRole.Coach && item.IsActive),
                Online.Users.Count(item => item.Role == UserRole.Trainee && item.IsActive),
                onlineInvoices.Count(item => !supported.Contains(item.TraineeUserId) && item.Status == InvoiceStatus.ProofSubmitted),
                onlineInvoices.Count(item => !supported.Contains(item.TraineeUserId) && item.Status != InvoiceStatus.Paid),
                Online.CoachSalaries.Count(item => item.Status == SalaryStatus.Pending && item.DueDate.Date <= DateTime.Today),
                Online.CoachCheckIns.Count(item => item.ApprovalStatus == CoachCheckInApprovalStatus.Pending
                                                   && CoachCheckInTime.HasCoachCheckout(item)));
        }

        await InitializeAsync();
        var dashboardLocalActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(dashboardLocalActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem tổng quan Founder.");
        var classes = await Database.Table<TrainingClass>()
            .Where(item => item.IsActive)
            .CountAsync();
        var coaches = await Database.Table<UserAccount>()
            .Where(item => item.Role == UserRole.Coach && item.IsActive)
            .CountAsync();
        var trainees = await Database.Table<UserAccount>()
            .Where(item => item.Role == UserRole.Trainee && item.IsActive)
            .CountAsync();
        var supportedTraineeIds = await GetSupportedTraineeIdsAsync();
        var invoices = await Database.Table<TuitionInvoice>().ToListAsync();
        var pendingProofs = invoices.Count(item =>
            !supportedTraineeIds.Contains(item.TraineeUserId)
            && item.Status == InvoiceStatus.ProofSubmitted);
        var unpaid = invoices.Count(item =>
            !supportedTraineeIds.Contains(item.TraineeUserId)
            && item.Status != InvoiceStatus.Paid);
        var overdueSalaries = await Database.Table<CoachSalary>()
            .Where(item => item.Status == SalaryStatus.Pending && item.DueDate <= DateTime.Today)
            .CountAsync();
        var pendingCoachCheckOuts = (await Database.Table<CoachCheckIn>().ToListAsync())
            .Count(item => item.ApprovalStatus == CoachCheckInApprovalStatus.Pending
                           && CoachCheckInTime.HasCoachCheckout(item));

        return new DashboardMetrics(
            classes,
            coaches,
            trainees,
            pendingProofs,
            unpaid,
            overdueSalaries,
            pendingCoachCheckOuts);
    }

    public async Task<IReadOnlyList<AuditLog>> GetAuditLogsAsync(
        string actorUserId,
        int limit = 100)
    {
        if (IsOnline)
        {
            var auditActor = await RequireOnlineUserAsync(actorUserId);
            if (!RoleCapabilities.IsFounderLike(auditActor.Role))
                throw new UnauthorizedAccessException("Tài khoản không có quyền xem lịch sử thao tác.");
            await EnsureOnlineSnapshotAsync();
            return Online.AuditLogs
                .OrderByDescending(item => item.CreatedAtUtc)
                .Take(limit)
                .ToList();
        }

        await InitializeAsync();
        var auditLocalActor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.IsFounderLike(auditLocalActor.Role))
            throw new UnauthorizedAccessException("Tài khoản không có quyền xem lịch sử thao tác.");
        var logs = await Database.Table<AuditLog>().ToListAsync();
        return logs.OrderByDescending(item => item.CreatedAtUtc).Take(limit).ToList();
    }

    private async Task EnsureClassAccessAsync(
        UserAccount actor,
        string classId,
        bool writeAttendance)
    {
        var trainingClass = await Database.FindAsync<TrainingClass>(classId);
        if (trainingClass is null)
        {
            throw new InvalidOperationException("Không tìm thấy lớp.");
        }

        if (RoleCapabilities.IsFounderLike(actor.Role))
        {
            return;
        }

        if (actor.Role == UserRole.Coach)
        {
            var assigned = await Database.Table<ClassCoachAssignment>()
                .Where(item => item.ClassId == classId
                               && item.CoachUserId == actor.Id
                               && item.IsActive)
                .CountAsync();
            if (assigned > 0)
            {
                return;
            }
        }

        if (!writeAttendance && actor.Role == UserRole.Trainee)
        {
            var enrolled = await Database.Table<ClassEnrollment>()
                .Where(item => item.ClassId == classId
                               && item.TraineeUserId == actor.Id
                               && item.IsActive)
                .CountAsync();
            if (enrolled > 0)
            {
                return;
            }
        }

        throw new UnauthorizedAccessException("Bạn không có quyền truy cập lớp này.");
    }

    private void EnsureOnlineClassAccess(UserAccount actor, string classId)
    {
        if (Online.Class(classId) is null)
            throw new InvalidOperationException("Không tìm thấy lớp.");
        if (RoleCapabilities.IsFounderLike(actor.Role)) return;
        if (actor.Role == UserRole.Coach
            && Online.ClassCoaches.Any(item => item.ClassId == classId
                                               && item.CoachUserId == actor.Id
                                               && item.IsActive))
            return;
        if (actor.Role == UserRole.Trainee
            && Online.ClassEnrollments.Any(item => item.ClassId == classId
                                                   && item.TraineeUserId == actor.Id
                                                   && item.IsActive))
            return;
        throw new UnauthorizedAccessException("Bạn không có quyền truy cập lớp này.");
    }

    private async Task<UserAccount> RequireUserAsync(string userId)
    {
        var user = await Database.FindAsync<UserAccount>(userId)
                   ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ.");
        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("Tài khoản đang bị khóa.");
        }

        return user;
    }

    private async Task<UserAccount> RequireRoleAsync(string userId, UserRole role)
    {
        var user = await RequireUserAsync(userId);
        if (user.Role != role)
        {
            throw new UnauthorizedAccessException("Tài khoản không có quyền thực hiện thao tác này.");
        }

        return user;
    }

    private async Task AddNotificationAsync(
        string recipientUserId,
        NotificationKind kind,
        string title,
        string message,
        string relatedEntityId,
        bool writeCloud = true)
    {
        var notification = new AppNotification
        {
            Id = EntityId.New(),
            RecipientUserId = recipientUserId,
            Kind = kind,
            Title = title,
            Message = message,
            RelatedEntityId = relatedEntityId,
            CreatedAtUtc = DateTime.UtcNow
        };
        if (writeCloud && _cloudOptions.IsConfigured)
        {
            try
            {
                var remote = await _cloudApi.PostAsync<object, CloudUploadResponse>(
                    "notifications",
                    new
                    {
                        recipientUserId,
                        kind = kind.ToString(),
                        title,
                        message,
                        relatedEntityId
                    },
                    EntityId.New());
                if (!string.IsNullOrWhiteSpace(remote.Id))
                {
                    notification.Id = remote.Id;
                }
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        if (IsOnline)
        {
            Online.Upsert(Online.Notifications, notification, item => item.Id == notification.Id);
            return;
        }

        await Database.InsertAsync(notification);
    }

    private async Task<bool> NotificationExistsAsync(
        string recipientUserId,
        NotificationKind kind,
        string relatedEntityId)
    {
        if (IsOnline)
        {
            await EnsureOnlineSnapshotAsync();
            return Online.Notifications.Any(item => item.RecipientUserId == recipientUserId
                                                    && item.Kind == kind
                                                    && item.RelatedEntityId == relatedEntityId);
        }

        return await Database.Table<AppNotification>()
            .Where(item => item.RecipientUserId == recipientUserId
                           && item.Kind == kind
                           && item.RelatedEntityId == relatedEntityId)
            .CountAsync() > 0;
    }

    private async Task AddAuditAsync(
        string actorUserId,
        string action,
        string entityType,
        string entityId,
        string details,
        bool writeCloud = true)
    {
        if (writeCloud && _cloudOptions.IsConfigured)
        {
            try
            {
                await _cloudApi.PostAsync(
                    "audit",
                    new { action, entityType, entityId, details },
                    EntityId.New());
            }
            catch (ApiException exception)
            {
                throw CloudOperationException(exception);
            }
        }

        if (IsOnline)
        {
            Online.Upsert(Online.AuditLogs, new AuditLog
            {
                Id = EntityId.New(),
                ActorUserId = actorUserId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = details,
                CreatedAtUtc = DateTime.UtcNow
            }, item => item.EntityId == entityId
                       && item.Action == action
                       && item.ActorUserId == actorUserId);
            return;
        }

        await Database.InsertAsync(new AuditLog
        {
            Id = EntityId.New(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private Task<UserAccount> CacheCloudIdentityAsync(
        CloudAuthResponse response,
        bool updateClub = true)
    {
        var cloudUser = response.User
                        ?? throw new InvalidOperationException(
                            "Máy chủ không trả về thông tin tài khoản.");
        return CacheCloudIdentityAsync(
            cloudUser,
            response.Profile,
            response.ActiveClub ?? response.Club,
            updateClub);
    }

    /// <summary>
    /// Persists Founder-managed structural data to D1 immediately after the
    /// local cache is updated.  This is deliberately a small delta rather
    /// than a full snapshot: sensitive route-only collections (selfies,
    /// payment proofs, receipts and audits) must use their dedicated APIs,
    /// and the Worker limits each sync request to 100 changes.
    /// </summary>
    private async Task PushCloudMutationAsync(
        string actorUserId,
        IEnumerable<Venue>? venues = null,
        IEnumerable<TrainingClass>? classes = null,
        IEnumerable<ClassCoachAssignment>? classCoaches = null,
        IEnumerable<ClassEnrollment>? classEnrollments = null)
    {
        if (!_cloudOptions.IsConfigured)
        {
            return;
        }

        await EnsureCloudWriteReadyAsync(actorUserId);

        var actor = await RequireUserAsync(actorUserId);
        if (!RoleCapabilities.CanCreateClasses(actor.Role))
        {
            return;
        }

        var snapshot = CloudSnapshotMapper.Export(new CloudSnapshotEntityCollections
        {
            TenantId = actor.TenantId,
            Role = actor.Role,
            Venues = venues?.ToArray() ?? [],
            Classes = classes?.ToArray() ?? [],
            ClassCoaches = classCoaches?.ToArray() ?? [],
            ClassEnrollments = classEnrollments?.ToArray() ?? []
        });

        try
        {
            await _cloudApi.PutSnapshotAsync(snapshot, EntityId.New());
            // The local projection was updated before this delta was sent.
            // Do not download the whole tenant snapshot here: the Worker has
            // already committed the delta, and the next online read/login will
            // reconcile server-owned rows such as invoices.  This keeps a
            // normal venue/class mutation to one D1 write round-trip.
            QueueCloudProjectionRefresh();
        }
        catch (ApiException exception)
        {
            throw CloudOperationException(exception);
        }
    }

    private async Task PushOnlineDeltaAsync(
        UserAccount actor,
        IEnumerable<Venue>? venues = null,
        IEnumerable<TrainingClass>? classes = null,
        IEnumerable<ClassCoachAssignment>? classCoaches = null,
        IEnumerable<ClassEnrollment>? classEnrollments = null,
        IEnumerable<TrainingSession>? trainingSessions = null,
        IEnumerable<SessionCoachAssignment>? sessionCoaches = null,
        IEnumerable<AttendanceRecord>? attendanceRecords = null,
        IEnumerable<CoachSalary>? coachSalaries = null)
    {
        if (!IsOnline) return;
        var delta = CloudSnapshotMapper.Export(new CloudSnapshotEntityCollections
        {
            TenantId = actor.TenantId,
            Role = actor.Role,
            Venues = venues?.ToArray() ?? [],
            Classes = classes?.ToArray() ?? [],
            ClassCoaches = classCoaches?.ToArray() ?? [],
            ClassEnrollments = classEnrollments?.ToArray() ?? [],
            TrainingSessions = trainingSessions?.ToArray() ?? [],
            SessionCoaches = sessionCoaches?.ToArray() ?? [],
            AttendanceRecords = attendanceRecords?.ToArray() ?? [],
            CoachSalaries = coachSalaries?.ToArray() ?? []
        });
        try
        {
            await _cloudApi.PutSnapshotAsync(delta, EntityId.New());
        }
        catch (ApiException exception)
        {
            throw CloudOperationException(exception);
        }
    }

    private async Task RefreshCloudProjectionAsync()
    {
        if (!_cloudOptions.IsConfigured)
        {
            return;
        }

        var wireSnapshot = await _cloudApi.GetSnapshotAsync();
        ApplyCloudSnapshot(CloudSnapshotMapper.Import(wireSnapshot));
    }

    /// <summary>
    /// Reconciles the local compatibility projection after an online write
    /// without making the user wait for a second full snapshot request.  A
    /// single coalesced refresh handles a burst of mutations (for example a
    /// class with many enrollments) and the catch keeps a transient refresh
    /// failure from turning a successful server mutation into a UI error.
    /// </summary>
    private void QueueCloudProjectionRefresh()
    {
        if (!_cloudOptions.IsConfigured
            || Interlocked.Exchange(ref _cloudProjectionRefreshQueued, 1) != 0)
        {
            return;
        }

        // D1 is authoritative.  Do not immediately download another full
        // tenant snapshot after a successful write; the mutation has already
        // updated the relevant in-memory row.  Marking the projection stale
        // makes the next screen load reconcile once, rather than making every
        // tap wait on a second full read.
        Online.InvalidateData();
        Interlocked.Exchange(ref _cloudProjectionRefreshQueued, 0);
    }

    private async Task EnsureCloudWriteReadyAsync(string actorUserId)
    {
        if (_cloudOptions.IsConfigured
            && !await HasCloudSessionForAsync(actorUserId))
        {
            throw new InvalidOperationException(
                "Account Founder chưa có phiên Cloudflare hợp lệ. Vui lòng đăng xuất và đăng nhập lại online trước khi lưu Sân hoặc Lớp học.");
        }
    }

    /// <summary>
    /// Reuses a projection that belongs to the same online identity and lets
    /// the coalesced background pull reconcile it with D1.  A different user
    /// or tenant still takes the full replacement path, so an account switch
    /// can never see another team's cache.
    /// </summary>
    private async Task<UserAccount?> UseCachedCloudProjectionIfSafeAsync(CloudAuthResponse response)
    {
        var cloudUser = response.User;
        if (cloudUser is null || cloudUser.Role == UserRole.Admin || string.IsNullOrWhiteSpace(cloudUser.TenantId))
        {
            return null;
        }

        if (IsOnline)
        {
            var cachedOnline = Online.User(cloudUser.Id);
            if (cachedOnline is null
                || !string.Equals(cachedOnline.TenantId, cloudUser.TenantId, StringComparison.Ordinal))
            {
                return null;
            }

            return await CacheCloudIdentityAsync(response);
        }

        var cached = await Database.FindAsync<UserAccount>(cloudUser.Id);
        if (cached is null || !string.Equals(cached.TenantId, cloudUser.TenantId, StringComparison.Ordinal))
        {
            return null;
        }

        var user = await CacheCloudIdentityAsync(response);
        QueueCloudProjectionRefresh();
        return user;
    }

    /// <summary>
    /// Downloads and validates the authoritative tenant snapshot before the
    /// previous on-device projection is replaced. Online sessions never fall
    /// back to stale SQLite data. System Admin intentionally has no team
    /// snapshot and only caches its public identity.
    /// </summary>
    private async Task<UserAccount> ReplaceCloudProjectionFromRequiredSnapshotAsync(
        CloudAuthResponse response)
    {
        var cloudUser = response.User
                        ?? throw new InvalidOperationException(
                            "Máy chủ không trả về thông tin tài khoản.");

        if (cloudUser.Role == UserRole.Admin)
        {
            await ResetCloudOperationalCacheAsync(cloudUser.Id);
            return await CacheCloudIdentityAsync(response);
        }

        // Download and deserialize first. If the network or schema is invalid,
        // keep the previous projection untouched and fail the online login.
        var wireSnapshot = await _cloudApi.GetSnapshotAsync();
        var imported = CloudSnapshotMapper.Import(wireSnapshot);
        var expectedTenantId = cloudUser.TenantId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(expectedTenantId)
            || !string.Equals(imported.TenantId, expectedTenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Snapshot không thuộc đúng đội bóng của tài khoản đang đăng nhập.");
        }

        await ResetCloudOperationalCacheAsync(cloudUser.Id);
        await ApplyCloudSnapshotAsync(imported);
        var cachedUser = await CacheCloudIdentityAsync(response);
        await VerifyCloudProjectionAsync(imported);
        return cachedUser;
    }

    private async Task VerifyCloudProjectionAsync(CloudSnapshotEntityCollections snapshot)
    {
        if (IsOnline)
        {
            // The online projection is already the object being verified.  A
            // SQLite row-count comparison would reintroduce the cache we just
            // removed from the production path.
            if (!Online.IsLoaded || !string.Equals(Online.TenantId, snapshot.TenantId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Không thể dựng dữ liệu online chính xác: projection không hợp lệ.");
            }

            return;
        }

        var mismatches = new List<string>();

        async Task CheckAsync<T>(string label, int expected) where T : new()
        {
            var actual = await Database.Table<T>().CountAsync();
            if (actual != expected)
            {
                mismatches.Add($"{label}: server={expected}, device={actual}");
            }
        }

        await CheckAsync<Venue>("Sân dạy", snapshot.Venues.Count);
        await CheckAsync<TrainingClass>("Lớp học", snapshot.Classes.Count);
        await CheckAsync<ClassCoachAssignment>("Phân công Coach", snapshot.ClassCoaches.Count);
        await CheckAsync<ClassEnrollment>("Phân lớp Trainee", snapshot.ClassEnrollments.Count);
        await CheckAsync<TrainingSession>("Buổi học", snapshot.TrainingSessions.Count);
        await CheckAsync<AuditLog>("Lịch sử thao tác", snapshot.AuditLogs.Count);

        if (mismatches.Count > 0)
        {
            throw new InvalidOperationException(
                "Không thể dựng dữ liệu online chính xác: " + string.Join("; ", mismatches));
        }
    }

    private Task ApplyCloudSnapshotAsync(CloudSnapshotEntityCollections snapshot)
    {
        // Online mode is deliberately memory-only.  The server snapshot is
        // projected into the volatile state used by the views; no SQLite
        // transaction, file copy, or local verifier is created here.
        ApplyCloudSnapshot(snapshot);
        return Task.CompletedTask;
    }

    private void ApplyCloudSnapshot(CloudSnapshotEntityCollections snapshot)
    {
        Online.Replace(snapshot);
    }

    // Retained temporarily for the legacy offline migration path.  It is not
    // reachable while CloudBackendOptions is configured.
    private async Task ApplyCloudSnapshotLegacyAsync(CloudSnapshotEntityCollections snapshot)
    {
        await Database.RunInTransactionAsync(connection =>
        {
            foreach (var user in snapshot.Users)
            {
                // Cloud verifiers are intentionally absent from snapshots. Do
                // not replace an existing verifier for an offline-only row;
                // the current Cloud identity is already cleared by
                // CacheCloudIdentityAsync before this method runs.
                var existing = connection.Find<UserAccount>(user.Id);
                if (existing is not null && !string.IsNullOrWhiteSpace(existing.PasswordHash))
                {
                    user.PasswordHash = existing.PasswordHash;
                    user.PasswordSalt = existing.PasswordSalt;
                    user.PasswordIterations = existing.PasswordIterations;
                }

                connection.InsertOrReplace(user);
            }

            foreach (var profile in snapshot.Profiles)
            {
                connection.InsertOrReplace(profile);
            }

            if (snapshot.Club is not null)
            {
                connection.InsertOrReplace(snapshot.Club);
            }

            foreach (var venue in snapshot.Venues)
            {
                connection.InsertOrReplace(venue);
            }

            foreach (var trainingClass in snapshot.Classes)
            {
                connection.InsertOrReplace(trainingClass);
            }

            foreach (var assignment in snapshot.ClassCoaches)
            {
                connection.InsertOrReplace(assignment);
            }

            foreach (var enrollment in snapshot.ClassEnrollments)
            {
                connection.InsertOrReplace(enrollment);
            }

            foreach (var session in snapshot.TrainingSessions)
            {
                connection.InsertOrReplace(session);
            }

            foreach (var assignment in snapshot.SessionCoaches)
            {
                connection.InsertOrReplace(assignment);
            }

            foreach (var checkIn in snapshot.CoachCheckIns)
            {
                // Snapshot stores private R2 object keys, while the local UI
                // needs the still-present capture path for preview. Preserve
                // an existing device file during a projection refresh.
                var existing = connection.Find<CoachCheckIn>(checkIn.Id);
                if (existing is not null)
                {
                    if (File.Exists(existing.SelfiePath))
                    {
                        checkIn.SelfiePath = existing.SelfiePath;
                    }

                    if (File.Exists(existing.CheckOutSelfiePath))
                    {
                        checkIn.CheckOutSelfiePath = existing.CheckOutSelfiePath;
                    }
                }
                connection.InsertOrReplace(checkIn);
            }

            foreach (var attendance in snapshot.AttendanceRecords)
            {
                connection.InsertOrReplace(attendance);
            }

            foreach (var invoice in snapshot.TuitionInvoices)
            {
                connection.InsertOrReplace(invoice);
            }

            foreach (var proof in snapshot.PaymentProofs)
            {
                var existing = connection.Find<PaymentProof>(proof.Id);
                if (existing is not null && File.Exists(existing.ImagePath))
                {
                    proof.ImagePath = existing.ImagePath;
                }
                connection.InsertOrReplace(proof);
            }

            foreach (var receipt in snapshot.Receipts)
            {
                var existing = connection.Find<Receipt>(receipt.Id);
                if (existing is not null && File.Exists(existing.PdfPath))
                {
                    receipt.PdfPath = existing.PdfPath;
                }
                connection.InsertOrReplace(receipt);
            }

            foreach (var salary in snapshot.CoachSalaries)
            {
                connection.InsertOrReplace(salary);
            }

            foreach (var notification in snapshot.Notifications)
            {
                connection.InsertOrReplace(notification);
            }

            foreach (var audit in snapshot.AuditLogs)
            {
                connection.InsertOrReplace(audit);
            }
        });

        // Keep the singleton club identity in sync even when the server used
        // the `activeClub` alias rather than `club` in a future response.
        if (snapshot.ActiveClub is not null && snapshot.Club is null)
        {
            await Database.InsertOrReplaceAsync(snapshot.ActiveClub);
        }
    }

    /// <summary>
    /// SQLite is only a device cache.  Before accepting a new Cloud session,
    /// remove operational rows belonging to any previous team so singleton
    /// legacy entities (ClubProfile) and global local queries cannot mix two
    /// Founders.  The current account's Google link is retained because the
    /// API does not expose links in the snapshot; all authoritative domain
    /// data is reloaded from D1 immediately after this reset.
    /// </summary>
    private async Task ResetCloudOperationalCacheAsync(string? currentUserId)
    {
        if (IsOnline)
        {
            Online.Clear();
            return;
        }

        await Database.RunInTransactionAsync(connection =>
        {
            connection.Execute("DELETE FROM Venues");
            connection.Execute("DELETE FROM TrainingClasses");
            connection.Execute("DELETE FROM ClassCoachAssignments");
            connection.Execute("DELETE FROM ClassEnrollments");
            connection.Execute("DELETE FROM TrainingSessions");
            connection.Execute("DELETE FROM SessionCoachAssignments");
            connection.Execute("DELETE FROM CoachCheckIns");
            connection.Execute("DELETE FROM AttendanceRecords");
            connection.Execute("DELETE FROM TuitionInvoices");
            connection.Execute("DELETE FROM PaymentProofs");
            connection.Execute("DELETE FROM Receipts");
            connection.Execute("DELETE FROM CoachSalaries");
            connection.Execute("DELETE FROM TraineeEvaluations");
            connection.Execute("DELETE FROM AppNotifications");
            connection.Execute("DELETE FROM AuditLogs");

            // Keep only system-admin identities. The active Cloud identity and
            // the current team's members are written back by CacheCloudIdentity
            // and the tenant-scoped snapshot import below.
            connection.Execute(
                "DELETE FROM UserAccounts WHERE Role <> ?",
                (int)UserRole.Admin);
            connection.Execute(
                "DELETE FROM PersonProfiles WHERE UserId NOT IN "
                + "(SELECT Id FROM UserAccounts WHERE Role = ?)",
                (int)UserRole.Admin);

            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                connection.Execute("DELETE FROM ExternalAccountLinks");
            }
            else
            {
                connection.Execute(
                    "DELETE FROM ExternalAccountLinks WHERE UserId <> ?",
                    currentUserId);
            }

            connection.Execute("DELETE FROM ClubProfile");
            connection.Insert(new ClubProfile
            {
                Id = 1,
                TeamName = "Community Football Club",
                FounderName = "Sáng lập & Điều hành",
                UpdatedAtUtc = DateTime.UtcNow
            });
        });
    }

    private async Task<UserAccount> CacheCloudIdentityAsync(
        CloudUserSnapshot cloudUser,
        CloudProfileSnapshot? cloudProfile,
        CloudClubSnapshot? club,
        bool updateClub,
        bool profileIsComplete = true)
    {
        if (string.IsNullOrWhiteSpace(cloudUser.Id)
            || string.IsNullOrWhiteSpace(cloudUser.Username))
        {
            throw new InvalidOperationException(
                "Máy chủ trả về định danh tài khoản không hợp lệ.");
        }

        if (IsOnline)
        {
            var onlineUser = CloudSnapshotMapper.ToEntity(cloudUser);
            var onlineExistingProfile = Online.Profile(onlineUser.Id);
            var onlineProfile = cloudProfile is null
                ? onlineExistingProfile ?? new PersonProfile { UserId = onlineUser.Id, FullName = onlineUser.Username }
                : profileIsComplete
                    ? CloudSnapshotMapper.ToEntity(cloudProfile)
                    : new PersonProfile
                    {
                        UserId = onlineUser.Id,
                        FullName = string.IsNullOrWhiteSpace(cloudProfile.FullName)
                            ? onlineExistingProfile?.FullName ?? onlineUser.Username
                            : cloudProfile.FullName,
                        Email = string.IsNullOrWhiteSpace(cloudProfile.Email)
                            ? onlineExistingProfile?.Email ?? cloudUser.Email
                            : cloudProfile.Email,
                        Phone = onlineExistingProfile?.Phone ?? string.Empty,
                        DateOfBirth = onlineExistingProfile?.DateOfBirth,
                        HeightCm = onlineExistingProfile?.HeightCm ?? 0,
                        WeightKg = onlineExistingProfile?.WeightKg ?? 0,
                        GuardianName = onlineExistingProfile?.GuardianName ?? string.Empty,
                        GuardianPhone = onlineExistingProfile?.GuardianPhone ?? string.Empty,
                        UpdatedAtUtc = cloudProfile.UpdatedAt.UtcDateTime
                    };
            Online.Upsert(Online.Users, onlineUser, item => item.Id == onlineUser.Id);
            Online.Upsert(Online.Profiles, onlineProfile, item => item.UserId == onlineProfile.UserId);
            var activeClub = updateClub && club is not null
                ? CloudSnapshotMapper.ToEntity(club)
                : Online.Club;
            Online.SetIdentity(onlineUser, onlineProfile, activeClub);
            return onlineUser;
        }

        var usernameNormalized = Normalize(cloudUser.Username);
        var conflict = await Database.Table<UserAccount>()
            .Where(item => item.UsernameNormalized == usernameNormalized)
            .FirstOrDefaultAsync();
        if (conflict is not null
            && !string.Equals(conflict.Id, cloudUser.Id, StringComparison.Ordinal))
        {
            // Preserve legacy domain rows instead of deleting the old local ID,
            // while freeing the globally unique normalized username for Cloud.
            conflict.UsernameNormalized = $"__LEGACY__{conflict.Id}";
            conflict.IsActive = false;
            conflict.UpdatedAtUtc = DateTime.UtcNow;
            await Database.UpdateAsync(conflict);
        }

        var existing = await Database.FindAsync<UserAccount>(cloudUser.Id);
        var now = DateTime.UtcNow;
        var user = existing ?? new UserAccount { Id = cloudUser.Id };
        user.Username = cloudUser.Username.Trim();
        user.TenantId = cloudUser.TenantId ?? string.Empty;
        user.UsernameNormalized = usernameNormalized;
        user.EmailNormalized = NormalizeEmail(
            string.IsNullOrWhiteSpace(cloudUser.Email)
                ? cloudProfile?.Email ?? string.Empty
                : cloudUser.Email);
        user.Role = cloudUser.Role;
        user.IsActive = cloudUser.IsActive;
        user.IsTuitionSupported = cloudUser.IsTuitionSupported;
        user.MustChangePassword = cloudUser.MustChangePassword;
        user.FailedLoginCount = 0;
        user.LockoutUntilUtc = null;
        user.CreatedAtUtc = CloudUtcOrFallback(
            cloudUser.CreatedAt,
            existing?.CreatedAtUtc ?? now);
        user.UpdatedAtUtc = CloudUtcOrFallback(cloudUser.UpdatedAt, now);

        // Only public identity data is cached. A Cloud-authenticated account
        // must never retain a password verifier that could bypass the server.
        user.PasswordHash = string.Empty;
        user.PasswordSalt = string.Empty;
        user.PasswordIterations = 0;
        await Database.InsertOrReplaceAsync(user);

        var existingProfile = await Database.FindAsync<PersonProfile>(user.Id);
        var profile = existingProfile ?? new PersonProfile { UserId = user.Id };
        if (cloudProfile is not null)
        {
            profile.FullName = string.IsNullOrWhiteSpace(cloudProfile.FullName)
                ? profile.FullName.Length == 0 ? user.Username : profile.FullName
                : cloudProfile.FullName.Trim();
            profile.Email = string.IsNullOrWhiteSpace(cloudProfile.Email)
                ? cloudUser.Email
                : cloudProfile.Email;
            if (profileIsComplete)
            {
                profile.Phone = cloudProfile.Phone;
                profile.DateOfBirth = cloudProfile.DateOfBirth?.ToDateTime(TimeOnly.MinValue);
                profile.HeightCm = cloudProfile.HeightCm;
                profile.WeightKg = cloudProfile.WeightKg;
                profile.GuardianName = cloudProfile.GuardianName;
                profile.GuardianPhone = cloudProfile.GuardianPhone;
            }
        }
        else
        {
            profile.FullName = string.IsNullOrWhiteSpace(profile.FullName)
                ? user.Username
                : profile.FullName;
            profile.Email = string.IsNullOrWhiteSpace(profile.Email)
                ? cloudUser.Email
                : profile.Email;
        }
        // PhotoObjectKey belongs to R2 and is not a local file path. Keep an
        // already-downloaded local photo until media sync is implemented.
        profile.UpdatedAtUtc = cloudProfile is null
            ? user.UpdatedAtUtc
            : CloudUtcOrFallback(cloudProfile.UpdatedAt, user.UpdatedAtUtc);
        await Database.InsertOrReplaceAsync(profile);

        if (updateClub && club is not null)
        {
            var cachedClub = await Database.FindAsync<ClubProfile>(1)
                             ?? new ClubProfile { Id = 1 };
            cachedClub.TeamName = string.IsNullOrWhiteSpace(club.TeamName)
                ? cachedClub.TeamName
                : club.TeamName.Trim();
            cachedClub.Phone = club.Phone;
            cachedClub.Email = club.Email;
            cachedClub.BankName = club.BankName;
            cachedClub.BankBin = club.BankBin;
            cachedClub.BankAccountNumber = club.BankAccountNumber;
            cachedClub.BankAccountName = club.BankAccountName;
            if (user.Role == UserRole.Founder
                && !string.IsNullOrWhiteSpace(profile.FullName))
            {
                cachedClub.FounderName = profile.FullName;
            }

            // LogoObjectKey is likewise retained by Cloud; local UI still needs
            // a downloaded file path, which belongs to the later media sync.
            cachedClub.UpdatedAtUtc = CloudUtcOrFallback(club.UpdatedAt, now);
            await Database.InsertOrReplaceAsync(cachedClub);
        }

        return user;
    }

    private async Task<bool> HasCloudSessionForAsync(string userId)
    {
        if (!_cloudOptions.IsConfigured)
        {
            return false;
        }

        var session = await _cloudTokens.LoadRefreshSessionAsync();
        return session is not null
               && string.Equals(session.UserId, userId, StringComparison.Ordinal);
    }

    private static bool IsCloudBackedAccount(UserAccount user) =>
        string.IsNullOrWhiteSpace(user.PasswordHash)
        && string.IsNullOrWhiteSpace(user.PasswordSalt)
        && user.PasswordIterations == 0;

    private static DateTime CloudUtcOrFallback(
        DateTimeOffset value,
        DateTime fallback) =>
        value == default ? fallback : value.UtcDateTime;

    private static bool IsCloudUnavailable(Exception exception) =>
        exception is HttpRequestException
        or TaskCanceledException
        or TimeoutException
        || exception is ApiException
        {
            StatusCode: HttpStatusCode.NotFound
                or HttpStatusCode.RequestTimeout
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.InternalServerError
        };

    private static InvalidOperationException CloudOperationException(ApiException exception)
    {
        var trace = string.IsNullOrWhiteSpace(exception.TraceId)
            ? string.Empty
            : $" (Mã theo dõi: {exception.TraceId})";
        return new InvalidOperationException(exception.Message + trace, exception);
    }

    private static string CurrentDeviceName()
    {
        try
        {
            return string.IsNullOrWhiteSpace(DeviceInfo.Current.Name)
                ? $"{DeviceInfo.Current.Platform} app"
                : DeviceInfo.Current.Name;
        }
        catch
        {
            return "AWAKEN Community FCM";
        }
    }

    private static string Normalize(string value) =>
        value.Trim().ToUpperInvariant();

    private static string NormalizeEmail(string value) =>
        value.Trim().ToUpperInvariant();
}
