using System.Text.RegularExpressions;
using NetworkHealthMonitor.Models;
using Xunit;

namespace NetworkHealthMonitor.Tests;

public sealed class UiUserFacingTextAuditTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    // Kullanıcıya görünen XAML öznitelik değerlerinde yasaklı İngilizce/ASCII kalıntıları.
    private static readonly Regex XamlUserTextAttribute = new(
        """(?i)(?:Content|Header|Text|ToolTip|Title)\s*=\s*"(?<text>[^"]*)" """,
        RegexOptions.Compiled);

    private static readonly string[] ForbiddenWholeWords =
    [
        "Timeout", "Retry", "Worker", "Health check", "Access token",
        "Open incident", "Closed incident", "Incident", "Online", "Offline",
        "Manual", "Automatic", "Import", "Export", "Pending", "Failed",
        "Success", "Error", "Inactive", "Last success", "Last error",
        "Save", "Cancel", "Apply", "Clear", "Refresh", "Source", "Active"
    ];

    private static readonly string[] ForbiddenAsciiTurkish =
    [
        "Acik", "Duzelme", "Basarisiz", "Erisilebilir", "Erisilemiyor",
        "Iceri aktar", "Disari aktar", "Baslangic", "Guncelle", "Kayit",
        "Surum", "onizleme", "alinmadi", "secilmedi", "gonderildi"
    ];

    // Teknik bağlamda görünebilecek ama kullanıcı metni olmayan XAML bağları / kısa etiketler.
    private static readonly string[] AllowedExact =
    [
        string.Empty,
        "{Binding",
        "IP",
        "SLA",
        "CSV",
        "CSV modu:",
        "MTTR",
        "MTBF",
        "ntfy"
    ];

    [Fact]
    public void Xaml_user_visible_attributes_have_no_forbidden_english_or_ascii_turkish()
    {
        var files = Directory.GetFiles(RepoRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(files);
        var findings = new List<string>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in XamlUserTextAttribute.Matches(lines[i]))
                {
                    var text = match.Groups["text"].Value.Trim();
                    if (string.IsNullOrWhiteSpace(text) || text.StartsWith("{", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (AllowedExact.Any(allowed =>
                            text.Equals(allowed, StringComparison.OrdinalIgnoreCase)
                            || (allowed.Length > 0 && text.StartsWith(allowed + " ", StringComparison.OrdinalIgnoreCase))))
                    {
                        continue;
                    }

                    // Kısa teknik etiketler (IP, SLA, CSV, MTTR...)
                    if (text is "IP" or "SLA" or "CSV" or "MTTR" or "MTBF" or "ntfy" or "SQLite")
                    {
                        continue;
                    }

                    foreach (var forbidden in ForbiddenWholeWords)
                    {
                        if (ContainsWholeWord(text, forbidden))
                        {
                            findings.Add($"{Relative(file)}:{i + 1}: '{forbidden}' in \"{text}\"");
                        }
                    }

                    foreach (var forbidden in ForbiddenAsciiTurkish)
                    {
                        if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                        {
                            findings.Add($"{Relative(file)}:{i + 1}: ASCII '{forbidden}' in \"{text}\"");
                        }
                    }
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "Kullanıcıya görünen XAML metinlerinde yasaklı ifadeler bulundu:" + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public void Display_name_helpers_return_turkish_canonical_terms()
    {
        Assert.Equal("Erişilebilir", DeviceStatus.Online.ToDisplayName());
        Assert.Equal("Erişilemiyor", DeviceStatus.Offline.ToDisplayName());
        Assert.Equal("Kontrol edilmedi", DeviceStatus.Unknown.ToDisplayName());
        Assert.Equal("Manuel kontrol", PingTriggerType.Manual.ToDisplayName());
        Assert.Equal("Otomatik kontrol", PingTriggerType.Scheduled.ToDisplayName());
        Assert.Equal("Bekliyor", UiDisplayTexts.OutboxStatus("Pending"));
        Assert.Equal("Başarısız", UiDisplayTexts.OutboxStatus("Failed"));
        Assert.Equal("Kesinti bildirimi", UiDisplayTexts.OutboxEventType("DeviceDown"));
        Assert.Equal("Düzelme bildirimi", UiDisplayTexts.OutboxEventType("DeviceRecovered"));
        Assert.Equal("Etkin", UiDisplayTexts.ActiveState(true));
        Assert.Equal("Devre dışı", UiDisplayTexts.ActiveState(false));
        Assert.Equal("Geçti", UiDisplayTexts.ReadinessLevelText(ReadinessLevel.Pass));
        Assert.Equal("Sağlıklı", new ServiceReadinessSnapshot().OverallStatusText);
    }

    private static bool ContainsWholeWord(string text, string word)
    {
        return Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Relative(string path)
    {
        return Path.GetRelativePath(RepoRoot, path);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NetworkHealthMonitor.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located.");
    }
}
