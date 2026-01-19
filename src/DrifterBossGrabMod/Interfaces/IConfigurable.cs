namespace DrifterBossGrabMod
{
    // Interface for components that can be configured and require initialization/cleanup
    public interface IConfigurable
    {
        // Initialize the configurable component. Called when the mod is loaded.
        void Initialize();

        // Cleanup the configurable component. Called when the mod is unloaded.
        void Cleanup();
    }
}