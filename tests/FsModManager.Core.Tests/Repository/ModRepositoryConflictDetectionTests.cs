using FsModManager.Core.Data;
using FsModManager.Core.Data.Entities;
using FsModManager.Core.Models;
using FsModManager.Core.ModScanning.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FsModManager.Core.Tests.Repository;

public sealed class ModRepositoryConflictDetectionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly FsModManagerDbContext _dbContext;
    private readonly ModRepository _repository;

    public ModRepositoryConflictDetectionTests()
    {
        // Keep-alive in-memory Sqlite connection so the schema/data survive across the DbContext's lifetime.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<FsModManagerDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new FsModManagerDbContext(options);
        _dbContext.Database.EnsureCreated();
        _repository = new ModRepository(_dbContext);
    }

    [Fact]
    public async Task DetectConflictsAsync_DuplicateInternalNames_ReturnsConflict()
    {
        await _repository.AddAsync(new InstalledMod { InternalName = "myTractorMod", FilePath = @"C:\mods\a.zip" });
        await _repository.AddAsync(new InstalledMod { InternalName = "MYTRACTORMOD", FilePath = @"C:\mods\b.zip" });

        var conflicts = await _repository.DetectConflictsAsync();

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictSeverity.Critical, conflict.Severity);
    }

    [Fact]
    public async Task DetectConflictsAsync_DuplicateStoreItemIds_ReturnsConflict()
    {
        await _repository.AddAsync(new InstalledMod
        {
            InternalName = "modA",
            FilePath = @"C:\mods\a.zip",
            StoreItemIdsCsv = "store/tool.xml",
        });
        await _repository.AddAsync(new InstalledMod
        {
            InternalName = "modB",
            FilePath = @"C:\mods\b.zip",
            StoreItemIdsCsv = "store/tool.xml,store/other.xml",
        });

        var conflicts = await _repository.DetectConflictsAsync();

        var conflict = Assert.Single(conflicts);
        Assert.Equal(ConflictSeverity.Likely, conflict.Severity);
        Assert.Contains("store/tool.xml", conflict.Description);
    }

    [Fact]
    public async Task DetectConflictsAsync_NoDuplicates_ReturnsEmpty()
    {
        await _repository.AddAsync(new InstalledMod { InternalName = "modA", FilePath = @"C:\mods\a.zip", StoreItemIdsCsv = "store/a.xml" });
        await _repository.AddAsync(new InstalledMod { InternalName = "modB", FilePath = @"C:\mods\b.zip", StoreItemIdsCsv = "store/b.xml" });

        var conflicts = await _repository.DetectConflictsAsync();

        Assert.Empty(conflicts);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
