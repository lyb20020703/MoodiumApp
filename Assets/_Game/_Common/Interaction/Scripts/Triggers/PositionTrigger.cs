using System;
using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 位置触发器：根据 Main Camera 的 XZ 坐标（用户位置）判断是否在区域内。
    /// 「进入」= 进入碰撞体时触发，「离开」= 离开碰撞体时触发。
    /// </summary>
    public class PositionTrigger : MonoBehaviour, ITrigger
    {
        public event Action<ITrigger, bool> Triggered;

        /// <summary> startFiresOnLeave：true=离开开始区域时触发开始；endFiresOnLeave：true=离开结束区域时触发结束。 </summary>
        public void SetConfig(Collider enterRegion, Collider leaveRegion, bool startFiresOnLeave = false, bool endFiresOnLeave = false)
        {
            _enterRegion = enterRegion;
            _leaveRegion = leaveRegion;
            _startFiresOnLeave = startFiresOnLeave;
            _endFiresOnLeave = endFiresOnLeave;
        }

        private Collider _enterRegion;
        private Collider _leaveRegion;
        private bool _startFiresOnLeave;
        private bool _endFiresOnLeave;
        private bool _wasInsideEnter;
        private bool _wasInsideLeave;
        private bool _enabled;

        private void Update()
        {
            if (!_enabled) return;

            var cam = Camera.main;
            if (cam == null) return;

            Vector3 userXZ = cam.transform.position;

            bool insideEnter = IsPointInColliderXZ(_enterRegion, userXZ);
            bool insideLeave = _leaveRegion != null && IsPointInColliderXZ(_leaveRegion, userXZ);

            // 开始：进入=进入区域时触发，离开=离开区域时触发
            if (_startFiresOnLeave)
            {
                if (_wasInsideEnter && !insideEnter)
                    Triggered?.Invoke(this, true);
            }
            else
            {
                if (insideEnter && !_wasInsideEnter)
                    Triggered?.Invoke(this, true);
            }

            // 结束：进入=进入区域时触发，离开=离开区域时触发
            if (_leaveRegion != null)
            {
                if (_endFiresOnLeave)
                {
                    if (_wasInsideLeave && !insideLeave)
                        Triggered?.Invoke(this, false);
                }
                else
                {
                    if (insideLeave && !_wasInsideLeave)
                        Triggered?.Invoke(this, false);
                }
            }

            _wasInsideEnter = insideEnter;
            _wasInsideLeave = insideLeave;
        }

        private static bool IsPointInColliderXZ(Collider col, Vector3 worldPointXZ)
        {
            if (col == null) return false;
            var b = col.bounds;
            var pt = new Vector3(worldPointXZ.x, b.center.y, worldPointXZ.z);
            return b.Contains(pt);
        }

        public void Enable()
        {
            _enabled = true;
        }

        public void Disable()
        {
            _enabled = false;
        }

        private void OnEnable()
        {
            _enabled = true;
        }

        private void OnDisable()
        {
            _enabled = false;
        }
    }
}
