# Tester Feedback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Signed-in pilots send "Report a problem" reports, optionally with one attachment. Allowlisted staff read
them and mark them handled.

**Architecture:**
- **Rules:** pure validation lives in `FeedbackRules`; staff membership is `StaffAccess` (shaped like
  `AccountAdmission`).
- **Storage:** two new tables, `feedback` and `feedback_attachment`, created by hand-written migration SQL like the
  existing migrations.
- **Service:** operations are a new `AccountsService.Feedback.cs` partial, using the existing `Execute` transaction
  and lock.
- **Web:** Razor pages `/Account/Feedback`, `/Staff/Feedback` and `/Staff/Feedback/{id}`.

**Tech Stack:** .NET 10, ASP.NET Core Razor Pages, EF Core + Npgsql, PostgreSQL 17 (local test cluster on 55439).
The tests are console executables, not `dotnet test`.

**Spec:** `docs/superpowers/specs/2026-10-04-tester-feedback-design.md`

## Global Constraints

- No new NuGet packages. `TreatWarningsAsErrors` is on, so builds must have 0 warnings.
- No inline `<script>` and no `style=` attributes, because the CSP has no `unsafe-inline`.
- Existing migrations stay as they are. The new migration id is `202610040002_Feedback`; it composes
  `AccountDeletionModel` and re-points the snapshot.
- Attachments: 1 byte–5,242,880 bytes; `image/png`, `image/jpeg`, `application/zip` or `application/json`, decided
  by content.
- Rate limit: 5 reports per account per rolling hour. Attachment storage cap: 200 MB in total.
- Staff: `Staff__Emails` (semicolon- or comma-separated), a verified email, and recent MFA (`RequireRecentMfa`).
  Anyone else gets 404.
- New copy says "S4 OPS". Don't rename `Alpha6Ops` identifiers.

## Review Focus

1. **A file whose name claims `.png` but holds text or HTML** must be refused (`invalid_attachment`) and never
   stored. Test: a `.png` name with `<html>` bytes is refused.
2. **A report submitted with no file part, or an empty file input**, which browsers send as a zero-length part
   named `""`, must be accepted as a report without an attachment and not fail. Test: a multipart post with an
   empty file part succeeds.
3. **A staff email in the allowlist but unverified at the provider** must not grant staff. Test:
   `StaffAccess.IsStaff` is false for `EmailVerified=false`.
