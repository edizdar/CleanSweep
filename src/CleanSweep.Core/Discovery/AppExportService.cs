using System.Globalization;
using System.Net;
using System.Text;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Discovery;

public static class AppExportService
{
    public static void ExportToCsv(IEnumerable<InstalledApp> apps, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Program Adı,Üretici / Yayıncı,Sürüm,Kaynak,Boyut,Kurulum Tarihi,Kurulum Yolu");

        foreach (var app in apps)
        {
            sb.Append(EscapeCsv(app.Name)).Append(',')
              .Append(EscapeCsv(app.Publisher)).Append(',')
              .Append(EscapeCsv(app.Version)).Append(',')
              .Append(EscapeCsv(app.Source.ToString())).Append(',')
              .Append(EscapeCsv(app.FormattedSize)).Append(',')
              .Append(EscapeCsv(FormatInstallDate(app.InstallDate))).Append(',')
              .AppendLine(EscapeCsv(app.InstallLocation));
        }

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void ExportToHtml(IEnumerable<InstalledApp> apps, string filePath, string appVersion = "0.6.0")
    {
        var list = apps.ToList();
        string cleanVer = string.IsNullOrWhiteSpace(appVersion) ? "0.6.0" : appVersion.TrimStart('v', 'V');
        string timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);

        var rows = new StringBuilder();
        foreach (var app in list)
        {
            rows.AppendLine("        <tr>");
            rows.Append("          <td>").Append(WebUtility.HtmlEncode(app.Name)).AppendLine("</td>");
            rows.Append("          <td>").Append(WebUtility.HtmlEncode(app.Publisher)).AppendLine("</td>");
            rows.Append("          <td>").Append(WebUtility.HtmlEncode(app.Version)).AppendLine("</td>");
            rows.Append("          <td>").Append(WebUtility.HtmlEncode(app.Source.ToString())).AppendLine("</td>");
            rows.Append("          <td>").Append(WebUtility.HtmlEncode(app.FormattedSize)).AppendLine("</td>");
            rows.Append("          <td>").Append(WebUtility.HtmlEncode(FormatInstallDate(app.InstallDate))).AppendLine("</td>");
            rows.Append("          <td class=\"path\">").Append(WebUtility.HtmlEncode(app.InstallLocation)).AppendLine("</td>");
            rows.AppendLine("        </tr>");
        }

        string html = $$"""
<!DOCTYPE html>
<html lang="tr">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>CleanSweep Kurulu Program Listesi</title>
  <style>
    body { font-family: 'Segoe UI', system-ui, sans-serif; background: #0B1120; color: #E2E8F0; margin: 0; padding: 28px; }
    .wrap { max-width: 1200px; margin: 0 auto; }
    .header { background: #131C31; border: 1px solid #26344D; border-radius: 12px; padding: 20px 24px; margin-bottom: 18px; }
    h1 { margin: 0 0 6px; font-size: 22px; color: #F8FAFC; }
    .meta { color: #94A3B8; font-size: 13px; }
    table { width: 100%; border-collapse: collapse; background: #111827; border: 1px solid #26344D; border-radius: 10px; overflow: hidden; }
    th, td { text-align: left; padding: 10px 12px; border-bottom: 1px solid #1E293B; font-size: 13px; }
    th { background: #1E293B; color: #93C5FD; font-weight: 600; }
    tr:hover td { background: #17253B; }
    td.path { color: #94A3B8; font-family: Consolas, monospace; font-size: 12px; word-break: break-all; }
    footer { margin-top: 18px; color: #94A3B8; font-size: 13px; text-align: center; }
    a { color: #60A5FA; text-decoration: none; }
    a:hover { text-decoration: underline; }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="header">
      <h1>CleanSweep — Kurulu Programlar Raporu</h1>
      <div class="meta">Toplam {{list.Count}} uygulama listelendi • Oluşturulma: {{WebUtility.HtmlEncode(timestamp)}}</div>
    </div>
    <table>
      <thead>
        <tr>
          <th>Program Adı</th>
          <th>Üretici / Yayıncı</th>
          <th>Sürüm</th>
          <th>Kaynak</th>
          <th>Boyut</th>
          <th>Kurulum Tarihi</th>
          <th>Kurulum Yolu</th>
        </tr>
      </thead>
      <tbody>
{{rows}}      </tbody>
    </table>
    <footer>CleanSweep v{{WebUtility.HtmlEncode(cleanVer)}} — Geliştirici: Erol Dizdar | Web: <a href="https://www.eroldizdar.tr">https://www.eroldizdar.tr</a></footer>
  </div>
</body>
</html>
""";

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(filePath, html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static string FormatInstallDate(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate)) return "-";
        string trimmed = rawDate.Trim();
        if (trimmed.Length == 8 && DateTime.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        return trimmed;
    }

    private static string EscapeCsv(string? value)
    {
        string v = value ?? string.Empty;
        if (v.Contains('"') || v.Contains(',') || v.Contains('\n') || v.Contains('\r'))
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }
}
