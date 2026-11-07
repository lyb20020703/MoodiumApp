using System;
using DG.Tweening;
using UnityEngine;
using RenderHeads.Media.AVProVideo;

namespace Interaction
{
    /// <summary>
    /// DOTween Transform 出现效果：从物体当前 Transform 状态 tween 到配置的目标位置/旋转/缩放。
    /// </summary>
    public class TransformAppear : MonoBehaviour, IAppearEffect
    {
        [Tooltip("出现效果作用的内容（根节点需有 Transform）。")]
        [SerializeField] private GameObject contentRoot;

        [SerializeField] private bool animatePosition;
        [SerializeField] private bool animateRotation;
        [SerializeField] private bool animateScale = true;
        [SerializeField] private Vector3 positionTo = Vector3.zero;
        [SerializeField] private Vector3 rotationTo = Vector3.zero;
        [SerializeField] private Vector3 scaleTo = Vector3.one;
        [SerializeField] private int easeType = (int)Ease.OutQuad;

        public GameObject ContentRoot => contentRoot;

        /// <summary> 由 InteractionModule 等从配置注入。easeType 为 DG.Tweening.Ease 的整型。 </summary>
        public void SetConfig(bool animPos, bool animRot, bool animScale, Vector3 posTo, Vector3 rotTo, Vector3 sclTo, int ease)
        {
            animatePosition = animPos;
            animateRotation = animRot;
            animateScale = animScale;
            positionTo = posTo;
            rotationTo = rotTo;
            scaleTo = sclTo;
            easeType = ease;
        }

        public void Appear(IContentHandle target, float duration, Action onComplete)
        {
            bool localAnimatePosition = animatePosition;
            bool localAnimateRotation = animateRotation;
            bool localAnimateScale = animateScale;
            Vector3 localPositionTo = positionTo;
            Vector3 localRotationTo = rotationTo;
            Vector3 localScaleTo = scaleTo;
            Ease localEase = (Ease)easeType;

            if (target?.Root == null)
            {
                onComplete?.Invoke();
                return;
            }

            // Video：优先对挂了 ApplyToMesh 的物体做 Transform 动画（如 360 球体）
            Transform t = target.Root.transform;
            if (target.Type == ContentType.Video)
            {
                var apply = target.GetComponentForEffect<ApplyToMesh>();
                if (apply != null)
                    t = apply.transform;
            }
            t.DOKill();

            // 起始为物体当前 Transform，不设 from 参数
            if (duration <= 0f)
            {
                if (localAnimatePosition) t.localPosition = localPositionTo;
                if (localAnimateRotation) t.localRotation = Quaternion.Euler(localRotationTo);
                if (localAnimateScale) t.localScale = localScaleTo;
                onComplete?.Invoke();
                return;
            }

            if (!localAnimatePosition && !localAnimateRotation && !localAnimateScale)
            {
                onComplete?.Invoke();
                return;
            }

            var seq = DOTween.Sequence().SetTarget(t);
            if (localAnimatePosition)
            {
                var tw = t.DOLocalMove(localPositionTo, duration);
                tw.SetEase(localEase);
                seq.Join(tw);
            }
            if (localAnimateRotation)
            {
                var tw = t.DOLocalRotate(localRotationTo, duration, RotateMode.Fast);
                tw.SetEase(localEase);
                seq.Join(tw);
            }
            if (localAnimateScale)
            {
                var tw = t.DOScale(localScaleTo, duration);
                tw.SetEase(localEase);
                seq.Join(tw);
            }
            seq.OnComplete(() => onComplete?.Invoke());
        }
    }
}
