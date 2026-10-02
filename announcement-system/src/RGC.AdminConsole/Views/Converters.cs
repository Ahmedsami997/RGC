using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RGC.AdminConsole.ViewModels;
using RGC.Branding;
using RGC.Shared;

namespace RGC.AdminConsole.Views;

/// <summary>UTC DateTime → local time text ("—" when empty).</summary>
public sealed class LocalTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime dt ? dt.ToLocalTime().ToString((parameter as string) ?? "dd MMM yyyy HH:mm:ss", culture) : "—";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PriorityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var p = value is AnnouncementPriority ap ? ap : AnnouncementPriority.Normal;
        return parameter as string == "soft" ? PriorityStyles.Soft(p) : PriorityStyles.Accent(p);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PriorityLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AnnouncementPriority p ? PriorityStyles.Label(p) : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Online → green, offline → grey.</summary>
public sealed class OnlineBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.FindResource(value is true ? "Brand.GreenDark" : "Brand.Line");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class RecipientStateBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.FindResource(value switch
        {
            RecipientState.Read => "Priority.Normal",
            RecipientState.Delivered => "Priority.Important",
            _ => "Brand.Muted"
        });

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
