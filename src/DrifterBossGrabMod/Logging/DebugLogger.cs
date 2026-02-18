using UnityEngine;

namespace DrifterBossGrabMod
{
    public class DebugLogger : ILogger
    {
        public void Log(LogLevel level, string message)
        {
            if (level >= LogLevel.Debug)
            {
                Debug.Log($"[DEBUG] {message}");
            }
        }
    }
}
