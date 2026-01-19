using UnityEngine;

namespace DrifterBossGrabMod
{
    public class NPCGrabbingStrategy : IGrabbingStrategy
    {
        public bool CanGrab(GameObject obj)
        {
            var characterBody = obj.GetComponent<RoR2.CharacterBody>();
            if (characterBody != null && !(characterBody.isBoss || characterBody.isChampion))
            {
                return PluginConfig.Instance.EnableNPCGrabbing.Value;
            }
            return false;
        }
    }
}
