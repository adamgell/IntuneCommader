using Microsoft.Graph.Beta.Models;

namespace Intune.Commander.Core.Formatters;

public static class RunScheduleFormatter
{
    public static string Format(DeviceHealthScriptRunSchedule? schedule)
    {
        if (schedule is null) return "No schedule";

        return schedule switch
        {
            DeviceHealthScriptRunOnceSchedule once => FormatRunOnce(once),
            DeviceHealthScriptDailySchedule daily => FormatDaily(daily),
            DeviceHealthScriptHourlySchedule hourly => FormatHourly(hourly),
            DeviceHealthScriptTimeSchedule time => FormatTime(time),
            _ => $"Every {schedule.Interval ?? 1} interval(s)"
        };
    }

    private static string FormatHourly(DeviceHealthScriptHourlySchedule h)
    {
        var interval = h.Interval ?? 1;
        return interval == 1 ? "Every hour" : $"Every {interval} hours";
    }

    private static string FormatDaily(DeviceHealthScriptDailySchedule d)
    {
        var interval = d.Interval ?? 1;
        var freq = interval == 1 ? "Daily" : $"Every {interval} days";
        var time = d.Time.HasValue ? $" at {d.Time.Value.Hour:D2}:{d.Time.Value.Minute:D2}" : "";
        var tz = d.UseUtc == true ? "UTC" : "local time";
        return $"{freq}{time} ({tz})";
    }

    private static string FormatRunOnce(DeviceHealthScriptRunOnceSchedule r)
    {
        var date = r.Date.HasValue
            ? $"{r.Date.Value.Year}-{r.Date.Value.Month:D2}-{r.Date.Value.Day:D2}"
            : "unspecified date";
        var time = r.Time.HasValue ? $" at {r.Time.Value.Hour:D2}:{r.Time.Value.Minute:D2}" : "";
        var tz = r.UseUtc == true ? "UTC" : "local time";
        return $"Once on {date}{time} ({tz})";
    }

    private static string FormatTime(DeviceHealthScriptTimeSchedule t)
    {
        var interval = t.Interval ?? 1;
        var time = t.Time.HasValue ? $" at {t.Time.Value.Hour:D2}:{t.Time.Value.Minute:D2}" : "";
        var tz = t.UseUtc == true ? "UTC" : "local time";
        return $"Every {interval} interval(s){time} ({tz})";
    }
}
