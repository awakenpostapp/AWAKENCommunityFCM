using Microsoft.Maui.Controls;

namespace CommunityFootballClubManager.Ui;

/// <summary>
/// Defines the tab navigation contract: each tab is a work area whose child
/// pages are transient. Returning to a tab must therefore show its root page.
/// </summary>
public static class TabNavigationPolicy
{
    public static Task ResetToHomeAsync(NavigationPage navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        return navigation.Navigation.NavigationStack.Count > 1
            ? navigation.PopToRootAsync(animated: false)
            : Task.CompletedTask;
    }
}
