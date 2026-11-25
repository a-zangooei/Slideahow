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

5. **اعمال و بازگردانی والپیپر (`WallpaperService` + `WindowsWallpaperPlatformAdapter`):**
   - **تنظیماتی که برنامه تغییر می‌دهد:** کلیدهای رجیستری `HKCU\Control Panel\Desktop\WallpaperStyle` و `TileWallpaper`، مقدار `Wallpaper` (از طریق `SystemParametersInfoW(SPI_SETDESKWALLPAPER, ...)` با پرچم‌های `SPIF_UPDATEINIFILE | SPIF_SENDCHANGE`) و فایل کش داخلی ویندوز `TranscodedWallpaper` هنگام اعمال تصویر جدید.
   - **ذخیرهٔ وضعیت اولیه پیش از اولین تغییر:** `GetCurrentDesktopWallpaperState()` مسیر فعال (`SPI_GETDESKWALLPAPER`)، مقادیر خام `WallpaperStyle` و `TileWallpaper` (از جمله حالت `Tile` با `0/1` که در `WallpaperFitMode` نیست) و `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers\BackgroundType` را می‌خواند و به‌صورت اتمیک در `%LOCALAPPDATA%\JellyfinDesktopSlideshow\wallpaper-state.json` ذخیره می‌کند. اگر مسیر اولیه به فایل موقت ویندوز (`%AppData%\Microsoft\Windows\Themes\TranscodedWallpaper`) اشاره کند، پیش از تغییر یک کپی پشتیبان در `%LOCALAPPDATA%\JellyfinDesktopSlideshow\backup\` ساخته می‌شود.
   - **جلوگیری از بازنویسی با والپیپر خود برنامه و بازیابی پس از خروج ناگهانی:** در تغییرهای بعدی اسلایدشو و نیز پس از راه‌اندازی مجدد بعد از کرش، وضعیت ذخیره‌شدهٔ دیسک حفظ می‌شود و مسیرهای داخل پوشه‌های داخلی برنامه (`cache\wallpaper`، `cache\backdrops`، `backup`) هرگز به‌عنوان وضعیت اولیهٔ کاربر ذخیره نمی‌شوند. اگر کاربر پس از کرش و پیش از اجرای مجدد برنامه، والپیپر ویندوز را به‌صورت دستی تغییر داده باشد، این تغییر تشخیص داده می‌شود تا روی انتخاب جدید کاربر بازنویسی نشود.
   - **بازگردانی:** هنگام غیرفعال‌شدن `ApplyToDesktopWallpaper` در تنظیمات یا هنگام خروج کامل (در صورت فعال بودن `RestoreWallpaperOnExit`)، مسیر ذخیره‌شده و مقادیر خام `WallpaperStyle`/`TileWallpaper` مستقیماً از طریق `RestoreDesktopWallpaper(...)` بازگردانده می‌شوند (بدون اعمال `WallpaperFitMode` برنامه).
   - **محدودیت‌های شناخته‌شده در ویندوز ۱۰/۱۱ (مستند و بدون fallback مخرب):**
     - **Windows Slideshow (`BackgroundType = 2`) و Windows Spotlight (`BackgroundType = 3`):** آخرین تصویر استاتیک و چینش آن قابل بازگردانی است، اما فعال‌سازی خودکار چرخش `Windows Slideshow` یا سرویس پویا `Windows Spotlight` از طریق `SystemParametersInfoW` امکان‌پذیر نیست و به کاربر اطلاع داده می‌شود.
     - **پس‌زمینهٔ رنگ یکدست (`Solid Color` / `BackgroundType = 1`):** ارسال رشتهٔ خالی به `SPI_SETDESKWALLPAPER` در ویندوز ۱۰/۱۱ می‌تواند صفحه را سیاه کند؛ بنابراین برنامه تغییر مخرب انجام نمی‌دهد و محدودیت را گزارش می‌کند.
     - **والپیپر متفاوت برای هر مانیتور:** `SPI_GETDESKWALLPAPER` / `SPI_SETDESKWALLPAPER` تک‌مسیر سراسری است و چیدمان چندتصویریِ مجزا به‌ازای هر مانیتور با این API قابل ذخیره و بازگردانی نیست.
     - **فایل خارجی حذف‌شده:** اگر فایل تصویر اصلی کاربر در دیسک حذف شده باشد، پیام شفاف خطا ثبت می‌شود و جایگزین جعلی اعمال نمی‌گردد.
   - فایل‌های `.webp` به `.png` در `cache\wallpaper\desktop_wallpaper.png` تبدیل می‌شوند تا در تمام نسخه‌های ویندوز بدون خطای فرمت اعمال شوند.
   - در `MainWindow.xaml.cs` رویداد `SystemEvents.DisplaySettingsChanged` به `RefreshDisplayConfiguration()` متصل است و در `OnClosed` از آن جدا (Unsubscribe) می‌شود.

6. **آیکون دسکتاپ (شناور و استاندارد) و باز کردن عنوان (`OverlayService` + `DesktopShortcutService` + `ItemLauncher`):**
   - **آیکون شناور (`OverlayWindow`):** پنجره‌ای شفاف، بدون قاب، خارج از Taskbar و دارای پرچم‌های Win32 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` است؛ مختصات X/Y در `OverlayService.RefreshFromSettings()` در محدودهٔ `SystemParameters.VirtualScreen*` محدود (Clamp) می‌شود.
   - **آیکون استاندارد روی دسکتاپ (`DesktopShortcutService` + `WindowsDesktopShortcutAdapter`):** با استفاده از COM (`IShellLinkW` / `IPersistFile`) یک میان‌بُر واقعی با نام `باز کردن در Jellyfin.lnk` و آیکون چندسایزی `.ico` در پوشهٔ `DesktopDirectory` کاربر می‌سازد تا دقیقاً مانند سایر آیکون‌های ویندوز قابل جابه‌جایی و کلیک باشد.
   - **ارتباط بین‌پردازه‌ای (`--open-current`):** دابل‌کلیک روی میان‌بُر دسکتاپ برنامه را با آرگومان `--open-current` صدا می‌زند؛ اگر نمونهٔ اصلی در حال اجرا باشد از طریق `EventWaitHandle` (`Local\JellyfinDesktopSlideshow_OpenCurrent_v1`) عنوان فعلی را باز می‌کند و در غیر این صورت نمونهٔ اصلی را راه‌اندازی کرده و پس از بارگذاری عنوان را باز می‌نماید.
   - در تب ۴ تنظیمات (`DesktopIconMode`) کاربر می‌تواند بین «فقط آیکون شناور»، «فقط آیکون استاندارد دسکتاپ»، «هر دو» یا «غیرفعال» انتخاب کند.
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
