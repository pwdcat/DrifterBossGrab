using UnityEngine;

namespace DrifterBossGrabMod
{
    public interface IGrabbingStrategy
    {
        bool CanGrab(GameObject obj);
    }
}
