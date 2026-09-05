# Native Android UI verification

This separate MAUI application links the real production Views, Ui, Models and Services source. Its package ID is `com.awaken.fcm.uiharness`, distinct from production. It cannot send HTTP to production: its `HttpMessageHandler` has no network collaborator and rejects any host except `ui-fixture.invalid`.

```powershell
dotnet build tools/UiHarness/UiHarness.csproj -c Debug
```

Install the resulting `com.awaken.fcm.uiharness-Signed.apk` on a disposable Android emulator. The opening menu selects Founder, Co-Founder, Manager, Coach, Trainee, attendance, empty achievements or failed achievements. Relaunch to reset fixtures.

Data: 18 fictional students; 17 present and one absent; two classes; current Coach check-in. Personal achievement balance 365 = current 20 + 15 + 30 and expired 150 + 100 + 60 - 10. Expired awards retain points but do not render in the active gallery.

Attendance saves are handled in memory. Logcat tag `FCM-UI-QA` records only fixture route, roster count and submit flag. A filtered view must still save all 18 records. This verifies the client interaction and payload, not the production server's authorization or financial calculations.

Never publish this APK. Production project excludes all `tools/**` code and XAML.
