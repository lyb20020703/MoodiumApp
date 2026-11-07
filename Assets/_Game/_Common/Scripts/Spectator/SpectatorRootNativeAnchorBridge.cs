using System;
using System.Runtime.InteropServices;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace VFXViewer
{
    internal static class SpectatorRootNativeAnchorBridge
    {
        private const int ErrorBufferCapacity = 512;

        [StructLayout(LayoutKind.Sequential)]
        private struct UnityXRNativeSession
        {
            public int version;
            public IntPtr sessionPtr;
        }

        public static bool IsSupported => PlatformRuntime.IsIOSButNotVisionOS && !Application.isEditor;

        public static bool EnsureObserver(ARSession session, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!IsSupported)
                return false;

            if (!TryGetNativeSessionPtr(session, out IntPtr nativeSessionPtr, out errorMessage))
                return false;

            IntPtr errorBuffer = Marshal.AllocHGlobal(ErrorBufferCapacity);
            try
            {
                ClearBuffer(errorBuffer, ErrorBufferCapacity);
                bool success = SpectatorRootAnchor_EnsureSessionObserver(
                    nativeSessionPtr,
                    errorBuffer,
                    ErrorBufferCapacity);

                errorMessage = Marshal.PtrToStringAnsi(errorBuffer) ?? string.Empty;
                return success;
            }
            finally
            {
                Marshal.FreeHGlobal(errorBuffer);
            }
        }

        public static bool TryUpsertNamedAnchor(ARSession session, string anchorName, Pose pose, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!IsSupported)
                return false;

            Pose sessionRelativePose = ConvertUnityWorldPoseToSessionRelative(pose);

            if (!TryGetNativeSessionPtr(session, out IntPtr nativeSessionPtr, out errorMessage))
                return false;

            IntPtr errorBuffer = Marshal.AllocHGlobal(ErrorBufferCapacity);
            try
            {
                ClearBuffer(errorBuffer, ErrorBufferCapacity);
                bool success = SpectatorRootAnchor_UpsertNamedAnchor(
                    nativeSessionPtr,
                    anchorName,
                    sessionRelativePose.position.x,
                    sessionRelativePose.position.y,
                    sessionRelativePose.position.z,
                    sessionRelativePose.rotation.x,
                    sessionRelativePose.rotation.y,
                    sessionRelativePose.rotation.z,
                    sessionRelativePose.rotation.w,
                    errorBuffer,
                    ErrorBufferCapacity);

                errorMessage = Marshal.PtrToStringAnsi(errorBuffer) ?? string.Empty;
                return success;
            }
            finally
            {
                Marshal.FreeHGlobal(errorBuffer);
            }
        }

        public static bool TryGetNamedAnchorPose(ARSession session, string anchorName, out Pose pose, out string errorMessage)
        {
            pose = default;
            errorMessage = string.Empty;
            if (!IsSupported)
                return false;

            if (!TryGetNativeSessionPtr(session, out IntPtr nativeSessionPtr, out errorMessage))
                return false;

            float px = 0f;
            float py = 0f;
            float pz = 0f;
            float qx = 0f;
            float qy = 0f;
            float qz = 0f;
            float qw = 1f;

            IntPtr errorBuffer = Marshal.AllocHGlobal(ErrorBufferCapacity);
            try
            {
                ClearBuffer(errorBuffer, ErrorBufferCapacity);
                bool success = SpectatorRootAnchor_TryGetNamedAnchorPose(
                    nativeSessionPtr,
                    anchorName,
                    ref px,
                    ref py,
                    ref pz,
                    ref qx,
                    ref qy,
                    ref qz,
                    ref qw,
                    errorBuffer,
                    ErrorBufferCapacity);

                errorMessage = Marshal.PtrToStringAnsi(errorBuffer) ?? string.Empty;
                if (!success)
                    return false;

                Pose sessionRelativePose = new Pose(new Vector3(px, py, pz), new Quaternion(qx, qy, qz, qw));
                pose = ConvertSessionRelativePoseToUnityWorld(sessionRelativePose);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(errorBuffer);
            }
        }

        public static bool TryGetObservedNamedAnchorPose(ARSession session, string anchorName, out Pose pose, out string errorMessage)
        {
            pose = default;
            errorMessage = string.Empty;
            if (!IsSupported)
                return false;

            if (!TryGetNativeSessionPtr(session, out IntPtr nativeSessionPtr, out errorMessage))
                return false;

            float px = 0f;
            float py = 0f;
            float pz = 0f;
            float qx = 0f;
            float qy = 0f;
            float qz = 0f;
            float qw = 1f;

            IntPtr errorBuffer = Marshal.AllocHGlobal(ErrorBufferCapacity);
            try
            {
                ClearBuffer(errorBuffer, ErrorBufferCapacity);
                bool success = SpectatorRootAnchor_TryGetObservedNamedAnchorPose(
                    nativeSessionPtr,
                    anchorName,
                    ref px,
                    ref py,
                    ref pz,
                    ref qx,
                    ref qy,
                    ref qz,
                    ref qw,
                    errorBuffer,
                    ErrorBufferCapacity);

                errorMessage = Marshal.PtrToStringAnsi(errorBuffer) ?? string.Empty;
                if (!success)
                    return false;

                Pose sessionRelativePose = new Pose(new Vector3(px, py, pz), new Quaternion(qx, qy, qz, qw));
                pose = ConvertSessionRelativePoseToUnityWorld(sessionRelativePose);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(errorBuffer);
            }
        }

        private static bool TryGetNativeSessionPtr(ARSession session, out IntPtr nativeSessionPtr, out string errorMessage)
        {
            nativeSessionPtr = IntPtr.Zero;
            errorMessage = string.Empty;

            if (session == null)
            {
                errorMessage = "ARSession was unavailable.";
                return false;
            }

            XRSessionSubsystem subsystem = session.subsystem;
            if (subsystem == null)
            {
                errorMessage = "XRSessionSubsystem was unavailable.";
                return false;
            }

            IntPtr nativePtr = subsystem.nativePtr;
            if (nativePtr == IntPtr.Zero)
            {
                errorMessage = "XRSessionSubsystem.nativePtr was zero.";
                return false;
            }

            try
            {
                UnityXRNativeSession nativeSession = Marshal.PtrToStructure<UnityXRNativeSession>(nativePtr);
                if (nativeSession.version <= 0 || nativeSession.sessionPtr == IntPtr.Zero)
                {
                    errorMessage = $"Unity native session struct was invalid. version={nativeSession.version}";
                    return false;
                }

                nativeSessionPtr = nativePtr;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to marshal XRSessionSubsystem.nativePtr: {ex.Message}";
                return false;
            }
        }

        private static void ClearBuffer(IntPtr buffer, int capacity)
        {
            for (int i = 0; i < capacity; i++)
                Marshal.WriteByte(buffer, i, 0);
        }

        private static Pose ConvertUnityWorldPoseToSessionRelative(Pose unityWorldPose)
        {
            XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            Transform trackablesParent = xrOrigin != null ? xrOrigin.TrackablesParent : null;
            return trackablesParent != null
                ? Unity.XR.CoreUtils.TransformExtensions.InverseTransformPose(trackablesParent, unityWorldPose)
                : unityWorldPose;
        }

        private static Pose ConvertSessionRelativePoseToUnityWorld(Pose sessionRelativePose)
        {
            XROrigin xrOrigin = UnityEngine.Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            Transform trackablesParent = xrOrigin != null ? xrOrigin.TrackablesParent : null;
            return trackablesParent != null
                ? Unity.XR.CoreUtils.TransformExtensions.TransformPose(trackablesParent, sessionRelativePose)
                : sessionRelativePose;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern bool SpectatorRootAnchor_EnsureSessionObserver(
            IntPtr nativeSessionPtr,
            IntPtr errorBuffer,
            int errorBufferCapacity);

        [DllImport("__Internal")]
        private static extern bool SpectatorRootAnchor_UpsertNamedAnchor(
            IntPtr nativeSessionPtr,
            string anchorName,
            float px,
            float py,
            float pz,
            float qx,
            float qy,
            float qz,
            float qw,
            IntPtr errorBuffer,
            int errorBufferCapacity);

        [DllImport("__Internal")]
        private static extern bool SpectatorRootAnchor_TryGetNamedAnchorPose(
            IntPtr nativeSessionPtr,
            string anchorName,
            ref float px,
            ref float py,
            ref float pz,
            ref float qx,
            ref float qy,
            ref float qz,
            ref float qw,
            IntPtr errorBuffer,
            int errorBufferCapacity);

        [DllImport("__Internal")]
        private static extern bool SpectatorRootAnchor_TryGetObservedNamedAnchorPose(
            IntPtr nativeSessionPtr,
            string anchorName,
            ref float px,
            ref float py,
            ref float pz,
            ref float qx,
            ref float qy,
            ref float qz,
            ref float qw,
            IntPtr errorBuffer,
            int errorBufferCapacity);
#else
        private static bool SpectatorRootAnchor_EnsureSessionObserver(
            IntPtr nativeSessionPtr,
            IntPtr errorBuffer,
            int errorBufferCapacity) => false;

        private static bool SpectatorRootAnchor_UpsertNamedAnchor(
            IntPtr nativeSessionPtr,
            string anchorName,
            float px,
            float py,
            float pz,
            float qx,
            float qy,
            float qz,
            float qw,
            IntPtr errorBuffer,
            int errorBufferCapacity) => false;

        private static bool SpectatorRootAnchor_TryGetNamedAnchorPose(
            IntPtr nativeSessionPtr,
            string anchorName,
            ref float px,
            ref float py,
            ref float pz,
            ref float qx,
            ref float qy,
            ref float qz,
            ref float qw,
            IntPtr errorBuffer,
            int errorBufferCapacity) => false;

        private static bool SpectatorRootAnchor_TryGetObservedNamedAnchorPose(
            IntPtr nativeSessionPtr,
            string anchorName,
            ref float px,
            ref float py,
            ref float pz,
            ref float qx,
            ref float qy,
            ref float qz,
            ref float qw,
            IntPtr errorBuffer,
            int errorBufferCapacity) => false;
#endif
    }
}
