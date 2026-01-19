using System;
using System.Collections.Generic;

namespace DrifterBossGrabMod
{
    public interface IPatchFactory
    {
        void RegisterPatch(Type patchType);
        void ApplyPatches();
        void CleanupPatches();
        IEnumerable<Type> GetRegisteredPatchTypes();
    }
}