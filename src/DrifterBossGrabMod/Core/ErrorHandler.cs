using System;

namespace DrifterBossGrabMod.Core
{
    public static class ErrorHandler
    {
        // Catching and logging any exceptions
        // T: The return type of the function
        // context: Context description for error logging (what operation was being performed)
        // action: The function to execute
        // defaultValue: The default value to return if an exception occurs
        // Returns: The result of the action, or defaultValue if an exception occurs
        public static T SafeExecute<T>(string context, Func<T> action, T defaultValue = default!)
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                Log.Error($"[{context}] Error: {ex.Message}");
                return defaultValue;
            }
        }

        // param: context - Description for error logging (what operation was being performed)
        // param: action - The action to execute
        public static void SafeExecute(string context, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error($"[{context}] Error: {ex.Message}");
            }
        }
    }
}
