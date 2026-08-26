using CommunityFootballClubManager.Services;

namespace CommunityFootballClubManager.Ui;

public abstract class AsyncContentPage : ContentPage
{
    private bool _loading;
    private bool _appearingReload;
    private bool _hasLoaded;
    private DateTime _lastLoadedAtUtc;

    // Navigation back to a tab should not refetch the same online snapshot on
    // every Appearing event.  Explicit retries/actions still reload immediately
    // because they run outside this guarded Appearing window.
    private static readonly TimeSpan AppearingFreshness = TimeSpan.FromSeconds(20);

    protected AsyncContentPage(SessionService session, string title)
    {
        Session = session;
        Title = title;
        BackgroundColor = UiKit.Background;
    }

    protected SessionService Session { get; }

    protected string CurrentUserId =>
        Session.CurrentUser?.Id
        ?? throw new UnauthorizedAccessException("Phiên đăng nhập đã kết thúc.");

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _appearingReload = true;
        try
        {
            await ReloadAsync();
        }
        finally
        {
            _appearingReload = false;
        }
    }

    protected async Task ReloadAsync()
    {
        if (_loading)
        {
            return;
        }

        if (_appearingReload
            && _hasLoaded
            && DateTime.UtcNow - _lastLoadedAtUtc < AppearingFreshness)
        {
            return;
        }

        _loading = true;
        try
        {
            await LoadAsync();
            _hasLoaded = true;
            _lastLoadedAtUtc = DateTime.UtcNow;
        }
        catch (Exception exception)
        {
            // Do not let a failed explicit refresh make the stale page look
            // fresh on the next Appearing event; the retry must be allowed to
            // hit the backend again.
            _hasLoaded = false;
            Content = UiKit.ScrollBody(UiKit.EmptyState(
                "Không thể tải dữ liệu",
                UserMessage(exception),
                UiKit.SecondaryButton("Thử lại", async (_, _) => await ReloadAsync())));
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// Marks this page stale so the next navigation back to it performs an
    /// authoritative load immediately. Mutations made on a child page (for
    /// example deleting a member from its profile) use this to bypass the
    /// short appearing freshness window without making every tab navigation
    /// perform a network request.
    /// </summary>
    public void InvalidateLoadCache()
    {
        _hasLoaded = false;
        _lastLoadedAtUtc = DateTime.MinValue;
    }

    protected abstract Task LoadAsync();

    /// <summary>
    /// Opens a child page without the Android navigation bar shifting the
    /// current layout during the transition. Founder tab roots intentionally
    /// hide their own bar; those pushes are therefore performed without the
    /// native animation, while pages that already show a bar keep it.
    /// </summary>
    protected Task PushPageAsync(Page page)
    {
        NavigationPage.SetHasNavigationBar(page, true);
        var animated = NavigationPage.GetHasNavigationBar(this);
        return Navigation.PushAsync(page, animated);
    }

    protected async Task RunActionAsync(
        Func<Task> action,
        Button? sourceButton = null,
        string? successMessage = null,
        bool reload = true)
    {
        if (sourceButton is not null)
        {
            sourceButton.IsEnabled = false;
        }

        try
        {
            await action();
            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                await DisplayAlertAsync("Hoàn tất", successMessage, "OK");
            }

            if (reload)
            {
                await ReloadAsync();
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Chưa thể thực hiện", UserMessage(exception), "Đóng");
        }
        finally
        {
            if (sourceButton is not null)
            {
                sourceButton.IsEnabled = true;
            }
        }
    }

    public static string UserMessage(Exception exception) => exception switch
    {
        TimeoutException => "Kết nối đang chậm. Vui lòng thử lại.",
        HttpRequestException => "Không thể kết nối máy chủ. Vui lòng thử lại.",
        UnauthorizedAccessException => exception.Message,
        InvalidOperationException => exception.Message,
        NotSupportedException => exception.Message,
        _ => "Đã xảy ra lỗi khi xử lý dữ liệu. Vui lòng thử lại."
    };
}
