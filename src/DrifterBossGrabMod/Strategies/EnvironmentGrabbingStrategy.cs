using UnityEngine;

namespace DrifterBossGrabMod
{
    public class EnvironmentGrabbingStrategy : IGrabbingStrategy
    {
        public bool CanGrab(GameObject obj)
        {
            var characterBody = obj.GetComponent<RoR2.CharacterBody>();
            if (characterBody == null)
            {
                return PluginConfig.Instance.EnableEnvironmentGrabbing.Value;
            }
            return false;
        }
    }
}
