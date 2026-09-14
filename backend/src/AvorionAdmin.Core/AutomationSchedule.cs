using System.Globalization;
using AvorionAdmin.Core.Models;

namespace AvorionAdmin.Core;

public static class AutomationSchedule
{
    public const string TimeZone = "Asia/Shanghai";
    private static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public static string? Validate(AutomationTaskUpsertRequest request)
    {
        if (request.Kind is not ("scheduled-save" or "scheduled-safe-restart"))
            return "当前版本只开放定时保存和定时安全重启";

        if (request.Kind == "scheduled-save")
        {
            if (request.ScheduleKind != "interval" || request.IntervalMinutes is not (15 or 30 or 60 or 120))
                return "定时保存只支持每 15、30、60 或 120 分钟执行";
            return null;
        }

        if (request.ScheduleKind != "weekly" || request.DayOfWeek is < 1 or > 7)
            return "定时安全重启需要选择每周执行日期";
        if (!TimeOnly.TryParseExact(request.LocalTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return "定时安全重启需要有效的 24 小时时间";
        return null;
    }

    public static DateTimeOffset NextRun(AutomationTaskUpsertRequest request, DateTimeOffset after)
    {
        var validation = Validate(request);
        if (validation is not null) throw new ArgumentException(validation, nameof(request));

        if (request.ScheduleKind == "interval")
            return after.ToUniversalTime().AddMinutes(request.IntervalMinutes!.Value);

        var localNow = after.ToOffset(ChinaOffset);
        var time = TimeOnly.ParseExact(request.LocalTime!, "HH:mm", CultureInfo.InvariantCulture);
        var currentDay = ((int)localNow.DayOfWeek + 6) % 7 + 1;
        var days = request.DayOfWeek!.Value - currentDay;
        if (days < 0) days += 7;
        var date = DateOnly.FromDateTime(localNow.DateTime).AddDays(days);
        var candidate = new DateTimeOffset(date.ToDateTime(time), ChinaOffset);
        if (candidate <= localNow) candidate = candidate.AddDays(7);
        return candidate.ToUniversalTime();
    }

    public static string NameFor(string kind) => kind switch
    {
        "scheduled-save" => "定时保存",
        "scheduled-safe-restart" => "定时安全重启",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
