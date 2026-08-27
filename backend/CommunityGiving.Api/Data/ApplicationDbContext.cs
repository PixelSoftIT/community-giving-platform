using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using CommunityGiving.Api.Models;

namespace CommunityGiving.Api.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Household> Households => Set<Household>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<ProgramParticipant> ProgramParticipants => Set<ProgramParticipant>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<Donation> Donations => Set<Donation>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventRegistration> EventRegistrations => Set<EventRegistration>();
    public DbSet<OrganizationSettings> OrganizationSettings => Set<OrganizationSettings>();

    // Communications
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<NotificationGroup> NotificationGroups => Set<NotificationGroup>();
    public DbSet<NotificationGroupRecipient> NotificationGroupRecipients => Set<NotificationGroupRecipient>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    // Finance
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<IncomeEntry> IncomeEntries => Set<IncomeEntry>();

    // Meetings
    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<MeetingAttendee> MeetingAttendees => Set<MeetingAttendee>();

    // Program registration
    public DbSet<ProgramTerm> ProgramTerms => Set<ProgramTerm>();
    public DbSet<SiblingDiscountTier> SiblingDiscountTiers => Set<SiblingDiscountTier>();
    public DbSet<ProgramRegistrationBatch> ProgramRegistrationBatches => Set<ProgramRegistrationBatch>();
    public DbSet<ProgramRegistrationChild> ProgramRegistrationChildren => Set<ProgramRegistrationChild>();
    public DbSet<ProgramLevel> ProgramLevels => Set<ProgramLevel>();

    public DbSet<EventProject> EventProjects => Set<EventProject>();

    // Security
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Member>()
            .HasOne(m => m.Household)
            .WithMany(h => h.Members)
            .HasForeignKey(m => m.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<ProgramParticipant>()
            .HasOne(s => s.Household)
            .WithMany(h => h.ProgramParticipants)
            .HasForeignKey(s => s.HouseholdId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Donation>()
            .HasOne(d => d.Fund)
            .WithMany(f => f.Donations)
            .HasForeignKey(d => d.FundId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Fund>()
            .HasOne(f => f.Project)
            .WithMany(p => p.Funds)
            .HasForeignKey(f => f.ProjectId)
            .OnDelete(DeleteBehavior.SetNull); // deleting a project doesn't delete its funds' history

        builder.Entity<Donation>()
            .HasOne(d => d.Member)
            .WithMany(m => m.Donations)
            .HasForeignKey(d => d.MemberId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Entity<Donation>()
            .Property(d => d.Amount).HasColumnType("decimal(12,2)");

        builder.Entity<Fund>()
            .Property(f => f.GoalAmount).HasColumnType("decimal(12,2)");

        builder.Entity<Project>()
            .Property(p => p.Budget).HasColumnType("decimal(12,2)");

        builder.Entity<Event>()
            .Property(e => e.TicketPrice).HasColumnType("decimal(12,2)");

        builder.Entity<EventRegistration>()
            .HasOne(r => r.Event)
            .WithMany(e => e.Registrations)
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        // ---------- Communications ----------
        builder.Entity<NotificationGroupRecipient>()
            .HasOne(r => r.NotificationGroup)
            .WithMany(g => g.Recipients)
            .HasForeignKey(r => r.NotificationGroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<NotificationGroupRecipient>()
            .HasOne(r => r.Member).WithMany().HasForeignKey(r => r.MemberId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<NotificationGroupRecipient>()
            .HasOne(r => r.Contact).WithMany().HasForeignKey(r => r.ContactId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Notification>()
            .HasOne(n => n.NotificationGroup).WithMany()
            .HasForeignKey(n => n.NotificationGroupId).OnDelete(DeleteBehavior.SetNull);

        builder.Entity<NotificationDelivery>()
            .HasOne(d => d.Notification).WithMany(n => n.Deliveries)
            .HasForeignKey(d => d.NotificationId).OnDelete(DeleteBehavior.Cascade);

        // ---------- Finance ----------
        builder.Entity<Invoice>().Property(i => i.Id);
        builder.Entity<InvoiceLineItem>()
            .Property(l => l.UnitPrice).HasColumnType("decimal(12,2)");
        builder.Entity<InvoiceLineItem>()
            .HasOne(l => l.Invoice).WithMany(i => i.LineItems)
            .HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Invoice>().HasOne(i => i.Member).WithMany().HasForeignKey(i => i.MemberId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Invoice>().HasOne(i => i.Contact).WithMany().HasForeignKey(i => i.ContactId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Invoice>().HasOne(i => i.Fund).WithMany().HasForeignKey(i => i.FundId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Invoice>().HasOne(i => i.Project).WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Invoice>().HasIndex(i => i.InvoiceNumber).IsUnique();

        builder.Entity<Receipt>().HasOne(r => r.Donation).WithMany().HasForeignKey(r => r.DonationId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Receipt>().HasOne(r => r.Invoice).WithMany().HasForeignKey(r => r.InvoiceId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Receipt>().HasIndex(r => r.ReceiptNumber).IsUnique();

        builder.Entity<Expense>().Property(e => e.Amount).HasColumnType("decimal(12,2)");
        builder.Entity<Expense>().HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<Expense>().HasOne(e => e.Fund).WithMany().HasForeignKey(e => e.FundId).OnDelete(DeleteBehavior.SetNull);

        builder.Entity<IncomeEntry>().Property(e => e.Amount).HasColumnType("decimal(12,2)");
        builder.Entity<IncomeEntry>().HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<IncomeEntry>().HasOne(e => e.Fund).WithMany().HasForeignKey(e => e.FundId).OnDelete(DeleteBehavior.SetNull);

        // ---------- Meetings ----------
        builder.Entity<MeetingAttendee>()
            .HasOne(a => a.Meeting).WithMany(m => m.Attendees)
            .HasForeignKey(a => a.MeetingId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<MeetingAttendee>()
            .HasOne(a => a.Member).WithMany().HasForeignKey(a => a.MemberId).OnDelete(DeleteBehavior.SetNull);

        // ---------- Security ----------
        builder.Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();
        builder.Entity<RefreshToken>().HasIndex(t => t.UserId);
        builder.Entity<AuditLog>().HasIndex(a => a.TimestampUtc);

        // Helpful indexes
        builder.Entity<Member>().HasIndex(m => m.Email);
        builder.Entity<Donation>().HasIndex(d => d.CreatedAtUtc);
        builder.Entity<Donation>().HasIndex(d => d.StripePaymentIntentId).IsUnique(false);
        builder.Entity<Contact>().HasIndex(c => c.Email);

        // ---------- Program registration ----------
        builder.Entity<ProgramTerm>().Property(t => t.FeePerChild).HasColumnType("decimal(12,2)");
        builder.Entity<SiblingDiscountTier>().HasIndex(t => t.ChildPosition).IsUnique();
        builder.Entity<SiblingDiscountTier>().Property(t => t.DiscountPercent).HasColumnType("decimal(5,2)");

        builder.Entity<ProgramRegistrationBatch>().Property(b => b.TotalAmount).HasColumnType("decimal(12,2)");
        builder.Entity<ProgramRegistrationBatch>()
            .HasOne(b => b.ProgramTerm).WithMany().HasForeignKey(b => b.ProgramTermId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProgramRegistrationBatch>()
            .HasOne(b => b.Household).WithMany().HasForeignKey(b => b.HouseholdId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProgramRegistrationBatch>()
            .HasOne(b => b.RegisteredByMember).WithMany().HasForeignKey(b => b.RegisteredByMemberId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ProgramRegistrationChild>().Property(c => c.BaseFee).HasColumnType("decimal(12,2)");
        builder.Entity<ProgramRegistrationChild>().Property(c => c.FeeCharged).HasColumnType("decimal(12,2)");
        builder.Entity<ProgramRegistrationChild>().Property(c => c.DiscountPercentApplied).HasColumnType("decimal(5,2)");
        builder.Entity<ProgramRegistrationChild>()
            .HasOne(c => c.Batch).WithMany(b => b.Children).HasForeignKey(c => c.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ProgramRegistrationChild>()
            .HasOne(c => c.ProgramTerm).WithMany(t => t.RegisteredChildren).HasForeignKey(c => c.ProgramTermId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ProgramRegistrationChild>()
            .HasOne(c => c.ProgramParticipant).WithMany().HasForeignKey(c => c.ProgramParticipantId).OnDelete(DeleteBehavior.SetNull);

        // ---------- Event <-> Project many-to-many ----------
        builder.Entity<EventProject>().HasKey(ep => new { ep.EventId, ep.ProjectId });
        builder.Entity<EventProject>()
            .HasOne(ep => ep.Event).WithMany(e => e.EventProjects).HasForeignKey(ep => ep.EventId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<EventProject>()
            .HasOne(ep => ep.Project).WithMany().HasForeignKey(ep => ep.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}
