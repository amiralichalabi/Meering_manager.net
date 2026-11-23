using MeetingManager.Data;
namespace MeetingManager.Core;

public static class Status
{
    public const string Done = "انجام شده";
    public const string Late = "عقب‌افتاده";
    public const string New = "جدید";
    public const string InProgress = "در حال انجام";
    public static readonly string[] All = { New, InProgress, Done, Late };

    public static string Effective(ActionItem a, string? today = null)
    {
        today ??= Jalali.Today();
        return a.Status != Done && a.Due != "" && string.CompareOrdinal(a.Due, today) < 0 ? Late : a.Status;
    }
    public static string ForProgress(int p, string r) => p >= 100 ? Done : (p > 0 && r == New ? InProgress : (r == "" ? New : r));
    public static string AfterProgress(int p, string r) => r == Done && p < 100 ? (p > 0 ? InProgress : New) : ForProgress(p, r);
}
