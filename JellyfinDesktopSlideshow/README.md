# Jellyfin Desktop Slideshow

یک برنامهٔ سبک و بومی ویندوز (**C# / .NET 10 LTS / WPF**) برای دریافت تصاویر پس‌زمینه (**Backdrop**) فیلم‌ها و سریال‌ها از سرور رسمی **Jellyfin**، نمایش اسلایدشو روی والپیپر دسکتاپ و قرار دادن یک آیکون شفاف و قابل‌کلیک روی دسکتاپ جهت باز کردن عنوانِ در حال نمایش.

## ویژگی‌های کلیدی
- **سبک و کاملاً بومی:** بدون Electron، WebView یا مرورگر داخلی؛ ساخته‌شده با WPF و Win32 API استاندارد.
- **سازگار با API رسمی Jellyfin:** احراز هویت اصلی با **کلید API** (`Dashboard -> Advanced -> API Keys` همراه با اعتبارسنجی از `GET /System/Info` و دریافت کتابخانه/رسانه از `/Library/MediaFolders` و `/Items`) و پشتیبانی از ورود با **نام کاربری و رمز عبور** (`POST /Users/AuthenticateByName`)، همگی با هدر امن `Authorization: MediaBrowser` (بدون قرار دادن کلید یا توکن در URL یا لاگ).
- **امنیت نشست و تنظیمات:** نگهداری کلید API یا توکن دسترسی در **Windows Credential Manager** و ذخیرهٔ تنظیمات غیرمحرمانه به‌صورت اتمیک در `%LOCALAPPDATA%\JellyfinDesktopSlideshow\settings.json`.
- **کش هوشمند محلی:** دانلود، اعتبارسنجی فرمت واقعی باینری تصاویر (JPEG/PNG/WebP/BMP)، حذف خودکار فایل‌های قدیمی/اضافی، پشتیبانی از آفلاین و تصویر جایگزین.
- **موتور اسلایدشوی کم‌مصرف:** استفاده از `DispatcherTimer` (بدون حلقهٔ پرمصرف)، حالت تصادفی با پنجرهٔ عدم تکرار، حالت ترتیبی، قبلی/بعدی و Backoff هوشمند هنگام قطع سرور.
- **والپیپر و چند مانیتور:** تغییر والپیپر از طریق `SystemParametersInfoW` و رجیستری دسکتاپ، تبدیل خودکار WebP به PNG، پشتیبانی از حالت‌های `Fill / Fit / Stretch / Center / Span`، واکنش به تغییر مانیتورها و امکان بازگردانی والپیپر اولیه هنگام خروج.
- **آیکون شناور دسکتاپ (`OverlayWindow`) و باز کردن عنوان (`ItemLauncher`):** کلیک روی آیکون شناور (با `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`) عنوان فعلی را در **مرورگر پیش‌فرض**، **برنامهٔ Jellyfin دسکتاپ** (از طریق `POST /Sessions/{id}/Viewing`) یا **الگوی URL سفارشیِ اعتبارسنجی‌شده** باز می‌کند.
- **System Tray و اجرای خودکار:** کنترل اسلایدشو، باز کردن تنظیمات ۷بخشی و خروج از طریق نوار اعلان ویندوز و اجرای خودکار با رجیستری کاربر جاری (`HKCU\...\Run`).

---

## پیش‌نیازهای توسعه و اجرا
- سیستم‌عامل **Windows 10 (1809+) یا Windows 11**
- **‎.NET 10 SDK** (برای ساخت و اجرای تست‌ها) یا **‎.NET 10 Desktop Runtime** (برای اجرای نسخهٔ منتشرشده روی سیستم کاربر)

---

## دستورات ساخت، تست و اجرا

تمام دستورات زیر را در مسیر ریشهٔ پروژه (`JellyfinDesktopSlideshow`) اجرا کنید:

### ۱. اجرای تست‌های واحد (xUnit)
```powershell
dotnet restore JellyfinDesktopSlideshow.sln
dotnet build JellyfinDesktopSlideshow.sln --configuration Release --no-restore
dotnet test JellyfinDesktopSlideshow.sln --configuration Release --no-build --verbosity normal
```

> **یکپارچه‌سازی مداوم (GitHub Actions CI):** فایل `.github/workflows/dotnet-wpf-ci.yml` به‌صورت خودکار روی `windows-latest` با `.NET 10 SDK` (`10.0.x`) در هر `push` (تمام شاخه‌ها)، `pull_request` و اجرای دستی (`workflow_dispatch`) مراحل بالا را اجرا می‌کند.

### ۲. ساخت پروژه (Debug)
```powershell
dotnet build -c Debug
```

### ۳. اجرای برنامه در محیط توسعه
```powershell
dotnet run --project src/JellyfinDesktopSlideshow/JellyfinDesktopSlideshow.csproj
```

### ۴. ساخت نسخهٔ نهایی (`.exe` مستقل و `.msi` نصبی)
بسته‌های خروجی روی `win-x64` به‌صورت **Self-Contained** ساخته می‌شوند تا کاربر نهایی بدون نیاز به نصب **‎.NET 10 Desktop Runtime** بتواند برنامه را مستقیماً اجرا کند (در مقابل خروجی Framework-Dependent که حدود ۱ تا ۲ مگابایت حجم دارد ولی به نصب پیش‌فرض Runtime نیازمند است؛ خروجی Self-Contained حجم بیشتری — حدود ۱۴۰ تا ۱۷۰ مگابایت — دارد اما بدون پیش‌نیاز روی ویندوز ۱۰/۱۱ اجرا می‌شود).

- **ساخت فایل `.exe` قابل‌حمل (Single-File Portable):**
```powershell
dotnet publish src/JellyfinDesktopSlideshow/JellyfinDesktopSlideshow.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o ./publish/win-x64-standalone
```

- **ساخت فایل نصبی `.msi` با WiX Toolset v5 (`WixToolset.Sdk/5.0.2`):**
```powershell
dotnet publish src/JellyfinDesktopSlideshow/JellyfinDesktopSlideshow.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o ./publish/win-x64-msi-payload
dotnet restore installer/JellyfinDesktopSlideshow.Installer.wixproj
dotnet build installer/JellyfinDesktopSlideshow.Installer.wixproj -c Release --no-restore -p:PayloadDir="$(Resolve-Path ./publish/win-x64-msi-payload)" -p:AppVersion="1.0.0" -o ./publish/win-x64-msi
```

### ۵. دانلود Artifactها از GitHub Actions
پس از موفقیت Build و تست‌ها در اجرای workflow (`.github/workflows/dotnet-wpf-ci.yml`)، در صفحهٔ آن اجرا روی GitHub وارد بخش **Artifacts** شوید:
- **`JellyfinDesktopSlideshow-<version>-build<N>-win-x64-exe`**: فایل `.exe` قابل‌حمل (بدون نیاز به نصب؛ کافی است دانلود و اجرا کنید).
- **`JellyfinDesktopSlideshow-<version>-build<N>-win-x64-msi`**: نصاب استاندارد ویندوز (نصب در `Program Files\Jellyfin Desktop Slideshow`، افزودن میانبر در Start Menu و پشتیبانی از حذف/ارتقا از طریق Apps & Features؛ نیازمند دسترسی مدیر سیستم هنگام نصب).
- **`test-results`**: فایل گزارش تست‌های خودکار با فرمت `.trx`.

---

## راهنمای ساختار پروژه و مستندات تکمیلی
برای جلوگیری از سردرگمی در توسعهٔ بعدی و نگهداری پروژه، مستندات زیر در پوشهٔ `docs` قرار دارند:
- [معماری پروژه، سرویس‌ها و قراردادهای API (`docs/ARCHITECTURE.md`)](docs/ARCHITECTURE.md)
- [راهنمای تست، عیب‌یابی، لاگ‌ها و انتشار (`docs/TESTING_AND_RELEASE.md`)](docs/TESTING_AND_RELEASE.md)

## مسیرهای ذخیره‌سازی محلی روی ویندوز
- تنظیمات JSON: `%LOCALAPPDATA%\JellyfinDesktopSlideshow\settings.json`
- کش تصاویر Backdrop: `%LOCALAPPDATA%\JellyfinDesktopSlideshow\cache\images\`
- فایل تبدیل‌شدهٔ والپیپر (در صورت WebP بودن): `%LOCALAPPDATA%\JellyfinDesktopSlideshow\cache\wallpaper\`
- لاگ‌های برنامه (حداکثر ۵ فایل ۵ مگابایتی با سانسور توکن/رمز): `%LOCALAPPDATA%\JellyfinDesktopSlideshow\logs\`
- کلید اجرای خودکار (در صورت فعال‌سازی): `HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Run\JellyfinDesktopSlideshow`
