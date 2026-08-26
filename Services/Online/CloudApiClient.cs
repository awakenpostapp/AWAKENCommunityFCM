using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CommunityFootballClubManager.Services.Online;

/// <summary>
/// HTTP client for the Cloudflare Worker API. Access tokens remain in memory;
/// rotating refresh sessions are delegated to <see cref="CloudTokenStore"/>.
/// </summary>
public sealed class CloudApiClient
{
    private readonly HttpClient _httpClient;
    private readonly CloudBackendOptions _options;
    private readonly CloudTokenStore _tokenStore;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    public CloudApiClient(
        HttpClient httpClient,
        CloudBackendOptions options,
        CloudTokenStore tokenStore)
    {
        _httpClient = httpClient;
        _options = options;
        _tokenStore = tokenStore;
        _httpClient.BaseAddress ??= options.ApiBaseAddress;
        _httpClient.Timeout = options.RequestTimeout;
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _jsonOptions.Converters.Add(new FlexibleBooleanJsonConverter());
        _jsonOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }

    public async Task<CloudAuthResponse> LoginAsync(
        CloudLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync<CloudAuthResponse>(
            HttpMethod.Post,
            "auth/login",
            request,
            requiresAuthentication: false,
            allowRefresh: false,
            cancellationToken: cancellationToken);
        await SaveSessionAsync(response);
        return response;
    }

