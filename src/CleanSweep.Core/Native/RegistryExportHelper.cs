using System.Text;
using CleanSweep.Core.Models;
using CleanSweep.Core.Safety;
using Microsoft.Win32;

namespace CleanSweep.Core.Native;

public static class RegistryExportHelper
{
    public static bool TryParseRegistryPath(
        string fullPath,
        out RegistryHive hive,
        out string subKeyPath,
        out string hiveFullName)
    {
        hive = RegistryHive.CurrentUser;
        subKeyPath = string.Empty;
        hiveFullName = string.Empty;

        string norm = SafetyGuard.NormalizeRegistryPath(fullPath);
        int idx = norm.IndexOf('\\');
        string prefix = idx >= 0 ? norm[..idx] : norm;
        subKeyPath = idx >= 0 ? norm[(idx + 1)..] : string.Empty;

        switch (prefix.ToUpperInvariant())
        {
            case "HKLM":
                hive = RegistryHive.LocalMachine;
                hiveFullName = "HKEY_LOCAL_MACHINE";
                return true;
            case "HKCU":
                hive = RegistryHive.CurrentUser;
                hiveFullName = "HKEY_CURRENT_USER";
                return true;
            case "HKCR":
                hive = RegistryHive.ClassesRoot;
                hiveFullName = "HKEY_CLASSES_ROOT";
                return true;
            case "HKU":
                hive = RegistryHive.Users;
                hiveFullName = "HKEY_USERS";
                return true;
            default:
                return false;
        }
    }

    public static QuarantinedRegistryKey? ExportKey(string fullPath, string? valueNameOnly = null, bool is32BitView = false)
    {
        if (!TryParseRegistryPath(fullPath, out var hive, out string subKeyPath, out _))
            return null;

        var view = is32BitView ? RegistryView.Registry32 : RegistryView.Registry64;
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
        if (key == null)
        {
            // Fallback to opposite view if 64-bit lookup didn't find it
            if (!is32BitView)
            {
                using var base32 = RegistryKey.OpenBaseKey(hive, RegistryView.Registry32);
                using var key32 = base32.OpenSubKey(subKeyPath, writable: false);
                if (key32 != null)
                {
                    return ExportOpenKey(SafetyGuard.NormalizeRegistryPath(fullPath), key32, valueNameOnly, is32BitView: true);
                }
            }
            return null;
        }

        return ExportOpenKey(SafetyGuard.NormalizeRegistryPath(fullPath), key, valueNameOnly, is32BitView);
    }

