namespace DrifterBossGrabMod
{
    // Interface for patches that require initialization and cleanup
    public interface IPatch
    {
        // Initialize the patch. Called when the mod is loaded.
        void Initialize();

        // Cleanup the patch. Called when the mod is unloaded.
        void Cleanup();
    }
}