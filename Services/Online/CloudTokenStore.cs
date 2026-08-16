namespace CommunityFootballClubManager.Services.Online;

public sealed record CloudRefreshSession(
    string SessionId,
    string RefreshToken,
    DateTimeOffset? RefreshTokenExpiresAtUtc,
    string? UserId,
    string? TenantId);

/// <summary>
/// Persists only the rotating refresh session in platform SecureStorage.
/// The short-lived access token deliberately remains in process memory.
/// </summary>
public sealed class CloudTokenStore
{
    private const string SessionBundleKey = "cloud.auth.session.v2";
    private const string SessionIdKey = "cloud.auth.session_id.v1";
    private const string RefreshTokenKey = "cloud.auth.refresh_token.v1";
    private const string RefreshExpiryKey = "cloud.auth.refresh_expires_utc.v1";
    private const string UserIdKey = "cloud.auth.user_id.v1";
    private const string TenantIdKey = "cloud.auth.tenant_id.v1";

    private static readonly TimeSpan DefaultAccessTokenSkew = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _storageGate = new(1, 1);
    private readonly object _memoryGate = new();
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAtUtc;
    private CloudRefreshSession? _refreshSession;

    private sealed record StoredRefreshSession(
        string SessionId,
        string RefreshToken,
        DateTimeOffset? RefreshTokenExpiresAtUtc,
        string? UserId,
        string? TenantId);

    public string? GetValidAccessToken(TimeSpan? minimumLifetime = null)
    {
        lock (_memoryGate)
        {
            var skew = minimumLifetime ?? DefaultAccessTokenSkew;
            if (string.IsNullOrWhiteSpace(_accessToken)
                || _accessTokenExpiresAtUtc <= DateTimeOffset.UtcNow.Add(skew))
            {
                return null;
            }

            return _accessToken;
        }
    }

    public void InvalidateAccessToken()
    {
        lock (_memoryGate)
        {
            _accessToken = null;
            _accessTokenExpiresAtUtc = default;
        }
    }

