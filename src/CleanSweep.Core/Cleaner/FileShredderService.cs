using System.Security.Cryptography;
using CleanSweep.Core.Models;
using CleanSweep.Core.Safety;

namespace CleanSweep.Core.Cleaner;

public enum ShredPassMode
{
    Fast1Pass = 1,
    Dod3Pass = 3,
    Maximum7Pass = 7
}

public sealed class FileShredderService
{
    public Task<OperationResult> ShredAsync(
        IEnumerable<string> paths,
        ShredPassMode mode,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var result = new OperationResult();
            int passes = (int)mode;

            foreach (string rawPath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                string norm = SafetyGuard.NormalizeFilePath(rawPath);

                if (SafetyGuard.IsProtectedFilePath(norm))
                {
                    result.Errors.Add($"Korunan sistem yolu engellendi: {norm}");
                    result.Outcomes.Add(new ItemExecutionOutcome
                    {
                        Target = norm,
                        Category = LeftoverCategory.File,
                        Succeeded = false,
                        Message = "SafetyGuard tarafından engellendi."
                    });
                    continue;
                }

                if (File.Exists(norm))
                {
                    ShredSingleFile(norm, passes, result, progress, ct);
                }
                else if (Directory.Exists(norm))
                {
                    try
                    {
                        foreach (string file in Directory.EnumerateFiles(norm, "*", SearchOption.AllDirectories))
                        {
                            ct.ThrowIfCancellationRequested();
                            ShredSingleFile(file, passes, result, progress, ct);
                        }

                        foreach (string sub in Directory.EnumerateDirectories(norm, "*", SearchOption.AllDirectories)
                            .OrderByDescending(d => d.Length))
                        {
                            try { Directory.Delete(sub, recursive: false); } catch { }
                        }

                        Directory.Delete(norm, recursive: false);
                        result.Outcomes.Add(new ItemExecutionOutcome
                        {
                            Target = norm,
                            Category = LeftoverCategory.Directory,
                            Succeeded = true,
                            Message = $"Klasör ({passes} geçiş) kalıcı olarak yok edildi."
                        });
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{norm}: {ex.Message}");
                    }
                }
            }

            result.Success = result.Errors.Count == 0;
            int ok = result.Outcomes.Count(o => o.Succeeded);
            result.Summary = $"{ok} öğe {passes} geçişli güvenli silme ile kalıcı olarak yok edildi." +
                             (result.Errors.Count > 0 ? $" ({result.Errors.Count} hata)" : string.Empty);
            return result;
        }, ct);
    }

    private static void ShredSingleFile(
        string filePath,
        int passes,
        OperationResult result,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        try
        {
            progress?.Report($"Öğütülüyor ({passes} geçiş): {Path.GetFileName(filePath)}");
            File.SetAttributes(filePath, FileAttributes.Normal);

            long length = new FileInfo(filePath).Length;
            if (length > 0)
            {
                byte[] buffer = new byte[64 * 1024];
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

                for (int pass = 1; pass <= passes; pass++)
                {
                    ct.ThrowIfCancellationRequested();
                    fs.Position = 0;

                    if (pass == 1)
                        Array.Clear(buffer);
                    else if (pass == 2)
                        Array.Fill(buffer, (byte)0xFF);

                    long remaining = length;
                    while (remaining > 0)
                    {
                        if (pass >= 3)
                            RandomNumberGenerator.Fill(buffer);

                        int toWrite = (int)Math.Min(buffer.Length, remaining);
                        fs.Write(buffer, 0, toWrite);
                        remaining -= toWrite;
                    }
                    fs.Flush(flushToDisk: true);
                }

                fs.SetLength(0);
                fs.Flush(flushToDisk: true);
            }

            string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            string obfuscatedPath = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
            File.Move(filePath, obfuscatedPath, overwrite: true);
            File.Delete(obfuscatedPath);

            result.Outcomes.Add(new ItemExecutionOutcome
            {
                Target = filePath,
                Category = LeftoverCategory.File,
                Succeeded = true,
                Message = $"{passes} geçişle kalıcı olarak silindi."
            });
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{filePath}: {ex.Message}");
            result.Outcomes.Add(new ItemExecutionOutcome
            {
                Target = filePath,
                Category = LeftoverCategory.File,
                Succeeded = false,
                Message = ex.Message
            });
        }
    }
}
