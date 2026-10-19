# VMM Facility Management — Expense Tracker (MVP)

A local web app for the Facility Manager to record expenses and export a PDF report.
It runs only on this Mac (localhost) and works without internet access.

- **Frontend:** React + TypeScript (Vite) in `frontend/`
- **Backend:** ASP.NET Core (.NET 10) + EF Core + SQLite in `backend/VmmFacility.Api/`

## Start the app

Requirements: .NET 10 SDK and Node.js (npm). Both are only needed on this Mac.

```bash
./start.sh
```

The script builds the frontend, starts the backend, and opens <http://localhost:5180> in your
browser. The backend serves the frontend, so this one process runs the whole app.
Press **Ctrl+C** in the terminal to stop it.

The first time you run it, `npm install` downloads the frontend packages, and the backend build
downloads its NuGet packages. After that, the app runs without internet access.

On first launch the app asks you to create the Facility Manager username and password.
Sessions end after 30 minutes of inactivity or when the browser is closed.

## Where data is stored

The SQLite database is at:

```
~/Library/Application Support/VMM Facility Management/vmm-facility.db
```

The same folder holds `keys/`, which signs the sign-in session cookie. Back up the whole
folder to keep your data. Set `VMM_DATA_DIR` to use a different folder.

## Network access

The backend listens only on `127.0.0.1` and `[::1]` (port 5180, or `VMM_PORT`) and only accepts
`localhost` host names. Other devices on the network can't reach it.

## API

All expense endpoints require a signed-in session. Requests that change data must include the
header `X-VMM-Request: 1`. Amounts are sent and returned as decimal strings, such as `"1250.50"`;
dates use `yyyy-MM-dd`.

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/expenses` | List all expenses, newest date first |
| GET | `/api/expenses/{id}` | Read one expense |
| POST | `/api/expenses` | Create an expense |
| PUT | `/api/expenses/{id}` | Edit an expense |
| GET | `/api/expenses/report.pdf` | Download the PDF report |
| GET | `/api/auth/session` | Show whether setup is needed and who is signed in |
| POST | `/api/auth/setup` | Create the single account (first launch only) |
| POST | `/api/auth/login` · `/api/auth/logout` | Sign in or sign out |

## Development

To run with hot reload, use two terminals:

```bash
dotnet run --project backend/VmmFacility.Api   # API on http://localhost:5180
cd frontend && npm run dev                     # UI on http://localhost:5173 (proxies /api)
```

The database schema is created automatically on first start (`EnsureCreated`). If the schema
changes in a later release, add EF Core migrations.

PDF reports are generated with [QuestPDF](https://www.questpdf.com/) under its Community
license. That license is free for organisations with less than USD 1M in annual revenue.
