namespace DrifterBossGrabMod
{
    // Wrapper for PersistenceManager to implement IConfigurable
    public class PersistenceManagerWrapper : IConfigurable
    {
        public void Initialize()
        {
            PersistenceManager.Initialize();
        }

        public void Cleanup()
        {
            PersistenceManager.Cleanup();
        }
    }
}