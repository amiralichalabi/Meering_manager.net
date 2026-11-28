using System.Text.RegularExpressions;
using MeetingManager.Core;

namespace MeetingManager.Api;

// Request bodies. JSON is snake_case (SnakeCaseLower), as the frontend sends it.
// Validate() throws ApiException(422) with a Persian message the UI shows as a toast.

public static class V
{
    public static ApiException Bad(string label) => new(422, $"مقدار «{label}» معتبر نیست یا خالی است.");
    public static string Text(string? v, string label, int min = 0, int max = int.MaxValue)
    {
        v ??= "";
        if (v.Length < min || v.Length > max) throw Bad(label);
        return v;
    }
    public static int Range(int v, string label, int lo = 0, int hi = 100) { if (v < lo || v > hi) throw Bad(label); return v; }
    public static string Date(string? v)
    {
        try { return Jalali.Normalize(v); } catch (FormatException e) { throw new ApiException(422, e.Message); }
    }
    static readonly Regex TimeRe = new(@"^([01]\d|2[0-3]):([0-5]\d)$");
    public static string Time(string? v)
    {
        var t = Jalali.AsciiDigits(v).Trim();
        if (t == "") return "";
        var m = TimeRe.Match(t);
        if (!m.Success) throw new ApiException(422, "ساعت باید به‌صورت ۲۴ ساعته و با فرمت ساعت:دقیقه باشد (مثلاً ۱۰:۳۰).");
        return $"{m.Groups[1].Value}:{m.Groups[2].Value}";
    }
    public static void CheckRange(string start, string end, string msg)
    {
        if (start != "" && end != "" && Jalali.ToOrdinal(end) < Jalali.ToOrdinal(start)) throw new ApiException(422, msg);
    }
}

