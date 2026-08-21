using System;
using System.IO;

namespace Lumenotepad.Services;

public static class StartupLog
{
    private static readonly object Gate = new();
    private static string? _path;
    private static bool _opened;

    public static void Mark(string stage) => Append($"{DateTime.Now:HH:mm:ss.fff}  {stage}\n");

    public static void Fail(string stage, Exception ex) => Crash(stage, ex);

    public static void Crash(string stage, Exception? ex)
    {
        if (ex is null)
        {
            Mark($"{stage} FAILED, and the runtime handed back no exception object");
            return;
        }
        Mark($"{stage} FAILED: {ex.GetType().FullName}: {ex.Message}");
        Append(ex.ToString() + "\n");
    }

    private static void Append(string text)
    {
        try
        {
            lock (Gate)
            {
                if (_path is null)
                {
                    string dir = AppSettings.DefaultDir;
                    Directory.CreateDirectory(dir);
                    _path = Path.Combine(dir, "startup.log");
                }
                if (!_opened)
                {
                    _opened = true;
                    File.WriteAllText(_path,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  Lumenotepad {AppVersion.Current}  " +
                        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} " +
                        $"({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})\n");
                }
                File.AppendAllText(_path, text);
            }
        }
        catch
        {
        }
    }
}
