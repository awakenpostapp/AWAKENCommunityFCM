using CommunityFootballClubManager.Services;

namespace CommunityFootballClubManager.Ui;

public abstract class AsyncContentPage : ContentPage
{
    private bool _loading;

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
        await ReloadAsync();
    }

    protected async Task ReloadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            await LoadAsync();
        }
        catch (Exception exception)
        {
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

    protected static string UserMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException => exception.Message,
        InvalidOperationException => exception.Message,
        NotSupportedException => exception.Message,
        _ => "Đã xảy ra lỗi khi xử lý dữ liệu. Vui lòng thử lại."
    };
}
