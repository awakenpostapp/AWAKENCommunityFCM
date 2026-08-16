namespace CommunityFootballClubManager.Services;

public sealed record RememberedCredential(string Username, string Password);

public sealed class RememberedLoginService
{
    private const string EnabledKey = "auth.remember.enabled";
    private const string UsernameKey = "auth.remember.username";
    // Kept only so older installations can have the legacy plaintext value
    // removed during the next login/logout. New builds never write a password.
    private const string PasswordKey = "auth.remember.password.v1";

    public Task<RememberedCredential?> LoadAsync()
    {
        try
        {
            if (!Preferences.Default.Get(EnabledKey, false))
            {
                return Task.FromResult<RememberedCredential?>(null);
            }

            var username = Preferences.Default.Get(UsernameKey, string.Empty);
            if (string.IsNullOrWhiteSpace(username))
            {
                Forget();
                return Task.FromResult<RememberedCredential?>(null);
            }

            // Passwords must never be persisted. The online session is kept by
            // CloudTokenStore using a rotating refresh token instead.
            try { SecureStorage.Default.Remove(PasswordKey); } catch { }
            return Task.FromResult<RememberedCredential?>(new RememberedCredential(username, string.Empty));
        }
        catch
        {
            Forget();
            return Task.FromResult<RememberedCredential?>(null);
        }
    }

    public Task SaveAsync(string username, string password)
    {
        _ = password;
        Preferences.Default.Set(UsernameKey, username.Trim());
        Preferences.Default.Set(EnabledKey, true);
        try { SecureStorage.Default.Remove(PasswordKey); } catch { }
        return Task.CompletedTask;
    }

    public void Forget()
    {
        try
        {
            Preferences.Default.Set(EnabledKey, false);
            Preferences.Default.Remove(UsernameKey);
        }
        catch
        {
            // Login must remain available even when preferences are unavailable.
        }

        try
        {
            SecureStorage.Default.Remove(PasswordKey);
        }
        catch
        {
            // A missing or invalid Android Keystore entry must not block login.
        }
    }
}
