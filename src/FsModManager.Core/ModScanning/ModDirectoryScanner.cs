using FsModManager.Core.ModScanning.Parsing;
using FsModManager.Core.Models;

namespace FsModManager.Core.ModScanning;

/// <inheritdoc cref="IModDirectoryScanner"/>
public sealed class ModDirectoryScanner : IModDirectoryScanner
{
    private readonly IModDescParser _parser;
    private readonly int _maxDegreeOfParallelism;

    public ModDirectoryScanner(IModDescParser parser, int? maxDegreeOfParallelism = null)
    {
        _parser = parser;
        _maxDegreeOfParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount;
    }

    public async Task<IReadOnlyList<ModMetadata>> ScanAsync(string modsFolderPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(modsFolderPath))
        {
            return Array.Empty<ModMetadata>();
        }

        var zipFiles = Directory.EnumerateFiles(modsFolderPath, "*.zip", SearchOption.TopDirectoryOnly).ToList();
        if (zipFiles.Count == 0)
        {
            return Array.Empty<ModMetadata>();
        }

        using var throttle = new SemaphoreSlim(_maxDegreeOfParallelism);
        var results = new ModMetadata[zipFiles.Count];

        var tasks = zipFiles.Select(async (zipFile, index) =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                results[index] = await _parser.ParseAsync(zipFile, cancellationToken);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results;
    }
}
