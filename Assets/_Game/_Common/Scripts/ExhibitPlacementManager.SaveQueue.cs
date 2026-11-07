using System.Collections;
using UnityEngine;

namespace VFXViewer
{
    public partial class ExhibitPlacementManager
    {
        private const float BatchedSaveDebounceSeconds = 0.12f;

        private Coroutine m_QueuedSaveCoroutine;
        private bool m_HasQueuedSave;
        private bool m_QueuedSaveLayout;
        private bool m_QueuedSaveSuppressStatusMessage = true;
        private float m_LastQueuedSaveRequestTime = float.NegativeInfinity;

        private void QueueSaveToFiles(bool saveLayout, bool suppressStatusMessage)
        {
            m_HasQueuedSave = true;
            m_QueuedSaveLayout |= saveLayout;
            m_QueuedSaveSuppressStatusMessage &= suppressStatusMessage;
            m_LastQueuedSaveRequestTime = Time.unscaledTime;

            if (m_QueuedSaveCoroutine == null && isActiveAndEnabled)
                m_QueuedSaveCoroutine = StartCoroutine(FlushQueuedSaveWhenIdle());
        }

        private void CancelQueuedSave()
        {
            ResetQueuedSaveState(stopCoroutine: true);
        }

        private void ConsumeQueuedSaveRequest(ref bool saveLayout, ref bool suppressStatusMessage)
        {
            if (!m_HasQueuedSave)
                return;

            saveLayout |= m_QueuedSaveLayout;
            suppressStatusMessage &= m_QueuedSaveSuppressStatusMessage;
            ResetQueuedSaveState(stopCoroutine: true);
        }

        private IEnumerator FlushQueuedSaveWhenIdle()
        {
            while (true)
            {
                while (Time.unscaledTime - m_LastQueuedSaveRequestTime < BatchedSaveDebounceSeconds)
                    yield return null;

                if (!m_HasQueuedSave)
                    break;

                bool saveLayout = m_QueuedSaveLayout;
                bool suppressStatusMessage = m_QueuedSaveSuppressStatusMessage;
                ResetQueuedSaveState(stopCoroutine: false);
                SaveAllToFiles(saveLayout, suppressStatusMessage);

                if (!m_HasQueuedSave)
                    break;
            }

            m_QueuedSaveCoroutine = null;
        }

        private void ResetQueuedSaveState(bool stopCoroutine)
        {
            if (stopCoroutine && m_QueuedSaveCoroutine != null)
                StopCoroutine(m_QueuedSaveCoroutine);

            if (stopCoroutine)
                m_QueuedSaveCoroutine = null;

            m_HasQueuedSave = false;
            m_QueuedSaveLayout = false;
            m_QueuedSaveSuppressStatusMessage = true;
            m_LastQueuedSaveRequestTime = float.NegativeInfinity;
        }
    }
}
