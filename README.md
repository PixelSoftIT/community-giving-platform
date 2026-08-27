# Community Giving Platform

A full-stack membership, fund/project management, and online-giving platform for
community organizations — **temples, churches, mosques, synagogues, NGOs, and general
community centers**. One deployment serves one organization; everything about its name,
type, and vocabulary is configurable from the admin console, not hardcoded.

Built with **React + TypeScript** (frontend) and **ASP.NET Core 8 Web API** (backend),
**PostgreSQL**, and **Stripe**.

## Who this is for

The domain model is intentionally denomination- and mission-neutral:

| Generic concept | Temple | Church | Mosque | NGO |
|---|---|---|---|---|
| Household | Family membership | Family membership | Family membership | Household/family served |
| Program participant | Sunday school student | Sunday school / confirmation student | Youth Islamic studies student | Program beneficiary / trainee |
| Fund | Renovation fund, seva fund | Building fund, missions fund | Zakat fund, masjid fund | Program fund, relief fund |
| Project | Multi-year renovation | Capital campaign | Community center expansion | Disaster relief campaign |
| Event | Festival, puja | Worship service, potluck | Eid celebration, iftar | Fundraiser gala, workshop |

None of these labels are hardcoded strings scattered through the code — the **Organization
Settings** admin tab lets you set the org's name, type, tagline, contact info, logo, and
what to call its enrollment-based offering (defaults to "Programs & Classes", but you
can rename it to "Sunday School," "Youth Halaqa," "Job Training Cohort," or anything
else, or turn that feature off entirely if it doesn't apply).

## Architecture

```
┌─────────────┐        HTTPS        ┌──────────────┐       HTTPS/TLS      ┌────────────┐
│   Browser   │ ───────────────────▶│  nginx (edge) │──────────────────────▶            │
│ React SPA   │                     │  TLS + reverse│                     │  ASP.NET   │
└─────────────┘                     │    proxy      │──────────────────────▶  Core API  │
                                     └──────┬────────┘                     └─────┬──────┘
                                            │  serves static build                │
                                     ┌──────▼────────┐                     ┌──────▼──────┐
                                     │ React (nginx) │                     │  PostgreSQL │
                                     │   container   │                     │  container  │
                                     └───────────────┘                     └─────────────┘
                                                                                   │
                                                                            ┌──────▼──────┐
                                                                            │   Stripe    │
                                                                            │ (payments)  │
                                                                            └─────────────┘
```

- **Client Portal** (`/portal`, requires login): a member sees their household, program
  enrollment (if the org has one), and their own donation history.
- **Admin Console** (`/admin`, requires `Admin` role): dashboard analytics, project
  management (groups of funds), fund management, household/member records, full
  donation ledger, and **organization settings** (branding/vocabulary).
- **Public Donate page** (`/`, no login required): anyone — member or guest — can pick
  a fund and pay by card via Stripe. If they're logged in, the donation is automatically
  linked to their member record for their giving history; otherwise their name/email is
  captured directly on the donation for the receipt.
- **Events** (`/events`): free or ticketed events, open to members and the public
  depending on how the admin configures each event.

## Data model

**How the schema gets created**: on first startup, the app builds all tables directly
from the C# model on first startup (checking for a known table, then generating and
running the `CREATE TABLE` script directly) rather than using versioned EF Core
migration files. Simpler to operate — no separate migration step, works the same in local
dev and in production — at the cost of not having incremental migration history. If the
data model changes after your database already has real data in it, `EnsureCreated` won't
apply the change automatically; that's the point at which switching to proper EF Core
migrations (`dotnet ef migrations add`, requiring the .NET SDK) becomes worth the setup.

- **OrganizationSettings** — singleton row: name, type (Temple/Church/Mosque/Synagogue/
  Ngo/CommunityCenter/Other), tagline, contact info, logo, currency, and the configurable
  "programs" label/toggle.
- **Household** — a family or group unit; one membership can cover multiple people.
- **Member** — an adult tied to a household, optionally linked to a login account.
- **Contact** — a non-member kept on file (guest donor, prospect, vendor) for invoicing
  and notifications without granting a login or full membership.
- **ProgramParticipant** — a person enrolled in the org's programs/classes.
- **Project** — an umbrella initiative that can contain several **Funds**, with budget-
  vs-raised tracking, status, and a manager contact.
