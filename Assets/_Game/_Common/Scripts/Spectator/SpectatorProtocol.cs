using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace VFXViewer
{
    public enum SpectatorMessageKind
    {
        Hello = 0,
        FullSceneSnapshot = 1,
        DeltaEventBatch = 2,
        Heartbeat = 3,
        Error = 4,
        Command = 5,
        CommandResult = 6,
    }

    public enum SpectatorCommandKind
    {
        ImportLayoutJson = 0,
        ExportLayoutJson = 1,
    }

    public enum SpectatorDeltaKind
    {
        LayoutChanged = 0,
        PlaybackChanged = 1,
        HandStateChanged = 2,
        ModeChanged = 3,
        GroupChanged = 4,
        ModuleTriggerStart = 5,
        ModuleTriggerEnd = 6,
        ModulePhaseCorrection = 7,
    }

    public enum SpectatorBonjourEventType
    {
        None = 0,
        ServiceFound = 1,
        ServiceLost = 2,
        ServiceResolved = 3,
        AdvertiserStarted = 4,
        AdvertiserStopped = 5,
        Error = 6,
    }

    public enum SpectatorHandedness
    {
        Left = 0,
        Right = 1,
    }

    [Serializable]
    public struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public SerializableVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public SerializableVector3(Vector3 value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }

    [Serializable]
    public struct SerializableQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public SerializableQuaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public SerializableQuaternion(Quaternion value)
        {
            x = value.x;
            y = value.y;
            z = value.z;
            w = value.w;
        }

        public Quaternion ToQuaternion()
        {
            return new Quaternion(x, y, z, w);
        }
    }

    [Serializable]
    public struct SerializablePose
    {
        public SerializableVector3 position;
        public SerializableQuaternion rotation;

        public SerializablePose(Pose pose)
        {
            position = new SerializableVector3(pose.position);
            rotation = new SerializableQuaternion(pose.rotation);
        }

        public Pose ToPose()
        {
            return new Pose(position.ToVector3(), rotation.ToQuaternion());
        }
    }

    [Serializable]
    public sealed class SpectatorHello
    {
        public int protocolVersion = 1;
        public string assetManifestVersion = "1";
        public string deviceRole = string.Empty;
        public string sessionId = string.Empty;
        public string sceneName = string.Empty;
    }

    [Serializable]
    public sealed class SharedLayoutItem
    {
        public string instanceId = string.Empty;
        public string moduleId = string.Empty;
        public string displayName = string.Empty;
        public SerializableVector3 relativePosition;
        public SerializableQuaternion relativeRotation;
        public SerializableVector3 scale;
        public bool placementContentHidden;
    }

    [Serializable]
    public sealed class SharedLayoutSnapshot
    {
        public int layoutRevision;
        public string rootObjectName = string.Empty;
        public SharedLayoutItem[] items = Array.Empty<SharedLayoutItem>();
    }

    [Serializable]
    public sealed class ModuleRemoteStateSnapshot
    {
        public string moduleId = string.Empty;
        public int phase;
        public int phaseSequence;
        public long hostTimestamp;
        public bool gameObjectActive;
        public bool pendingStartWhileInitial;
        public bool pendingEndWhileAppearing;
    }

    [Serializable]
    public sealed class PlaybackSnapshot
    {
        public int playbackRevision;
        public int appMode;
        public int currentFlatIndex;
        public int currentGroupIndex;
        public string[] activeModuleIds = Array.Empty<string>();
        public ModuleRemoteStateSnapshot[] moduleStates = Array.Empty<ModuleRemoteStateSnapshot>();
    }

    [Serializable]
    public sealed class RemoteHandState
    {
        public int handedness;
        public bool isTracked;
        public SerializablePose rootPose;
        public SerializablePose wristPose;
        public float[] fingerCurls = Array.Empty<float>();
        public bool isGrabbing;
        public string heldObjectInstanceId = string.Empty;
        public bool hasHeldObjectRelativePose;
        public SerializablePose heldObjectRelativePose;
        public int sequence;
        public long hostTimestamp;
    }

    [Serializable]
    public sealed class FullSceneSnapshot
    {
        public int layoutRevision;
        public int playbackRevision;
        public int handRevision;
        public SharedLayoutSnapshot sharedLayout = new SharedLayoutSnapshot();
        public PlaybackSnapshot playbackSnapshot = new PlaybackSnapshot();
        public RemoteHandState[] remoteHands = Array.Empty<RemoteHandState>();
    }

    [Serializable]
    public sealed class DeltaEvent
    {
        public long eventId;
        public int kind;
        public string payloadJson = string.Empty;
        public long hostTimestamp;
    }

    [Serializable]
    public sealed class DeltaEventBatch
    {
        public DeltaEvent[] events = Array.Empty<DeltaEvent>();
    }

    [Serializable]
    public sealed class SpectatorHeartbeat
    {
        public long hostTimestamp;
    }

    [Serializable]
    public sealed class SpectatorErrorMessage
    {
        public string message = string.Empty;
    }

    [Serializable]
    public sealed class SpectatorCommandMessage
    {
        public int commandKind;
        public string payloadJson = string.Empty;
    }

    [Serializable]
    public sealed class SpectatorCommandResult
    {
        public int commandKind;
        public bool success;
        public string message = string.Empty;
        public string payloadJson = string.Empty;
    }

    [Serializable]
    public sealed class SpectatorLayoutImportRequest
    {
        public string fileName = string.Empty;
        public string layoutJson = string.Empty;
        public bool replaceExistingScene = true;
        public bool rebuildIfPossible = true;
    }

    [Serializable]
    public sealed class SpectatorLayoutExportResponse
    {
        public string fileName = string.Empty;
        public string layoutJson = string.Empty;
    }

    [Serializable]
    public sealed class SpectatorEnvelope
    {
        public int kind;
        public string jsonPayload = string.Empty;
    }

    [Serializable]
    public sealed class SpectatorBonjourEvent
    {
        public int type;
        public string serviceName = string.Empty;
        public string serviceType = string.Empty;
        public string hostName = string.Empty;
        public int port;
        public string error = string.Empty;
    }

    public static class SpectatorProtocol
    {
        public static string SerializeEnvelope<T>(SpectatorMessageKind kind, T payload)
        {
            var envelope = new SpectatorEnvelope
            {
                kind = (int)kind,
                jsonPayload = payload == null ? string.Empty : JsonUtility.ToJson(payload),
            };
            return JsonUtility.ToJson(envelope);
        }

        public static bool TryDeserializeEnvelope(string json, out SpectatorEnvelope envelope)
        {
            envelope = null;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                envelope = JsonUtility.FromJson<SpectatorEnvelope>(json);
                return envelope != null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpectatorProtocol] Failed to parse envelope: {ex.Message}");
                return false;
            }
        }

        public static T DeserializePayload<T>(string json) where T : class, new()
        {
            if (string.IsNullOrWhiteSpace(json))
                return new T();

            try
            {
                return JsonUtility.FromJson<T>(json) ?? new T();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SpectatorProtocol] Failed to parse payload {typeof(T).Name}: {ex.Message}");
                return new T();
            }
        }

        public static byte[] EncodeFrame(string payload)
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            byte[] frame = new byte[4 + jsonBytes.Length];
            byte[] lengthBytes = BitConverter.GetBytes(jsonBytes.Length);
            Buffer.BlockCopy(lengthBytes, 0, frame, 0, 4);
            if (jsonBytes.Length > 0)
                Buffer.BlockCopy(jsonBytes, 0, frame, 4, jsonBytes.Length);
            return frame;
        }

        public static long NowUnixMilliseconds()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public static string ReadFrameFromBuffer(MemoryStream stream)
        {
            if (stream == null || stream.Length < 4)
                return null;

            long originalPosition = stream.Position;
            stream.Position = 0;
            byte[] lengthBytes = new byte[4];
            stream.Read(lengthBytes, 0, 4);
            int payloadLength = BitConverter.ToInt32(lengthBytes, 0);
            if (payloadLength < 0 || stream.Length - 4 < payloadLength)
            {
                stream.Position = originalPosition;
                return null;
            }

            byte[] payloadBytes = new byte[payloadLength];
            if (payloadLength > 0)
                stream.Read(payloadBytes, 0, payloadLength);

            long remainingLength = stream.Length - stream.Position;
            if (remainingLength > 0)
            {
                byte[] remaining = new byte[remainingLength];
                stream.Read(remaining, 0, (int)remainingLength);
                stream.SetLength(0);
                stream.Write(remaining, 0, remaining.Length);
            }
            else
            {
                stream.SetLength(0);
            }

            stream.Position = stream.Length;
            return Encoding.UTF8.GetString(payloadBytes);
        }
    }
}