    public async Task SaveAsync(
        CloudTokenResponse tokens,
        string? userId = null,
        string? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (string.IsNullOrWhiteSpace(tokens.AccessToken)
            || string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            throw new InvalidOperationException("Backend không trả về đầy đủ access token và refresh token.");
        }

        var accessExpiry = tokens.AccessTokenExpiresAtUtc
                           ?? DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, tokens.ExpiresIn));
        var sessionId = string.IsNullOrWhiteSpace(tokens.SessionId)
            ? ExtractSessionId(tokens.RefreshToken)
            : tokens.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("Backend không trả về session ID hợp lệ.");
        }

        await _storageGate.WaitAsync();
        try
        {
            var refreshSession = new CloudRefreshSession(
                sessionId,
                tokens.RefreshToken,
                tokens.RefreshTokenExpiresAtUtc,
                userId,
                tenantId);
            var stored = new StoredRefreshSession(
                refreshSession.SessionId,
                refreshSession.RefreshToken,
                refreshSession.RefreshTokenExpiresAtUtc,
                refreshSession.UserId,
                refreshSession.TenantId);
            // Android Keystore access is comparatively expensive. Persist one
            // authenticated JSON value instead of five sequential values.
            await SecureStorage.Default.SetAsync(
                SessionBundleKey,
                System.Text.Json.JsonSerializer.Serialize(stored));
            RemoveLegacyKeys();

            lock (_memoryGate)
            {
                _accessToken = tokens.AccessToken;
                _accessTokenExpiresAtUtc = accessExpiry;
                _refreshSession = refreshSession;
            }
        }
        catch
        {
            ClearCore();
            throw;
        }
        finally
        {
            _storageGate.Release();
        }
    }

    public async Task<CloudRefreshSession?> LoadRefreshSessionAsync()
    {
        await _storageGate.WaitAsync();
        try
        {
            return await LoadRefreshSessionCoreAsync();
        }
        catch
        {
            ClearCore();
            return null;
        }
        finally
        {
            _storageGate.Release();
        }
    }

    public bool HasRefreshSessionFor(string userId)
    {
        lock (_memoryGate)
        {
            return !string.IsNullOrWhiteSpace(userId)
                   && _refreshSession?.UserId == userId
                   && (_refreshSession.RefreshTokenExpiresAtUtc is null
                       || _refreshSession.RefreshTokenExpiresAtUtc > DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Atomically detaches the local refresh session before the login screen
    /// is shown. The returned token may be revoked in the background without
    /// risking deletion of a newly-created login session.
    /// </summary>
    public async Task<CloudRefreshSession?> TakeAndClearAsync()
    {
        await _storageGate.WaitAsync();
        try
        {
            var session = await LoadRefreshSessionCoreAsync();
            ClearCore();
            return session;
        }
        finally
        {
            _storageGate.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _storageGate.WaitAsync();
        try
        {
            ClearCore();
        }
        finally
        {
            _storageGate.Release();
        }
    }

    private async Task<CloudRefreshSession?> LoadRefreshSessionCoreAsync()
    {
        lock (_memoryGate)
        {
            if (_refreshSession is { } cached
                && (cached.RefreshTokenExpiresAtUtc is null
                    || cached.RefreshTokenExpiresAtUtc > DateTimeOffset.UtcNow))
            {
                return cached;
            }
        }

        var bundle = await SecureStorage.Default.GetAsync(SessionBundleKey);
        if (!string.IsNullOrWhiteSpace(bundle))
        {
            try
            {
                var stored = System.Text.Json.JsonSerializer.Deserialize<StoredRefreshSession>(bundle);
                if (stored is not null)
                {
                    return CacheValidatedSession(new CloudRefreshSession(
                        stored.SessionId,
                        stored.RefreshToken,
                        stored.RefreshTokenExpiresAtUtc,
                        stored.UserId,
                        stored.TenantId));
                }
            }
            catch (System.Text.Json.JsonException)
            {
                ClearCore();
                return null;
            }
        }

        // One-time compatibility migration from the five-value v1 layout.
        var sessionId = await SecureStorage.Default.GetAsync(SessionIdKey);
        var refreshToken = await SecureStorage.Default.GetAsync(RefreshTokenKey);
        var refreshExpiryText = await SecureStorage.Default.GetAsync(RefreshExpiryKey);
        var userId = await SecureStorage.Default.GetAsync(UserIdKey);
        var tenantId = await SecureStorage.Default.GetAsync(TenantIdKey);
        DateTimeOffset? refreshExpiry = null;
        DateTimeOffset parsed = default;
        if (!string.IsNullOrWhiteSpace(refreshExpiryText)
            && !DateTimeOffset.TryParse(
                refreshExpiryText,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out parsed))
        {
            ClearCore();
            return null;
        }
        else if (!string.IsNullOrWhiteSpace(refreshExpiryText))
        {
            refreshExpiry = parsed;
        }

        var migrated = CacheValidatedSession(new CloudRefreshSession(
            sessionId ?? string.Empty,
            refreshToken ?? string.Empty,
            refreshExpiry,
            string.IsNullOrWhiteSpace(userId) ? null : userId,
            string.IsNullOrWhiteSpace(tenantId) ? null : tenantId));
        if (migrated is not null)
        {
            await SecureStorage.Default.SetAsync(
                SessionBundleKey,
                System.Text.Json.JsonSerializer.Serialize(new StoredRefreshSession(
                    migrated.SessionId,
                    migrated.RefreshToken,
                    migrated.RefreshTokenExpiresAtUtc,
                    migrated.UserId,
                    migrated.TenantId)));
            RemoveLegacyKeys();
        }
        return migrated;
    }

    private CloudRefreshSession? CacheValidatedSession(CloudRefreshSession session)
    {
        if (string.IsNullOrWhiteSpace(session.SessionId)
            || string.IsNullOrWhiteSpace(session.RefreshToken)
            || session.RefreshTokenExpiresAtUtc is { } expiresAt
            && expiresAt <= DateTimeOffset.UtcNow)
        {
            ClearCore();
            return null;
        }
        lock (_memoryGate)
        {
            _refreshSession = session;
        }
        return session;
    }

    private void ClearCore()
    {
        TryRemove(SessionBundleKey);
        RemoveLegacyKeys();
        lock (_memoryGate)
        {
            _refreshSession = null;
        }
        InvalidateAccessToken();
    }

    private static void RemoveLegacyKeys()
    {
        TryRemove(SessionIdKey);
        TryRemove(RefreshTokenKey);
        TryRemove(RefreshExpiryKey);
        TryRemove(UserIdKey);
        TryRemove(TenantIdKey);
    }

    private static void TryRemove(string key)
    {
        try
        {
            SecureStorage.Default.Remove(key);
        }
        catch
        {
            // A stale or invalid platform keystore entry must not prevent logout.
        }
    }

    private static string ExtractSessionId(string refreshToken)
    {
        var separator = refreshToken.IndexOf('.', StringComparison.Ordinal);
        return separator > 0 ? refreshToken[..separator] : string.Empty;
    }
}
