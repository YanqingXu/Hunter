using UnityEngine;

namespace BigWorld.HotUpdate
{
    /// <summary>Lives in Assembly-CSharp and is invoked only after the C# hot-update assemblies are loaded.</summary>
    public static class GameEntryPoint
    {
        public const string CodeVersion = "1";
        public static void Initialize()
        {
            Debug.Log("BIGWORLD_HYBRIDCLR_CODE_VERSION: " + CodeVersion);
        }
    }
}