public class LoginIn { public string Username { get; set; } = ""; public string Password { get; set; } = ""; }
public class PersonIn
{
    public string Name { get; set; } = ""; public string Department { get; set; } = ""; public string Role { get; set; } = ""; public string Category { get; set; } = "";
    public int? CategoryId { get; set; } public bool IsActive { get; set; } = true;
    public PersonIn Validate() { V.Text(Name, "نام", 1, 200); return this; }
}
public class CategoryIn { public string Name { get; set; } = ""; public CategoryIn Validate() { V.Text(Name, "نام", 1, 120); return this; } }
public class TeamIn
{
    public string Name { get; set; } = ""; public string Description { get; set; } = ""; public int? ManagerId { get; set; }
    public List<int> MemberIds { get; set; } = new(); public bool IsActive { get; set; } = true;
    public TeamIn Validate() { V.Text(Name, "نام", 1, 200); return this; }
}
public class LocationIn
{
    public string Name { get; set; } = ""; public string Address { get; set; } = ""; public string Description { get; set; } = ""; public bool IsActive { get; set; } = true;
    public LocationIn Validate() { V.Text(Name, "نام", 1, 200); return this; }
}
public class ProjectIn
{
    public string Name { get; set; } = ""; public string Description { get; set; } = ""; public string Status { get; set; } = "فعال"; public int? ManagerId { get; set; }
    public string StartDate { get; set; } = ""; public string EndDate { get; set; } = ""; public int Progress { get; set; }
    public List<int> MemberIds { get; set; } = new(); public string Color { get; set; } = "";
    public ProjectIn Validate()
    {
        V.Text(Name, "نام", 1, 250); V.Range(Progress, "پیشرفت");
        if (Color != "" && !Regex.IsMatch(Color, "^#[0-9a-fA-F]{6}$")) throw new ApiException(422, "رنگ نامعتبر است.");
        StartDate = V.Date(StartDate); EndDate = V.Date(EndDate);
        V.CheckRange(StartDate, EndDate, "تاریخ پایان پروژه نمی‌تواند قبل از تاریخ شروع باشد.");
        return this;
    }
}
public class ProfileIn
{
    public string BirthDate { get; set; } = ""; public string Avatar { get; set; } = "";
    public ProfileIn Validate()
    {
        BirthDate = V.Date(BirthDate);
        if (Avatar.Length > 400000) throw new ApiException(422, "اطلاعات واردشده معتبر نیست.");
        if (Avatar != "" && !Regex.IsMatch(Avatar, "^data:image/(png|jpeg|gif|webp);base64,[A-Za-z0-9+/=]+$")) throw new ApiException(422, "تصویر نامعتبر است.");
        return this;
    }
}
public class PersonalNoteIn
{
    public string Text { get; set; } = ""; public string Category { get; set; } = "شخصی"; public bool Done { get; set; }
    public PersonalNoteIn Validate() { V.Text(Text, "متن", 1, 1000); V.Text(Category, "دسته", 1, 60); return this; }
}
public class PreviousIn
{
    public int? Id { get; set; } public string Activity { get; set; } = ""; public int? OwnerId { get; set; } public string Status { get; set; } = ""; public int Progress { get; set; } public string Note { get; set; } = "";
    public void Validate() => V.Range(Progress, "پیشرفت");
}
public class ActionIn
{
    public int? Id { get; set; } public string Subject { get; set; } = ""; public string Decision { get; set; } = ""; public int? OwnerId { get; set; } public int? AssigneeId { get; set; }
    public List<int> CollaboratorIds { get; set; } = new(); public string StartDate { get; set; } = ""; public string Due { get; set; } = ""; public string Status { get; set; } = "جدید";
    public string Priority { get; set; } = "متوسط"; public int Progress { get; set; } public int? ProjectId { get; set; } public int? ParentId { get; set; } public string Sprint { get; set; } = "";
    public ActionIn Validate()
    {
        V.Text(Subject, "موضوع", 1); if (Subject.Trim() == "") throw V.Bad("موضوع");
        V.Range(Progress, "پیشرفت"); V.Text(Sprint, "اسپرینت", 0, 100);
        StartDate = V.Date(StartDate); Due = V.Date(Due);
        V.CheckRange(StartDate, Due, "مهلت اقدام نمی‌تواند قبل از تاریخ شروع باشد.");
        return this;
    }
}
public class MeetingIn
{
    public string Title { get; set; } = ""; public string MeetingDate { get; set; } = ""; public string Time { get; set; } = ""; public string Place { get; set; } = "";
    public string Description { get; set; } = ""; public string Summary { get; set; } = ""; public int? ProjectId { get; set; } public List<int> AttendeeIds { get; set; } = new();
    public List<PreviousIn> PreviousItems { get; set; } = new(); public List<ActionIn> Actions { get; set; } = new(); public List<int> LinkedActionIds { get; set; } = new();
    public MeetingIn Validate()
    {
        V.Text(Title, "عنوان", 1, 300);
        Time = V.Time(Time);
        MeetingDate = V.Date(MeetingDate);
        if (MeetingDate == "") throw new ApiException(422, "تاریخ جلسه الزامی است.");
        foreach (var p in PreviousItems) p.Validate();
        foreach (var a in Actions) a.Validate();
        return this;
    }
}
public class ProgressIn
{
    public int NewProgress { get; set; } public string UpdateDate { get; set; } = ""; public string Note { get; set; } = "";
    public ProgressIn Validate()
    {
        V.Range(NewProgress, "پیشرفت");
        UpdateDate = V.Date(UpdateDate);
        if (UpdateDate == "") throw new ApiException(422, "تاریخ ثبت پیشرفت الزامی است.");
        return this;
    }
}
public class CommentIn { public string Body { get; set; } = ""; public CommentIn Validate() { V.Text(Body, "متن", 1, 5000); return this; } }
public class ChecklistIn { public string Title { get; set; } = ""; public bool IsDone { get; set; } public ChecklistIn Validate() { V.Text(Title, "عنوان", 1, 500); return this; } }
public class PasswordIn
{
    public string CurrentPassword { get; set; } = ""; public string NewPassword { get; set; } = "";
    public PasswordIn Validate() { V.Text(NewPassword, "رمز عبور", 6, 200); return this; }
}
public class UserIn
{
    public string Username { get; set; } = ""; public string FullName { get; set; } = ""; public string Password { get; set; } = ""; public string Role { get; set; } = "user"; public int? PersonId { get; set; } public bool IsActive { get; set; } = true;
    public UserIn Validate() { V.Text(Username, "نام کاربری", 3, 120); V.Text(FullName, "نام کامل", 1, 200); V.Text(Password, "رمز عبور", 6, 200); return this; }
}
public class UserUpdateIn
{
    public string Username { get; set; } = ""; public string FullName { get; set; } = ""; public string? Password { get; set; } public string Role { get; set; } = "user"; public int? PersonId { get; set; } public bool IsActive { get; set; } = true;
    public UserUpdateIn Validate() { V.Text(Username, "نام کاربری", 3, 120); V.Text(FullName, "نام کامل", 1, 200); if (Password != null) V.Text(Password, "رمز عبور", 6, 200); return this; }
}
public class SprintIn
{
    public string Name { get; set; } = ""; public string StartDate { get; set; } = ""; public string EndDate { get; set; } = ""; public int? ProjectId { get; set; }
    public SprintIn Validate()
    {
        V.Text(Name, "نام", 1, 100); if (Name.Trim() == "") throw V.Bad("نام");
        StartDate = V.Date(StartDate); EndDate = V.Date(EndDate);
        V.CheckRange(StartDate, EndDate, "پایان اسپرینت نمی‌تواند قبل از شروع آن باشد.");
        return this;
    }
}
public class PermissionIn { public string Role { get; set; } = ""; public List<string> PermissionCodes { get; set; } = new(); }
public class AssistantIn
{
    public string Question { get; set; } = ""; public List<System.Text.Json.JsonElement> History { get; set; } = new();
    public AssistantIn Validate() { V.Text(Question, "سؤال", 1, 2000); if (History.Count > 20) throw new ApiException(422, "اطلاعات واردشده معتبر نیست."); return this; }
}
public class AssistantConfigIn { public string Url { get; set; } = ""; public string Model { get; set; } = ""; public string Timeout { get; set; } = ""; }

