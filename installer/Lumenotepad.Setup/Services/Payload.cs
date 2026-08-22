using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Lumenotepad.Setup.Services;

public static class Payload
{
    public const string ResourceName = "Lumenotepad.payload";

    public static bool Exists => Stream() is not null;

    public static long CompressedBytes
    {
        get { using var s = Stream(); return s?.Length ?? 0; }
    }

    private static Stream? Stream() => Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);

    public static async Task ExtractAsync(string targetDir, IProgress<double>? progress,
                                          Action<string>? log, CancellationToken ct)
    {
        await using var res = Stream() ?? throw new InvalidOperationException(
            "This build carries no payload, so it can't install anything.");
        Directory.CreateDirectory(targetDir);

        long total = res.Length;
        var counting = new PositionReportingStream(res, p => progress?.Report(total > 0 ? Math.Clamp((double)p / total, 0, 1) : 0));
        await using var brotli = new BrotliStream(counting, CompressionMode.Decompress);
        await using var reader = new TarReader(brotli);

        int files = 0;
        while (await reader.GetNextEntryAsync(cancellationToken: ct) is { } entry)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;

            string rel = entry.Name.Replace('/', Path.DirectorySeparatorChar);
            string dest = Path.GetFullPath(Path.Combine(targetDir, rel));
            if (!dest.StartsWith(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Payload entry escapes the install folder: {entry.Name}");

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await entry.ExtractToFileAsync(dest, overwrite: true, ct);
            files++;
        }
        progress?.Report(1);
        log?.Invoke($"Extracted {files} files to {targetDir}");
    }

    public static async Task ExtractZipAsync(string archivePath, string targetDir, IProgress<double>? progress,
                                             Action<string>? log, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        string root = Path.GetFullPath(targetDir);

        using var zip = ZipFile.OpenRead(archivePath);
        var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        string? prefix = CommonRoot(files.Select(e => e.FullName));

        int done = 0;
        foreach (var entry in files)
        {
            ct.ThrowIfCancellationRequested();

            string rel = prefix is not null ? entry.FullName[prefix.Length..] : entry.FullName;
            string dest = Path.GetFullPath(Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !dest.Equals(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Archive entry escapes the install folder: {entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            await using (var source = entry.Open())
            await using (var file = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None,
                                                   bufferSize: 128 * 1024, useAsync: true))
            {
                await source.CopyToAsync(file, ct);
            }

            done++;
            progress?.Report(files.Count > 0 ? Math.Clamp((double)done / files.Count, 0, 1) : 1);
        }

        progress?.Report(1);
        log?.Invoke($"Extracted {done} files to {targetDir}");
    }

    public static string? CommonRoot(IEnumerable<string> entryNames)
    {
        string? prefix = null;
        foreach (string name in entryNames)
        {
            int slash = name.IndexOf('/');
            if (slash <= 0) return null;
            string first = name[..(slash + 1)];
            if (prefix is null) prefix = first;
            else if (!prefix.Equals(first, StringComparison.OrdinalIgnoreCase)) return null;
        }
        return prefix;
    }

    private sealed class PositionReportingStream(Stream inner, Action<long> onRead) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            _read += n; onRead(_read);
            return n;
        }
        public override int Read(Span<byte> buffer)
        {
            int n = inner.Read(buffer);
            _read += n; onRead(_read);
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
