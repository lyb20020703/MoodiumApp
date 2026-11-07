using UnityEngine;

public class SpatialPhoto : MonoBehaviour
{
    public enum LocalAxis
    {
        X,
        Y,
        Z
    }

    [Header("References")]
    public Transform targetCamera;
    public Transform photoTransform;

    [Header("Depth Movement")]
    public LocalAxis depthAxis = LocalAxis.X;
    public Vector2 depthBoundary = new Vector2(-0.15f, 0.5f);
    [Min(0.001f)] public float baseDistance = 2f;

    private const float MinBaseDistance = 0.001f;

    private Vector3 initialLocalPosition;
    private Vector3 baseCameraPosition;
    private Vector2 depthEdge;
    private bool hasLoggedMissingPhoto;

    void Reset()
    {
        TryAutoAssignReferences();
    }

    void Awake()
    {
        TryAutoAssignReferences();
        CacheInitialState();
    }

    void Update()
    {
        if (photoTransform == null)
        {
            if (!hasLoggedMissingPhoto)
            {
                Debug.LogError("SpatialPhoto needs a Photo transform reference.", this);
                hasLoggedMissingPhoto = true;
            }

            return;
        }

        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }

        if (targetCamera == null)
        {
            return;
        }

        float safeBaseDistance = Mathf.Max(baseDistance, MinBaseDistance);
        Vector3 worldDelta = targetCamera.position - baseCameraPosition;
        Vector3 localDelta = photoTransform.parent != null
            ? photoTransform.parent.InverseTransformVector(worldDelta)
            : worldDelta;

        float boundaryExtent = Mathf.Max(Mathf.Abs(depthBoundary.x), Mathf.Abs(depthBoundary.y));
        float distanceRate = boundaryExtent / safeBaseDistance;

        Vector3 targetLocalPos = initialLocalPosition;
        float targetDepth = GetAxisValue(initialLocalPosition, depthAxis) +
                            GetAxisValue(localDelta, depthAxis) * distanceRate;

        SetAxisValue(ref targetLocalPos, depthAxis, Mathf.Clamp(targetDepth, depthEdge.x, depthEdge.y));
        photoTransform.localPosition = targetLocalPos;
    }

    void OnValidate()
    {
        baseDistance = Mathf.Max(baseDistance, MinBaseDistance);

        if (!Application.isPlaying)
        {
            TryAutoAssignReferences();
            CacheInitialState();
        }
    }

    private void TryAutoAssignReferences()
    {
        if (targetCamera == null && Camera.main != null)
        {
            targetCamera = Camera.main.transform;
        }

        if (photoTransform == null)
        {
            Transform photoChild = transform.Find("Photo");
            if (photoChild != null)
            {
                photoTransform = photoChild;
            }
        }
    }

    private void CacheInitialState()
    {
        if (photoTransform == null)
        {
            return;
        }

        float safeBaseDistance = Mathf.Max(baseDistance, MinBaseDistance);
        initialLocalPosition = photoTransform.localPosition;

        float initialDepth = GetAxisValue(initialLocalPosition, depthAxis);
        depthEdge = new Vector2(
            initialDepth + depthBoundary.x,
            initialDepth + depthBoundary.y
        );

        baseCameraPosition = photoTransform.position - photoTransform.forward * safeBaseDistance;
    }

    private static float GetAxisValue(Vector3 value, LocalAxis axis)
    {
        switch (axis)
        {
            case LocalAxis.X:
                return value.x;
            case LocalAxis.Y:
                return value.y;
            default:
                return value.z;
        }
    }

    private static void SetAxisValue(ref Vector3 value, LocalAxis axis, float axisValue)
    {
        switch (axis)
        {
            case LocalAxis.X:
                value.x = axisValue;
                break;
            case LocalAxis.Y:
                value.y = axisValue;
                break;
            default:
                value.z = axisValue;
                break;
        }
    }
}
