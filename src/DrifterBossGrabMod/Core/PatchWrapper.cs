using System;
using System.Reflection;

namespace DrifterBossGrabMod
{
    // Wrapper for patch types that implement IConfigurable by calling static Initialize and Cleanup methods
    public class PatchWrapper : IConfigurable
    {
        private readonly Type _patchType;

        public PatchWrapper(Type patchType)
        {
            _patchType = patchType ?? throw new ArgumentNullException(nameof(patchType));
        }

        public void Initialize()
        {
            var initializeMethod = _patchType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
            if (initializeMethod != null)
            {
                initializeMethod.Invoke(null, null);
            }
            else
            {
                Log.Error($"Patch type {_patchType.Name} does not have a public static Initialize method");
            }
        }

        public void Cleanup()
        {
            var cleanupMethod = _patchType.GetMethod("Cleanup", BindingFlags.Public | BindingFlags.Static);
            if (cleanupMethod != null)
            {
                cleanupMethod.Invoke(null, null);
            }
            else
            {
                Log.Error($"Patch type {_patchType.Name} does not have a public static Cleanup method");
            }
        }
    }
}