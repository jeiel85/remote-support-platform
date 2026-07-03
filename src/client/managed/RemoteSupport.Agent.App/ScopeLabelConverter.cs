using System.Globalization;
using System.Windows.Data;

namespace RemoteSupport.Agent.App;

public sealed class ScopeLabelConverter : IValueConverter
{
    public static ScopeLabelConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = "Scope_" + (value as string ?? string.Empty);
        return System.Windows.Application.Current.TryFindResource(key) as string ?? value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
