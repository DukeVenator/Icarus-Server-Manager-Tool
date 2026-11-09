using System.Globalization;
using IcarusServerManager.Models;

namespace IcarusServerManager.Services;

/// <summary>
/// Decides whether a scheduled restart occurrence is due and remembers which occurrence was already
/// actioned. Extracted from the form so the timing rules can be tested with an explicit clock.
///
/// An occurrence is the configured local time on a given day. It becomes actionable at that moment
/// and stays actionable for a grace window, so a timer tick delayed by a busy UI thread or a long
/// install still performs the restart instead of silently skipping the day.
/// </summary>
internal sealed class ScheduledRestartPolicy
{
    /// <summary>How late a tick may still start the scheduled restart.</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(15);

    private DateTime _lastConsumedOccurrence = DateTime.MinValue;

    /// <summary>
    /// True when <paramref name="now"/> falls inside the grace window for today's occurrence and that
    /// occurrence has not been consumed yet.
    /// </summary>
    public bool IsDue(ManagerOptions options, DateTime now, out DateTime occurrence)
    {
        occurrence = default;
        if (!options.UpdateScheduleEnabled || !TryParseSchedule(options.UpdateScheduleTime, out var scheduledTime))
        {
            return false;
        }

        occurrence = now.Date.Add(scheduledTime);
        return now >= occurrence &&
               now < occurrence + DefaultGrace &&
               !WasConsumed(occurrence);
    }

    public bool WasConsumed(DateTime occurrence) => _lastConsumedOccurrence == occurrence;

    /// <summary>Records that the occurrence was actioned so later ticks cannot repeat it.</summary>
    public void MarkConsumed(DateTime occurrence) => _lastConsumedOccurrence = occurrence;

    /// <summary>
    /// Parses a configured restart time. Values saved by older versions may use a single-digit hour,
    /// so the flexible parser is used rather than the strict HH:mm format enforced by the settings
    /// dialog.
    /// </summary>
    public static bool TryParseSchedule(string? text, out TimeSpan schedule)
    {
        schedule = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out schedule) &&
               schedule >= TimeSpan.Zero &&
               schedule < TimeSpan.FromDays(1);
    }
}
