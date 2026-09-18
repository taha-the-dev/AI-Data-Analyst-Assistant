using AnalystAI.Api.Models;
using AnalystAI.Api.Services;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AnalystAI.Api.Data;

/// <summary>
/// Accounts, everything they own, and the keys that encrypt their sessions.
///
/// Isolation between accounts is enforced here rather than in each endpoint.
/// Every owned entity carries a query filter on the signed-in account, so a
/// query that forgets to scope itself still only sees its own rows, and an id
/// that belongs to someone else reads as not found. Inserts are stamped with
/// the signed-in account, and an update can never move a record to another.
///
/// The model is shared; the database is not. <see cref="SqliteAppDbContext"/>
/// and <see cref="PostgresAppDbContext"/> each carry their own migrations.
/// </summary>
public abstract class AppDbContext(DbContextOptions options, ICurrentUser currentUser)
    : IdentityUserContext<AppUser>(options), IDataProtectionKeyContext
{
    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<DatasetColumn> DatasetColumns => Set<DatasetColumn>();
    public DbSet<DatasetSource> DatasetSources => Set<DatasetSource>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    /// <summary>
    /// The keys that encrypt session cookies. Kept here so a host that wipes its
    /// disk on restart does not sign everyone out.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// Read by every query filter. EF evaluates it per query on this context
    /// instance, so it is never shared between requests. It is null outside a
    /// signed-in request, which matches no rows at all.
    /// </summary>
    private string? CurrentUserId => currentUser.Id;

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Dataset>()
            .HasMany(d => d.Columns)
            .WithOne(c => c.Dataset!)
            .HasForeignKey(c => c.DatasetId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<DatasetSource>()
            .HasOne<Dataset>()
            .WithOne()
            .HasForeignKey<DatasetSource>(s => s.DatasetId)
            .OnDelete(DeleteBehavior.Cascade);

        b.Entity<ChatSession>()
            .HasMany(s => s.Messages)
            .WithOne(m => m.Session!)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Written out per entity rather than generated in a loop, so the rule
        // that keeps accounts apart can be read in one screen.
        b.Entity<Dataset>().HasQueryFilter(e => e.UserId == CurrentUserId);
        b.Entity<DatasetColumn>().HasQueryFilter(e => e.UserId == CurrentUserId);
        b.Entity<DatasetSource>().HasQueryFilter(e => e.UserId == CurrentUserId);
        b.Entity<ChatSession>().HasQueryFilter(e => e.UserId == CurrentUserId);
        b.Entity<ChatMessage>().HasQueryFilter(e => e.UserId == CurrentUserId);
        b.Entity<Report>().HasQueryFilter(e => e.UserId == CurrentUserId);
        b.Entity<UserSettings>().HasQueryFilter(e => e.UserId == CurrentUserId);

        // Deleting an account deletes everything it owns.
        b.Entity<Dataset>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DatasetColumn>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DatasetSource>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChatSession>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChatMessage>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Report>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<UserSettings>().HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Dataset>().HasIndex(e => e.UserId);
        b.Entity<DatasetColumn>().HasIndex(e => e.UserId);
        b.Entity<DatasetSource>().HasIndex(e => e.UserId);
        b.Entity<ChatSession>().HasIndex(e => e.UserId);
        b.Entity<ChatMessage>().HasIndex(e => e.UserId);
        b.Entity<Report>().HasIndex(e => e.UserId);
        b.Entity<UserSettings>().HasIndex(e => e.UserId).IsUnique();

    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// A new record belongs to the signed-in account whatever an endpoint set,
    /// and an existing record's owner is never written.
    /// </summary>
    private void StampOwnership()
    {
        foreach (var entry in ChangeTracker.Entries<IOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.UserId = CurrentUserId
                        ?? throw new InvalidOperationException("Data can only be saved inside a signed-in request.");
                    break;

                case EntityState.Modified:
                    entry.Property(nameof(IOwned.UserId)).IsModified = false;
                    break;
            }
        }
    }
}
