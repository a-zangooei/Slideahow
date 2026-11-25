# Jellyfin Desktop Slideshow

یک برنامهٔ سبک و بومی ویندوز (**C# / .NET 10 LTS / WPF**) برای دریافت تصاویر پس‌زمینه (**Backdrop**) فیلم‌ها و سریال‌ها از سرور رسمی **Jellyfin**، نمایش اسلایدشو روی والپیپر دسکتاپ و قرار دادن یک آیکون شفاف و قابل‌کلیک روی دسکتاپ جهت باز کردن عنوانِ در حال نمایش.

## ویژگی‌های کلیدی
- **سبک و کاملاً بومی:** بدون Electron، WebView یا مرورگر داخلی؛ ساخته‌شده با WPF و Win32 API استاندارد.
- **سازگار با API رسمی Jellyfin:** احراز هویت با `POST /Users/AuthenticateByName`، دریافت کتابخانه‌ها، رسانه‌ها، تصاویر Backdrop با هدر `Authorization: MediaBrowser` (بدون قرار دادن توکن در URL یا لاگ).
- **امنیت نشست و تنظیمات:** نگهداری توکن دسترسی در **Windows Credential Manager** و ذخیرهٔ تنظیمات غیرمحرمانه به‌صورت اتمیک در `%LOCALAPPDATA%\JellyfinDesktopSlideshow\settings.json`.
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
dotnet test
```

### ۲. ساخت پروژه (Debug)
```powershell
dotnet build -c Debug
```

### ۳. اجرای برنامه در محیط توسعه
```powershell
dotnet run --project src/JellyfinDesktopSlideshow/JellyfinDesktopSlideshow.csproj
```

### ۴. ساخت نسخهٔ نهایی (Release) برای نصب روی ویندوز
برای ساخت فایل‌های انتشار (وابسته به Desktop Runtime نصب‌شده با حجم کم):
```powershell
dotnet publish src/JellyfinDesktopSlideshow/JellyfinDesktopSlideshow.csproj -c Release -r win-x64 --self-contained false -o ./publish/win-x64-fd
```

یا ساخت نسخهٔ **کاملاً مستقل (Self-Contained Single File)** بدون نیاز به نصب پیش‌فرض .NET روی سیستم مقصد:
```powershell
dotnet publish src/JellyfinDesktopSlideshow/JellyfinDesktopSlideshow.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish/win-x64-standalone
```

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
