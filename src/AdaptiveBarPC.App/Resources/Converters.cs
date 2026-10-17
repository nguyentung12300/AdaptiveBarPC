using System;
using System.Globalization;
using System.Windows.Data;

namespace AdaptiveBarPC.Resources;

public sealed class CommandLabelConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length > 0 && values[0] is string label)
        {
            bool isPixel = values.Length > 1 && values[1] is true;
            if (isPixel && label.Length <= 14)
            {
                return label.ToUpperInvariant();
            }
            return label;
        }
        return values.Length > 0 ? values[0] : "";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PixelGlyphConverter : IMultiValueConverter
{
    private static readonly IReadOnlyDictionary<char, char> Glyphs = new Dictionary<char, char>
    {
        ['\uE80F'] = '\uEBEA', ['\uE713'] = '\uED22', ['\uE721'] = '\uED18', ['\uE72C'] = '\uECF8',
        ['\uE710'] = '\uECD8', ['\uE70F'] = '\uECC5', ['\uE74E'] = '\uED0E', ['\uE74D'] = '\uEDBE',
        ['\uE711'] = '\uEB30', ['\uE73E'] = '\uEB0A', ['\uE8B7'] = '\uEBAD', ['\uE8A5'] = '\uEB99',
        ['\uE734'] = '\uED6D', ['\uE946'] = '\uEB20', ['\uE712'] = '\uECA3', ['\uE718'] = '\uEC7B',
        ['\uE71B'] = '\uEC5F', ['\uE72B'] = '\uEA70', ['\uE72A'] = '\uEA73', ['\uE74A'] = '\uEA7A',
        ['\uE74B'] = '\uEA6D', ['\uE76B'] = '\uEA70', ['\uE76C'] = '\uEA73', ['\uE892'] = '\uEB13',
        ['\uE893'] = '\uEB15', ['\uE8A7'] = '\uEB8E',
        ['\uE768'] = '\uECD3', ['\uE769'] = '\uECC0', ['\uE71A'] = '\uED75', ['\uEB9D'] = '\uEB15',
        ['\uEB9E'] = '\uEB13', ['\uE8D6'] = '\uECAD', ['\uE714'] = '\uEDE8', ['\uE740'] = '\uEB8C',
        ['\uE8EE'] = '\uECFE', ['\uE8B1'] = '\uED31', ['\uE767'] = '\uEDEB', ['\uE994'] = '\uEDEA',
        ['\uE993'] = '\uEDE9', ['\uE74F'] = '\uEDED', ['\uE720'] = '\uEC97', ['\uE7F6'] = '\uEBE6',
        ['\uE7F5'] = '\uEDEE', ['\uE7E8'] = '\uECE0', ['\uE895'] = '\uECFA', ['\uE72E'] = '\uEC6C',
        ['\uE785'] = '\uEDD1', ['\uE1D0'] = '\uEAE3', ['\uE756'] = '\uED95', ['\uE9F9'] = '\uEB02',
        ['\uE823'] = '\uEB2F', ['\uE787'] = '\uEAED', ['\uE7F4'] = '\uECA0', ['\uE83F'] = '\uEAA6',
        ['\uEA93'] = '\uEAAC', ['\uE706'] = '\uED7D', ['\uE765'] = '\uEC15', ['\uE962'] = '\uECA6',
        ['\uE950'] = '\uEB5B', ['\uE964'] = '\uEC87', ['\uEDA2'] = '\uED20', ['\uE9D9'] = '\uECF5',
        ['\uE7FC'] = '\uEBB5', ['\uE715'] = '\uEC77', ['\uE8BD'] = '\uEC93', ['\uE717'] = '\uECCC',
        ['\uE774'] = '\uEBC6', ['\uE701'] = '\uEE01', ['\uE702'] = '\uEC5F', ['\uE839'] = '\uED20',
        ['\uE72D'] = '\uED27', ['\uE8C8'] = '\uEB49', ['\uE8C6'] = '\uEB68', ['\uE77F'] = '\uEB2E',
        ['\uE7A7'] = '\uEDCB', ['\uE7A6'] = '\uECF4', ['\uE8A3'] = '\uEE0B', ['\uE8A4'] = '\uEE0C',
        ['\uE722'] = '\uEAEF', ['\uE749'] = '\uECE4', ['\uE7A3'] = '\uEDAE'
    };

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var glyph = values.Length > 0 ? values[0] as string ?? "" : "";
        var isPixel = values.Length > 1 && values[1] is true;
        return isPixel && glyph.Length == 1 && Glyphs.TryGetValue(glyph[0], out var pixelGlyph)
            ? pixelGlyph.ToString()
            : glyph;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