- **Fund** — a campaign donors give to; can stand alone or belong to a project.
- **Donation** — a payment record, works identically for members and non-member guests.
- **Invoice** / **InvoiceLineItem** — a billable request (dues, event fee, pledge) sent to
  a member or contact, optionally with a Stripe payment link, emailed as a PDF.
- **Receipt** — auto-generated and auto-emailed (as PDF) the moment a donation or paid
  invoice succeeds.
- **Expense** / **IncomeEntry** — project/fund-based outgoing costs (with an approval
  workflow) and manually recorded income (cash, check, grant) that didn't come through Stripe.
- **NotificationGroup** / **NotificationGroupRecipient** / **Notification** /
  **NotificationDelivery** — saved recipient lists and categorized (General/Payments/
  Events/Meetings/Finance/Urgent) email/SMS blasts with per-recipient delivery tracking.
- **Meeting** / **MeetingAttendee** — covers both the upcoming meeting calendar and the
  historical minutes/attendance record, in one place.
- **RefreshToken** / **AuditLog** — security infrastructure (see below).
- **Event** / **EventRegistration** — free or ticketed events open to members and/or
  the public.

## Feature summary

- **Member & non-member records**: full membership via Household/Member, or lightweight
  Contact records for anyone else the org wants to invoice or message.
- **Invoicing & receipts**: create invoices with line items for members or non-members,
  optionally attach a Stripe payment link, email as a PDF. Receipts are generated and
  emailed automatically on every successful payment — no manual step.
- **Payment links by email/SMS**: send a one-off Stripe payment link directly to someone's
  email (and phone, via SMS) without them needing to find the donate page themselves.
- **Notifications**: categorized (General/Payments/Events/Meetings/Finance/Urgent),
  sent by email, SMS, or both, to saved groups, specific members/contacts, and/or
  ad-hoc recipients, with delivery tracking per recipient.
- **Meeting minutes & schedules**: one place to schedule upcoming meetings and record
  minutes/attendance for past ones.
- **Project-based expense & income management**: track costs and manually-recorded
  income (cash/check/grant) against a Project, alongside Stripe donations, to see a full
  income-vs-expense picture per initiative.

## Why this stack

| Concern | Choice | Why |
|---|---|---|
| Backend | ASP.NET Core 8 Web API | Strong typing, built-in Identity (auth), mature EF Core ORM, good perf, easy to host on a small Contabo VPS via Docker |
| Frontend | React + TypeScript + Vite | Fast dev loop, componentized admin/member/public views, small production bundle |
| Database | PostgreSQL | Free, robust, works great in Docker on a modest VPS |
| Auth | ASP.NET Identity + JWT + refresh tokens | Password hashing, lockout, roles built in; short-lived access tokens backed by rotating refresh tokens |
| Payments | Stripe (Payment Intents, Elements, Payment Links) | PCI compliance is handled by Stripe — card data never touches your server |
| Email | MailKit over SMTP | Works with any provider (SendGrid, Postmark, SES, Google Workspace) — swap providers via config, not code |
| SMS | Twilio | Industry-standard SMS API; swappable behind `ISmsSender` |
| PDF generation | QuestPDF (Community license) | Generates receipt/invoice PDFs server-side, no external service needed |
| Styling | Tailwind CSS | Fast, consistent, responsive out of the box |

## Security measures included

- Passwords hashed via ASP.NET Identity (PBKDF2), never stored in plain text
- Account lockout after 5 failed logins (15 min)
- **JWT access tokens (8h) + rotating refresh tokens (30d, stored hashed, revocable)** —
  a leaked access token expires quickly; refresh tokens are revoked on password reset and
  can be individually revoked on sign-out
- **Role-based access control**: `Admin` (full access), `Treasurer` (invoices, expenses,
  income, receipts), `Secretary` (meetings, notifications), `Member` (self-service portal
  only) — new roles can only be granted by an existing Admin, never self-assigned
- **Password reset flow** that doesn't leak whether an email is registered, and revokes
  all active sessions on a successful reset
- **Audit log** of sensitive actions (expense approvals, invoice sends, notification
  blasts) — who did what, when, from what IP
- HTTPS enforced end-to-end (HSTS + redirect at the edge, `UseHttpsRedirection` in the API)
- CORS locked to your known frontend origin only
- Rate limiting on login, registration, password reset, payment, invoice, and
  notification-send endpoints (`AspNetCoreRateLimit`)
