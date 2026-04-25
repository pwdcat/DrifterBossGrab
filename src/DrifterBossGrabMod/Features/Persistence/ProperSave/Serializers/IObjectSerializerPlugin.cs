#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace DrifterBossGrabMod.ProperSave.Serializers
{
    public interface IObjectSerializerPlugin
    {
        bool CanHandle(GameObject obj);

        Dictionary<string, object>? CaptureState(GameObject obj);

        bool RestoreState(GameObject obj, Dictionary<string, object> state);

        int Priority { get; }

        string PluginName { get; }
    }
}
