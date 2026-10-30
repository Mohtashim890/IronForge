# IronForge — Portfolio Project Brief

> **For the AI updating the portfolio website:** this file is the source material for the IronForge project entry. Everything in it was taken from the source code and repository. Use it to write the project card, the project detail page and any tech-stack/skills sections.
> - Keep claims within what is written here. IronForge is built for production but has **no paying gyms yet**: describe it as a "production-grade SaaS platform", not as a product with customers or revenue.
> - Section 13 has ready-made copy at three lengths, and section 14 suggests page layout and visuals.
> - Items marked **[verify]** should be confirmed by the owner before publishing.

---

## 1. At a glance

| | |
|---|---|
| **Name** | IronForge |
| **Tagline** | Agentic, multi-tenant gym management SaaS |
| **Type** | Full-stack SaaS platform: web dashboard, cross-platform mobile app, operator console, REST API, MCP server |
| **Role** | Solo developer: product design, architecture, backend, frontend, mobile, AI, DevOps |
| **Timeline** | 2026 (repository baseline 6 Oct 2026, active development since) |
| **Status** | Feature-complete v1, being hardened for real gym customers; deployed with CI/CD to a VPS |
| **Target markets** | Gyms in Pakistan and Malaysia first (English UI; multi-currency per gym, e.g. PKR, USD, GBP) |
| **Core stack** | .NET 10, ASP.NET Core, Blazor, .NET MAUI, EF Core 10, PostgreSQL, OpenAI via Microsoft.Extensions.AI, Model Context Protocol, HitPay, Safepay, Docker, Caddy, GitHub Actions |
| **Source** | https://github.com/Mohtashim890/IronForge **[verify: public or private?]** |
| **Live** | Gym dashboard `https://mohtashim.dev`, platform console `https://platform.mohtashim.dev`, API `https://api.mohtashim.dev` **[verify: live and OK to link?]** |

---

## 2. The problem

Small and mid-sized gyms usually run on spreadsheets, paper registers and WhatsApp. Typical pains:
- Memberships lapse without anyone noticing; invoices and reminders are manual; overdue members keep walking in.
- Front-desk check-in is slow, and members share cards or passes.
- Existing fingerprint terminals (very common in South Asia) are standalone and know nothing about who has paid.
- Trainers have no simple way to hand out routines or follow members' progress.
- Owners can't easily answer "how much did we make this month?" or "who is about to expire?".

## 3. The solution

IronForge gives every gym its own isolated workspace with four connected apps:

1. **Gym dashboard (web)** for owners, admins and trainers: members, plans, billing, check-ins, fingerprint terminals, training content, announcements, staff, AI agents and approvals.
2. **Member app (Android, iOS, Windows, macOS)**: an offline-capable rotating QR pass, membership and invoices with online payment, routines, workout logging, progress charts, goals and an AI fitness coach.
3. **Platform console (web)** for the SaaS operator: onboard gyms, manage subscription plans and add-on modules, AI token budgets, agent template rollout and a cross-tenant audit log.
4. **AI agents with governance**: assistants that act through the same permission-checked business services as the UI, with human approval and four-eyes control for risky actions, also exposed to external agents through an MCP server.

---

## 4. Feature overview

### Gym dashboard (Admins and Trainers)
- **Members**: profiles with photos, join requests (members join with an 8-character gym code), approve/decline, freeze, cancel, reactivate, GDPR-style anonymisation that keeps financial records.
- **Membership plans and billing**: monthly, quarterly or yearly plans; automatic invoices, overdue tracking, grace periods, automatic expiry, reminder emails before and after the due date; cash and bank-transfer recording; revenue reporting.
- **Check-ins, four ways**: front desk by name, staff scanning the member's pass, unattended **kiosk** devices (`/kiosk`, camera or USB/Bluetooth barcode scanner), and members scanning a rotating **entrance QR code** shown at the door. Admins choose which methods are enabled.
- **Fingerprint terminals (ZKTeco)**: integrates existing hardware through the ZKTeco push protocol. Imports the terminal's user list, lets staff link fingerprint IDs to members, and pushes access windows to the terminal so it **refuses unpaid members on its own, even offline**. Includes a browser-based **terminal simulator** for demos and testing. Never stores biometric data.
- **Training**: shared and gym-specific exercise library, routines assigned by trainers.
- **Engagement**: announcements to all, active, overdue or per-plan members (in-app plus email), in-app notification centre.
- **Governance**: staff management and invitations, AI agent configuration, approval queue, full activity log.

