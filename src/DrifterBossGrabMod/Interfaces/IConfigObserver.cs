using System;

namespace DrifterBossGrabMod
{
    public interface IConfigObserver
    {
        void OnConfigChanged(string key, object value);
    }
}
