using System.Globalization;

namespace TextFlow.Services;

/// <summary>Small compatibility layer for the most useful QuickTextPaste tokens.</summary>
public static class TemplateExpander
{
    public static string Expand(string template, string clipboardText)
    {
        var now = DateTime.Now;
        var calendar = ISOWeek.GetWeekOfYear(now);
        return template
            .Replace("%send_tab%", "\t", StringComparison.OrdinalIgnoreCase)
            .Replace("%send_enter%", Environment.NewLine, StringComparison.OrdinalIgnoreCase)
            .Replace("%pptxt%", clipboardText, StringComparison.OrdinalIgnoreCase)
            .Replace("%YYYY%", now.ToString("yyyy"), StringComparison.OrdinalIgnoreCase)
            .Replace("%YY%", now.ToString("yy"), StringComparison.OrdinalIgnoreCase)
            .Replace("%LDF_UC%", now.ToShortDateString().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("%MMMM%", now.ToString("MMMM"), StringComparison.OrdinalIgnoreCase)
            .Replace("%MMM_UC%", now.ToString("MMM").ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("%MMM%", now.ToString("MMM"), StringComparison.OrdinalIgnoreCase)
            .Replace("%MM_UC%", now.ToString("MM"), StringComparison.OrdinalIgnoreCase)
            .Replace("%MM%", now.ToString("MM"), StringComparison.OrdinalIgnoreCase)
            .Replace("%M%", now.ToString("M"), StringComparison.OrdinalIgnoreCase)
            .Replace("%DDDD_UC%", now.ToString("dddd").ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("%DDDD%", now.ToString("dddd"), StringComparison.OrdinalIgnoreCase)
            .Replace("%DDD_UC%", now.ToString("ddd").ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)
            .Replace("%DDD%", now.ToString("ddd"), StringComparison.OrdinalIgnoreCase)
            .Replace("%DD%", now.ToString("dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("%D%", now.ToString("d"), StringComparison.OrdinalIgnoreCase)
            .Replace("%hh%", now.ToString("HH"), StringComparison.OrdinalIgnoreCase)
            .Replace("%mm%", now.ToString("mm"), StringComparison.OrdinalIgnoreCase)
            .Replace("%ss%", now.ToString("ss"), StringComparison.OrdinalIgnoreCase)
            .Replace("%LTF%", now.ToShortTimeString(), StringComparison.OrdinalIgnoreCase)
            .Replace("%LDF%", now.ToShortDateString(), StringComparison.OrdinalIgnoreCase)
            .Replace("%CW%", calendar.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }
}
