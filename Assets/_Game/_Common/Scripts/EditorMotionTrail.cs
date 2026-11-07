using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VFXViewer
{
    [ExecuteAlways]
    [RequireComponent(typeof(LineRenderer))]
    public class EditorMotionTrail : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float trailLifetime = 0.6f;
        [SerializeField] private float minDistance = 0.01f;
        [SerializeField] private float resetDistance = 1.0f;
        [SerializeField] private int maxPoints = 48;

        private readonly List<Vector3> _positions = new();
        private readonly List<float> _times = new();

        private LineRenderer _lineRenderer;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;

        private void OnEnable()
        {
            _lineRenderer = GetComponent<LineRenderer>();
            if (target == null && transform.parent != null)
                target = transform.parent;

            ConfigureLineRenderer();
            ResetTrail();
        }

        private void OnValidate()
        {
            trailLifetime = Mathf.Max(0.05f, trailLifetime);
            minDistance = Mathf.Max(0.0001f, minDistance);
            resetDistance = Mathf.Max(minDistance, resetDistance);
            maxPoints = Mathf.Max(4, maxPoints);

            if (_lineRenderer == null)
                _lineRenderer = GetComponent<LineRenderer>();

            ConfigureLineRenderer();
        }

        private void Update()
        {
            if (_lineRenderer == null)
                _lineRenderer = GetComponent<LineRenderer>();

            if (target == null)
                return;

            float now = GetNow();
            Vector3 currentPosition = target.position;

            if (!_hasLastPosition)
            {
                _lastPosition = currentPosition;
                _hasLastPosition = true;
                AddPoint(currentPosition, now, true);
                UpdateRenderer();
                return;
            }

            float moved = Vector3.Distance(_lastPosition, currentPosition);
            if (moved >= resetDistance)
            {
                ResetTrail();
                AddPoint(currentPosition, now, true);
            }
            else if (moved >= minDistance)
            {
                AddPoint(currentPosition, now, false);
                _lastPosition = currentPosition;
            }

            TrimOldPoints(now);
            UpdateRenderer();

#if UNITY_EDITOR
            if (!Application.isPlaying)
                SceneView.RepaintAll();
#endif
        }

        public void ResetTrail()
        {
            _positions.Clear();
            _times.Clear();
            _hasLastPosition = false;

            if (_lineRenderer != null)
            {
                _lineRenderer.positionCount = 0;
            }
        }

        private void ConfigureLineRenderer()
        {
            if (_lineRenderer == null)
                return;

            _lineRenderer.useWorldSpace = true;
            _lineRenderer.loop = false;
            _lineRenderer.alignment = LineAlignment.View;
        }

        private void AddPoint(Vector3 position, float time, bool force)
        {
            if (!force && _positions.Count > 0)
            {
                if ((_positions[^1] - position).sqrMagnitude < minDistance * minDistance)
                    return;
            }

            _positions.Add(position);
            _times.Add(time);

            while (_positions.Count > maxPoints)
            {
                _positions.RemoveAt(0);
                _times.RemoveAt(0);
            }
        }

        private void TrimOldPoints(float now)
        {
            while (_times.Count > 0 && now - _times[0] > trailLifetime)
            {
                _times.RemoveAt(0);
                _positions.RemoveAt(0);
            }
        }

        private void UpdateRenderer()
        {
            if (_lineRenderer == null)
                return;

            if (_positions.Count == 0)
            {
                _lineRenderer.positionCount = 0;
                return;
            }

            _lineRenderer.positionCount = _positions.Count;
            _lineRenderer.SetPositions(_positions.ToArray());
        }

        private static float GetNow()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return (float)EditorApplication.timeSinceStartup;
#endif
            return Time.realtimeSinceStartup;
        }
    }
}
