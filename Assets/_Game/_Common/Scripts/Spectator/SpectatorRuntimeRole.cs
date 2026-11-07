using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VFXViewer
{
    public static class SpectatorRuntimeRole
    {
        private static bool s_HasOverride;
        private static AppRole s_OverrideRole;

        public static event Action<AppRole> RoleChanged;

        public static AppRole CurrentRole => ResolveRoleForScene(SceneManager.GetActiveScene());

        public static void SetOverride(AppRole role)
        {
            s_HasOverride = true;
            s_OverrideRole = role;
            RoleChanged?.Invoke(CurrentRole);
        }

        public static void ClearOverride()
        {
            if (!s_HasOverride)
                return;

            s_HasOverride = false;
            RoleChanged?.Invoke(CurrentRole);
        }

        public static AppRole ResolveRoleForScene(Scene scene)
        {
            if (s_HasOverride)
                return s_OverrideRole;

            string sceneName = scene.IsValid() ? scene.name : string.Empty;
            if (!string.IsNullOrWhiteSpace(sceneName))
            {
                if (sceneName.IndexOf("Spectator", StringComparison.OrdinalIgnoreCase) >= 0)
                    return AppRole.IPadSpectator;

                if (sceneName.IndexOf("VisionHost", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sceneName.IndexOf("Host", StringComparison.OrdinalIgnoreCase) >= 0)
                    return AppRole.VisionHost;
            }

            if (PlatformRuntime.IsVisionOS)
                return AppRole.VisionHost;

            if (PlatformRuntime.IsIOSButNotVisionOS)
                return AppRole.IPadSpectator;

            return AppRole.VisionHost;
        }

        public static bool IsRoleSupported(AppRole role)
        {
            switch (role)
            {
                case AppRole.VisionHost:
                    return PlatformRuntime.SupportsVisionHostFeatures;
                case AppRole.IPadSpectator:
                    return PlatformRuntime.SupportsSpectatorClientFeatures;
                default:
                    return false;
            }
        }
    }
}
