using UnityEngine;

namespace DrifterBossGrabMod
{
    public class ErrorLogger : ILogger
    {
        public void Log(LogLevel level, string message)
        {
            if (level >= LogLevel.Error)
            {
                Debug.LogError($"[ERROR] {message}");
            }
        }
    }
}