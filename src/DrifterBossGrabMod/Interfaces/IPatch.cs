namespace DrifterBossGrabMod
{
    // Interface for patches that require initialization and cleanup
    public interface IPatch
    {
        // Initialize patch. Called when mod is loaded.
        void Initialize();

        // Cleanup patch. Called when mod is unloaded.
        void Cleanup();
    }
}
