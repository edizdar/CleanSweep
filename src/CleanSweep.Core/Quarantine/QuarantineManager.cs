using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CleanSweep.Core.Models;
using CleanSweep.Core.Native;
using CleanSweep.Core.Safety;

namespace CleanSweep.Core.Quarantine;

public sealed class QuarantineManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string QuarantineRoot { get; }

    public QuarantineManager(string? customRoot = null)
    {
        QuarantineRoot = customRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSweep",
            "Quarantine");
    }

    public QuarantineSession CreateSession(string appName, IReadOnlyList<LeftoverItem> items)
    {
        Directory.CreateDirectory(QuarantineRoot);
        string safeApp = SanitizeFileName(appName);
        string sessionId = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_{safeApp}_{Guid.NewGuid().ToString("N")[..6]}";
        string sessionDir = Path.Combine(QuarantineRoot, sessionId);
        Directory.CreateDirectory(sessionDir);

        var session = new QuarantineSession
        {
            SessionId = sessionId,
            AppName = appName,
            CreatedAt = DateTimeOffset.UtcNow,
            SessionDirectory = sessionDir
        };

        string zipPath = Path.Combine(sessionDir, "files.zip");
        using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            int entryIdx = 0;
            foreach (var item in items)
            {
                if (item.Category == LeftoverCategory.File)
                {
                    BackupSingleFile(item.Path, archive, session, ref entryIdx);
                }
                else if (item.Category == LeftoverCategory.Directory)
                {
                    BackupDirectory(item.Path, archive, session, ref entryIdx);
                }
            }
        }

        foreach (var item in items)
        {
            if (item.Category == LeftoverCategory.RegistryKey)
            {
                var exported = RegistryExportHelper.ExportKey(item.Path);
                if (exported != null)
                {
                    session.RegistryEntries.Add(exported);
                }
            }
            else if (item.Category is LeftoverCategory.RegistryValue or LeftoverCategory.FirewallRule)
            {
                if (!string.IsNullOrWhiteSpace(item.ValueName))
                {
                    var exported = RegistryExportHelper.ExportKey(item.Path, item.ValueName);
                    if (exported != null)
                    {
                        session.RegistryEntries.Add(exported);
                    }
                }
            }
        }

        if (session.RegistryEntries.Count > 0)
        {
            string regJsonPath = Path.Combine(sessionDir, "registry.json");
            File.WriteAllText(regJsonPath, JsonSerializer.Serialize(session.RegistryEntries, JsonOptions));

            string regTextPath = Path.Combine(sessionDir, "registry_backup.reg");
            File.WriteAllText(regTextPath, RegistryExportHelper.GenerateRegFileText(session.RegistryEntries));
        }

        string manifestPath = Path.Combine(sessionDir, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(session, JsonOptions));

        return session;
    }

    public IReadOnlyList<QuarantineSession> ListSessions()
    {
        var sessions = new List<QuarantineSession>();
        if (!Directory.Exists(QuarantineRoot)) return sessions;

        foreach (string dir in Directory.EnumerateDirectories(QuarantineRoot))
        {
            string manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath)) continue;

            try
            {
                string json = File.ReadAllText(manifestPath);
                var session = JsonSerializer.Deserialize<QuarantineSession>(json, JsonOptions);
                if (session != null)
                {
                    session.SessionDirectory = dir;
                    sessions.Add(session);
                }
            }
            catch { }
        }

        return sessions.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public OperationResult RestoreSession(string sessionId)
    {
        var result = new OperationResult();
        string sessionDir = Path.Combine(QuarantineRoot, sessionId);
        string manifestPath = Path.Combine(sessionDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            result.Success = false;
            result.Summary = $"Karantina oturumu bulunamadı: {sessionId}";
            result.Errors.Add(result.Summary);
            return result;
        }

        var session = JsonSerializer.Deserialize<QuarantineSession>(File.ReadAllText(manifestPath), JsonOptions);
        if (session == null)
        {
            result.Success = false;
            result.Summary = "Karantina manifest dosyası okunamadı.";
            result.Errors.Add(result.Summary);
            return result;
        }

        string zipPath = Path.Combine(sessionDir, "files.zip");
        if (File.Exists(zipPath) && session.Files.Count > 0)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var fileEntry in session.Files)
                {
                    try
                    {
                        if (fileEntry.IsDirectory)
                        {
                            Directory.CreateDirectory(fileEntry.OriginalPath);
                            result.Outcomes.Add(new ItemExecutionOutcome
                            {
                                Target = fileEntry.OriginalPath,
                                Category = LeftoverCategory.Directory,
                                Succeeded = true,
                                Message = "Klasör geri yüklendi."
                            });
                            continue;
                        }

                        var zipItem = archive.GetEntry(fileEntry.ArchiveEntryPath);
                        if (zipItem == null)
                        {
                            result.Errors.Add($"Arşivde dosya bulunamadı: {fileEntry.OriginalPath}");
                            continue;
                        }

                        string? parent = Path.GetDirectoryName(fileEntry.OriginalPath);
                        if (!string.IsNullOrEmpty(parent))
                            Directory.CreateDirectory(parent);

                        zipItem.ExtractToFile(fileEntry.OriginalPath, overwrite: true);
                        result.Outcomes.Add(new ItemExecutionOutcome
                        {
                            Target = fileEntry.OriginalPath,
                            Category = LeftoverCategory.File,
                            Succeeded = true,
                            Message = "Dosya geri yüklendi."
                        });
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"Dosya geri yüklenemedi ({fileEntry.OriginalPath}): {ex.Message}");
                        result.Outcomes.Add(new ItemExecutionOutcome
                        {
                            Target = fileEntry.OriginalPath,
                            Category = LeftoverCategory.File,
                            Succeeded = false,
                            Message = ex.Message
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Karantina arşivi açılamadı: {ex.Message}");
            }
        }

        string regJsonPath = Path.Combine(sessionDir, "registry.json");
        List<QuarantinedRegistryKey> regEntries = session.RegistryEntries;
        if (File.Exists(regJsonPath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<List<QuarantinedRegistryKey>>(File.ReadAllText(regJsonPath), JsonOptions);
                if (loaded != null) regEntries = loaded;
            }
            catch { }
        }

        foreach (var regEntry in regEntries)
        {
            try
            {
                RegistryExportHelper.RestoreKey(regEntry);
                result.Outcomes.Add(new ItemExecutionOutcome
                {
                    Target = regEntry.FullKeyPath,
                    Category = regEntry.WasValueOnly ? LeftoverCategory.RegistryValue : LeftoverCategory.RegistryKey,
                    Succeeded = true,
                    Message = "Kayıt defteri geri yüklendi."
                });
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Kayıt defteri geri yüklenemedi ({regEntry.FullKeyPath}): {ex.Message}");
                result.Outcomes.Add(new ItemExecutionOutcome
                {
                    Target = regEntry.FullKeyPath,
                    Category = LeftoverCategory.RegistryKey,
                    Succeeded = false,
                    Message = ex.Message
                });
            }
        }

        result.Success = result.Errors.Count == 0;
        result.QuarantineSessionId = sessionId;
        result.Summary = result.Success
            ? $"Karantina oturumu başarıyla geri yüklendi ({result.Outcomes.Count(o => o.Succeeded)} öğe)."
            : $"Geri yükleme kısmen tamamlandı ({result.Errors.Count} hata).";
        return result;
    }

    public bool PurgeSession(string sessionId)
    {
        string sessionDir = Path.Combine(QuarantineRoot, sessionId);
        if (!Directory.Exists(sessionDir)) return false;
        Directory.Delete(sessionDir, recursive: true);
        return true;
    }

    private static void BackupSingleFile(string path, ZipArchive archive, QuarantineSession session, ref int idx)
    {
        string norm = SafetyGuard.NormalizeFilePath(path);
        if (!File.Exists(norm)) return;

        try
        {
            string entryName = $"f_{idx++}_{Path.GetFileName(norm)}";
            archive.CreateEntryFromFile(norm, entryName, CompressionLevel.Optimal);
            var fi = new FileInfo(norm);
            session.Files.Add(new QuarantinedFileEntry
            {
                OriginalPath = norm,
                ArchiveEntryPath = entryName,
                IsDirectory = false,
                SizeBytes = fi.Length,
                Sha256 = ComputeSha256Safe(norm)
            });
            session.TotalBackedUpBytes += fi.Length;
        }
        catch (Exception ex)
        {
            session.Warnings.Add($"Dosya yedeklenemedi ({norm}): {ex.Message}");
        }
    }

    private static void BackupDirectory(string dirPath, ZipArchive archive, QuarantineSession session, ref int idx)
    {
        string normDir = SafetyGuard.NormalizeFilePath(dirPath);
        if (!Directory.Exists(normDir)) return;

        session.Files.Add(new QuarantinedFileEntry
        {
            OriginalPath = normDir,
            ArchiveEntryPath = string.Empty,
            IsDirectory = true
        });

        try
        {
            foreach (string subDir in Directory.EnumerateDirectories(normDir, "*", SearchOption.AllDirectories))
            {
                session.Files.Add(new QuarantinedFileEntry
                {
                    OriginalPath = SafetyGuard.NormalizeFilePath(subDir),
                    ArchiveEntryPath = string.Empty,
                    IsDirectory = true
                });
            }

            foreach (string file in Directory.EnumerateFiles(normDir, "*", SearchOption.AllDirectories))
            {
                BackupSingleFile(file, archive, session, ref idx);
            }
        }
        catch (Exception ex)
        {
            session.Warnings.Add($"Klasör yedeklenirken kısmi hata ({normDir}): {ex.Message}");
        }
    }

    private static string ComputeSha256Safe(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Where(c => !invalid.Contains(c) && c != ' ').Take(24).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "app" : cleaned;
    }
}
