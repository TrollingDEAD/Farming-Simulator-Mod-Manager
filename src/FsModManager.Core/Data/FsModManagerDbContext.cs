using FsModManager.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FsModManager.Core.Data;

public sealed class FsModManagerDbContext : DbContext
{
    public FsModManagerDbContext(DbContextOptions<FsModManagerDbContext> options) : base(options)
    {
    }

    public DbSet<InstalledMod> InstalledMods => Set<InstalledMod>();

    public DbSet<ModSource> ModSources => Set<ModSource>();

    public DbSet<ConflictRecord> ConflictRecords => Set<ConflictRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstalledMod>(entity =>
        {
            entity.HasIndex(m => m.InternalName);
            entity.Property(m => m.InternalName).IsRequired();
            entity.Property(m => m.FilePath).IsRequired();

            entity.HasOne(m => m.ModSource)
                .WithMany(s => s.InstalledMods)
                .HasForeignKey(m => m.ModSourceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ModSource>(entity =>
        {
            entity.Property(m => m.Url).IsRequired();
            entity.Property(m => m.Type).IsRequired();
        });

        modelBuilder.Entity<ConflictRecord>(entity =>
        {
            entity.HasOne(c => c.ModA)
                .WithMany()
                .HasForeignKey(c => c.ModAId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.ModB)
                .WithMany()
                .HasForeignKey(c => c.ModBId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
