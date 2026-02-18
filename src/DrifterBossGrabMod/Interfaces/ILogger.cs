using System;

namespace DrifterBossGrabMod
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Fatal
    }

    public interface ILogger
    {
        void Log(LogLevel level, string message);
    }
}