### Member app (.NET MAUI)
- **Pass tab**: rotating QR pass computed **offline** on the phone (new code every 30 s), scanned at the front desk or a kiosk; phone unlock (biometric/PIN) before the pass is shown; a pass is bound to one phone.
- Membership status, invoices and **Pay now** (the gym's HitPay or Safepay checkout in the system browser), or the gym's payment instructions when it takes no online payments.
- Assigned and self-made routines, workout logging, body metrics, goals, weekly volume and weight charts, streaks.
- Visit history, notifications, profile and photo.
- **AI Fitness Coach** that reads only the member's own data and can log workouts, record measurements, set goals and design routines.

### Platform console (SaaS operator)
- Overview metrics across all gyms; gym onboarding in one transaction (gym, settings, default AI agents, admin invitation email).
- Subscription status (Trial, Active, PastDue, Canceled), suspension, plan changes, add-on modules, monthly AI token limits.
- Agent template catalogue with versioned rollout to all gyms or one gym.
- Cross-tenant audit log.

### Subscription plans (defined in code)

| Plan | Modules | AI tokens / month |
|---|---|---|
| Starter | Core, Billing, Engagement | 500k |
| Professional (default) | + Coaching, Audit insights | 2M |
| Enterprise | + Access control (fingerprint terminals) | 10M |

Features, AI tools and AI agents switch on and off with the gym's modules; upgrading a plan installs the newly covered agents straight away.

---

## 5. AI agents (key differentiator)

Three agents, defined as versioned templates and installed per gym as database rows:

| Agent | Users | What it does |
|---|---|---|
| **Gym Admin Agent** | Admins, Trainers | Searches members, attendance, revenue, overdue invoices, expiring memberships, routines. With approval: freeze a membership, send a payment reminder, record a cash payment (capped at 2,000), send an announcement. |
| **Fitness Coach** | Members | Reads the member's profile, goals, routines, workouts and progress; logs workouts and measurements, sets goals, builds routines. Can only ever see the signed-in member. |
| **Insights Agent** | Admins | Read-only analyst over the activity log and key numbers. |

**Governance model (what makes it safe):**
- **An agent never has more authority than the user it acts for.** The model is only offered tools whose permission both the user and the agent hold, and whose subscription module is enabled.
- Every tool calls the same application services as the UI, so tenant isolation, validation and auditing are identical.
- **Risk-based approvals**: Medium-risk actions need the requester's confirmation; High and Critical need a **second admin (four-eyes)**. Amount caps are enforced on payment tools.
- **Token quotas** per gym per month (HTTP 429 when exceeded); `standard` and `economy` model profiles per plan.
- Prompt-injection hardening: instructions treat tool results as data, and agents re-query data instead of reusing earlier numbers.
- **MCP server**: the same governed tool catalogue is exposed to external AI agents via the Model Context Protocol, with delegated credentials whose rights are the intersection of user, agent and delegation scopes. External agents can't run approval-gated tools. A demo MCP client is included.

---

## 6. Architecture

### Solution layout (15 projects)

```
                 ┌────────────── Front ends ──────────────┐
  IronForge.Web ── UI.Admin ─┐                            │
  IronForge.Platform ── UI.Platform ─┼── IronForge.Shared (DTOs, permissions,
  IronForge (MAUI) ── UI.Member ─┘     API clients, auth, UI kit)
                                            │  HTTPS + JWT
                 ┌──────────────── Back end ┴──────────────┐
  IronForge.Api ─┐
  IronForge.Mcp ─┼─► IronForge.Infrastructure ─► IronForge.Application ─► Shared
  IronForge.Migrator ┘   (EF Core, Postgres,     (entities + business
                          payments, SMTP, jobs)   services + AI agents)
```

| Layer | Projects | Responsibility |
|---|---|---|
| Shared contracts | `IronForge.Shared` | DTOs, permission constants, subscription catalogue, typed API clients, JWT auth for clients, shared pages and UI components (charts, QR scanner, modals) |
| UI libraries | `UI.Admin`, `UI.Member`, `UI.Platform` | Razor class libraries with each app's pages, reused by different hosts |
| Hosts | `IronForge.Web`, `IronForge.Platform` (Blazor Server), `IronForge` (MAUI Blazor Hybrid) | Thin hosts per audience |
| API | `IronForge.Api` | REST API, rate limiting, payment webhooks and return pages, ZKTeco endpoints, background jobs |
| Application | `IronForge.Application` | Domain entities and services: members, billing, check-ins, access control, fitness, notifications, tenants, auth, AI agents |
| Infrastructure | `IronForge.Infrastructure` | EF Core + PostgreSQL, repositories, SMTP, background services, demo seeder |
| Payments | `IronForge.Payments` | Payment provider module: HitPay (Malaysia), Safepay (Pakistan), test checkout, behind one `IPaymentProvider` contract |
| Agents | `IronForge.Mcp`, `IronForge.Mcp.Client` | MCP server and demo client |
| Ops | `IronForge.Migrator` | Migrations, demo seeding, operator tasks |
| Tests | `Application.Tests`, `Application.IntegrationTests` | Unit tests and integration tests against real PostgreSQL |

**Key design decisions**
- **Clean, layered architecture** with a strict dependency direction; the Application layer doesn't reference EF Core (repository abstraction with server-side projections).
- **One UI codebase, many hosts**: Blazor components in Razor class libraries run both in Blazor Server web apps and inside the native MAUI app (BlazorWebView).
- **Authorization lives in services**, not controllers, so the API, the MCP server and AI agents all get the same checks.
- **Thin controllers** and a uniform `ServiceResult<T>` pattern mapped to HTTP status codes.

### Multi-tenancy
- One PostgreSQL database, row-level isolation: every tenant-owned table has a `TenantId` and an **EF Core global query filter** driven by the `tenant_id` claim in the JWT.
- With no tenant in context, tenant data simply returns nothing (secure by default).
- Cross-tenant reads (operator views, anonymous join, background jobs) only go through a few explicit, permission-checked stores.
- Background jobs process each tenant in its own DI scope with an explicit execution context.
- Integration tests prove tenant isolation and member self-scoping.

### Reliability patterns
- **Transactional outbox** for email: emails are written in the same transaction as the change; a background processor sends them with exponential backoff.
- **Idempotent billing engine**: hourly job, single-instance across servers using **PostgreSQL advisory locks**; every rule can be rerun safely.
- **Authoritative, idempotent payment confirmation**: webhooks are signature-checked with each gym's keys, and an invoice is marked paid only on the provider API's own status (processed-event table, amount and currency checks).
- **Optimistic concurrency** with PostgreSQL `xmin` on invoices and invitations.
- **Atomic invoice numbering** via a single SQL `UPDATE … RETURNING`.
- Time-zone-correct calendar logic (`DateOnly` in the gym's time zone, injected `TimeProvider` for testable time).

---

## 7. Security highlights

- **Custom JWT authentication** with 15-minute access tokens and rotating refresh-token families (reuse detection).
- **Two-step sign-in**: account-level token first, then a tenant-scoped token with role-based permission claims.
- **TOTP two-factor authentication** (RFC 6238) implemented from scratch: required for admins and operators in production, AES-GCM encrypted secrets, single-use codes, hashed recovery codes.
- **Every request re-validated**: account active, security stamp, membership and role unchanged, gym subscription active (cached 30 s). Removing a staff member or resetting a password ends their sessions immediately.
- **Account lockout** with exponential back-off, plus per-IP **rate limiting** policies (auth, anonymous, webhook, device).
- **Fine-grained permissions** (Admin, Trainer, Member, PlatformAdmin) enforced in services; UI gated with policy-based authorization.
- **Anti-fraud check-in design**: HMAC-signed, time-stepped member passes (replay protection with a last-used step), per-member key versions, one phone per pass, biometric phone unlock; forged or copied passes are audited.
- **Device authentication** for kiosks (one-time pairing code exchanged for a hashed device key) and fingerprint terminals (serial number + optional IP allow-list) with minimal device-only permissions.
- **Privacy by design**: no biometric templates stored (tests assert it), member data anonymisation, no account enumeration on password reset.
- Hardened deployment: Caddy with HSTS, nosniff, frame denial, referrer and permissions policies; camera allowed only on the gym dashboard.
- Full **audit trail** of significant actions across users, agents and devices.

---

## 8. Integrations

| Integration | Details |
|---|---|
| **OpenAI** | Responses API through Microsoft.Extensions.AI; tool calling with governed tools |
| **Model Context Protocol** | MCP server (ModelContextProtocol.AspNetCore) exposing the tool catalogue to external agents |
| **HitPay & Safepay** | Each gym connects its own account (keys verified, stored encrypted) and is paid directly: FPX, DuitNow and Touch 'n Go in Malaysia, cards and wallets in Pakistan; hosted checkout, signed webhooks |
| **ZKTeco fingerprint terminals** | Push protocol (ADMS/iclock): user import, access-window sync, attendance log ingestion, offline enforcement |
| **SMTP email** | MailKit, outbox with retries (Mailpit in development) |
| **QR codes** | QRCoder (server-side SVG), jsQR in the browser/webview for camera scanning |
| **Native device features** | Camera (Android/iOS/Windows), biometric unlock (Android BiometricPrompt, iOS LocalAuthentication, Windows Hello), SecureStorage |
| **Observability** | OpenTelemetry (OTLP) to Grafana Cloud, health checks (`/health/live`, `/health/ready`) |

---

## 9. Tech stack

**Languages:** C# 14, Razor, SQL, JavaScript, CSS, YAML, Bash

**Backend**
- .NET 10, ASP.NET Core Web API
- Entity Framework Core 10 with Npgsql (PostgreSQL 17; Supabase in hosted environments)
- Microsoft.Extensions.AI + OpenAI
- ModelContextProtocol 2.2
- MailKit, QRCoder
- Custom JWT (System.IdentityModel.Tokens.Jwt), ASP.NET Core rate limiting, health checks
- OpenTelemetry

**Frontend and mobile**
- Blazor Web App (Interactive Server) for the gym dashboard and platform console
- .NET MAUI Blazor Hybrid for Android, iOS, macOS (Mac Catalyst) and Windows
- Razor class libraries shared across hosts; Bootstrap plus a custom design system (tokens, cards, tables, badges, modals, chat)
- Dependency-free SVG chart components (bar and line charts)

**Testing**
- xUnit, Moq
- Testcontainers (real PostgreSQL in Docker) for integration tests
- coverlet

**DevOps**
- Docker and Docker Compose (local and production)
- Caddy reverse proxy with automatic HTTPS
- GitHub Actions CI: build, tests, vulnerable-package audit, publish, Docker image builds, MAUI Android and Windows compile checks
- GitHub Actions CD: images to GitHub Container Registry, SSH deploy to a VPS with pre-migration backup, health checks and automatic rollback
- Daily PostgreSQL backups with retention, a missed-backup alert and a scripted restore test
- Signed Android App Bundle and MSIX packaging

---

## 10. Engineering challenges worth featuring

Use these as "challenge → solution" cards on the project page.

1. **Isolating many gyms in one database.** → Global EF Core query filters keyed on the token's tenant, secure-by-default empty results, a handful of explicit cross-tenant stores, and integration tests that prove isolation.
2. **Letting AI take real actions safely.** → Agents act through the same permission-checked services as people, are offered only tools both the agent and the user may use, and route risky actions through approval or four-eyes review, with amount caps, token quotas and an audit trail. The same governance applies to external agents over MCP.
3. **Check-in passes that work offline but can't be shared.** → TOTP-style HMAC passes computed on the phone from a per-member derived key, replay protection on the server, key versioning for resets, one phone per pass and biometric unlock before display.
4. **Making "dumb" fingerprint terminals billing-aware.** → Implemented the ZKTeco push protocol: import existing users, link them to members, and push access end dates so the terminal refuses unpaid members by itself even when the internet is down. Plus a browser simulator speaking the same protocol.
5. **Billing that never double-charges.** → An idempotent rules engine, a single-instance job via Postgres advisory locks, atomic invoice numbers, idempotent webhooks and the outbox pattern, so changes, notifications and emails commit together.
6. **One UI for web and native mobile.** → Pages in Razor class libraries hosted by Blazor Server and by MAUI BlazorWebView, with native camera, biometrics and secure storage bridged in per platform.
7. **Payments that respect local markets.** → Stripe does not serve Pakistani gyms, so each gym connects a local provider (HitPay in Malaysia, Safepay in Pakistan) through a self-service settings page, behind one provider abstraction; cash and bank transfer with gym-specific payment instructions remain.
8. **Subscription-driven features.** → Code-defined plans and modules gate features, AI tools and agent templates; upgrades roll out new agents automatically.

---

## 11. Project metrics (from the repository, Oct 2026)

| Metric | Value |
|---|---|
| Projects in the solution | 15 |
| C# code (excluding migrations) | ~54,000 lines in ~460 files |
| Razor UI | ~16,700 lines in ~115 components/pages |
| CSS | ~7,800 lines |
| Automated tests | ~300 (251 test methods incl. parameterised theories), unit + integration on real PostgreSQL |
| Database migrations | 33 |
| API controller groups | 18 |
| AI agents / tools | 3 agents, 24 governed tools |
| Client platforms | Web (2 apps), Android, iOS, macOS, Windows |

---

## 12. Demo data

A deterministic seeder creates a realistic showcase: an operator account and four gyms on different plans and subscription states (Iron Paradise Gym in PKR on Professional, FlexZone Fitness in USD on a trial, Titan Strength Club in GBP on Enterprise and past due, Old Barn Gym canceled) with 80 members, invoices, payments, check-ins by every method, routines, workouts and a registered virtual fingerprint terminal.
**[verify: whether to publish demo login details on the portfolio; if so, the owner should provide them.]**

---

## 13. Ready-made copy

**One line (project card)**
> IronForge: a multi-tenant gym management SaaS with governed AI agents, built with .NET 10, Blazor, .NET MAUI and PostgreSQL.

**Short (~50 words)**
> IronForge is a production-grade SaaS that runs gyms end to end: members, automated billing, online payments through HitPay and Safepay, four kinds of check-in, fingerprint-terminal integration and training. Staff get a web dashboard, members a cross-platform mobile app, and operators a platform console. AI agents help staff and coach members, with permission checks and human approvals.

**Long (~150 words)**
> IronForge is a multi-tenant gym management platform I designed and built solo on .NET 10. Each gym gets an isolated workspace with a Blazor web dashboard for staff, a .NET MAUI app for members on Android, iOS and Windows, and a separate console for the SaaS operator.
>
> It automates the gym's daily work: recurring invoices, reminders, grace periods and expiry; online payments into each gym's own HitPay or Safepay account; bulk import of existing members from a CSV file; check-in through offline QR passes, kiosks or existing ZKTeco fingerprint terminals that it makes billing-aware.
>
> Its AI agents (a gym admin assistant, a personal fitness coach and an insights analyst) act through the same permission-checked services as people. Risky actions need human or four-eyes approval, and the same governed tools are exposed to external agents over the Model Context Protocol.
>
> Under the hood: clean layered architecture, EF Core tenant isolation, outbox emails, idempotent billing, TOTP two-factor, ~300 automated tests and CI/CD to Docker with automatic rollback.

**Skills demonstrated (tags)**
`.NET 10` `C#` `ASP.NET Core` `Blazor` `.NET MAUI` `Entity Framework Core` `PostgreSQL` `Multi-tenant SaaS` `Clean Architecture` `REST API` `JWT / 2FA` `AI Agents` `OpenAI` `Microsoft.Extensions.AI` `Model Context Protocol (MCP)` `Payment gateway integration` `IoT / Hardware integration` `Background jobs` `Outbox pattern` `xUnit` `Testcontainers` `Docker` `Caddy` `GitHub Actions CI/CD` `OpenTelemetry`

---

## 14. Suggestions for the portfolio page

- **Hero**: name, tagline, the one-line copy, tags, links (live demo, source **[verify]**).
- **Overview**: problem (section 2) and solution (section 3).
- **"Four apps, one platform"**: four cards (gym dashboard, member app, platform console, AI agents and MCP).
- **Feature highlights**: a short grid from section 4; give check-ins and fingerprint terminals their own visual.
- **AI governance**: a flow diagram: *User request → agent → tools filtered by user ∩ agent ∩ module permissions → risk check → (approval / four-eyes) → domain service → audit log*.
- **Architecture**: the layer diagram from section 6 plus the multi-tenancy explanation.
- **Challenges and solutions**: 4–6 cards from section 10.
- **Security**: bullet list from section 7.
- **Tech stack**: grouped badges from section 9.
- **Metrics strip**: 15 projects · ~70k lines · ~300 tests · 5 client platforms · 3 AI agents.
- **Screenshots to capture [owner to provide]**: gym dashboard home (stat tiles and charts), member detail with photo, invoices page, kiosk screen with entrance QR, terminal simulator, member app check-in tab and progress charts, AI agent chat with an approval request, platform console gym list.

---

## 15. Accuracy notes (do not overstate)

- No real gym customers or revenue yet; it's being prepared for its first gyms in Pakistan and Malaysia.
- Online payments use HitPay (Malaysia) and Safepay (Pakistan), tested against their sandboxes; a live payment with a real gym account has not been made yet.
- Out of scope for v1, so don't list them as features: class booking, point-of-sale, payroll, nutrition tracking, push notifications, streaming chat.
- Fingerprint support covers ZKTeco push-protocol terminals only; IronForge never stores fingerprints.
- Platform billing of gyms is manual (operators set subscription status); there's no automated SaaS billing.
