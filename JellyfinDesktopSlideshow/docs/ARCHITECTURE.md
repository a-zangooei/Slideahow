# معماری فنی Jellyfin Desktop Slideshow

این سند نقشهٔ کامل لایه‌ها، سرویس‌ها، مدل‌های داده و قراردادهای API را شرح می‌دهد تا هر توسعه‌دهنده‌ای بتواند بدون حدس زدن، باگ‌ها را برطرف کرده یا قابلیت جدیدی اضافه کند.

---

## ۱. ساختار پوشه‌ها و تفکیک مسئولیت (MVVM)

```text
JellyfinDesktopSlideshow/
├── JellyfinDesktopSlideshow.sln
├── Directory.Build.props
├── README.md
├── docs/
│   ├── ARCHITECTURE.md
│   └── TESTING_AND_RELEASE.md
├── src/JellyfinDesktopSlideshow/
│   ├── App.xaml / App.xaml.cs          # ریشهٔ DI دستی، هماهنگی پنجره‌ها، Tray و Shutdown امن
│   ├── Models/                         # مدل‌های خالص و DTOهای شبکه
│   ├── Infrastructure/                 # وابستگی‌های بومی ویندوز (Registry، Credential Manager، Tray، Timer، Logging)
│   ├── Services/                       # منطق کسب‌وکار و کلاینت HTTP (بدون وابستگی مستقیم به کنترل‌های XAML)
│   ├── ViewModels/                     # MVVM ViewModels + AsyncRelayCommand
│   └── Views/                          # MainWindow، SettingsWindow، OverlayWindow و Converterها
└── tests/JellyfinDesktopSlideshow.Tests/
    └── ...                             # تست‌های واحد xUnit با Fake/Stub برای شبکه، رجیستری، تایمر و فایل‌سیستم
```

### قانون طلایی معماری
- **هیچ سرویسی در `Services/` نباید به کنترل‌های UI (`MainWindow`, `SettingsWindow`, `TextBox`, ...) ارجاع دهد.**
- تمام عملیات سیستمی و ویندوز (Win32 API، رجیستری، `Process.Start`، `DispatcherTimer`) پشت Interfaceهایی در `Infrastructure/` یا `Services/` قرار دارند تا تست‌پذیری در `xUnit` صددرصد حفظ شود.

---

## ۲. جریان داده از اتصال تا دسکتاپ

1. **احراز هویت (`AuthenticationService` + `JellyfinClient` + `SecureStorageService`):**
   - تست اتصال: `GET /System/Info/Public` برای دریافت نام، نسخه و `ServerId`.
   - ورود: `POST /Users/AuthenticateByName` با هدر `Authorization: MediaBrowser Client="...", Device="...", DeviceId="...", Version="..."`.
   - توکن دریافت‌شده **هرگز** در `settings.json` یا لاگ‌ها نوشته نمی‌شود؛ فقط از طریق `CredWriteW` در **Windows Credential Manager** با کلید `JellyfinDesktopSlideshow:AccessToken` ذخیره می‌شود.
   - هنگام شروع برنامه، `TryRestoreSessionAsync()` توکن را از Credential Manager خوانده و با `GET /Users/Me` اعتبار آن را می‌سنجد.

2. **دریافت کتابخانه‌ها و رسانه‌ها (`LibraryService`):**
   - دریافت کتابخانه‌های مجاز کاربر از `GET /Users/{userId}/Views` (و در صورت خطای `404` روی برخی نسخه‌ها، fallback خودکار به `GET /UserViews?userId={userId}`).
   - دریافت صفحهٔ اول و صفحات بعدی فیلم‌ها/سریال‌ها از `GET /Users/{userId}/Items` با `Recursive=true` و `Fields=ProductionYear,BackdropImageTags`.

3. **دریافت و کش تصاویر (`ImageService` + `CacheService` + `ImageFormatValidator`):**
   - تصویر Backdrop از `GET /Items/{itemId}/Images/Backdrop/{index}?maxWidth=1920&quality=92&tag=...` با هدر `Authorization: MediaBrowser` دانلود می‌شود (بدون `api_key` در QueryString).
   - بایت‌های دریافتی با Magic Bytes (`ImageFormatValidator.TryDetectImageExtension`) بررسی می‌شوند تا فایل خراب یا HTML خطا ذخیره نشود.
   - اگر عنوان `Backdrop` نداشته باشد یا شبکه قطع باشد، ترتیب اولویت عبارت است از: **کش محلی** -> **تصویر جایگزین (`FallbackImagePath`)** -> **رد کردن عنوان یا حفظ تصویر قبلی**.

4. **زمان‌بندی اسلایدشو (`SlideshowService` + `WpfDispatcherTimerAdapter`):**
   - زمان‌بندی با `DispatcherTimer` در ترد UI انجام می‌شود (بدون `while(true)` یا Thread Sleep).
   - حالت **Random** از تاریخچهٔ عدم تکرار (`NonRepeatHistoryCount`) استفاده می‌کند تا عناوین اخیر بلافاصله تکرار نشوند.
   - در حالت شکست مداوم شبکه، مکانیزم **Exponential Backoff** بازهٔ تلاش مجدد را تا سقف ۵ دقیقه افزایش می‌دهد تا به سرور یا CPU فشاری وارد نشود.

