# IronForge — Agentic Gym Management Platform

IronForge is a multi-tenant SaaS for gyms. Each gym (tenant) manages members, membership plans, billing, attendance and member communication. AI agents help staff run the gym and coach members on their training.

| App | Who | Project | Local URL |
|---|---|---|---|
| Gym dashboard (web) | Gym admins and trainers | `IronForge.Web` + `IronForge.UI.Admin` | https://localhost:7056 |
| Member app (mobile) | Gym members | `IronForge` (MAUI) + `IronForge.UI.Member` | Android / Windows |
| Platform console (web) | IronForge operators | `IronForge.Platform` + `IronForge.UI.Platform` | https://localhost:7090 |
| API | All apps, payment provider webhooks | `IronForge.Api` | https://localhost:7226 |
| MCP server | External agents | `IronForge.Mcp` | |

## Architecture

```
IronForge.Shared         contracts (DTOs, permissions, plans), API clients, shared UI (sign-in, charts)
IronForge.UI.Admin       gym dashboard pages          ─┐
IronForge.UI.Member      member app pages              ├─ Razor class libraries hosted by the apps
IronForge.UI.Platform    platform console pages       ─┘
IronForge.Application    domain entities and services (Gym/*, Agents/*, Tenants/*, Auth/*)
IronForge.Infrastructure EF Core + PostgreSQL, SMTP email, background jobs, demo seeder
IronForge.Payments       online payment providers: HitPay (Malaysia), Safepay (Pakistan), test checkout
IronForge.Api            REST API, rate limiting, payment pages
IronForge.Migrator       migrations and demo seeding
```

