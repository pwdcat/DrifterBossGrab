using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace DrifterBossGrabMod
{
    // Factory for managing and applying Harmony patches and manual patches
    public class PatchFactory : IPatchFactory, IConfigurable
    {
        private readonly List<Type> registeredPatchTypes = new List<Type>();
        private Harmony? harmonyInstance;

        public static IPatchFactory Instance { get; } = new PatchFactory();

        // Register a patch type that has Initialize and Cleanup static methods
        public void RegisterPatch(Type patchType)
        {
            if (patchType == null)
                throw new ArgumentNullException(nameof(patchType));

            registeredPatchTypes.Add(patchType);
        }

        // Apply all registered patches and Harmony patches
        public void ApplyPatches()
        {
            // Apply Harmony patches via attributes
            harmonyInstance = new Harmony("pwdcat.DrifterBossGrab");

            // Initialize manual patches
            foreach (var patchType in registeredPatchTypes)
            {
                try
                {
                    var initializeMethod = patchType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
                    if (initializeMethod != null)
                    {
                        initializeMethod.Invoke(null, null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to initialize patch {patchType.Name}: {ex.Message}");
                }
            }
        }

        // Cleanup all registered patches
        public void CleanupPatches()
        {
            // Cleanup manual patches
            foreach (var patchType in registeredPatchTypes)
            {
                try
                {
                    var cleanupMethod = patchType.GetMethod("Cleanup", BindingFlags.Public | BindingFlags.Static);
                    if (cleanupMethod != null)
                    {
                        cleanupMethod.Invoke(null, null);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to cleanup patch {patchType.Name}: {ex.Message}");
                }
            }

            // Clear registered patches
            registeredPatchTypes.Clear();

            // Unpatch Harmony patches
            if (harmonyInstance != null)
            {
                harmonyInstance.UnpatchSelf();
                harmonyInstance = null;
            }
        }

        // Get all registered patch types (for testing/debugging)
        public IEnumerable<Type> GetRegisteredPatchTypes()
        {
            return registeredPatchTypes.AsReadOnly();
        }

        // Initialize the patch factory (implements IConfigurable)
        void IConfigurable.Initialize()
        {
            ApplyPatches();
        }

        // Cleanup the patch factory (implements IConfigurable)
        void IConfigurable.Cleanup()
        {
            CleanupPatches();
        }
    }
}