4. **The staff attachment download** must never render inline (it's an XSS vector for JSON and images). Test:
   `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff` are present.
5. **An account pending deletion** must not be able to submit reports. Test: submitting returns
   `account_pending_deletion`, which the existing `Execute` guard gives for free; asserted in the DB section.

---

### Task 1: Rules and staff access (pure)

**Files:**
- Create: `src/Alpha6Ops.Accounts/FeedbackRules.cs`
- Create: `src/Alpha6Ops.Accounts/StaffAccess.cs`
- Modify: `tests/Alpha6Ops.Accounts.Tests/Program.cs` (pure section, after the `AccountAdmission` checks around line 46)

**Interfaces — Produces:**
- `enum FeedbackCategory { FlightRecording = 0, Bug = 1, Idea = 2, Other = 3 }`
- `enum FeedbackStatus { New = 0, Handled = 1 }`
- `record FeedbackRequest(FeedbackCategory Category, string? Message, string? FlightNumber, DateOnly? FlownOn)`
- `record FeedbackFile(string FileName, byte[] Bytes)`
- `record CleanAttachment(string FileName, string ContentType, byte[] Bytes, string Sha256)`
- `FeedbackRules.Request(FeedbackRequest, DateOnly today) → FeedbackRequest` (throws `invalid_feedback`, 400)
- `FeedbackRules.Attachment(FeedbackFile) → CleanAttachment` (throws `invalid_attachment`, 400)
- Constants: `FeedbackRules.MaxAttachmentBytes = 5_242_880`, `PerHour = 5`, `StorageCapBytes = 200L * 1024 * 1024`
- `StaffAccess.None`, `StaffAccess.FromEmails(string?)`, `bool StaffAccess.IsStaff(ActorIdentity)`,
  `bool StaffAccess.IsConfigured`

- [ ] **Step 1: Failing tests** (pure section of the accounts suite):

```csharp
var today = new DateOnly(2026, 10, 4);
var goodReport = new FeedbackRequest(FeedbackCategory.FlightRecording, "  Block in never fired at KORD gate C17.  ", " s4 601 ", new DateOnly(2026, 10, 3));
var cleanReport = FeedbackRules.Request(goodReport, today);
Check(cleanReport is { Message: "Block in never fired at KORD gate C17.", FlightNumber: "S4601" } && cleanReport.FlownOn == new DateOnly(2026, 10, 3), "feedback normalization");
Reject(() => FeedbackRules.Request(goodReport with { Message = "short" }, today), "invalid_feedback");
Reject(() => FeedbackRules.Request(goodReport with { Message = new string('x', 4001) }, today), "invalid_feedback");
Reject(() => FeedbackRules.Request(goodReport with { Category = (FeedbackCategory)9 }, today), "invalid_feedback");
Reject(() => FeedbackRules.Request(goodReport with { FlightNumber = "S" }, today), "invalid_feedback");
Reject(() => FeedbackRules.Request(goodReport with { FlownOn = today.AddDays(1) }, today), "invalid_feedback");
Reject(() => FeedbackRules.Request(goodReport with { FlownOn = new DateOnly(2019, 12, 31) }, today), "invalid_feedback");
Check(FeedbackRules.Request(goodReport with { FlightNumber = null, FlownOn = null }, today) is { FlightNumber: "", FlownOn: null }, "flight and date are optional");
byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
Check(FeedbackRules.Attachment(new("Screen Shot (1).PNG", pngBytes)) is { ContentType: "image/png", FileName: "Screen_Shot__1_.png" }, "png detected by content and the name sanitised");
Check(FeedbackRules.Attachment(new("a.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 0])).ContentType == "image/jpeg", "jpeg detected");
Check(FeedbackRules.Attachment(new("log.zip", [0x50, 0x4B, 0x03, 0x04, 0])) is { ContentType: "application/zip", FileName: "log.zip" }, "zip detected");
Check(FeedbackRules.Attachment(new("log", "{\"flight\":\"S4601\"}"u8.ToArray())) is { ContentType: "application/json", FileName: "log.json" }, "json detected and given its extension");
Check(FeedbackRules.Attachment(new("x.zip", pngBytes)).FileName == "x.png", "extension follows the content, not the name");
Reject(() => FeedbackRules.Attachment(new("evil.png", "<html><script>alert(1)</script>"u8.ToArray())), "invalid_attachment");
Reject(() => FeedbackRules.Attachment(new("a.svg", "<svg onload=alert(1)/>"u8.ToArray())), "invalid_attachment");
Reject(() => FeedbackRules.Attachment(new("a.exe", [0x4D, 0x5A, 0x90, 0])), "invalid_attachment");
Reject(() => FeedbackRules.Attachment(new("a.json", "{not json"u8.ToArray())), "invalid_attachment");
Reject(() => FeedbackRules.Attachment(new("big.zip", [0x50, 0x4B, 0x03, 0x04, .. new byte[FeedbackRules.MaxAttachmentBytes]])), "invalid_attachment");
var staff = StaffAccess.FromEmails(" Reece@Example.com ; dan@example.com,");
Check(staff.IsConfigured && staff.IsStaff(actor with { Email = "reece@example.com" }) && staff.IsStaff(actor with { Email = "DAN@EXAMPLE.COM" }), "staff allowlist matches case-insensitively");
Check(!staff.IsStaff(actor with { Email = "reece@example.com", EmailVerified = false }), "an unverified email never grants staff");
Check(!staff.IsStaff(actor with { Email = "someone@example.com" }) && !StaffAccess.None.IsConfigured && !StaffAccess.FromEmails("").IsStaff(actor), "no list means no staff");
```

- [ ] **Step 2:** Build. Expect compile errors (types missing).

- [ ] **Step 3: Implement** `FeedbackRules.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

public enum FeedbackCategory { FlightRecording = 0, Bug = 1, Idea = 2, Other = 3 }
public enum FeedbackStatus { New = 0, Handled = 1 }
public sealed record FeedbackRequest(FeedbackCategory Category, string? Message, string? FlightNumber, DateOnly? FlownOn);
public sealed record FeedbackFile(string FileName, byte[] Bytes);
public sealed record CleanAttachment(string FileName, string ContentType, byte[] Bytes, string Sha256);

/// <summary>Validation for problem reports. Attachment types are decided by content, never by the file name.</summary>
public static partial class FeedbackRules
{
    public const int MaxAttachmentBytes = 5_242_880;
    public const int PerHour = 5;
    public const long StorageCapBytes = 200L * 1024 * 1024;

    public static FeedbackRequest Request(FeedbackRequest request, DateOnly today)
    {
        static IdentityException Invalid(string message) => new("invalid_feedback", message, 400);
        if (!Enum.IsDefined(request.Category)) throw Invalid("Choose what kind of problem this is.");
        var message = (request.Message ?? "").Trim();
        if (message.Length is < 10 or > 4000) throw Invalid("Describe the problem in 10 to 4000 characters.");
        var flight = (request.FlightNumber ?? "").Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
        if (flight.Length > 0 && !FlightNumberPattern().IsMatch(flight)) throw Invalid("Flight number must be 2 to 10 letters or digits, or blank.");
        if (request.FlownOn is { } date && (date > today || date < new DateOnly(2020, 1, 1))) throw Invalid("The flight date must be between 2020 and today.");
        return new(request.Category, message, flight, request.FlownOn);
    }

    public static CleanAttachment Attachment(FeedbackFile file)
    {
        static IdentityException Invalid(string message) => new("invalid_attachment", message, 400);
        var bytes = file.Bytes ?? [];
        if (bytes.Length is 0 or > MaxAttachmentBytes) throw Invalid("Attach one file of up to 5 MB.");
        var (type, extension) = Detect(bytes) ?? throw Invalid("Attach a PNG or JPEG screenshot, or the desktop's exported test log (.zip or .json).");
        var stem = Path.GetFileNameWithoutExtension(file.FileName ?? "");
        stem = UnsafeName().Replace(stem, "_").Trim('.');
        if (stem.Length == 0) stem = "attachment";
        if (stem.Length > 100) stem = stem[..100];
        return new(stem + extension, type, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    private static (string Type, string Extension)? Detect(byte[] b)
    {
        if (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual((byte[])[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return ("image/png", ".png");
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ("image/jpeg", ".jpg");
        if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] == 0x03 && b[3] == 0x04) return ("application/zip", ".zip");
        try
        {
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(b).TrimStart('﻿');
            using var _ = JsonDocument.Parse(text);
            return ("application/json", ".json");
        }
        catch (Exception ex) when (ex is DecoderFallbackException or JsonException) { return null; }
    }

    [GeneratedRegex("^[A-Z0-9]{2,10}$")]
    private static partial Regex FlightNumberPattern();
    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeName();
}
```

`StaffAccess.cs`:

```csharp
using Alpha6Ops.Identity;

namespace Alpha6Ops.Accounts;

/// <summary>
/// Who may read problem reports: an allowlist of emails from configuration (Staff__Emails). Only a verified
/// email counts. Callers add a recent-MFA check; this is not a role and grants nothing else.
/// </summary>
public sealed class StaffAccess
{
    private readonly HashSet<string> emails;
    private StaffAccess(HashSet<string> emails) => this.emails = emails;

    public static StaffAccess None { get; } = new([]);
    public bool IsConfigured => emails.Count > 0;

    /// <summary>Semicolon- or comma-separated emails. Empty or missing means no staff.</summary>
    public static StaffAccess FromEmails(string? list) => new(new HashSet<string>(
        (list ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        StringComparer.OrdinalIgnoreCase));

    public bool IsStaff(ActorIdentity actor) => actor.EmailVerified && emails.Contains((actor.Email ?? "").Trim());
}
```

- [ ] **Step 4:** Build the accounts suite and run it **without** `ALPHA6_TEST_DATABASE` (pure part only). All new
  checks pass.
- [ ] **Step 5:** Commit `feat(accounts): problem-report rules and staff allowlist`.

---

### Task 2: Tables, model and migration

**Files:**
- Modify: `src/Alpha6Ops.Accounts/Models.cs` (add entities)
- Modify: `src/Alpha6Ops.Accounts/AccountsDbContext.cs` (DbSets plus mapping)
- Create: `src/Alpha6Ops.Accounts/Migrations/202610040002_Feedback.cs`
- Modify: `src/Alpha6Ops.Accounts/Migrations/AccountsDbContextModelSnapshot.cs` (`BuildModel → FeedbackModel.Build`)
- Modify: `tests/Alpha6Ops.Accounts.Tests/Program.cs` (migration count 8 → 9)

**Interfaces — Produces:** `Feedback` entity (`Id, UserId, Category, Message, FlightNumber, FlownOn, AppVersion,
Status, CreatedAt, HandledAt, HandledByUserId`); `FeedbackAttachment` entity (`FeedbackId, FileName,
ContentType, Bytes, Sha256`); `db.Feedback`, `db.FeedbackAttachments`.

- [ ] **Step 1:** Change the test to expect `Count() == 9`. Running it fails with "snapshot matches" or the count.
- [ ] **Step 2:** Entities in `Models.cs`:

```csharp
public sealed class Feedback
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public FeedbackCategory Category { get; set; }
    public string Message { get; set; } = "";
    public string FlightNumber { get; set; } = "";
    public DateOnly? FlownOn { get; set; }
    public string AppVersion { get; set; } = "";
    public FeedbackStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
    public Guid? HandledByUserId { get; set; }
}

public sealed class FeedbackAttachment
{
    public Guid FeedbackId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
    public string Sha256 { get; set; } = "";
}
```

DbContext: add `public DbSet<Feedback> Feedback => Set<Feedback>();` and
`public DbSet<FeedbackAttachment> FeedbackAttachments => Set<FeedbackAttachment>();`, plus the mapping at the end
of `OnModelCreating`:

```csharp
        var feedback = model.Entity<Feedback>(); feedback.ToTable("feedback"); feedback.HasKey(x => x.Id);
        feedback.Property(x => x.Message).HasMaxLength(4000); feedback.Property(x => x.FlightNumber).HasMaxLength(10);
        feedback.Property(x => x.AppVersion).HasMaxLength(40);
        feedback.HasIndex(x => new { x.UserId, x.CreatedAt }); feedback.HasIndex(x => new { x.Status, x.CreatedAt });
        feedback.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        feedback.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.HandledByUserId).OnDelete(DeleteBehavior.Restrict);
        var attachment = model.Entity<FeedbackAttachment>(); attachment.ToTable("feedback_attachment"); attachment.HasKey(x => x.FeedbackId);
        attachment.Property(x => x.FileName).HasMaxLength(120); attachment.Property(x => x.ContentType).HasMaxLength(40);
        attachment.Property(x => x.Sha256).HasMaxLength(64);
        attachment.HasOne<Feedback>().WithOne().HasForeignKey<FeedbackAttachment>(x => x.FeedbackId).OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 3:** The migration (SQL, Down, and the frozen `FeedbackModel`):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Alpha6Ops.Accounts.Migrations;

[DbContext(typeof(AccountsDbContext))]
[Migration("202610040002_Feedback")]
public sealed class Feedback : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => FeedbackModel.Build(modelBuilder);

    // Problem reports from pilots, read by allowlisted staff. Attachments live apart from media_blob, whose
    // constraints admit only small images.
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE feedback (
            "Id" uuid CONSTRAINT "PK_feedback" PRIMARY KEY,
            "UserId" uuid NOT NULL CONSTRAINT "FK_feedback_user_account_UserId" REFERENCES user_account ("Id") ON DELETE RESTRICT,
            "Category" integer NOT NULL CHECK ("Category" IN (0,1,2,3)),
            "Message" varchar(4000) NOT NULL CHECK (length(btrim("Message")) >= 10),
            "FlightNumber" varchar(10) NOT NULL DEFAULT '',
            "FlownOn" date NULL,
            "AppVersion" varchar(40) NOT NULL DEFAULT '',
            "Status" integer NOT NULL CHECK ("Status" IN (0,1)),
            "CreatedAt" timestamptz NOT NULL,
            "HandledAt" timestamptz NULL,
            "HandledByUserId" uuid NULL CONSTRAINT "FK_feedback_user_account_HandledByUserId" REFERENCES user_account ("Id") ON DELETE RESTRICT,
            CONSTRAINT feedback_handled_date CHECK (("Status" = 1) = ("HandledAt" IS NOT NULL)));
        CREATE INDEX "IX_feedback_UserId_CreatedAt" ON feedback ("UserId", "CreatedAt");
        CREATE INDEX "IX_feedback_Status_CreatedAt" ON feedback ("Status", "CreatedAt");
        CREATE INDEX "IX_feedback_HandledByUserId" ON feedback ("HandledByUserId");
        CREATE TABLE feedback_attachment (
            "FeedbackId" uuid CONSTRAINT "PK_feedback_attachment" PRIMARY KEY
                CONSTRAINT "FK_feedback_attachment_feedback_FeedbackId" REFERENCES feedback ("Id") ON DELETE CASCADE,
            "FileName" varchar(120) NOT NULL,
            "ContentType" varchar(40) NOT NULL CHECK ("ContentType" IN ('image/png','image/jpeg','application/zip','application/json')),
            "Bytes" bytea NOT NULL CHECK (octet_length("Bytes") BETWEEN 1 AND 5242880),
            "Sha256" varchar(64) NOT NULL);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TABLE feedback_attachment;
        DROP TABLE feedback;
        """);
}

internal static class FeedbackModel
{
    internal static void Build(ModelBuilder model)
    {
        AccountDeletionModel.Build(model);
        model.Entity("Alpha6Ops.Accounts.Feedback", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
            b.Property<string>("AppVersion").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<int>("Category").HasColumnType("integer");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("FlightNumber").IsRequired().HasMaxLength(10).HasColumnType("character varying(10)");
            b.Property<DateOnly?>("FlownOn").HasColumnType("date");
            b.Property<DateTimeOffset?>("HandledAt").HasColumnType("timestamp with time zone");
            b.Property<Guid?>("HandledByUserId").HasColumnType("uuid");
            b.Property<string>("Message").IsRequired().HasMaxLength(4000).HasColumnType("character varying(4000)");
            b.Property<int>("Status").HasColumnType("integer");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.HasKey("Id"); b.HasIndex("HandledByUserId"); b.HasIndex("Status", "CreatedAt"); b.HasIndex("UserId", "CreatedAt");
            b.ToTable("feedback");
        });
        model.Entity("Alpha6Ops.Accounts.FeedbackAttachment", b =>
        {
            b.Property<Guid>("FeedbackId").HasColumnType("uuid");
            b.Property<byte[]>("Bytes").IsRequired().HasColumnType("bytea");
            b.Property<string>("ContentType").IsRequired().HasMaxLength(40).HasColumnType("character varying(40)");
            b.Property<string>("FileName").IsRequired().HasMaxLength(120).HasColumnType("character varying(120)");
            b.Property<string>("Sha256").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.HasKey("FeedbackId"); b.ToTable("feedback_attachment");
        });
        model.Entity("Alpha6Ops.Accounts.Feedback", b =>
        {
            b.HasOne("Alpha6Ops.Accounts.UserAccount", null).WithMany().HasForeignKey("HandledByUserId").OnDelete(DeleteBehavior.Restrict);
            b.HasOne("Alpha6Ops.Accounts.UserAccount", null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
        model.Entity("Alpha6Ops.Accounts.FeedbackAttachment", b =>
            b.HasOne("Alpha6Ops.Accounts.Feedback", null).WithOne().HasForeignKey("Alpha6Ops.Accounts.FeedbackAttachment", "FeedbackId").OnDelete(DeleteBehavior.Cascade).IsRequired());
    }
}
```

Snapshot: `protected override void BuildModel(ModelBuilder modelBuilder) => FeedbackModel.Build(modelBuilder);`

**Naming note:** the migration class is named `Feedback`, the same as the entity, but it sits in the `.Migrations`
namespace. Inside `FeedbackModel` refer to the entity only by string name. If it still causes ambiguity, name the
class `FeedbackReports`.

- [ ] **Step 4:** Build and run the accounts suite against the local DB. "migration snapshot matches current model"
  and "migrations are repeatable" (9) pass. If the snapshot check fails, compare `HasIndex`/annotation order with
  the runtime model and adjust `FeedbackModel`.
- [ ] **Step 5:** Commit `feat(accounts): feedback tables`.

---

### Task 3: Service operations, deletion and export

**Files:**
- Create: `src/Alpha6Ops.Accounts/AccountsService.Feedback.cs`
- Modify: `src/Alpha6Ops.Accounts/AccountsService.Deletion.cs` (`Erase`; export record and query)
- Modify: `src/Alpha6Ops.Server/Pages/Account/Profile.cshtml.cs` (`feedback.json` in the ZIP, README line)
- Modify: `tests/Alpha6Ops.Accounts.Tests/Program.cs` (DB section)

**Interfaces:**
- Consumes: Task 1 and Task 2 types.
- Produces:
  - `record FeedbackView(Guid Id, FeedbackCategory Category, string Message, string FlightNumber, DateOnly? FlownOn,
    string AppVersion, FeedbackStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? HandledAt,
    string? AttachmentName)`
  - `record StaffFeedbackView(FeedbackView Report, Guid UserId, string ReporterName, string ReporterEmail,
    string ReporterCallsign, int? AttachmentBytes)`
  - `Task<FeedbackView> SubmitFeedbackAsync(ActorIdentity, FeedbackRequest, FeedbackFile?, CancellationToken)`
  - `Task<FeedbackView[]> ListMyFeedbackAsync(ActorIdentity, CancellationToken)`
  - `Task<StaffFeedbackView[]> ListFeedbackAsync(ActorIdentity, StaffAccess, FeedbackStatus?, CancellationToken)`
  - `Task<StaffFeedbackView> GetFeedbackAsync(ActorIdentity, StaffAccess, Guid, CancellationToken)`
  - `Task<CleanAttachment> GetFeedbackAttachmentAsync(ActorIdentity, StaffAccess, Guid, CancellationToken)`
  - `Task<StaffFeedbackView> SetFeedbackStatusAsync(ActorIdentity, StaffAccess, Guid, FeedbackStatus, CancellationToken)`
  - `AccountExport` gains `FeedbackView[] Feedback`.

- [ ] **Step 1: Failing DB tests**, placed after the deletion tests' setup actors exist. Use fresh actors:

```csharp
var reporter = actor with { Subject = prefix + "-reporter", DisplayName = "Reporter", Email = prefix + "-reporter@example.com", HasMfa = false };
var staffer = actor with { Subject = prefix + "-staff", DisplayName = "Staffer", Email = prefix + "-staff@example.com", HasMfa = true, AuthenticatedAt = DateTimeOffset.UtcNow };
var staffList = StaffAccess.FromEmails(staffer.Email);
var report = new FeedbackRequest(FeedbackCategory.FlightRecording, "Block in never fired at the gate.", "S4601", null);
var sent = await Run(s => s.SubmitFeedbackAsync(reporter, report, new FeedbackFile("log.json", "{\"ok\":true}"u8.ToArray())));
Check(sent is { Status: FeedbackStatus.New, AttachmentName: "log.json", FlightNumber: "S4601" }, "a report with an attachment is stored as new");
await Run(s => s.SubmitFeedbackAsync(reporter, report with { Category = FeedbackCategory.Idea }, null));
Check((await Run(s => s.ListMyFeedbackAsync(reporter))).Length == 2 && (await Run(s => s.ListMyFeedbackAsync(staffer))).Length == 0, "pilots see only their own reports");
for (var i = 0; i < 3; i++) await Run(s => s.SubmitFeedbackAsync(reporter, report, null));
await RejectAsync(() => Run(s => s.SubmitFeedbackAsync(reporter, report, null)), "feedback_rate_limited");
await RejectAsync(() => Run(s => s.ListFeedbackAsync(reporter, staffList, null)), "not_found");
await RejectAsync(() => Run(s => s.ListFeedbackAsync(staffer with { HasMfa = false, AuthenticatedAt = null }, staffList, null)), "mfa_required");
var inbox = await Run(s => s.ListFeedbackAsync(staffer, staffList, FeedbackStatus.New));
Check(inbox.Any(x => x.Report.Id == sent.Id && x.ReporterEmail == reporter.Email.ToLowerInvariant() && x.AttachmentBytes == 11), "staff see new reports with the reporter and attachment size");
var file = await Run(s => s.GetFeedbackAttachmentAsync(staffer, staffList, sent.Id));
Check(file is { ContentType: "application/json", FileName: "log.json" } && file.Bytes.Length == 11, "staff download the attachment");
var handled = await Run(s => s.SetFeedbackStatusAsync(staffer, staffList, sent.Id, FeedbackStatus.Handled));
Check(handled.Report is { Status: FeedbackStatus.Handled, HandledAt: not null }, "staff mark a report handled");
Check((await Run(s => s.SetFeedbackStatusAsync(staffer, staffList, sent.Id, FeedbackStatus.New))).Report is { Status: FeedbackStatus.New, HandledAt: null }, "and can reopen it");
await RejectAsync(() => Run(s => s.GetFeedbackAsync(staffer, staffList, Guid.NewGuid())), "not_found");
Check((await Run(s => s.ExportAsync(reporter))).Data.Feedback.Length == 5, "the data export includes the pilot's reports");
```

Deletion: after the existing purge test passes, add a check that a purged user's reports are gone. Use a dedicated
actor that submits one report with an attachment, requests deletion, and is purged with the clock moved forward.
Mirror how the existing deletion test advances time (it uses a custom `TimeProvider`; reuse its helper). Then:

```csharp
await using (var db = Context())
    Check(!await db.Feedback.AnyAsync(x => x.UserId == leaverId) && !await db.FeedbackAttachments.AnyAsync(x => x.FeedbackId == leaverReport.Id), "erasing an account removes its reports and attachments");
```

Pending deletion (Review Focus 5): while the actor is pending deletion,
`await RejectAsync(() => Run(s => s.SubmitFeedbackAsync(pendingActor, report, null)), "account_pending_deletion");`

- [ ] **Step 2:** Run. It fails to compile, then fails.
- [ ] **Step 3: Implement** `AccountsService.Feedback.cs`:

```csharp
using Alpha6Ops.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alpha6Ops.Accounts;

public sealed record FeedbackView(Guid Id, FeedbackCategory Category, string Message, string FlightNumber, DateOnly? FlownOn,
    string AppVersion, FeedbackStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? HandledAt, string? AttachmentName);
public sealed record StaffFeedbackView(FeedbackView Report, Guid UserId, string ReporterName, string ReporterEmail, string ReporterCallsign, int? AttachmentBytes);

// Problem reports. Pilots write and read their own; allowlisted staff with a fresh MFA read all of them.
public sealed partial class AccountsService
{
    public Task<FeedbackView> SubmitFeedbackAsync(ActorIdentity actor, FeedbackRequest request, FeedbackFile? file, CancellationToken ct = default) =>
        Execute(actor, async (user, now) =>
        {
            var clean = FeedbackRules.Request(request, DateOnly.FromDateTime(now.UtcDateTime));
            var attachment = file is null ? null : FeedbackRules.Attachment(file);
            var since = now.AddHours(-1);
            if (await db.Feedback.CountAsync(x => x.UserId == user.Id && x.CreatedAt > since, ct) >= FeedbackRules.PerHour)
                throw new IdentityException("feedback_rate_limited", "You've sent several reports in the last hour. Try again a little later.", 429);
            if (attachment is not null && await db.FeedbackAttachments.SumAsync(x => (long)x.Bytes.Length, ct) + attachment.Bytes.Length > FeedbackRules.StorageCapBytes)
                throw new IdentityException("feedback_storage_full", "Attachments are full for now. Send the report without the file and we'll ask for it.", 507);
            var version = await db.Profiles.Where(x => x.UserId == user.Id).Select(x => x.LastSeenVersion).FirstOrDefaultAsync(ct) ?? "";
            var row = new Feedback { Id = Guid.NewGuid(), UserId = user.Id, Category = clean.Category, Message = clean.Message!,
                FlightNumber = clean.FlightNumber!, FlownOn = clean.FlownOn, AppVersion = version, Status = FeedbackStatus.New, CreatedAt = now };
            db.Feedback.Add(row);
            if (attachment is not null)
                db.FeedbackAttachments.Add(new() { FeedbackId = row.Id, FileName = attachment.FileName, ContentType = attachment.ContentType, Bytes = attachment.Bytes, Sha256 = attachment.Sha256 });
            Audit(user.Id, null, "feedback.submitted", row.Id, clean.Category.ToString(), now);
            return View(row, attachment?.FileName);
        }, ct);

    public Task<FeedbackView[]> ListMyFeedbackAsync(ActorIdentity actor, CancellationToken ct = default) => Execute(actor, async (user, _) =>
        (await Reports(db.Feedback.Where(x => x.UserId == user.Id), ct)).Select(x => x.Report).ToArray(), ct);

    public Task<StaffFeedbackView[]> ListFeedbackAsync(ActorIdentity actor, StaffAccess staff, FeedbackStatus? status, CancellationToken ct = default) =>
        Execute(actor, async (_, now) =>
        {
            RequireStaff(actor, staff, now);
            return await Reports(status is { } s ? db.Feedback.Where(x => x.Status == s) : db.Feedback, ct);
        }, ct);

    public Task<StaffFeedbackView> GetFeedbackAsync(ActorIdentity actor, StaffAccess staff, Guid id, CancellationToken ct = default) =>
        Execute(actor, async (_, now) => { RequireStaff(actor, staff, now); return await Report(id, ct); }, ct);

    public Task<CleanAttachment> GetFeedbackAttachmentAsync(ActorIdentity actor, StaffAccess staff, Guid id, CancellationToken ct = default) =>
        Execute(actor, async (_, now) =>
        {
            RequireStaff(actor, staff, now);
            var a = await db.FeedbackAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.FeedbackId == id, ct) ?? throw NotFound();
            return new CleanAttachment(a.FileName, a.ContentType, a.Bytes, a.Sha256);
        }, ct);

    public Task<StaffFeedbackView> SetFeedbackStatusAsync(ActorIdentity actor, StaffAccess staff, Guid id, FeedbackStatus status, CancellationToken ct = default) =>
        Execute(actor, async (user, now) =>
        {
            RequireStaff(actor, staff, now);
            if (!Enum.IsDefined(status)) throw new IdentityException("invalid_feedback", "Choose New or Handled.", 400);
            var row = await db.Feedback.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
            if (row.Status != status)
            {
                row.Status = status;
                row.HandledAt = status == FeedbackStatus.Handled ? now : null;
                row.HandledByUserId = status == FeedbackStatus.Handled ? user.Id : null;
                Audit(user.Id, null, status == FeedbackStatus.Handled ? "feedback.handled" : "feedback.reopened", row.Id, "", now);
                await db.SaveChangesAsync(ct);
            }
            return await Report(id, ct);
        }, ct);

    // Staff pages answer "not found" to everyone else, so their existence isn't revealed.
    private static void RequireStaff(ActorIdentity actor, StaffAccess staff, DateTimeOffset now)
    {
        if (!staff.IsStaff(actor)) throw NotFound();
        AccountRules.RequireRecentMfa(actor, now);
    }
    private static IdentityException NotFound() => new("not_found", "That page doesn't exist.", 404);

    private async Task<StaffFeedbackView> Report(Guid id, CancellationToken ct) =>
        (await Reports(db.Feedback.Where(x => x.Id == id), ct)).SingleOrDefault() ?? throw NotFound();

    private async Task<StaffFeedbackView[]> Reports(IQueryable<Feedback> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(500)
            .Select(x => new
            {
                Row = x,
                Attachment = db.FeedbackAttachments.Where(a => a.FeedbackId == x.Id).Select(a => new { a.FileName, Size = a.Bytes.Length }).FirstOrDefault(),
                User = db.Users.Where(u => u.Id == x.UserId).Select(u => new { u.DisplayName, u.Email }).First(),
                Callsign = db.Profiles.Where(p => p.UserId == x.UserId).Select(p => p.Callsign).FirstOrDefault()
            }).ToListAsync(ct);
        return rows.Select(x => new StaffFeedbackView(View(x.Row, x.Attachment?.FileName), x.Row.UserId, x.User.DisplayName, x.User.Email,
            x.Callsign ?? "", x.Attachment?.Size)).ToArray();
    }

    private static FeedbackView View(Feedback x, string? attachmentName) =>
        new(x.Id, x.Category, x.Message, x.FlightNumber, x.FlownOn, x.AppVersion, x.Status, x.CreatedAt, x.HandledAt, attachmentName);
}
```

**Note on the "not found" lists:** `ListMyFeedbackAsync` returns `.Report` from `Reports`. That reuse is fine; the
reporter fields are simply unused there.

`Erase` (`AccountsService.Deletion.cs`), before `await db.Profiles...`:

```csharp
        await db.Feedback.Where(x => x.UserId == id).ExecuteDeleteAsync(ct); // attachments cascade
```

Export: add `FeedbackView[] Feedback` as the last parameter of `AccountExport`. In `ExportAsync`, compute
`var feedback = (await Reports(db.Feedback.Where(x => x.UserId == user.Id), ct)).Select(x => x.Report).ToArray();`
and pass it. In `Profile.cshtml.cs`, add `AddJson("feedback.json", data.Feedback);` and the README line
`feedback.json  problem reports you sent (attachments are not included)`.

- [ ] **Step 4:** Run the accounts suite with the DB. Everything passes.
- [ ] **Step 5:** Commit `feat(accounts): submit, list and triage problem reports`.

---

### Task 4: Web pages, links and settings

**Files:**
- Modify: `src/Alpha6Ops.Server/Program.cs` (register `StaffAccess` from `Staff:Emails`, always, so unconfigured
  servers also have one)
- Create: `src/Alpha6Ops.Server/Pages/Account/Feedback.cshtml` + `.cshtml.cs`
- Create: `src/Alpha6Ops.Server/Pages/Staff/Feedback.cshtml` + `.cshtml.cs` (list, `@page "/Staff/Feedback"`)
- Create: `src/Alpha6Ops.Server/Pages/Staff/Report.cshtml` + `.cshtml.cs` (`@page "/Staff/Feedback/{id:guid}"`,
  handlers `Attachment` (GET) and `Status` (POST))
- Create: `src/Alpha6Ops.Server/Pages/Staff/StaffPage.cs`: the base page that maps `not_found` to `NotFound()`
- Modify: `Pages/Account/Index.cshtml` (a "Report a problem" button in the account actions), `Pages/Support.cshtml`
  (a card), `Pages/Download.cshtml` (point "If something goes wrong" at `/Account/Feedback`),
  `Pages/Privacy.cshtml` (a "Problem reports" entry), `wwwroot/css/site.css` (small table and radio styles if
  needed), `docs/hosting.md` (`Staff__Emails`)
- Modify: `tests/Alpha6Ops.Server.Tests/Program.cs`: a `Browser(subject, mfa)` helper on `ServerFactory`, a
  multipart helper, and a feedback section using its own `ServerFactory` with `Staff:Emails`.

**Interfaces — Consumes:** the Task 3 service methods and `StaffAccess`.

- [ ] **Step 1: Failing server tests.** At the end of the authenticated section, before the final summary:

```csharp
using (var feedbackHost = new ServerFactory(connection, settings: new() { ["Staff:Emails"] = $"staff-{run}@example.test" }))
{
    using var reporterBrowser = feedbackHost.Browser($"reporter-{run}");
    using var staffBrowser = feedbackHost.Browser($"staff-{run}");
    using var staffNoMfa = feedbackHost.Browser($"staff-{run}", mfa: false);
    using var anon = feedbackHost.CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
    Check((await anon.GetAsync("/Account/Feedback")).StatusCode == HttpStatusCode.Redirect, "The report form needs sign-in");
    var formPage = await reporterBrowser.GetAsync("/Account/Feedback");
    var formHtml = await formPage.Content.ReadAsStringAsync();
    Check(formPage.IsSuccessStatusCode && formHtml.Contains("enctype=\"multipart/form-data\"") && !Regex.IsMatch(formHtml, @"\sstyle=""") && !formHtml.Contains("<script"), "The report form renders without scripts or inline styles");
    Check((await reporterBrowser.PostAsync("/Account/Feedback", new FormUrlEncodedContent(new Dictionary<string, string> { ["Message"] = "forged report text" }))).StatusCode == HttpStatusCode.BadRequest, "Report without CSRF token is rejected");
    var fields = new Dictionary<string, string> { ["Category"] = "Bug", ["Message"] = "The map froze after takeoff from KMKE.", ["FlightNumber"] = "s4 601", ["FlownOn"] = "" };
    var sentWithFile = await PostMultipartAsync(reporterBrowser, formPage, formHtml, "/Account/Feedback", fields, ("Attachment", Png(4, 4), "frozen map.png"));
    Check(sentWithFile.StatusCode == HttpStatusCode.Redirect, "A report with a screenshot is accepted");
    var sentEmptyFile = await PostMultipartAsync(reporterBrowser, formPage, formHtml, "/Account/Feedback", fields, ("Attachment", [], ""));
    Check(sentEmptyFile.StatusCode == HttpStatusCode.Redirect, "An empty file input sends the report without an attachment");
    var lying = await PostMultipartAsync(reporterBrowser, formPage, formHtml, "/Account/Feedback", fields, ("Attachment", "<html>hi</html>"u8.ToArray(), "evil.png"));
    Check(lying.StatusCode == HttpStatusCode.BadRequest && (await lying.Content.ReadAsStringAsync()).Contains("PNG or JPEG"), "A file that isn't what its name says is refused with a readable message");
    var mine = await reporterBrowser.GetStringAsync("/Account/Feedback");
    Check(mine.Contains("The map froze after takeoff") && mine.Contains("S4601"), "Pilots see their own reports");
    Check((await reporterBrowser.GetAsync("/Staff/Feedback")).StatusCode == HttpStatusCode.NotFound, "Staff inbox is 404 for pilots");
    Check((await browser.GetAsync("/Staff/Feedback")).StatusCode == HttpStatusCode.NotFound, "Staff inbox is 404 when no staff are configured");
    var noMfaInbox = await staffNoMfa.GetAsync("/Staff/Feedback");
    Check(noMfaInbox.StatusCode == HttpStatusCode.Forbidden && (await noMfaInbox.Content.ReadAsStringAsync()).Contains("/auth/step-up"), "Staff without a fresh MFA are sent to a security check");
    var inboxHtml = await staffBrowser.GetStringAsync("/Staff/Feedback");
    var reportId = Regex.Match(inboxHtml, "/Staff/Feedback/([0-9a-f-]{36})").Groups[1].Value;
    Check(inboxHtml.Contains($"reporter-{run}") && reportId.Length == 36, "Staff see the new reports");
    var reportPage = await staffBrowser.GetAsync($"/Staff/Feedback/{reportId}");
    var reportHtml = await reportPage.Content.ReadAsStringAsync();
    var download = await staffBrowser.GetAsync($"/Staff/Feedback/{reportId}?handler=Attachment");
    Check(download.Content.Headers.ContentDisposition?.DispositionType == "attachment" && download.Headers.TryGetValues("X-Content-Type-Options", out var sniff) && sniff.Contains("nosniff"), "Attachments download, never render inline");
    var markHandled = await SubmitAsync(staffBrowser, reportPage, reportHtml, $"/Staff/Feedback/{reportId}?handler=Status", new() { ["status"] = "Handled" });
    Check(markHandled.StatusCode == HttpStatusCode.Redirect && !(await staffBrowser.GetStringAsync("/Staff/Feedback")).Contains(reportId), "Handled reports leave the New list");
}
```

Helpers (add next to `UploadAsync`, and to `ServerFactory`):

```csharp
static async Task<HttpResponseMessage> PostMultipartAsync(HttpClient client, HttpResponseMessage page, string html, string path, Dictionary<string, string> fields, (string Field, byte[] Bytes, string FileName)? file)
{
    var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    var antiforgery = page.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies.Select(c => c.Split(';')[0]).FirstOrDefault(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal)) : null;
    var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
    foreach (var (key, value) in fields) content.Add(new StringContent(value), key);
    if (file is { } f) { var part = new ByteArrayContent(f.Bytes); part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); content.Add(part, f.Field, f.FileName); }
    using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
    request.Headers.Add("Cookie", string.Join("; ", client.DefaultRequestHeaders.GetValues("Cookie").Append(antiforgery ?? "")));
    return await client.SendAsync(request);
}
```

```csharp
    // A signed-in website session for `subject`, as the cookie the portal issues after login.
    public HttpClient Browser(string subject, bool mfa = true)
    {
        var claims = Claims(subject);
        if (!mfa) { claims[TrustedActor.Prefix + "mfa"] = false; claims.Remove(TrustedActor.Prefix + "mfa_at"); }
        var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims.Select(p => new Claim(p.Key, p.Value.ToString()!)), CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, CookieAuthenticationDefaults.AuthenticationScheme);
        var client = CreateClient(new() { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", "__Host-Alpha6.Session=" + options.TicketDataFormat.Protect(ticket));
        return client;
    }
```

**Empty-file note:** an empty `<input type=file>` posts a part with an empty filename. ASP.NET then binds
`IFormFile` as `null` or as `Length == 0`. The page model treats `Attachment is null || Attachment.Length == 0`
as "no attachment".

- [ ] **Step 2:** Run. The tests fail (404s, missing helper).

- [ ] **Step 3: Implement.** Register the staff list in `Program.cs`, next to the other `Configure` lines and
  outside `if (settings.IsConfigured)`:

```csharp
builder.Services.AddSingleton(StaffAccess.FromEmails(builder.Configuration["Staff:Emails"]));
```

`Pages/Account/Feedback.cshtml.cs`:

```csharp
using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Account;

[RequestSizeLimit(5_600_000), RequestFormLimits(MultipartBodyLengthLimit = 5_600_000)]
public class FeedbackModel(AccountsService accounts, ServerSettings settings) : AccountPage(accounts, settings)
{
    [BindProperty] public FeedbackCategory Category { get; set; } = FeedbackCategory.FlightRecording;
    [BindProperty] public string? Message { get; set; }
    [BindProperty] public string? FlightNumber { get; set; }
    [BindProperty] public DateOnly? FlownOn { get; set; }
    [BindProperty] public IFormFile? Attachment { get; set; }
    public FeedbackView[] Reports { get; private set; } = [];
    public bool Sent { get; private set; }

    public static IReadOnlyList<(FeedbackCategory Value, string Label, string Hint)> Categories { get; } =
    [
        (FeedbackCategory.FlightRecording, "A flight wasn't recorded right", "A missing or wrong OUT, OFF, ON or IN time, or a flight that didn't log."),
        (FeedbackCategory.Bug, "Something is broken", "An error, a crash, or a screen that doesn't work."),
        (FeedbackCategory.Idea, "An idea", "Something that would make S4 OPS better for you."),
        (FeedbackCategory.Other, "Something else", "")
    ];
    public static string Label(FeedbackCategory c) => Categories.First(x => x.Value == c).Label;

    public async Task<IActionResult> OnGetAsync(bool sent, CancellationToken ct)
    {
        try { Reports = await Accounts.ListMyFeedbackAsync(Actor, ct); Sent = sent; return Page(); }
        catch (IdentityException ex) { return Failure(ex); }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        try
        {
            FeedbackFile? file = null;
            if (Attachment is { Length: > 0 })
            {
                using var buffer = new MemoryStream();
                await Attachment.CopyToAsync(buffer, ct);
                file = new(Attachment.FileName, buffer.ToArray());
            }
            await Accounts.SubmitFeedbackAsync(Actor, new(Category, Message, FlightNumber, FlownOn), file, ct);
            return RedirectToPage(new { sent = true });
        }
        catch (IdentityException ex)
        {
            try { Reports = await Accounts.ListMyFeedbackAsync(Actor, ct); } catch (IdentityException) { }
            return Failure(ex);
        }
    }
}
```

`Pages/Account/Feedback.cshtml`:

```cshtml
@page
@model Alpha6Ops.Server.Pages.Account.FeedbackModel
@{ ViewData["Title"] = "Report a problem"; }
<div class="page-heading"><a href="/Account">← Your workspaces</a><div class="eyebrow">Testing</div><h1>Report a problem.</h1>
<p class="lead">Tell us what happened. Reports go straight to the Sierra 4 team, tied to your account so we can follow up.</p></div>
@if (Model.Sent) { <p class="notice" role="status">Thanks, your report was sent.</p> }
<partial name="_AccountError" model="Model" />
<section class="card">
    <form method="post" enctype="multipart/form-data" class="stack">
        <fieldset class="stack"><legend>What kind of problem?</legend>
        @foreach (var c in Alpha6Ops.Server.Pages.Account.FeedbackModel.Categories) {
            <label class="check"><input type="radio" name="Category" value="@c.Value" checked="@(Model.Category == c.Value)" /> <span><strong>@c.Label</strong>@if (c.Hint.Length > 0) { <small class="block">@c.Hint</small> }</span></label>
        }
        </fieldset>
        <div class="stack"><label asp-for="Message">What happened?</label><textarea asp-for="Message" rows="6" maxlength="4000" required minlength="10" placeholder="What you did, what you expected, and what happened instead."></textarea></div>
        <div class="grid two">
            <div class="stack"><label asp-for="FlightNumber">Flight number (optional)</label><input asp-for="FlightNumber" maxlength="10" class="upper" placeholder="S4601" /></div>
            <div class="stack"><label asp-for="FlownOn">Date flown (optional)</label><input asp-for="FlownOn" type="date" /></div>
        </div>
        <div class="stack"><label asp-for="Attachment">Attachment (optional)</label><input asp-for="Attachment" type="file" accept=".png,.jpg,.jpeg,.zip,.json" />
        <small>One screenshot (PNG or JPEG) or the desktop's test log (Settings → Export test log), up to 5 MB.</small></div>
        <div class="actions"><button class="button" type="submit">Send report</button></div>
    </form>
</section>
@if (Model.Reports.Length > 0) {
<section class="card">
    <h2>Your reports</h2>
    <ul class="report-list">
    @foreach (var r in Model.Reports) {
        <li><span class="badge @(r.Status == FeedbackStatus.Handled ? "ok" : "")">@(r.Status == FeedbackStatus.Handled ? "Handled" : "Received")</span>
        <strong>@Alpha6Ops.Server.Pages.Account.FeedbackModel.Label(r.Category)</strong>@if (r.FlightNumber.Length > 0) { <span class="mono"> · @r.FlightNumber</span> }
        <span class="muted"> · @r.CreatedAt.ToString("u")</span><p>@r.Message</p></li>
    }
    </ul>
</section>
}
```

`_ViewImports.cshtml` needs `@using Alpha6Ops.Accounts` for `FeedbackStatus`. Check whether it's already there; if
not, use the fully qualified name.

`Pages/Staff/StaffPage.cs`:

```csharp
using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;
using Alpha6Ops.Server.Pages.Account;

namespace Alpha6Ops.Server.Pages.Staff;

// Staff pages reuse the account error handling, but "not found" really answers 404 so the pages stay hidden.
public abstract class StaffPage(AccountsService accounts, ServerSettings settings, StaffAccess staff) : AccountPage(accounts, settings)
{
    protected StaffAccess Staff { get; } = staff;
    protected IActionResult StaffFailure(IdentityException error) => error.Code == "not_found" ? NotFound() : Failure(error);
}
```

**Precondition:** this needs `AccountPage.Failure` to be `protected` and `AccountPage` to be public abstract. It
already is.

`Pages/Staff/Feedback.cshtml.cs` (list):

```csharp
using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Staff;

public class FeedbackInboxModel(AccountsService accounts, ServerSettings settings, StaffAccess staff) : StaffPage(accounts, settings, staff)
{
    public StaffFeedbackView[] Reports { get; private set; } = [];
    public string Show { get; private set; } = "new";

    public async Task<IActionResult> OnGetAsync(string? show, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        Show = show is "handled" or "all" ? show : "new";
        FeedbackStatus? filter = Show switch { "handled" => FeedbackStatus.Handled, "all" => null, _ => FeedbackStatus.New };
        try { Reports = await Accounts.ListFeedbackAsync(Actor, Staff, filter, ct); return Page(); }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }
}
```

`Pages/Staff/Feedback.cshtml`:

```cshtml
@page "/Staff/Feedback"
@model Alpha6Ops.Server.Pages.Staff.FeedbackInboxModel
@{ ViewData["Title"] = "Problem reports"; }
<div class="page-heading"><div class="eyebrow">Staff</div><h1>Problem reports</h1>
<nav class="tabs" aria-label="Filter"><a href="?show=new" aria-current="@(Model.Show == "new" ? "page" : null)">New</a><a href="?show=handled" aria-current="@(Model.Show == "handled" ? "page" : null)">Handled</a><a href="?show=all" aria-current="@(Model.Show == "all" ? "page" : null)">All</a></nav></div>
<partial name="_AccountError" model="Model" />
@if (Model.ErrorMessage is null) {
<section class="card">
@if (Model.Reports.Length == 0) { <p class="muted">No reports here.</p> } else {
    <div class="table-wrap"><table>
        <thead><tr><th>Received</th><th>Kind</th><th>Pilot</th><th>Flight</th><th>Report</th><th></th></tr></thead>
        <tbody>
        @foreach (var r in Model.Reports) {
            <tr><td class="mono">@r.Report.CreatedAt.ToString("yyyy-MM-dd HH:mm")Z</td><td>@Alpha6Ops.Server.Pages.Account.FeedbackModel.Label(r.Report.Category)</td>
            <td>@r.ReporterName<br /><span class="muted break">@r.ReporterEmail</span></td><td class="mono">@r.Report.FlightNumber</td>
            <td>@(r.Report.Message.Length > 120 ? r.Report.Message[..120] + "…" : r.Report.Message)@if (r.AttachmentBytes is not null) { <span class="badge">file</span> }</td>
            <td><a href="/Staff/Feedback/@r.Report.Id">Open</a></td></tr>
        }
        </tbody>
    </table></div>
}
</section>
}
```

`Pages/Staff/Report.cshtml.cs`:

```csharp
using Alpha6Ops.Accounts;
using Alpha6Ops.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Alpha6Ops.Server.Pages.Staff;

public class ReportModel(AccountsService accounts, ServerSettings settings, StaffAccess staff) : StaffPage(accounts, settings, staff)
{
    public StaffFeedbackView? Report { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { Report = await Accounts.GetFeedbackAsync(Actor, Staff, id, ct); return Page(); }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }

    public async Task<IActionResult> OnGetAttachmentAsync(Guid id, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try
        {
            var file = await Accounts.GetFeedbackAttachmentAsync(Actor, Staff, id, ct);
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(file.Bytes, file.ContentType, file.FileName); // FileContentResult with a name => Content-Disposition: attachment
        }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, FeedbackStatus status, CancellationToken ct)
    {
        if (!Staff.IsConfigured) return NotFound();
        try { await Accounts.SetFeedbackStatusAsync(Actor, Staff, id, status, ct); return Redirect("/Staff/Feedback"); }
        catch (IdentityException ex) { return StaffFailure(ex); }
    }
}
```

`Pages/Staff/Report.cshtml`:

```cshtml
@page "/Staff/Feedback/{id:guid}"
@model Alpha6Ops.Server.Pages.Staff.ReportModel
@{ ViewData["Title"] = "Problem report"; }
<div class="page-heading"><a href="/Staff/Feedback">← Problem reports</a><div class="eyebrow">Staff</div><h1>Problem report</h1></div>
<partial name="_AccountError" model="Model" />
@if (Model.Report is { } r) {
<section class="card">
    <dl class="facts">
        <dt>Kind</dt><dd>@Alpha6Ops.Server.Pages.Account.FeedbackModel.Label(r.Report.Category)</dd>
        <dt>Pilot</dt><dd>@r.ReporterName · <span class="break">@r.ReporterEmail</span>@if (r.ReporterCallsign.Length > 0) { <span class="mono"> · @r.ReporterCallsign</span> }</dd>
        <dt>Received</dt><dd class="mono">@r.Report.CreatedAt.ToString("u")</dd>
        <dt>Flight</dt><dd class="mono">@(r.Report.FlightNumber.Length > 0 ? r.Report.FlightNumber : "—")@(r.Report.FlownOn is { } d ? $" · {d:yyyy-MM-dd}" : "")</dd>
        <dt>Desktop version</dt><dd class="mono">@(r.Report.AppVersion.Length > 0 ? r.Report.AppVersion : "—")</dd>
        <dt>Status</dt><dd>@(r.Report.Status == Alpha6Ops.Accounts.FeedbackStatus.Handled ? $"Handled {r.Report.HandledAt:u}" : "New")</dd>
    </dl>
    <h2>What happened</h2>
    <p class="prewrap">@r.Report.Message</p>
    @if (r.Report.AttachmentName is { } name) { <p><a class="button secondary" href="/Staff/Feedback/@r.Report.Id?handler=Attachment">Download @name (@((r.AttachmentBytes ?? 0) / 1024 + 1) KB)</a></p> }
    <form method="post" asp-page-handler="Status" asp-route-id="@r.Report.Id">
        <input type="hidden" name="status" value="@(r.Report.Status == Alpha6Ops.Accounts.FeedbackStatus.Handled ? "New" : "Handled")" />
        <button class="button" type="submit">@(r.Report.Status == Alpha6Ops.Accounts.FeedbackStatus.Handled ? "Reopen" : "Mark handled")</button>
    </form>
</section>
}
```

CSS (`site.css`, only the classes not already defined; grep first): `.prewrap { white-space: pre-wrap; }`,
`.block { display: block; }`, `.report-list { list-style: none; padding: 0; display: grid; gap: var(--space-4); }`,
`.tabs { display: flex; gap: var(--space-4); }`, `.tabs a[aria-current="page"] { color: var(--text); font-weight: 600; }`.
Reuse `.table-wrap`/`table` if they exist.

**Links:**
- `Account/Index.cshtml` account actions: add `<a class="button secondary" asp-page="Feedback">Report a problem</a>`.
- `Support.cshtml`: add the card
  `<article class="card" id="report"><h2>Report a problem</h2><p>If a flight wasn't recorded the way you expected, or something doesn't work, <a href="/Account/Feedback">send us a report</a> from your account. You can attach a screenshot or the desktop's test log (Settings → Export test log).</p></article>`
- `Download.cshtml` "If something goes wrong": replace the "Send it to <partial _Contact>" sentence with
  `<p><a href="/Account/Feedback">Report it from your account</a> and attach the file. The log holds the flight's recorded simulator readings and flight plan.</p>`
- `Privacy.cshtml` "What we store": add `<dt>Problem reports</dt><dd>Reports you send from your account, with any file you attach and the desktop version you last used. Only the Sierra 4 team can read them, and they're deleted with your account.</dd>`
- `docs/hosting.md` environment table: ``| `Staff__Emails` | Optional. Semicolon-separated emails (verified at Auth0) that may read problem reports at `/Staff/Feedback`, after a fresh MFA. Empty means the staff pages answer 404 |``

- [ ] **Step 4:** Build both solutions. Run the server suite with the DB, then the accounts suite. Everything
  passes. Run `/ui-check` (or Playwright by hand) on `/Account/Feedback` and `/Staff/Feedback` at 1440 and 390 px.
- [ ] **Step 5:** Commit `feat(portal): Report a problem page and staff inbox`.

---

### Task 5: Verify and hand off

- [ ] Full `/verify`: both builds, all four suites with the local PostgreSQL. Then stop PostgreSQL.
- [ ] Final whole-branch review (one reviewer on `feat/launch-prep` versus `merge/dan-7.26b`).
- [ ] Update `docs/s4-migration/PROGRESS.md` (`/handoff`): what landed and the settings Reece adds on Render
  (`Staff__Emails`, `Legal__Contact`, `Release__*`).
