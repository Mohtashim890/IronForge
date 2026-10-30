# Member onboarding and check-in

How a gym brings its members into IronForge, and the ways members check in. Every step below is also shown in the dashboard, with live status.

The flows are independent:

| Flow | What it does | Where |
|---|---|---|
| **Member onboarding** | Creates members: one by one, by join code, or **in bulk from a CSV file** | Members, Members → Import members |
| **Member pass check-in** | Members show a rotating QR code from the app; staff or a kiosk scan it | Pass tab (app), Check-ins, `/kiosk` |
| **Fingerprint check-in** | Existing ZKTeco terminals keep the fingerprints; IronForge tells them who may enter | Fingerprint check-in, Check-in devices |
| **Front desk by name** | Staff find the member and check them in | Check-ins (always available) |

Staff (admins and trainers) are never imported. Each is invited on the **Staff** page, so every account with access to gym data is added on purpose.

---

## 1. Bulk member onboarding (CSV import)

**Who:** gym admin (`gym.members.manage`). **Before:** create the membership plans under **Membership plans**.

1. **Prepare the file.** Export your members from your old system or spreadsheet. Save it as CSV in the format below, or start from the template on the Import members page.
2. **Check the file.** Choose or paste the file, then click **Check file**. IronForge reads every row and shows what it would do. Nothing is saved.
3. **Fix or accept.** Each row is *Ready*, *Skipped* (already a member: same email, left as is) or *Error* (with the reason). Fix the errors and check again, or import the valid rows now.
4. **Import.** Optionally tick *Email new members an invitation to the IronForge app*. The valid rows become **active, approved members** in one transaction.
   - Members whose email already has an IronForge account are linked at once and can sign in.
   - The others sign up through the invitation, or with the gym's join code.
5. **Next.** Members check in with their pass. If the gym has fingerprint terminals, link their fingerprints (section 3).

### File format

- CSV, UTF-8 (a byte order mark is fine). The first line is the header.
- Comma or semicolon between columns. Put a value in double quotes if it contains the separator; double a quote inside quotes.
- Column names as below, in any order. Capitalisation, spaces and underscores in the names are ignored. **Unknown columns refuse the file**, to catch typos.
- Dates are `yyyy-MM-dd`. At most 2000 members and 2 MB per file. Empty lines are ignored.

| Column | Required | Content |
|---|---|---|
| `FullName` | yes | The member's name, up to 150 characters |
| `Email` | yes | Unique in the file and in the gym; used to sign in to the app |
| `Plan` | yes | Name of an active membership plan, as under Membership plans (any capitalisation) |
| `Phone` | no | Up to 50 characters |
| `PaidThrough` | no | Last day already paid for in the old system |
| `MemberSince` | no | When they first joined (for the record); default: import date |
| `DateOfBirth` | no | |
| `Gender` | no | `Female`, `Male` or `Other` |
| `EmergencyContact` | no | Name and phone, up to 200 characters |
| `Notes` | no | Staff-only notes, up to 2000 characters |

Example:

```csv
FullName,Email,Plan,Phone,PaidThrough,MemberSince
Sara Khan,sara.khan@example.com,Monthly,+92 300 1234567,2026-11-30,2023-04-01
Omar Ali,omar.ali@example.com,Quarterly,,,
```

### Rules

- **Billing.** The import creates no invoices and sends no invoice emails. Billing starts on the day after `PaidThrough` when that is today or later, otherwise today. Nobody is billed for the time before the import. The hourly billing job then issues invoices as usual: `InvoiceLeadDays` before the billing date, with the gym's reminders and grace period.
- **Members only.** A row whose email belongs to an admin or trainer of the gym is refused. There is no role column.
- **Duplicates.** The same email twice in the file is an error on the second row. An email that is already a member of the gym is skipped. The same person may be a member of another gym; gyms never see each other's members.
- **Status.** Everyone starts Active. Freeze or cancel individual members afterwards on their page.

---

## 2. Member pass check-in (QR code in the app)

**Who:** members with an approved membership. **Switch:** Gym settings → Check-in methods → *Member pass* (on by default). Optional: *Ask for the phone's fingerprint, face or screen lock* (on by default).

1. **The member opens the Pass tab.** The app downloads the pass once and keeps it in the phone's secure storage. It then works without internet.
2. **The member confirms it's them** with the phone's fingerprint, face or screen lock, if the gym requires it. The pass shows for 60 seconds, then hides again.
3. **The code changes every 30 seconds.** It is signed for this member, gym and pass version, so a screenshot stops working within about a minute.
4. **Someone scans it.** Staff on **Check-ins** use the camera or a USB/Bluetooth scanner; unattended, a **kiosk** (`/kiosk`, paired under Check-in devices) does.
5. **IronForge decides.**
   - Only Active members enter. Inside the grace period of an overdue invoice they enter with a warning to staff; after it they are refused.
   - A code already used, an old code, a code from a reset pass, or another gym's pass is refused, and copies are audited.
   - A second check-in within 30 minutes counts as the same visit.
6. **The result shows** with the member's photo at the front desk. The visit, or the refusal, appears under Check-ins.

**Against misuse:**
- A pass works on one phone. Moving it to another phone resets it and emails the member.
- Staff can reset a member's pass from the member page, for example after a lost phone.

---

## 3. Fingerprint check-in (ZKTeco terminals)

**Who:** gyms with the *Access control* module (Enterprise plan, or an add-on). Fingerprints never leave the terminal; IronForge stores only the user number, the name on the terminal, and whether a finger is enrolled.

1. **Members are in IronForge.** Add them one by one, or bulk-import them (section 1).
2. **Connect a terminal.** On **Check-in devices** → *Add fingerprint terminal*, enter its serial number (optionally restricted to the gym's fixed IP). On the terminal, set the Cloud Server / ADMS address to IronForge.
3. **The terminal sends who is enrolled.** On first contact IronForge asks for its user list. The terminal is the only source of terminal users; there is no separate file import.
4. **Link people to members** on **Fingerprint check-in**: *Link all name matches*, or choose a member per person. Mark staff and others as *Not a member*; IronForge never changes them on the terminal.
5. **The terminals receive each member's access** within a minute: allowed until a date (the end of the grace period of the next payment), or refused. The terminal then decides at the door by itself, also when the internet is down.
6. **Members enter with their finger.** Each entry becomes a visit. If a terminal working offline let in someone IronForge would have refused, the visit is kept with a warning and the terminal is updated at once.

**New members after the migration:** on the member's page click *Add to fingerprint terminals* (or use *Add a member to the terminals*). They get the next user number; enrol their finger on the terminal under that number.

**Try it without hardware:** Check-in devices → *Try the terminal simulator*.
