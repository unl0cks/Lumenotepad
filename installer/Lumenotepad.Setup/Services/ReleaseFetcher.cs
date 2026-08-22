using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Lumenotepad.Setup.Services;

public sealed class ReleaseFetcher : IDisposable
{
    private static readonly string Agent = "Lumenotepad-Setup/" + SetupInfo.Version;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public ReleaseFetcher() : this(new HttpClient { Timeout = TimeSpan.FromMinutes(10) }, ownsClient: true) { }

    public ReleaseFetcher(HttpClient http, bool ownsClient = false)
    {
        _http = http;
        _ownsClient = ownsClient;
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd(Agent))
            _http.DefaultRequestHeaders.Add("User-Agent", "Lumenotepad-Setup");
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    public async Task<ReleaseSource.Release?> LatestAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(ReleaseSource.ManifestUrl, ct);
            if (!response.IsSuccessStatusCode) return null;
            return ReleaseSource.ParseManifest(await response.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task DownloadClientAsync(ReleaseSource.Release release, string destination,
                                          IProgress<double>? progress, CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string actualHash;
        try
        {
            actualHash = await StreamToFileAsync(release.Client, destination, progress, ct);
        }
        catch
        {
            Delete(destination);
            throw;
        }

        if (!ReleaseSource.HashMatches(release.Client.Sha256, actualHash))
        {
            Delete(destination);
            throw new InvalidOperationException(
                $"The download does not match the hash version {release.Version} published for it. It was " +
                "corrupted in transit or is not the released file; it has been discarded.");
        }
    }

    private async Task<string> StreamToFileAsync(ReleaseSource.Build build, string destination,
                                                 IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(build.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? build.Size;

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
                                              bufferSize: 128 * 1024, useAsync: true);
        using var hash = SHA256.Create();

        byte[] buffer = new byte[128 * 1024];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            hash.TransformBlock(buffer, 0, read, null, 0);
            copied += read;
            if (total > 0) progress?.Report(Math.Clamp((double)copied / total, 0, 1));
        }

        hash.TransformFinalBlock([], 0, 0);
        progress?.Report(1);
        return Convert.ToHexString(hash.Hash!).ToLowerInvariant();
    }

    private static void Delete(string path)
    {
        try { File.Delete(path); }
        catch
        {
        }
    }
}
