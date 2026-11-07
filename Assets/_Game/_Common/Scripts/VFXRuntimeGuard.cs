using UnityEngine;

public static class VFXRuntimeGuard
{
    public static bool IsVFXRuntimeSupported()
    {
        return true;
    }

    public static bool DisableUnsupportedVFX(GameObject root, Object context = null)
    {
        return false;
    }
}
