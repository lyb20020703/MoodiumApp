namespace VFXViewer
{
    public static class PlatformRuntime
    {
#if UNITY_VISIONOS
        public static bool IsVisionOS => true;
#else
        public static bool IsVisionOS => false;
#endif

#if UNITY_IOS && !UNITY_VISIONOS
        public static bool IsIOSButNotVisionOS => true;
#else
        public static bool IsIOSButNotVisionOS => false;
#endif

        public static bool SupportsVisionHostFeatures
        {
            get
            {
                return IsVisionOS || UnityEngine.Application.isEditor;
            }
        }

        public static bool SupportsSpectatorClientFeatures
        {
            get
            {
                return IsIOSButNotVisionOS || UnityEngine.Application.isEditor;
            }
        }
    }
}
