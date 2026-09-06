using System.Net;
using System.Net.Http.Headers;
using FsModManager.Core.Hashing;
using FsModManager.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace FsModManager.Core.Download;

/// <inheritdoc cref="IDownloadManager"/>
public sealed class DownloadManager : IDownloadManager
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DownloadManagerOptions _options;
    private readonly ILogger<DownloadManager> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public DownloadManager(
        IHttpClientFactory httpClientFactory,
        IOptions<DownloadManagerOptions> options,
        ILogger<DownloadManager> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;

        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<IOException>()
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex, "Download attempt {Attempt} failed, retrying in {Delay}s", attempt, delay.TotalSeconds));
    }

    public async Task<DownloadResult> DownloadModAsync(
        Uri downloadUrl,
        string destinationFileName,
        IProgress<DownloadProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.StagingFolderPath);
        Directory.CreateDirectory(_options.ModsFolderPath);

        var stagingFilePath = Path.Combine(_options.StagingFolderPath, destinationFileName + ".part");

        try
        {
            await _retryPolicy.ExecuteAsync(ct => DownloadToStagingAsync(downloadUrl, stagingFilePath, progress, ct), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed after retries: {Url}", downloadUrl);
            return new DownloadResult(false, null, null, ex.Message);
        }

        string hash;
        try
        {
            hash = await FileHashing.ComputeSha256Async(stagingFilePath, cancellationToken);
        }
        catch (Exception ex)
        {
            return new DownloadResult(false, null, null, $"Hash verification failed: {ex.Message}");
        }

        var finalPath = Path.Combine(_options.ModsFolderPath, destinationFileName);
        try
        {
            File.Move(stagingFilePath, finalPath, overwrite: true);
        }
        catch (Exception ex)
        {
            return new DownloadResult(false, null, hash, $"Failed to move file into mods folder: {ex.Message}");
        }

        return new DownloadResult(true, finalPath, hash, null);
    }

    private async Task DownloadToStagingAsync(
        Uri downloadUrl,
        string stagingFilePath,
        IProgress<DownloadProgressInfo>? progress,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(DownloadManager));

        var resumeOffset = File.Exists(stagingFilePath) ? new FileInfo(stagingFilePath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        if (resumeOffset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(resumeOffset, null);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        // If the server doesn't support Range requests it will return 200 with the full body; start over.
        var isResuming = resumeOffset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (resumeOffset > 0 && !isResuming)
        {
            resumeOffset = 0;
        }

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength is { } contentLength
            ? contentLength + resumeOffset
            : (long?)null;

        await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(
            stagingFilePath,
            isResuming ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        var buffer = new byte[81920];
        long totalRead = resumeOffset;
        int bytesRead;
        while ((bytesRead = await httpStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;
            progress?.Report(new DownloadProgressInfo(totalRead, totalBytes));
        }
    }
}