    private static QuarantinedRegistryKey ExportOpenKey(
        string normPath,
        RegistryKey key,
        string? valueNameOnly,
        bool is32BitView)
    {
        var result = new QuarantinedRegistryKey
        {
            FullKeyPath = normPath,
            Is32BitView = is32BitView,
            WasValueOnly = !string.IsNullOrEmpty(valueNameOnly)
        };

        if (!string.IsNullOrEmpty(valueNameOnly))
        {
            object? val = key.GetValue(valueNameOnly, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (val != null)
            {
                var kind = key.GetValueKind(valueNameOnly);
                result.Values.Add(SerializeValue(valueNameOnly, kind, val));
            }
            return result;
        }

        foreach (string vName in key.GetValueNames())
        {
            object? val = key.GetValue(vName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (val == null) continue;
            var kind = key.GetValueKind(vName);
            result.Values.Add(SerializeValue(vName, kind, val));
        }

        foreach (string subName in key.GetSubKeyNames())
        {
            try
            {
                using var subKey = key.OpenSubKey(subName, writable: false);
                if (subKey != null)
                {
                    result.SubKeys.Add(ExportOpenKey($"{normPath}\\{subName}", subKey, null, is32BitView));
                }
            }
            catch
            {
                // Skip unreadable subkeys
            }
        }

        return result;
    }

    private static QuarantinedRegistryValue SerializeValue(string name, RegistryValueKind kind, object value)
    {
        return kind switch
        {
            RegistryValueKind.MultiString => new QuarantinedRegistryValue
            {
                Name = name,
                Kind = kind.ToString(),
                MultiStringData = value as string[] ?? []
            },
            RegistryValueKind.Binary => new QuarantinedRegistryValue
            {
                Name = name,
                Kind = kind.ToString(),
                Data = Convert.ToBase64String(value as byte[] ?? [])
            },
            RegistryValueKind.DWord => new QuarantinedRegistryValue
            {
                Name = name,
                Kind = kind.ToString(),
                Data = unchecked((uint)Convert.ToInt32(value)).ToString()
            },
            RegistryValueKind.QWord => new QuarantinedRegistryValue
            {
                Name = name,
                Kind = kind.ToString(),
                Data = unchecked((ulong)Convert.ToInt64(value)).ToString()
            },
            _ => new QuarantinedRegistryValue
            {
                Name = name,
                Kind = kind.ToString(),
                Data = Convert.ToString(value) ?? string.Empty
            }
        };
    }

    public static void RestoreKey(QuarantinedRegistryKey entry)
    {
        if (!TryParseRegistryPath(entry.FullKeyPath, out var hive, out string subKeyPath, out _))
            return;

        var view = entry.Is32BitView ? RegistryView.Registry32 : RegistryView.Registry64;
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.CreateSubKey(subKeyPath, writable: true);
        if (key == null) return;

        foreach (var val in entry.Values)
        {
            if (!Enum.TryParse<RegistryValueKind>(val.Kind, out var kind))
                kind = RegistryValueKind.String;

            object boxed = kind switch
            {
                RegistryValueKind.MultiString => val.MultiStringData ?? [],
                RegistryValueKind.Binary => Convert.FromBase64String(val.Data),
                RegistryValueKind.DWord => unchecked((int)uint.Parse(val.Data)),
                RegistryValueKind.QWord => unchecked((long)ulong.Parse(val.Data)),
                _ => val.Data
            };
            key.SetValue(val.Name, boxed, kind);
        }

        foreach (var sub in entry.SubKeys)
        {
            RestoreKey(sub);
        }
    }

    public static string GenerateRegFileText(IEnumerable<QuarantinedRegistryKey> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            AppendRegKey(sb, entry);
        }

        return sb.ToString();
    }

    private static void AppendRegKey(StringBuilder sb, QuarantinedRegistryKey entry)
    {
        if (!TryParseRegistryPath(entry.FullKeyPath, out _, out string subKeyPath, out string hiveFullName))
            return;

        sb.Append('[').Append(hiveFullName).Append('\\').Append(subKeyPath).AppendLine("]");
        foreach (var val in entry.Values)
        {
            string valName = string.IsNullOrEmpty(val.Name) ? "@" : $"\"{EscapeRegString(val.Name)}\"";
            switch (val.Kind)
            {
                case nameof(RegistryValueKind.DWord):
                    uint dw = uint.TryParse(val.Data, out uint d) ? d : 0;
                    sb.AppendLine($"{valName}=dword:{dw:x8}");
                    break;
                case nameof(RegistryValueKind.QWord):
                    ulong qw = ulong.TryParse(val.Data, out ulong q) ? q : 0;
                    byte[] qBytes = BitConverter.GetBytes(qw);
                    sb.AppendLine($"{valName}=hex(b):{string.Join(",", qBytes.Select(b => b.ToString("x2")))}");
                    break;
                case nameof(RegistryValueKind.Binary):
                    byte[] bytes = Convert.FromBase64String(val.Data);
                    sb.AppendLine($"{valName}=hex:{string.Join(",", bytes.Select(b => b.ToString("x2")))}");
                    break;
                default:
                    sb.AppendLine($"{valName}=\"{EscapeRegString(val.Data)}\"");
                    break;
            }
        }
        sb.AppendLine();

        foreach (var sub in entry.SubKeys)
        {
            AppendRegKey(sb, sub);
        }
    }

    private static string EscapeRegString(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
