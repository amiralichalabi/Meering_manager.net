# Meeting Manager

A meeting and action-item manager for teams, built on **ASP.NET Core 10** (C#, EF Core, SQLite). It serves a Persian (right-to-left, Jalali calendar) web UI and a JSON API, and exchanges data with the Android app through a sync file.

## Features
- Meetings, action items (with sub-tasks, checklists, notes, progress history), projects, sprints, people and teams
- Dashboard, calendar, kanban board and personal notes
- Role-based permissions (admin / manager / user) enforced on every endpoint
- **Import / export:** Excel, Word and PDF reports, the Azure DevOps task sheet (`Full_Tasks.xlsx`, sheet `Items`) in and out, database backup
- Sync file shared with the Android app (last change per record wins, deletions travel as tombstones)
- Optional local AI assistant (LM Studio / Ollama, OpenAI-compatible); it only sees what the signed-in user may see and never changes data

## Requirements
[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Run
```
dotnet run --project src/MeetingManager.Web
```
Open <http://127.0.0.1:8000> and sign in with `admin` / `Admin@123`. **Change that password right away** (profile menu); the UI warns until you do.

| Setting | How | Default |
|---|---|---|
| Data folder (`meeting_manager.db`, backups, `llm.json`, `error.log`) | env `MEETING_MANAGER_DATA_DIR` | `./data` |
| Listen address | env `MM_URLS` | `http://127.0.0.1:8000` |
| Assistant server / model / timeout (s) | env `MM_LLM_URL`, `MM_LLM_MODEL`, `MM_LLM_TIMEOUT`, or the admin dialog (saved to `llm.json`, which wins) | none |

The database schema is stable: a `meeting_manager.db` from an earlier release opens unchanged (missing columns are added on start). Point `MEETING_MANAGER_DATA_DIR` at the folder that contains it.

## Test
```
dotnet test
```
The tests cover the API, permissions, exports, the sync merge rules (pinned reference vectors in `tests/MeetingManager.Tests/golden`), and opening a database written by an earlier release.

## Publish a standalone build
```
dotnet publish src/MeetingManager.Web -c Release -r win-x64 --self-contained true -o publish
```
or run the **Windows build** GitHub Actions workflow and download the artifact. Run `MeetingManager.exe` and open the address above.

## Security notes
- Binds to localhost by default; put a reverse proxy with HTTPS in front before exposing it to a network.
- Sessions use random 256-bit tokens (only a SHA-256 hash is stored), expire after 7 days and are revoked when the password changes or the user is deactivated.
- Passwords: PBKDF2-SHA256, 210,000 iterations, per-user salt. Failed logins are throttled per user name.
- Downloads use short-lived, single-URL tickets; responses carry a Content-Security-Policy, `nosniff`, `X-Frame-Options: DENY` and `no-store` for API data.

## Database: SQLite (default) or SQL Server

By default the app keeps everything in one SQLite file (`data/meeting_manager.db`). To use SQL Server instead, set either environment variables

```
MEETING_MANAGER_DB=sqlserver
MEETING_MANAGER_SQLSERVER=Server=myhost,1433;Database=MeetingManager;User Id=app;Password=...;TrustServerCertificate=True
```

or put a `database.json` next to the data folder's database:

```json
{ "provider": "sqlserver", "connection": "Server=myhost;Database=MeetingManager;Integrated Security=True;TrustServerCertificate=True" }
```

The database is created on first start if it does not exist (the login needs the `dbcreator` role, or create an empty database first), and the tables are created automatically. The Android/iPhone apps keep local SQLite and exchange data through the sync file, so they work the same with either engine. Moving existing SQLite data into SQL Server: export a sync file from the old setup and import it in the new one.
