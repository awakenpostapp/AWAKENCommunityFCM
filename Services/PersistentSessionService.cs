using CommunityFootballClubManager.Services.Online;

namespace CommunityFootballClubManager.Services;

public sealed class PersistentSessionService
{
    private const string ActiveUserKey = "auth.active.user_id.v1";
    private readonly CloudTokenStore _cloudTokens;

    public PersistentSessionService(CloudTokenStore cloudTokens)
    {
        _cloudTokens = cloudTokens;
    }

    public async Task<string?> LoadUserIdAsync()
    {
        try
        {
            var cloudSession = await _cloudTokens.LoadRefreshSessionAsync();
            if (!string.IsNullOrWhiteSpace(cloudSession?.UserId))
            {
                return cloudSession.UserId;
            }

            var userId = await SecureStorage.Default.GetAsync(ActiveUserKey);
            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }
        catch
        {
            Clear();
            return null;
        }
    }

    public Task SaveAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID không hợp lệ.", nameof(userId));
        }

        // CloudTokenStore already persists the online user ID together with
        // its refresh session. Avoid a second Android Keystore write on every
        // successful online login.
        return _cloudTokens.HasRefreshSessionFor(userId)
            ? Task.CompletedTask
            : SecureStorage.Default.SetAsync(ActiveUserKey, userId);
    }

    public void Clear()
    {
        try
        {
            SecureStorage.Default.Remove(ActiveUserKey);
        }
        catch
        {
            // Người dùng vẫn phải có thể mở màn hình đăng nhập nếu Keystore lỗi.
        }
    }
}
