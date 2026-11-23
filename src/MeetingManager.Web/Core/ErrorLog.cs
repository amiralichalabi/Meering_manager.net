using MeetingManager.Data;
namespace MeetingManager.Core;

public static class ErrorLog
{
    public static void Write(string where, Exception e)
    {
        try { File.AppendAllText(Path.Combine(Paths.DataDir, "error.log"), $"[{DateTime.Now:s}] {where}\n{e}\n\n"); } catch { }
    }
}
