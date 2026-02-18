using UnityEngine.SceneManagement;
using RoR2;

namespace DrifterBossGrabMod
{
    public interface IPersistenceManager
    {
        void OnSceneChanged(Scene oldScene, Scene newScene);
        void ScheduleAutoGrab(CharacterMaster master);
    }
}
