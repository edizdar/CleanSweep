using CleanSweep.Core.Models;

namespace CleanSweep.Core.Safety;

public enum MatchStrength
{
    TraceExact,
    InstallLocationExact,
    CommunityRule,
    ExactAppNameMatch,
    PathReferenceExact,
    PartialTokenMatch,
    SharedPublisherFolder
}

public static class RiskScorer
{
    public static void ApplyScore(
        LeftoverItem item,
        MatchStrength strength,
        bool hasOtherSamePublisherApps = false,
        string? customReason = null)
    {
        switch (strength)
        {
            case MatchStrength.TraceExact:
                item.Confidence = 98;
                item.Risk = RiskLevel.Safe;
                item.IsSelected = true;
                item.MatchReason = customReason ?? "Kurulum izleme kaydında (Trace) bu uygulama tarafından oluşturulduğu doğrulandı.";
                break;

            case MatchStrength.InstallLocationExact:
                item.Confidence = 96;
                item.Risk = RiskLevel.Safe;
                item.IsSelected = true;
                item.MatchReason = customReason ?? "Doğrudan uygulamanın kurulum diziniyle (InstallLocation) eşleşiyor.";
                break;

            case MatchStrength.PathReferenceExact:
                item.Confidence = 92;
                item.Risk = RiskLevel.Safe;
                item.IsSelected = true;
                item.MatchReason = customReason ?? "Hedef değer doğrudan uygulamanın kurulum yoluna işaret ediyor.";
                break;

            case MatchStrength.CommunityRule:
                item.Confidence = 90;
                item.Risk = RiskLevel.Safe;
                item.IsSelected = true;
                item.MatchReason = customReason ?? $"Açık kaynak topluluk kuralıyla eşleşti ({item.RuleSource}).";
                break;

            case MatchStrength.ExactAppNameMatch:
                item.Confidence = 85;
                item.Risk = RiskLevel.Safe;
                item.IsSelected = true;
                item.MatchReason = customReason ?? "Uygulama adıyla birebir eşleşen özel klasör/kayıt defteri anahtarı.";
                break;

            case MatchStrength.PartialTokenMatch:
                item.Confidence = 68;
                item.Risk = RiskLevel.Moderate;
                item.IsSelected = false;
                item.MatchReason = customReason ?? "Uygulama adı parçacığıyla eşleşti; silmeden önce kontrol etmeniz önerilir.";
                break;

            case MatchStrength.SharedPublisherFolder:
                if (hasOtherSamePublisherApps)
                {
                    item.Confidence = 38;
                    item.Risk = RiskLevel.Caution;
                    item.IsSelected = false;
                    item.MatchReason = customReason ?? "Yayıncı adı eşleşiyor ancak aynı yayıncıya ait başka kurulu uygulamalar mevcut (ortak klasör olabilir).";
                }
                else
                {
                    item.Confidence = 72;
                    item.Risk = RiskLevel.Moderate;
                    item.IsSelected = false;
                    item.MatchReason = customReason ?? "Yayıncı klasörü/anahtarı eşleşti; aynı yayıncıya ait başka uygulama tespit edilmedi.";
                }
                break;
        }
    }
}