- **Multi-tenancy:** every gym's data is isolated by EF Core query filters on the tenant in the signed-in user's token. Cross-tenant operator views read through explicit, permission-checked services.
- **Billing:** `BillingBackgroundService` runs hourly, one instance at a time (a PostgreSQL advisory lock). Each run issues invoices ahead of each billing date, marks late invoices overdue, expires memberships after the grace period, and sends reminders before and after the due date. Every step is idempotent.
- **Payments:** each gym connects its own **HitPay** (Malaysia: FPX, DuitNow, Touch 'n Go, cards) or **Safepay** (Pakistan) account under *Settings → Online payments*, and members pay invoices in the app straight into it. An invoice is marked paid only on what the provider's API reports (webhook, return page or "I've paid"), idempotently. Without a provider, members pay at the desk or by bank transfer; demo deployments can offer a **simulated checkout** (`Payments:AllowSimulated`) where no money moves.
- **Email:** emails are written to an outbox in the same transaction as the change that caused them. `EmailOutboxProcessor` delivers them with retries and backoff.
- **AI agents:** each agent is a database definition with governed tools (Microsoft.Extensions.AI on OpenAI). An agent never has more authority than the user it acts for. Risky actions require human approval, and High-risk ones require a second admin (four-eyes).
  - **Gym Admin Agent** (Admin, Trainer): searches members, attendance, revenue and overdue invoices. With approval, it can send payment reminders, freeze memberships, record cash payments (capped at 2,000) and send announcements.
  - **Fitness Coach** (Member): reads the member's own profile, routines, workouts and progress. It can log workouts and measurements, set goals and design routines. It only ever sees the signed-in member's data.
  - **Insights Agent** (Admin): read-only analysis of the activity log and key numbers.

### Plans (platform subscriptions)

| Plan | Modules | AI tokens / month |
|---|---|---|
| Starter | Gym core, Billing, Engagement (Gym Admin Agent) | 500k |
| Professional | + Coaching (routines, progress, Fitness Coach) + Activity insights | 2M |
| Enterprise | Same as Professional | 10M |

### Roles

| Permission area | Admin | Trainer | Member |
|---|:-:|:-:|:-:|
| Members, check-ins, routines | ✓ | ✓ | own data only |
| Billing, plans, settings, staff, announcements | ✓ | | |
| Approvals, agents, activity log | ✓ | | |
| Pay own invoices, log workouts, AI coach | | | ✓ |

Platform operators (`PlatformAdmin`) use the platform console and don't belong to any gym.

## Run locally

Prerequisites: .NET 10 SDK, Docker.

```bash
# 1. Database and a mail catcher (Mailpit inbox: http://localhost:8025)
docker run -d --name ironforge-db -e POSTGRES_PASSWORD=dev -p 5432:5432 postgres:17
docker run -d --name ironforge-mail -p 1025:1025 -p 8025:8025 axllent/mailpit:v1.21

# 2. Secrets for the API (once)
cd IronForge.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=ironforge;Username=postgres;Password=dev"
dotnet user-secrets set "JwtSettings:Key" "<at least 32 random characters>"
dotnet user-secrets set "AISettings:ApiKey" "<OpenAI API key>"
cd ..

# 3. Migrate and load demo data (--reset wipes existing data first)
export ConnectionStrings__DefaultConnection="Host=localhost;Database=ironforge;Username=postgres;Password=dev"
DOTNET_ENVIRONMENT=Development dotnet run --project IronForge.Migrator -- --seed-demo --reset

# 4. Run the apps (separate terminals)
dotnet run --project IronForge.Api --launch-profile https
dotnet run --project IronForge.Web --launch-profile https
dotnet run --project IronForge.Platform --launch-profile https
```

To try real sandbox payments, sign in as a gym admin, open *Settings → Online payments* and enter the gym's HitPay sandbox keys (MYR gyms) or Safepay sandbox keys (PKR gyms, e.g. Iron Paradise). Members then pay on the provider's sandbox checkout (HitPay test card `4242 4242 4242 4242`); returning to IronForge confirms the payment, so no webhook tunnel is needed locally. The provider adapters can be checked against the real sandboxes with `IRONFORGE_HITPAY_SANDBOX_KEY`, `IRONFORGE_SAFEPAY_SANDBOX_PUBLIC_KEY` and `IRONFORGE_SAFEPAY_SANDBOX_SECRET` set: `dotnet test tests/IronForge.Application.Tests --filter ProviderSandboxTests`.

The whole stack can also run with `docker compose up` (Caddy serves `https://localhost`, `https://platform.localhost` and `https://api.localhost`).

## Demo accounts

Every demo account's password is `Password123!`. Sign in with the email address or the username.

| Account | Where | What to try |
|---|---|---|
| `operator@ironforge.dev` | Platform console | Overview; Titan (past due) → set Active; add a module; roll out agent templates |
| `admin@ironparadise.demo` | Gym dashboard | Dashboard, approve a join request, record cash, send a reminder, ask the AI assistant "who is overdue?" |
| `trainer1@ironparadise.demo` | Gym dashboard | Trainer view: members and routines, no billing |
| `member@ironparadise.demo` | Member app | Home, pay the open invoice, log a workout, ask the coach "how am I progressing?" |
| `admin@flexzone.demo` | Gym dashboard | Starter plan in trial: no coaching module |
| `admin@titanstrength.demo` | Gym dashboard | Enterprise, past due |

| Gym | Plan | Status | Join code |
|---|---|---|---|
| Iron Paradise Gym (PKR, Lahore) | Professional | Active | `PARADISE` |
| FlexZone Fitness (USD, New York) | Starter | Trial, ends in 5 days | `FLEXZONE` |
| Titan Strength Club (GBP, Manchester) | Enterprise | Past due | `TITAN777` |
| Old Barn Gym (USD) | Starter | Canceled (no access) | `OLDBARN1` |

New members can join from the mobile app's **Join your gym** page with one of these codes.

## Tests

```bash
dotnet test IronForge.Server.slnf   # unit tests + PostgreSQL integration tests (Testcontainers, needs Docker)
```

The integration tests cover:
- tenant isolation
- members only seeing their own data
- joining with a code and approval
- the billing job (invoicing, reminders, overdue, expiry, freeze)
- idempotent card payments
- agent tool governance
- the demo seeder
- the platform operator views

## Production notes

- Gyms connect their own payment provider in the dashboard (live keys) and register the webhook URL it shows. Keep `Payments__AllowSimulated=false`; optionally set `Payments__EncryptionKey`.
- Set `Email__Host`, `Email__Username`, `Email__Password` and `Email__FromAddress`; otherwise emails are only logged.
- The web apps persist their Data Protection keys in the `/keys` volume so sign-ins survive deployments.
- CORS allows only the web origins in `appsettings.Production.json`.
- Sign-in, joining, password reset and payment callbacks are rate-limited per IP.
- Members can ask for their data to be deleted: **Member → Delete personal data** erases the profile and training data, and keeps invoices anonymously for accounting.
