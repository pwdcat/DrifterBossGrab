using UnityEngine;

namespace DrifterBossGrabMod
{
    public class InfoLogger : ILogger
    {
        public void Log(LogLevel level, string message)
        {
            if (level >= LogLevel.Info)
            {
                Debug.Log($"[INFO] {message}");
            }
        }
    }
}
