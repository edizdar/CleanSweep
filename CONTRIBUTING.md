# CleanSweep'e Katkıda Bulunma Rehberi

CleanSweep'e kod veya topluluk kuralı katkısında bulunmak isterseniz aşağıdaki adımları takip edebilirsiniz.

## 1. Yeni Topluluk Kuralı Ekleme (`rules/*.json`)

1. `rules/schema.json` şemasına uygun yeni bir `<uygulama-adi>.json` dosyası oluşturun.
2. Yalnızca **ortam değişkeni yer tutucuları** (`%APPDATA%`, `%LOCALAPPDATA%`, `%PROGRAMDATA%`, `%PROGRAMFILES%`, `%PROGRAMFILES(X86)%`, `%USERPROFILE%`) kullanın; sabit kullanıcı adı veya sürücü harfi yazmayın.
3. Kök klasörleri (`%APPDATA%`, `%LOCALAPPDATA%`, `C:\Program Files`) asla kurala eklemeyin.
4. `dotnet test` komutunu çalıştırarak tüm kuralların şema ve güvenlik testlerinden geçtiğini doğrulayın.

## 2. Kod Katkıları

- `CleanSweep.Core`, `CleanSweep.Cli` ve `CleanSweep.App` projelerinde harici NuGet bağımlılığı eklemeyin.
- Silme ve kayıt defteri değişikliği yapan tüm kod yolları `SafetyGuard` doğrulamasından geçmelidir.
- Yeni özellikler için `tests/CleanSweep.Tests` altında birim testi ekleyin.