public class EventIn
{
    public static readonly string[] Kinds = { "مصاحبه", "تحویل", "بازدید", "یادآوری", "سایر" };
    public int? Id { get; set; }
    public string Title { get; set; } = ""; public string Kind { get; set; } = "مصاحبه"; public string EventDate { get; set; } = "";
    public string StartTime { get; set; } = ""; public string EndTime { get; set; } = ""; public string Place { get; set; } = ""; public string Description { get; set; } = "";
    public int? ProjectId { get; set; } public List<int> AttendeeIds { get; set; } = new();
    public EventIn Validate()
    {
        V.Text(Title, "عنوان", 1, 300);
        if (Title.Trim() == "") throw new ApiException(422, "عنوان رویداد خالی است.");
        if (Array.IndexOf(Kinds, Kind) < 0) throw new ApiException(422, "نوع رویداد معتبر نیست.");
        StartTime = V.Time(StartTime); EndTime = V.Time(EndTime);
        EventDate = V.Date(EventDate);
        if (EventDate == "") throw new ApiException(422, "تاریخ رویداد الزامی است.");
        if (EndTime != "" && StartTime == "") throw new ApiException(422, "برای ساعت پایان، ساعت شروع هم لازم است.");
        if (StartTime != "" && EndTime != "" && string.CompareOrdinal(EndTime, StartTime) <= 0) throw new ApiException(422, "ساعت پایان باید بعد از ساعت شروع باشد.");
        return this;
    }
}

public class DraftSheetAction { public string Subject { get; set; } = ""; public string Decision { get; set; } = ""; public string Due { get; set; } = ""; public int? OwnerId { get; set; } public int? AssigneeId { get; set; } }

public class DraftSheetIn { public int? ProjectId { get; set; } public string Sprint { get; set; } = ""; public string Title { get; set; } = ""; public List<DraftSheetAction> Actions { get; set; } = new(); }

public class TextDraftIn { public string? Transcript { get; set; } }

public class TicketIn { public string? Url { get; set; } }
