using System.Windows;
using System.Windows.Media;
using RGC.Shared;

namespace RGC.Branding;

public static class PriorityStyles
{
    public static Brush Accent(AnnouncementPriority p) => Find(p switch
    {
        AnnouncementPriority.Critical => "Priority.Critical",
        AnnouncementPriority.Important => "Priority.Important",
        _ => "Priority.Normal"
    });

    public static Brush Soft(AnnouncementPriority p) => Find(p switch
    {
        AnnouncementPriority.Critical => "Priority.CriticalSoft",
        AnnouncementPriority.Important => "Priority.ImportantSoft",
        _ => "Priority.NormalSoft"
    });

    public static string Label(AnnouncementPriority p) => p switch
    {
        AnnouncementPriority.Critical => "CRITICAL",
        AnnouncementPriority.Important => "IMPORTANT",
        _ => "NORMAL"
    };

    private static Brush Find(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
