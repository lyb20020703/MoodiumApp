using UnityEngine;

public class FaceMainCamera : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool onlyYaw = false;
    [SerializeField] private bool invertFacing = false;

    private void LateUpdate()
    {
        if (!TryGetFacingRotation(out var rotation))
            return;

        transform.rotation = rotation;
    }

    public bool TryGetFacingRotation(out Quaternion rotation)
    {
        rotation = transform.rotation;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
            return false;

        Vector3 targetPos = cam.transform.position;
        if (onlyYaw)
            targetPos.y = transform.position.y;

        Vector3 forward = targetPos - transform.position;
        if (forward.sqrMagnitude < 0.000001f)
            return false;

        rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        if (invertFacing)
            rotation *= Quaternion.Euler(0f, 180f, 0f);

        return true;
    }
}
