# مدیریت جلسات (نسخهٔ .NET)

نسخهٔ ASP.NET Core 10 از برنامهٔ دسکتاپ «Meeting Manager Pro» (بدون بخش موبایل).
همان فرانت‌اند (HTML/JS در `wwwroot`)، همان API و همان ساختار SQLite؛ فایل `meeting_manager.db` نسخهٔ پایتون بدون تغییر باز می‌شود و فایل همگام‌سازی با اپ اندروید سازگار است.

## اجرا
```
dotnet run --project src/MeetingManager.Web
```
آدرس: http://127.0.0.1:8000 — ورود اولیه: `admin` / `Admin@123`

- پوشهٔ داده: متغیر محیطی `MEETING_MANAGER_DATA_DIR` (پیش‌فرض: پوشهٔ `data` کنار محل اجرا). برای استفادهٔ دوباره از دادهٔ نسخهٔ پایتون همان پوشهٔ `data` را نشان دهید.
- آدرس شنود: `MM_URLS` (پیش‌فرض `http://127.0.0.1:8000`).
- دستیار هوشمند محلی: `MM_LLM_URL`، `MM_LLM_MODEL`، `MM_LLM_TIMEOUT` یا فایل `llm.json` در پوشهٔ داده.

## آزمون
```
dotnet test
```