- Stripe handles all card data — your server only ever sees a `PaymentIntent`/payment
  link id
- Stripe webhook signature verification — payment status is only ever trusted from
  Stripe's signed server-to-server callback, never from the browser
- Security headers (`X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`)
- API container runs as a non-root user
- Secrets (DB password, JWT key, Stripe keys, SMTP/Twilio credentials) are injected via
  environment variables / `.env`, never committed to source control

**Before going live**, also do these (outside the scope of code):
- Put the whole thing behind Contabo's firewall — only open ports 80/443 and SSH (change the SSH port from 22 and disable password auth, use SSH keys)
- Enable automatic OS security updates on the VPS
- Set up automated Postgres backups (e.g. nightly `pg_dump` to off-server storage)
- Rotate the JWT signing key and Stripe/SMTP/Twilio keys if they're ever exposed
- Review the `AuditLog` table periodically, especially for finance actions

## Local development

**Backend:**
```bash
cd backend/CommunityGiving.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "dev-only-secret-at-least-32-chars-long"
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..."
# Email/SMS are optional for local dev — leave unset and the app will log instead of sending
dotnet user-secrets set "Email:SmtpHost" "smtp.sendgrid.net"
dotnet user-secrets set "Email:SmtpUsername" "apikey"
dotnet user-secrets set "Email:SmtpPassword" "..."
dotnet user-secrets set "Sms:TwilioAccountSid" "AC..."
dotnet user-secrets set "Sms:TwilioAuthToken" "..."
# point ConnectionStrings:DefaultConnection at a local Postgres, e.g. via docker run postgres
dotnet run
# Tables are created automatically on first run (see "Database schema" note below) —
# no separate migration step needed.
```

**Frontend:**
```bash
cd frontend
npm install
echo "VITE_API_URL=http://localhost:8080" > .env
echo "VITE_STRIPE_PUBLISHABLE_KEY=pk_test_..." >> .env
npm run dev
```

## Setting up your organization

After first deploy, sign in as an Admin and go to **Admin Console → Organization
Settings** to set:
- Name, type (Temple/Church/Mosque/Synagogue/NGO/Community Center/Other), and tagline
- Contact email, phone, and address (shown in receipts and footers as you extend them)
- Logo URL (shown in the navbar)
- Currency
- What to call your enrollment-based offering, and whether to show it at all

Then use **Projects** and **Funds** to set up your giving campaigns, and **Members &
Households** to start recording your congregation/community.

## Deploying to a Contabo VPS

1. **Provision the VPS** (Ubuntu 22.04/24.04 recommended, 4GB RAM is plenty to start —
   if you're on a smaller/cheaper plan, see the `docker-compose.small-footprint.yml`
   overlay described in the Oracle Cloud section below; it applies to any host, not just
   Oracle).
2. **Point DNS**: create `A` records for `app.yourorg.org` and `api.yourorg.org`
   pointing at the VPS's IP.
3. **Install Docker**:
   ```bash
   curl -fsSL https://get.docker.com | sh
   sudo usermod -aG docker $USER
   ```
