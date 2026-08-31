# 🚀 How to Run Locally

Step-by-step guide to get the Showcase Event Desk running on your machine.

---

## Prerequisites

| Requirement | Notes |
|:---|:---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Required for both backend API and web dashboard |
| SQL Server | LocalDB is included with Visual Studio and works out of the box |
| [Flutter SDK](https://flutter.dev/docs/get-started/install) | *Optional* — only needed for the mobile app |

> **No manual database setup required.** The database is automatically created and seeded with demo data (buildings, floors, rooms, domains, users) on first run via EF Core migrations.

---

## Option 1 — One-Click Launch (Windows)

Double-click the batch file at the project root:

```bash
start_app.bat
```

This opens two terminal windows — one for the API, one for the web app — and starts both automatically.

---

## Option 2 — Manual Launch

### Step 1 · Start the Backend API

```bash
cd backend-api
dotnet restore
dotnet run --launch-profile http
```

> API is now live at **http://localhost:5287**

### Step 2 · Start the Web Dashboard

Open a **new terminal**:

```bash
cd web
dotnet restore
dotnet run --launch-profile http
```

> Web app is now live at **http://localhost:5182**

---

## 🔐 Demo Credentials

Log in with any of these pre-seeded accounts to explore different roles:

| Role | Email | Password |
|:---|:---|:---|
| 🛡️ **Admin** | `admin@fyp.local` | `Admin@123` |
| 🧑‍🏫 **Advisor** | `advisor.ai@fyp.local` | `Advisor@123` |
| ⭐ **Evaluator** | `eval.ai@fyp.local` | `Eval@123` |
| 👨‍🎓 **Student** | `student@fyp.local` | `Student@123` |

---

## 🧪 Running Tests

```bash
dotnet test tests/BackendApi.Tests
```

---

## 🔧 Configuration

The backend connection string is in `backend-api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=FypEventManagement;..."
  }
}
```

Change this if you're using a remote SQL Server instance instead of LocalDB.

---

## 💡 Tips

- **Room codes** follow the format `A-001`, `B-102`, `C-201` (Building letter + floor/room number).
- **Email Outbox** — View test emails at the Admin → Settings tab or via `GET /api/notifications/email-outbox`.
- **Audit Logs** — Admins can view full system activity from the sidebar (red eye icon at the bottom).
- **File uploads** are stored physically in the `uploads/` folder; only metadata is saved in the database.
