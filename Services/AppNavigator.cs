using Microsoft.Extensions.DependencyInjection;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services.Online;
using CommunityFootballClubManager.Ui;
using CommunityFootballClubManager.Views;

namespace CommunityFootballClubManager.Services;

public sealed class AppNavigator
{
    private readonly IServiceProvider _services;
    private readonly SessionService _session;
    private readonly PersistentSessionService _persistentSession;
    private readonly CloudApiClient _cloudApi;
    private Window? _window;

    public AppNavigator(
        IServiceProvider services,
        SessionService session,
        PersistentSessionService persistentSession,
        CloudApiClient cloudApi)
    {
        _services = services;
        _session = session;
        _persistentSession = persistentSession;
        _cloudApi = cloudApi;
    }

    public void Attach(Window window)
    {
        _window = window;
    }

    public void ShowLogin(bool clearPersistentSession = true)
    {
        if (clearPersistentSession)
        {
            _persistentSession.Clear();
        }

        _session.End();
        var login = ActivatorUtilities.CreateInstance<LoginPage>(_services);
        SetRoot(new NavigationPage(login));
    }

    public async Task LogoutAsync()
    {
        CloudRefreshSession? detachedSession = null;
        try
        {
            // Detach the local token first, then switch screens immediately.
            // Remote revocation is best-effort and never blocks the UI.
            detachedSession = await _cloudApi.DetachLocalSessionAsync();
        }
        catch
        {
            // Local sign-out must remain available if the network is unavailable.
        }
        ShowLogin();
        if (detachedSession is not null)
        {
            _ = RevokeDetachedSessionSafelyAsync(detachedSession);
        }
    }

    private async Task RevokeDetachedSessionSafelyAsync(CloudRefreshSession session)
    {
        try
        {
            await _cloudApi.RevokeDetachedSessionAsync(session);
        }
        catch
        {
            // The local session is already gone. The remote session will
            // expire naturally if the device is offline during sign-out.
        }
    }

    public void ShowMain()
    {
        if (!_session.IsAuthenticated)
        {
            ShowLogin();
            return;
        }

        if (_session.CurrentUser?.Role == UserRole.Admin)
        {
            var adminPage = ActivatorUtilities.CreateInstance<AdminManagementPage>(_services);
            SetRoot(new NavigationPage(adminPage));
            return;
        }

        if (_session.CurrentUser?.MustChangePassword == true)
        {
            var changePassword = ActivatorUtilities.CreateInstance<ForcedPasswordChangePage>(_services);
            SetRoot(new NavigationPage(changePassword));
            return;
        }

        SetRoot(new RoleTabbedPage(_services, _session));
    }

    private void SetRoot(Page page)
    {
        var window = _window
                     ?? Application.Current?.Windows.FirstOrDefault()
                     ?? throw new InvalidOperationException("Không tìm thấy cửa sổ ứng dụng.");
        window.Page = page;
    }
}
