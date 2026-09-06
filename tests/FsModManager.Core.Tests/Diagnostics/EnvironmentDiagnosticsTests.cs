using FsModManager.Core.Diagnostics;
using Xunit;

namespace FsModManager.Core.Tests.Diagnostics;

public class EnvironmentDiagnosticsTests
{
    [Fact]
    public void CheckDocumentsFolderSync_WhenNotInOneDrive_ReturnsNotOneDrive()
    {
        var envVars = new Dictionary<string, string?>
        {
            ["OneDrive"] = @"C:\Users\TestUser\OneDrive",
            ["OneDriveConsumer"] = null,
            ["OneDriveCommercial"] = null,
        };

        var docsPath = @"C:\Users\TestUser\Documents";
        var gameFolder = @"C:\Users\TestUser\Documents\My Games\FarmingSimulator2025";

        var result = EnvironmentDiagnostics.CheckDocumentsFolderSync(
            documentsPath: docsPath,
            expectedGameFolderPath: gameFolder,
            environmentVariables: envVars,
            directoryExists: _ => true);

        Assert.Equal(OneDriveSyncStatus.NotOneDrive, result.Status);
        Assert.Null(result.MatchedOneDriveRoot);
        Assert.Equal(docsPath, result.DocumentsPath);
    }

    [Fact]
    public void CheckDocumentsFolderSync_WhenInOneDriveAndGameFolderExists_ReturnsOneDriveButSynced()
    {
        var oneDriveRoot = @"C:\Users\TestUser\OneDrive";
        var envVars = new Dictionary<string, string?>
        {
            ["OneDrive"] = oneDriveRoot,
            ["OneDriveConsumer"] = null,
            ["OneDriveCommercial"] = null,
        };

        var docsPath = @"C:\Users\TestUser\OneDrive\Documents";
        var gameFolder = @"C:\Users\TestUser\OneDrive\Documents\My Games\FarmingSimulator2025";

        var result = EnvironmentDiagnostics.CheckDocumentsFolderSync(
            documentsPath: docsPath,
            expectedGameFolderPath: gameFolder,
            environmentVariables: envVars,
            directoryExists: path => string.Equals(path, gameFolder, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(OneDriveSyncStatus.OneDriveButSynced, result.Status);
        Assert.Equal(oneDriveRoot, result.MatchedOneDriveRoot);
        Assert.Contains(oneDriveRoot, result.Message);
    }

    [Fact]
    public void CheckDocumentsFolderSync_WhenInOneDriveCommercialAndGameFolderMissing_ReturnsOneDriveAndMissing()
    {
        var oneDriveCommercial = @"C:\Users\TestUser\OneDrive - Contoso";
        var envVars = new Dictionary<string, string?>
        {
            ["OneDrive"] = null,
            ["OneDriveConsumer"] = null,
            ["OneDriveCommercial"] = oneDriveCommercial,
        };

        var docsPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents";
        var gameFolder = @"C:\Users\TestUser\OneDrive - Contoso\Documents\My Games\FarmingSimulator2025";

        var result = EnvironmentDiagnostics.CheckDocumentsFolderSync(
            documentsPath: docsPath,
            expectedGameFolderPath: gameFolder,
            environmentVariables: envVars,
            directoryExists: _ => false);

        Assert.Equal(OneDriveSyncStatus.OneDriveAndMissing, result.Status);
        Assert.Equal(oneDriveCommercial, result.MatchedOneDriveRoot);
        Assert.Contains("inaccessible", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckDocumentsFolderSync_WhenOneDriveConsumerMatched_ReturnsCorrectStatus()
    {
        var oneDriveConsumer = @"C:\Users\TestUser\OneDrive";
        var envVars = new Dictionary<string, string?>
        {
            ["OneDrive"] = null,
            ["OneDriveConsumer"] = oneDriveConsumer,
            ["OneDriveCommercial"] = null,
        };

        var docsPath = @"C:\Users\TestUser\OneDrive\Documents";
        var gameFolder = @"C:\Users\TestUser\OneDrive\Documents\My Games\FarmingSimulator2025";

        var result = EnvironmentDiagnostics.CheckDocumentsFolderSync(
            documentsPath: docsPath,
            expectedGameFolderPath: gameFolder,
            environmentVariables: envVars,
            directoryExists: _ => true);

        Assert.Equal(OneDriveSyncStatus.OneDriveButSynced, result.Status);
        Assert.Equal(oneDriveConsumer, result.MatchedOneDriveRoot);
    }

    [Fact]
    public void CheckDocumentsFolderSync_WhenDirectoryExistsThrows_TreatsAsMissing()
    {
        var oneDriveRoot = @"C:\Users\TestUser\OneDrive";
        var envVars = new Dictionary<string, string?>
        {
            ["OneDrive"] = oneDriveRoot,
        };

        var docsPath = @"C:\Users\TestUser\OneDrive\Documents";
        var gameFolder = @"C:\Users\TestUser\OneDrive\Documents\My Games\FarmingSimulator2025";

        var result = EnvironmentDiagnostics.CheckDocumentsFolderSync(
            documentsPath: docsPath,
            expectedGameFolderPath: gameFolder,
            environmentVariables: envVars,
            directoryExists: _ => throw new UnauthorizedAccessException("Access denied"));

        Assert.Equal(OneDriveSyncStatus.OneDriveAndMissing, result.Status);
    }
}
