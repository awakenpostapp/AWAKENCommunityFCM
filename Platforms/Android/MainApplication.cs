using Android.App;
using Android.Runtime;

namespace CommunityFootballClubManager;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
