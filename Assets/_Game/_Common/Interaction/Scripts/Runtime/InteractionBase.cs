using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using VFXViewer;

namespace Interaction
{
    public abstract class InteractionBase : MonoBehaviour
    {
        public string ModuleId
        {
            get
            {
                TryResolveModuleId(out var moduleId);
                return moduleId;
            }
        }

        public virtual GameObject PlaybackTarget
        {
            get
            {
                var info = GetComponentInParent<ExhibitInfo>(true);
                return info != null ? info.gameObject : gameObject;
            }
        }

        public abstract bool TryResolveModuleId(out string moduleId);

        protected bool TryResolveModuleIdFromExhibitInfo(out string moduleId)
        {
            moduleId = string.Empty;
            var info = GetComponentInParent<ExhibitInfo>(true);
            if (info == null)
                return false;

            moduleId = NormalizeModuleIdValue(info.moduleId);
            return !string.IsNullOrEmpty(moduleId);
        }

        protected bool TryResolveModuleIdFromGameObjectName(out string moduleId)
        {
            moduleId = NormalizeModuleIdValue(gameObject.name);
            return !string.IsNullOrEmpty(moduleId);
        }

        internal static string NormalizeModuleIdValue(string moduleId)
        {
            return string.IsNullOrWhiteSpace(moduleId) ? string.Empty : moduleId.Trim();
        }

        internal static int GetDefaultCandidateScore(Component component)
        {
            if (component == null)
                return int.MinValue;

            int score = 0;
            var info = component.GetComponentInParent<ExhibitInfo>(true);
            if (info != null)
                score += 10;

            if (component.GetComponentInParent<ARAnchor>(true) != null)
                score += 100;

            if (info != null && info.GetComponentInParent<ARAnchor>(true) != null)
                score += 100;

            if (info != null && info.name.StartsWith("Exhibit_", StringComparison.Ordinal))
                score += 20;

            if (component.gameObject.scene.IsValid())
                score += 1;

            return score;
        }
    }
}