4. **Firewall** (allow only what's needed):
   ```bash
   sudo ufw allow OpenSSH
   sudo ufw allow 80,443/tcp
   sudo ufw enable
   ```
5. **Clone/copy this project** to the server, e.g. `/opt/community-giving`.
6. **Create `.env`** from `.env.example` and fill in real secrets (DB password, JWT
   key, Stripe live keys, your domains).
7. **First-run certificate bootstrap** — Let's Encrypt needs the edge nginx running on
   port 80 to validate domain ownership before certs exist. Easiest path:
   ```bash
   # Start db/api/client first, and a temporary plain-HTTP nginx to answer the ACME challenge
   docker compose up -d db api client
   docker run --rm -p 80:80 -v community-giving_certbot_www:/var/www/certbot \
     -v $(pwd)/nginx/bootstrap.conf:/etc/nginx/conf.d/default.conf nginx:1.27-alpine &
   docker run --rm -v community-giving_certbot_www:/var/www/certbot -v community-giving_certbot_certs:/etc/letsencrypt \
     certbot/certbot certonly --webroot -w /var/www/certbot \
     -d app.yourorg.org -d api.yourorg.org --email you@yourorg.org --agree-tos
   # stop the temporary nginx, then bring up the full stack including edge + certbot renewer
   docker compose up -d
   ```
   (Docker Compose prefixes named volumes with the project's *folder name* — adjust the
   `community-giving_certbot_www` / `_certbot_certs` volume names above if you cloned
   this into a differently-named directory; check with `docker volume ls`.)
8. **Verify**: visit `https://app.yourorg.org` and `https://api.yourorg.org/swagger`
   (disable Swagger in production once verified — it's gated to `Development` already
   in `Program.cs`, so this only works if you temporarily flip `ASPNETCORE_ENVIRONMENT`).
9. **Configure the Stripe webhook**: in the Stripe dashboard, add an endpoint
   `https://api.yourorg.org/api/payments/webhook` listening for
   `payment_intent.succeeded` and `payment_intent.payment_failed`, then copy the signing
   secret into `.env` as `STRIPE_WEBHOOK_SECRET` and restart the `api` container.
10. **Configure email and SMS**: fill in `SMTP_*` (any provider — SendGrid, Postmark,
    Amazon SES, Google Workspace all work over SMTP) and `TWILIO_*` in `.env`, then
    restart the `api` container. Until these are set, the app runs fine but emails/texts
    are silently skipped and logged instead — useful for testing without live credentials.
11. **Create your first Admin**: register a normal account through the app, then either
    run a one-off SQL update to add them to the `AspNetUserRoles` table, or use the
    Admin Console's **Users & Roles** tab once you have at least one Admin — see
    "Bootstrapping the first Admin" below for the very first one.
12. **Set up your organization profile**: sign in as Admin and fill out the Organization
    Settings tab (see above) so the site shows your org's real name/type/branding.
13. **Updates**: `git pull && docker compose up -d --build`.

### Bootstrapping the first Admin

The Admin Console's role-management tools require an existing Admin, which is a
chicken-and-egg problem for the very first one. Simplest fix: after your first
registration, connect to Postgres and run:

```sql
INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id" FROM "AspNetUsers" u, "AspNetRoles" r
WHERE u."Email" = 'you@yourorg.org' AND r."Name" = 'Admin';
```

From then on, use the **Users & Roles** tab in the Admin Console to promote others to
Admin, Treasurer, or Secretary — no more SQL needed.

## Deploying to Oracle Cloud (Always Free)

Oracle's Always Free tier is a strong fit for this app: the **Ampere A1 (ARM)** shape
gives you up to **4 OCPUs, 24GB RAM, and 200GB block storage** for free, permanently —
well beyond what a Postgres + API + React stack needs, and far more than typical
budget-VPS free tiers. The one real difference from a normal VPS is that Oracle Cloud
has **two firewall layers** you must open (the cloud network's Security List/NSG, *and*
the instance's own `iptables`), and the instance is **ARM64**, not x86 — the images used
here are all published for both architectures (`postgres`, `nginx`, `certbot`,
`mcr.microsoft.com/dotnet`, `node`), so Docker pulls the right one automatically; you
don't need to change anything in the Dockerfiles or compose file.

1. **Create the instance**: Console → Compute → Instances → Create Instance.
   - **Image**: Ubuntu 22.04 or 24.04 (Canonical's official image).
   - **Shape**: click "Change shape" → Ampere → **VM.Standard.A1.Flex** → set
     4 OCPUs / 24GB memory (the full Always Free allowance; you can run this on fewer
     OCPUs/less RAM if you plan to run other things on the same allowance too).
   - **Networking**: use the default VCN/subnet Oracle offers to create, with
     "Assign a public IPv4 address" checked.
   - **SSH keys**: upload your public key (or have Oracle generate a pair for you).
   - Ampere A1 capacity is sometimes tight in a given region/availability domain —
     if you get an "Out of host capacity" error, try a different Availability Domain
     in the same region, or try again in a few minutes; this is a known Always-Free
     quirk, not a problem with your setup.

2. **Reserve a static public IP** (recommended): Networking → IP Management → Reserved
   Public IPs → Create, then attach it to the instance's VNIC. An ephemeral IP works too,
   but changes if you ever recreate the instance, breaking your DNS.

3. **Point DNS**: create `A` records for `app.yourorg.org` and `api.yourorg.org`
   pointing at that IP.

4. **Open ports in the Security List / Network Security Group** (this is the part that's
   easy to miss and the most common reason "it works from the instance but not the
   browser"): Console → Networking → Virtual Cloud Networks → your VCN → Security Lists
   (or Network Security Groups, if you used one) → Add Ingress Rules:
   - Source CIDR `0.0.0.0/0`, IP Protocol TCP, Destination Port `80`
   - Source CIDR `0.0.0.0/0`, IP Protocol TCP, Destination Port `443`
   - (Port 22/SSH is usually already open by default — verify it's restricted to your IP if possible.)

5. **SSH in and open the OS-level firewall too**. Oracle's Ubuntu images ship with
   `iptables` rules that block inbound traffic by default, *separately* from the Security
   List above — both layers must allow the traffic:
   ```bash
   sudo iptables -I INPUT -p tcp --dport 80 -j ACCEPT
   sudo iptables -I INPUT -p tcp --dport 443 -j ACCEPT
   sudo netfilter-persistent save   # persist across reboots (install: sudo apt install iptables-persistent)
   ```
   If you'd rather manage the OS firewall with `ufw` instead, that works too — just make
   sure `ufw`'s rules end up allowing 80/443 (Ubuntu's `ufw` sits in front of the same
   `iptables` rules Oracle preconfigures, so test after enabling it).

6. **Install Docker**:
   ```bash
   curl -fsSL https://get.docker.com | sh
   sudo usermod -aG docker $USER
   # log out and back in for the group change to take effect
   ```

7. **Clone/copy this project** to the instance, e.g. `/opt/community-giving`, create
   `.env` from `.env.example` with real secrets, and follow the same **certificate
   bootstrap → `docker compose up -d`** steps as the Contabo instructions above — they're
   identical from here on, since this is still just Docker Compose on a Linux VM.

8. **Free-tier resource notes**: 24GB RAM is generous for this stack if it's the only
   thing running on the instance — the defaults in `docker-compose.yml` fit comfortably
   with room to spare. If you're **sharing** the Always Free budget with other projects
   (e.g. running on a smaller 1 OCPU / 6GB slice, or other containers/instances on the
   same tenancy), apply the optional resource-capped overlay instead:
   ```bash
   docker compose -f docker-compose.yml -f docker-compose.small-footprint.yml up -d
   ```
   This caps Postgres, the API, and nginx to a combined ~1.2-1.5GB RAM footprint (tuning
   Postgres' `shared_buffers`/`work_mem`/connection limit down, and switching the .NET API
   to workstation GC with a heap ceiling) — comfortable for a single organization's data
   without needing a dedicated large instance. See the comments in that file for details
   and for when to skip it. The 200GB Always Free block volume is plenty either way.

9. **Outbound email/SMS**: Oracle Cloud, like many cloud providers, blocks outbound port
   25 (raw SMTP) by default to fight spam — this doesn't affect this app, since `Email:SmtpPort`
   is configured to `587` (STARTTLS submission), which is open. If you ever change it to
   port 25, it won't work on Oracle Cloud without requesting a Service Limit increase.

## Deploying to Render

Render is a much more point-and-click alternative to running your own VPS: no Docker
Compose, no manual nginx/certbot, no server to patch. It builds directly from a GitHub
repository and handles HTTPS automatically. This repo includes a `render.yaml`
**Blueprint** file, so the whole stack (database + backend API + frontend) can be
provisioned in one step instead of configuring each service by hand.

**A note on Render's free tier**, since it's tempting to reach for: the free PostgreSQL
database **auto-deletes after ~44 days** unless upgraded, free web services **sleep after
15 minutes of inactivity** (30-60 second cold start on the next visit), and free web
services **block outbound SMTP ports** (25/465/587) — which breaks this app's email
receipts/notifications entirely. None of that is fatal for kicking the tires, but it's a
poor fit for an org actually tracking real donations. The instructions below assume the
paid Starter/Basic plans (roughly $13-19/month total), which avoid all three issues.

1. **Push this repo to GitHub** (Render deploys from a Git repository — GitHub, GitLab, or
   Bitbucket all work). If you're not comfortable with git on the command line,
   [GitHub Desktop](https://desktop.github.com) does this with buttons instead of typed
   commands: add this folder as a local repository, then click "Publish repository."
   **Keep the repository private** — even though secrets themselves are entered directly
   into Render's dashboard (never committed to the repo), there's no reason to make the
   source public.

2. **Review `render.yaml`** at the repo root before deploying. In particular:
   - The service names (`community-giving-api`, `community-giving-app`) determine your
     free `*.onrender.com` URLs. Rename them if you like, but keep the `AllowedOrigins__0`
     value in the API service and the `VITE_API_URL` value in the frontend service
     consistent with whatever names you choose.
   - The `region` fields — pick whichever Render region is closest to your users, the
     same region for all three services (database, API, frontend) keeps traffic between
     them fast and free (cross-region traffic on Render incurs bandwidth costs).

3. **In the Render dashboard**: click **New → Blueprint**, connect your GitHub account,
   select this repository. Render detects `render.yaml` and shows you every service it's
   about to create.

4. **Fill in the secrets** Render prompts for (everything marked `sync: false` in
   `render.yaml`): your Stripe secret key, Stripe webhook secret (see step 6), SMTP
   credentials, Twilio credentials, and your Stripe *publishable* key for the frontend.
   Don't have some of these yet (e.g. Twilio)? Leave them blank — the app runs fine
   without them, those specific features (SMS) just no-op until configured later via the
   same dashboard.

5. **Click "Apply"**. Render provisions the database, then builds and deploys the API and
   frontend. First deploy typically takes 5-10 minutes (mostly the Docker build for the
   API). Watch progress on each service's "Events" tab.

6. **Configure the Stripe webhook**: once the API service shows "Live," copy its URL
   (`https://community-giving-api.onrender.com`, or your renamed equivalent). In the
   Stripe dashboard, add a webhook endpoint at `<that URL>/api/payments/webhook`,
   listening for `payment_intent.succeeded` and `payment_intent.payment_failed`. Copy the
   webhook's signing secret, then go to the API service in Render → Environment → update
   `Stripe__WebhookSecret` with it, which triggers a redeploy.

7. **Visit your site**: `https://community-giving-app.onrender.com` (or your renamed
   equivalent). Register an account, then promote it to Admin — Render's dashboard has a
   built-in database shell (open the `community-giving-db` service → "Connect" → "PSQL
   Command") so you can run the same bootstrap SQL described above without installing
   anything locally:
   ```sql
   INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
   SELECT u."Id", r."Id" FROM "AspNetUsers" u, "AspNetRoles" r
   WHERE u."Email" = 'you@yourorg.org' AND r."Name" = 'Admin';
   ```

8. **Custom domain (optional)**: once you have a domain, add it under either service's
   Settings → Custom Domains. Render provisions and renews the HTTPS certificate for you
   automatically — no certbot, no manual renewal, unlike the Contabo/Oracle path above.

9. **Updates**: push to your GitHub repo's default branch, and Render auto-deploys both
   services. No SSH, no `docker compose up -d --build` — it's the same one-step flow every
   time.

### Why the code has a `PORT` env var and a connection-string normalizer

Two small differences from the Contabo/Oracle setup make the same codebase work
unmodified on Render:
- Render assigns a port dynamically via the `PORT` environment variable rather than a
  fixed one; `Program.cs` reads it (falling back to `8080` for docker-compose, where it's
  unset) instead of hardcoding it.
- Render hands out its Postgres connection string as a `postgres://user:pass@host/db`
  URI, while Npgsql expects `Host=...;Username=...` keyword format; `Program.cs` detects
  and converts either form automatically, so you never have to hand-edit it.

## What's included vs. what to extend next

**Included (working end-to-end):** registration/login with refresh-token rotation, role-
based access (Admin/Treasurer/Secretary/Member), configurable organization branding/type/
vocabulary, fund CRUD, projects that group multiple funds together, Stripe payment
intents and payment links for members & non-member guests/contacts, webhook-confirmed
payment status with **automatic PDF receipt generation and emailing**, invoicing with
optional Stripe payment links emailed as PDFs, categorized email/SMS notifications to
saved groups or ad-hoc recipients with delivery tracking, meeting scheduling and minutes,
project-based expense approval workflow and manual income recording with a combined
financial summary, an audit log of sensitive actions, admin dashboard with charts, member
self-service portal, household/program-participant data model.

**Good next additions:** an admin UI form for adding new households/members (the API
endpoint `POST /api/members/households` already exists), a UI for adding recipients to a
notification group (the API endpoint already exists, `POST /api/notifications/groups/{id}/recipients`),
recurring/subscription donations (Stripe Subscriptions), CSV/Excel export of the donation
ledger and financial reports, file upload for expense receipt images (currently a URL
field), two-factor authentication (ASP.NET Identity already supports it — just needs
UI), and multi-language support.
