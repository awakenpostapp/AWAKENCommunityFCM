using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;

namespace CommunityFootballClubManager;

/// <summary>
/// Receives the custom-scheme redirect from the system browser and hands the
/// query parameters back to WebAuthenticator. This must be a dedicated
/// WebAuthenticatorCallbackActivity; MainActivity is not the callback target.
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "communityfootballclubmanager",
    DataHost = "oauth",
    DataPath = "/callback")]
public sealed class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
}
