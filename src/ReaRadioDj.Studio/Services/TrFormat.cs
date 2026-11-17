using System.Globalization;
using Avalonia.Data.Converters;
using ReaRadioDj.Contracts.Localization;

namespace ReaRadioDj.Studio.Services;

/// <summary>
/// A StringFormat that is translated: <c>{Binding X, Converter={StaticResource Tr}, ConverterParameter='gespeeld {0}'}</c>
/// shows the Dutch format in the studio's language with the value filled in. A plain StringFormat with words in it would
/// stay Dutch, because <see cref="AutoTranslate"/> leaves bound texts alone.
/// </summary>
public sealed class TrFormat : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is string format ? (value is null ? "" : Tr.F(format, value)) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
