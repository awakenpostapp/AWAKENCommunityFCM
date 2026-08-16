using CommunityFootballClubManager.Models;

namespace CommunityFootballClubManager.Services;

public sealed class SessionService
{
    public UserAccount? CurrentUser { get; private set; }
    public PersonProfile? CurrentProfile { get; private set; }

    public bool IsAuthenticated => CurrentUser is not null;

    public void Start(UserAccount user, PersonProfile profile)
    {
        CurrentUser = user;
        CurrentProfile = profile;
    }

    public void RefreshProfile(PersonProfile profile)
    {
        CurrentProfile = profile;
    }

    public void MarkPasswordChanged()
    {
        if (CurrentUser is not null)
        {
            CurrentUser.MustChangePassword = false;
        }
    }

    public void End()
    {
        CurrentUser = null;
        CurrentProfile = null;
    }
}
