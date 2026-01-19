using UnityEngine;

namespace DrifterBossGrabMod
{
    public class BossGrabbingStrategy : IGrabbingStrategy
    {
        public bool CanGrab(GameObject obj)
        {
            var characterBody = obj.GetComponent<RoR2.CharacterBody>();
            if (characterBody != null && (characterBody.isBoss || characterBody.isChampion))
            {
                return PluginConfig.Instance.EnableBossGrabbing.Value;
            }
            return false;
        }
    }
}
