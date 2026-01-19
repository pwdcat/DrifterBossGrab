using System;

namespace DrifterBossGrabMod
{
    public static class SafeExecutor
    {
        private static readonly ILogger ErrorLogger = new ErrorLogger();

        public static void Execute(Action action, string operationName)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                ErrorLogger.Log(LogLevel.Error, $"Error in {operationName}: {ex.Message}");
            }
        }

        public static T Execute<T>(Func<T> func, string operationName, T defaultValue = default)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                ErrorLogger.Log(LogLevel.Error, $"Error in {operationName}: {ex.Message}");
                return defaultValue;
            }
        }
    }
}