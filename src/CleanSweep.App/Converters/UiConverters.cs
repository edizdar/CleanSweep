using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CleanSweep.Core.Models;

namespace CleanSweep.App.Converters;

public sealed class RiskToTurkishConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is RiskLevel risk ? risk switch
        {
            RiskLevel.Safe => "Güvenle Silinebilir",
            RiskLevel.Moderate => "Kontrol Et",
            RiskLevel.Caution => "Dikkatli Ol",
            _ => value.ToString() ?? string.Empty
        } : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RiskToBrushConverter : IValueConverter
{
    private static readonly Brush SafeBg = CreateFrozen("#14532D");
    private static readonly Brush ModBg = CreateFrozen("#78350F");
    private static readonly Brush CautBg = CreateFrozen("#7F1D1D");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is RiskLevel risk ? risk switch
        {
            RiskLevel.Safe => SafeBg,
            RiskLevel.Moderate => ModBg,
            RiskLevel.Caution => CautBg,
            _ => ModBg
        } : ModBg;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush CreateFrozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

public sealed class BoolToStatusBrushConverter : IValueConverter
{
    private static readonly Brush ActiveBg = CreateFrozen("#14532D");
    private static readonly Brush DisabledBg = CreateFrozen("#374151");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? ActiveBg : DisabledBg;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush CreateFrozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

public sealed class BoolToAutorunTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Açık" : "Kapalı";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
