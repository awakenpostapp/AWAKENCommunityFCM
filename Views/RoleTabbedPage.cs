using Microsoft.Extensions.DependencyInjection;
using CommunityFootballClubManager.Models;
using CommunityFootballClubManager.Services;
using CommunityFootballClubManager.Ui;
using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;

namespace CommunityFootballClubManager.Views;

public interface IResettableTabPage
{
    void ResetTabState();
}

public sealed class RoleTabbedPage : Microsoft.Maui.Controls.TabbedPage
{
    private readonly IServiceProvider _services;

    public RoleTabbedPage(IServiceProvider services, SessionService session)
    {
        _services = services;
        BarBackgroundColor = UiKit.Surface;
        SelectedTabColor = UiKit.Primary;
        UnselectedTabColor = UiKit.TextSecondary;
        this.On<Microsoft.Maui.Controls.PlatformConfiguration.Android>()
            .SetToolbarPlacement(ToolbarPlacement.Bottom);

        var role = session.CurrentUser?.Role
                   ?? throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ.");
        switch (role)
        {
            case UserRole.Founder:
            case UserRole.CoFounder:
                AddTab<FounderDashboardPage>("Tổng quan", "tab_home.svg", hideRootNavigationBar: true);
                AddTab<ClassListPage>("Lớp học", "tab_classes.svg", hideRootNavigationBar: true);
                AddTab<AchievementHubPage>("Thành tích", "tab_achievements.svg", hideRootNavigationBar: true);
                AddTab<FounderFinancePage>("Tài chính", "tab_finance.svg", hideRootNavigationBar: true);
                AddTab<MorePage>("Quản lý", "tab_more.svg", hideRootNavigationBar: true);
                break;
            case UserRole.Manager:
                AddTab<ManagerDashboardPage>("Tổng quan", "tab_home.svg", hideRootNavigationBar: true);
                AddTab<ClassListPage>("Lớp học", "tab_classes.svg", hideRootNavigationBar: true);
                AddTab<MemberManagementPage>("Thành viên", "tab_people.svg", hideRootNavigationBar: true);
                AddTab<ManagerFinancePage>("Tài chính", "tab_finance.svg", hideRootNavigationBar: true);
                AddTab<ManagerOperationsPage>("Xử lý", "tab_more.svg", hideRootNavigationBar: true);
                break;
            case UserRole.Coach:
                AddTab<CoachDashboardPage>("Hôm nay", "tab_home.svg", hideRootNavigationBar: true);
                AddTab<ClassListPage>("Lớp học", "tab_classes.svg", hideRootNavigationBar: true);
                AddTab<AttendanceHubPage>("Điểm danh", "tab_attendance.svg", hideRootNavigationBar: true);
                AddTab<AchievementHubPage>("Thành tích", "tab_achievements.svg", hideRootNavigationBar: true);
                AddTab<ProfileHubPage>("Hồ sơ", "tab_profile.svg");
                break;
            case UserRole.Trainee:
                AddTab<TraineeDashboardPage>("Hôm nay", "tab_home.svg");
                AddTab<ClassListPage>("Lịch học", "tab_classes.svg");
                AddTab<TuitionPage>("Học phí", "tab_tuition.svg");
                AddTab<AchievementHubPage>("Thành tích", "tab_achievements.svg", hideRootNavigationBar: true);
                AddTab<ProfileHubPage>("Hồ sơ", "tab_profile.svg");
                break;
        }

        // Switching work areas preserves each navigation stack and its filters.
        // Data-changing child pages still explicitly invalidate their parents.
    }

    private void AddTab<TPage>(string title, string icon, bool hideRootNavigationBar = false)
        where TPage : Page
    {
        var page = ActivatorUtilities.CreateInstance<TPage>(_services);
        if (hideRootNavigationBar)
        {
            NavigationPage.SetHasNavigationBar(page, false);
        }
        var navigation = new NavigationPage(page)
        {
            Title = title,
            IconImageSource = icon,
            BarBackgroundColor = UiKit.Surface,
            BarTextColor = UiKit.TextPrimary
        };
        Children.Add(navigation);
    }

}