5. **اعمال والپیپر (`WallpaperService` + `WindowsWallpaperPlatformAdapter`):**
   - ابتدا مسیر والپیپر فعلی کاربر ذخیره می‌شود تا در صورت فعال بودن `RestoreWallpaperOnExit` هنگام خروج بازگردانده شود.
   - فایل‌های `.webp` به `.png` تبدیل می‌شوند تا در تمام نسخه‌های ویندوز بدون خطای فرمت اعمال شوند.
   - حالت نمایش (`Fill`, `Fit`, `Stretch`, `Center`, `Span`) در کلید رجیستری `Control Panel\Desktop` (`WallpaperStyle` و `TileWallpaper`) تنظیم و سپس `SystemParametersInfoW(SPI_SETDESKWALLPAPER, ...)` فراخوانی می‌شود.
   - در `MainWindow.xaml.cs` رویداد `SystemEvents.DisplaySettingsChanged` به `RefreshDisplayConfiguration()` متصل است و در `OnClosed` از آن جدا (Unsubscribe) می‌شود.

6. **آیکون شناور و باز کردن عنوان (`OverlayService` + `OverlayWindow` + `ItemLauncher`):**
   - `OverlayWindow` پنجره‌ای شفاف، بدون قاب، خارج از Taskbar و دارای پرچم‌های Win32 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` است.
   - مختصات X/Y در `OverlayService.RefreshFromSettings()` در محدودهٔ `SystemParameters.VirtualScreen*` محدود (Clamp) می‌شود تا با قطع‌شدن یک مانیتور، آیکون خارج از صفحه گیر نیفتد.
   - `ItemLauncher` سه حالت دارد:
     - `DefaultBrowser`: ساخت لینک `{baseUrl}/web/index.html#!/details?id={itemId}&serverId={serverId}`
     - `JellyfinDesktopClient`: بررسی نصب کلاینت در مسیرها/رجیستری، یافتن نشست با `SupportsRemoteControl == true` در `GET /Sessions` و ارسال `POST /Sessions/{sessionId}/Viewing`
     - `CustomUrlTemplate`: اعتبارسنجی امنیتی برای جلوگیری از Command Injection (تنها پروتکل `http://` یا `https://` و بدون فاصله/کوتیشن مجاز است).

7. **System Tray و اجرای خودکار (`TrayService` + `StartupService`):**
   - `TrayService` با Win32 `Shell_NotifyIconW` + `HwndSource` + `ContextMenu` خالص WPF پیاده‌سازی شده است (بدون نیاز به بارگذاری WinForms و بدون تداخل Namespaceها). همچنین پیام `TaskbarCreated` را گوش می‌دهد تا در صورت ری‌استارت شدن `explorer.exe` آیکون دوباره در نوار اعلان ثبت شود.
   - `StartupService` مقدار رجیستری `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\JellyfinDesktopSlideshow` را مدیریت می‌کند و در صورت فعال بودن `StartMinimizedToTray` پارامتر `--minimized` را اضافه می‌نماید.
   - در `App.xaml.cs` مقدار `ShutdownMode = ShutdownMode.OnExplicitShutdown` تنظیم شده تا با بسته‌شدن یا مخفی‌شدن `MainWindow` برنامه در System Tray فعال بماند و تنها با دستور صریح «خروج کامل» تمام منابع پاک‌سازی و والپیپر (در صورت انتخاب) بازیابی شود.

---

## ۳. راهنمای افزودن قابلیت جدید بدون شکستن ساختار

- **افزودن تنظیم جدید:**
  1. فیلد جدید را با مقدار پیش‌فرض امن به `Models/AppSettings.cs` اضافه کنید.
  2. در `Services/SettingsService.cs` حدود مجاز آن را داخل `Load()` کنترل (`Math.Clamp`) کنید.
  3. Property متناظر را در `ViewModels/SettingsViewModel.cs` اضافه و به تب مناسب در `Views/SettingsWindow.xaml` بایند کنید.
  4. تست واحد برای ذخیره و بازیابی آن در `SettingsViewModelTests.cs` یا `SettingsServiceTests.cs` اضافه کنید.

- **افزودن Endpoint جدید به Jellyfin:**
  1. متد را به `IJellyfinClient` و پیاده‌سازی آن را با `ApplyMediaBrowserAuthorization` به `JellyfinClient` اضافه کنید (هرگز توکن را در QueryString قرار ندهید).
  2. DTO را در `Models/JellyfinDtos.cs` تعریف کنید.
  3. با `StubHttpMessageHandler` در `JellyfinClientTests.cs` رفتار موفق و خطاهای `401/403/404/500` را تست کنید.
