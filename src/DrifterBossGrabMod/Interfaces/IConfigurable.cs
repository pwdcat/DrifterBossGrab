namespace DrifterBossGrabMod
{
    // Interface for components that can be configured and require initialization/cleanup
    public interface IConfigurable
    {
        // Initialize() configurable component. Called when mod is loaded.
        void Initialize();

        // Cleanup() configurable component. Called when mod is unloaded.
        void Cleanup();
    }
}