    public async Task<CloudAuthResponse> RegisterFounderAsync(
        CloudFounderRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var idempotencyKey = Guid.NewGuid().ToString("N");
        CloudAuthResponse? response = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                response = await SendJsonAsync<CloudAuthResponse>(
                    HttpMethod.Post,
                    "auth/register-founder",
                    request,
                    requiresAuthentication: false,
                    allowRefresh: false,
                    idempotencyKey: idempotencyKey,
                    cancellationToken: cancellationToken);
                break;
            }
            catch (Exception exception) when (
                attempt == 0
                && !cancellationToken.IsCancellationRequested
                && IsTransientConnectionFailure(exception))
            {
                await Task.Delay(300, cancellationToken);
            }
        }
        if (response is null)
        {
            throw new HttpRequestException("Không nhận được phản hồi từ backend.");
        }
        if (response.HasSessionTokens)
        {
            await SaveSessionAsync(response);
        }
        return response;
    }

    private static bool IsTransientConnectionFailure(Exception exception) =>
        exception is HttpRequestException
            or TaskCanceledException
            or TimeoutException
        || exception is ApiException
        {
            StatusCode: HttpStatusCode.RequestTimeout
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.InternalServerError
        };

    public async Task<CloudAuthResponse> ExchangeOAuthCodeAsync(
        CloudOAuthExchangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync<CloudAuthResponse>(
            HttpMethod.Post,
            "auth/oauth/exchange",
            request,
            requiresAuthentication: false,
            allowRefresh: false,
            cancellationToken: cancellationToken);
        await SaveSessionAsync(response);
        return response;
    }

    public Task<CloudOAuthLinkResponse> LinkOAuthAsync(
        CloudOAuthExchangeRequest request,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<CloudOAuthLinkResponse>(
            HttpMethod.Post,
            "auth/oauth/exchange",
            request,
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey: Guid.NewGuid().ToString("N"),
            cancellationToken: cancellationToken);

    public Task<CloudOAuthLinksResponse> GetOAuthLinksAsync(
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<CloudOAuthLinksResponse>(
            HttpMethod.Get,
            "auth/oauth/links",
            payload: null,
            requiresAuthentication: true,
            allowRefresh: true,
            cancellationToken: cancellationToken);

    public Task PatchFounderStatusAsync(
        string founderId,
        bool isActive,
        CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(
            HttpMethod.Patch,
            $"admin/founders/{Uri.EscapeDataString(founderId)}/status",
            new CloudUserStatusRequest(isActive),
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey: Guid.NewGuid().ToString("N"),
            cancellationToken);

    public async Task<CloudAuthResponse> RefreshSessionAsync(
        CancellationToken cancellationToken = default)
    {
        // Force a refresh even when this process still has a valid access token,
        // because callers need the authoritative public user/profile bundle too.
        var currentAccessToken = _tokenStore.GetValidAccessToken(TimeSpan.Zero);
        var refreshed = await RefreshAccessTokenAsync(
            rejectedAccessToken: currentAccessToken,
            cancellationToken);
        return refreshed
               ?? throw new ApiException(
                   HttpStatusCode.Unauthorized,
                   "session_unavailable",
                   "Không tìm thấy phiên Cloud hợp lệ. Vui lòng đăng nhập lại.");
    }

    public Task<CloudCurrentSessionResponse> GetCurrentSessionAsync(
        CancellationToken cancellationToken = default) =>
        GetAsync<CloudCurrentSessionResponse>("auth/me", cancellationToken);

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        var refreshSession = await DetachLocalSessionAsync();
        if (refreshSession is not null)
        {
            await RevokeDetachedSessionAsync(refreshSession, cancellationToken);
        }
    }

    public Task<CloudRefreshSession?> DetachLocalSessionAsync() =>
        _tokenStore.TakeAndClearAsync();

    public Task RevokeDetachedSessionAsync(
        CloudRefreshSession refreshSession,
        CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(
            HttpMethod.Post,
            "auth/logout",
            new CloudLogoutRequest(refreshSession.RefreshToken),
            requiresAuthentication: false,
            allowRefresh: false,
            idempotencyKey: null,
            cancellationToken);

    public Task<CloudDataSnapshot> GetSnapshotAsync(
        long? afterSyncVersion = null,
        CancellationToken cancellationToken = default)
    {
        var path = afterSyncVersion is > 0
            ? $"sync/snapshot?afterSyncVersion={afterSyncVersion.Value}"
            : "sync/snapshot";
        return GetAsync<CloudDataSnapshot>(path, cancellationToken);
    }

    public Task<CloudSnapshotApplyResponse> PutSnapshotAsync(
        CloudDataSnapshot snapshot,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<CloudSnapshotApplyResponse>(
            HttpMethod.Put,
            "sync/snapshot",
            snapshot,
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey: idempotencyKey,
            cancellationToken: cancellationToken);

    public Task<TResponse> GetAsync<TResponse>(
        string relativePath,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<TResponse>(
            HttpMethod.Get,
            relativePath,
            payload: null,
            requiresAuthentication: true,
            allowRefresh: true,
            cancellationToken: cancellationToken);

    public Task<TResponse> PostAsync<TRequest, TResponse>(
        string relativePath,
        TRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<TResponse>(
            HttpMethod.Post,
            relativePath,
            request,
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey: idempotencyKey,
            cancellationToken: cancellationToken);

    public async Task<CloudUploadResponse> UploadFileAsync(
        string filePath,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("Không tìm thấy tệp cần tải lên.", filePath);
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var contentType = extension switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "image/jpeg"
        };
        return await SendBytesAsync<CloudUploadResponse>(
            HttpMethod.Post,
            "uploads",
            await File.ReadAllBytesAsync(filePath, cancellationToken),
            contentType,
            purpose,
            Guid.NewGuid().ToString("N"),
            cancellationToken);
    }

    public Task<CloudBinaryResponse> DownloadFileAsync(
        string relativePath,
        CancellationToken cancellationToken = default) =>
        SendBinaryAsync(relativePath, cancellationToken);

    public Task<TResponse> PutAsync<TRequest, TResponse>(
        string relativePath,
        TRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<TResponse>(
            HttpMethod.Put,
            relativePath,
            request,
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey: idempotencyKey,
            cancellationToken: cancellationToken);

    public Task<TResponse> PatchAsync<TRequest, TResponse>(
        string relativePath,
        TRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<TResponse>(
            HttpMethod.Patch,
            relativePath,
            request,
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey: idempotencyKey,
            cancellationToken: cancellationToken);

    public Task PostAsync<TRequest>(
        string relativePath,
        TRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(
            HttpMethod.Post,
            relativePath,
            request,
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey,
            cancellationToken);

    public Task PutAsync<TRequest>(
        string relativePath,
        TRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(
            HttpMethod.Put,
            relativePath,
            request,
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey,
            cancellationToken);

    public Task PatchAsync<TRequest>(
        string relativePath,
        TRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(
            HttpMethod.Patch,
            relativePath,
            request,
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey,
            cancellationToken);

    public Task ClearLocalSessionAsync() => _tokenStore.ClearAsync();

    public Task DeleteAsync(
        string relativePath,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(
            HttpMethod.Delete,
            relativePath,
            payload: null,
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey: idempotencyKey,
            cancellationToken: cancellationToken);

    public Task DeleteMemberAsync(
        string userId,
        CancellationToken cancellationToken = default) =>
        DeleteAsync(
            $"users/{Uri.EscapeDataString(userId)}",
            idempotencyKey: Guid.NewGuid().ToString("N"),
            cancellationToken: cancellationToken);

    public Task MarkAllNotificationsReadAsync(CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(
            HttpMethod.Post,
            "notifications/read-all",
            new { },
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey: Guid.NewGuid().ToString("N"),
            cancellationToken);

    public Task DeleteAllNotificationsAsync(CancellationToken cancellationToken = default) =>
        SendWithoutResponseAsync(
            HttpMethod.Delete,
            "notifications",
            payload: null,
            requiresAuthentication: true,
            allowRefresh: true,
            idempotencyKey: Guid.NewGuid().ToString("N"),
            cancellationToken);

    private async Task<CloudAuthResponse?> RefreshAccessTokenAsync(
        string? rejectedAccessToken,
        CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var currentAccessToken = _tokenStore.GetValidAccessToken(TimeSpan.Zero);
            if (!string.IsNullOrWhiteSpace(currentAccessToken)
                && !string.Equals(
                    currentAccessToken,
                    rejectedAccessToken,
                    StringComparison.Ordinal))
            {
                return new CloudAuthResponse
                {
                    AccessToken = currentAccessToken
                };
            }

            var stored = await _tokenStore.LoadRefreshSessionAsync();
            if (stored is null)
            {
                return null;
            }

            try
            {
                var response = await SendJsonAsync<CloudAuthResponse>(
                    HttpMethod.Post,
                    "auth/refresh",
                    new CloudRefreshRequest(stored.RefreshToken),
                    requiresAuthentication: false,
                    allowRefresh: false,
                    cancellationToken: cancellationToken);
                await SaveSessionAsync(response, stored.UserId, stored.TenantId);
                return response;
            }
            catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
            {
                await _tokenStore.ClearAsync();
                return null;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task SaveSessionAsync(
        CloudAuthResponse response,
        string? fallbackUserId = null,
        string? fallbackTenantId = null)
    {
        var tokens = response.EffectiveTokens;
        var userId = response.User?.Id ?? fallbackUserId;
        var tenantId = response.User?.TenantId
                       ?? response.ActiveClub?.TenantId
                       ?? response.Club?.TenantId
                       ?? fallbackTenantId;
        await _tokenStore.SaveAsync(tokens, userId, tenantId);
    }

    private async Task<TResponse> SendJsonAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        object? payload,
        bool requiresAuthentication,
        bool allowRefresh,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        _options.EnsureConfigured();
        var serializedPayload = payload is null
            ? null
            : JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), _jsonOptions);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = requiresAuthentication
                ? _tokenStore.GetValidAccessToken()
                : null;
            if (requiresAuthentication && string.IsNullOrWhiteSpace(accessToken))
            {
                var refreshed = allowRefresh
                    ? await RefreshAccessTokenAsync(null, cancellationToken)
                    : null;
                accessToken = refreshed is null
                    ? null
                    : _tokenStore.GetValidAccessToken(TimeSpan.Zero);
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new ApiException(
                        HttpStatusCode.Unauthorized,
                        "session_unavailable",
                        "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
                }
            }

            using var request = CreateRequest(
                method,
                relativePath,
                serializedPayload,
                accessToken,
                idempotencyKey);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized
                && requiresAuthentication
                && allowRefresh
                && attempt == 0)
            {
                _tokenStore.InvalidateAccessToken();
                if (await RefreshAccessTokenAsync(accessToken, cancellationToken) is not null)
                {
                    continue;
                }
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response, body);
            }

            if (body.Length == 0)
            {
                throw new ApiException(
                    response.StatusCode,
                    "empty_response",
                    "Backend trả về dữ liệu rỗng ngoài dự kiến.",
                    response.Headers.TryGetValues("cf-ray", out var values)
                        ? values.FirstOrDefault()
                        : null);
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(body, _jsonOptions)
                       ?? throw new JsonException("Response JSON is null.");
            }
            catch (JsonException exception)
            {
                throw new ApiException(
                    response.StatusCode,
                    "invalid_response",
                    "Backend trả về dữ liệu không đúng định dạng.",
                    response.Headers.TryGetValues("cf-ray", out var values)
                        ? values.FirstOrDefault()
                        : null,
                    innerException: exception);
            }
        }

        throw new ApiException(
            HttpStatusCode.Unauthorized,
            "session_unavailable",
            "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
    }

    private async Task<TResponse> SendBytesAsync<TResponse>(
        HttpMethod method,
        string relativePath,
        byte[] payload,
        string contentType,
        string uploadPurpose,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = _tokenStore.GetValidAccessToken();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                var refreshed = await RefreshAccessTokenAsync(null, cancellationToken);
                accessToken = refreshed is null ? null : _tokenStore.GetValidAccessToken(TimeSpan.Zero);
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new ApiException(HttpStatusCode.Unauthorized, "session_unavailable",
                        "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
                }
            }

            using var request = CreateRequest(method, relativePath, payload, accessToken, idempotencyKey, contentType);
            request.Headers.TryAddWithoutValidation("x-upload-purpose", uploadPurpose);
            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _tokenStore.InvalidateAccessToken();
                if (await RefreshAccessTokenAsync(accessToken, cancellationToken) is not null)
                {
                    continue;
                }
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response, body);
            }

            try
            {
                return JsonSerializer.Deserialize<TResponse>(body, _jsonOptions)
                       ?? throw new JsonException("Response JSON is null.");
            }
            catch (JsonException exception)
            {
                throw new ApiException(response.StatusCode, "invalid_response",
                    "Backend trả về dữ liệu không đúng định dạng.",
                    response.Headers.TryGetValues("cf-ray", out var values) ? values.FirstOrDefault() : null,
                    innerException: exception);
            }
        }

        throw new ApiException(HttpStatusCode.Unauthorized, "session_unavailable",
            "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
    }

    private async Task<CloudBinaryResponse> SendBinaryAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = _tokenStore.GetValidAccessToken();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                var refreshed = await RefreshAccessTokenAsync(null, cancellationToken);
                accessToken = refreshed is null
                    ? null
                    : _tokenStore.GetValidAccessToken(TimeSpan.Zero);
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new ApiException(
                        HttpStatusCode.Unauthorized,
                        "session_unavailable",
                        "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
                }
            }

            using var request = CreateRequest(
                HttpMethod.Get,
                relativePath,
                payload: null,
                accessToken,
                idempotencyKey: null);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _tokenStore.InvalidateAccessToken();
                if (await RefreshAccessTokenAsync(accessToken, cancellationToken) is not null)
                {
                    continue;
                }
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response, body);
            }

            return new CloudBinaryResponse
            {
                Bytes = body,
                ContentType = response.Content.Headers.ContentType?.MediaType
                              ?? "application/octet-stream"
            };
        }

        throw new ApiException(
            HttpStatusCode.Unauthorized,
            "session_unavailable",
            "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
    }

    private async Task SendWithoutResponseAsync(
        HttpMethod method,
        string relativePath,
        object? payload,
        bool requiresAuthentication,
        bool allowRefresh,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();
        var serializedPayload = payload is null
            ? null
            : JsonSerializer.SerializeToUtf8Bytes(payload, payload.GetType(), _jsonOptions);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = requiresAuthentication
                ? _tokenStore.GetValidAccessToken()
                : null;
            if (requiresAuthentication && string.IsNullOrWhiteSpace(accessToken))
            {
                var refreshed = allowRefresh
                    ? await RefreshAccessTokenAsync(null, cancellationToken)
                    : null;
                accessToken = refreshed is null
                    ? null
                    : _tokenStore.GetValidAccessToken(TimeSpan.Zero);
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new ApiException(
                        HttpStatusCode.Unauthorized,
                        "session_unavailable",
                        "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
                }
            }

            using var request = CreateRequest(
                method,
                relativePath,
                serializedPayload,
                accessToken,
                idempotencyKey);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized
                && requiresAuthentication
                && allowRefresh
                && attempt == 0)
            {
                _tokenStore.InvalidateAccessToken();
                if (await RefreshAccessTokenAsync(accessToken, cancellationToken) is not null)
                {
                    continue;
                }
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response, body);
            }

            return;
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string relativePath,
        byte[]? payload,
        string? accessToken,
        string? idempotencyKey,
        string contentType = "application/json")
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("API path không được để trống.", nameof(relativePath));
        }

        var request = new HttpRequestMessage(method, relativePath.TrimStart('/'));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (payload is not null)
        {
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType)
            {
                CharSet = Encoding.UTF8.WebName
            };
        }

        return request;
    }

    private ApiException CreateApiException(
        HttpResponseMessage response,
        byte[] body)
    {
        CloudApiErrorEnvelope? envelope = null;
        if (body.Length > 0)
        {
            try
            {
                envelope = JsonSerializer.Deserialize<CloudApiErrorEnvelope>(body, _jsonOptions);
            }
            catch (JsonException)
            {
                // A safe generic message is used below. Never include raw response
                // bodies because they can contain tokens or personal information.
            }
        }

        var error = envelope?.Error;
        var code = error?.Code ?? envelope?.Code ?? "api_error";
        var message = error?.Message
                      ?? envelope?.Message
                      ?? response.ReasonPhrase
                      ?? "Không thể kết nối với backend.";
        var traceId = error?.TraceId ?? envelope?.TraceId;
        if (string.IsNullOrWhiteSpace(traceId)
            && response.Headers.TryGetValues("cf-ray", out var values))
        {
            traceId = values.FirstOrDefault();
        }

        return new ApiException(
            response.StatusCode,
            code,
            message,
            traceId,
            error?.Details);
    }
}
