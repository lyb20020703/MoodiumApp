using System;
using UnityEngine;
using UnityEngine.Video;

namespace Moodium.Opening
{
    /// <summary>
    /// Transfers ownership of the opening video's already prepared runtime objects
    /// between opening, sticker selection and World flow without creating a second player.
    /// </summary>
    public sealed class OpeningVideoSession
    {
        readonly Action m_Dispose;

        public OpeningVideoSession(
            VideoPlayer player,
            Transform displayRoot,
            GameObject instructionRoot,
            bool english,
            Action dispose)
        {
            Player = player;
            DisplayRoot = displayRoot;
            InstructionRoot = instructionRoot;
            English = english;
            m_Dispose = dispose;
        }

        public VideoPlayer Player { get; }
        public Transform DisplayRoot { get; }
        public GameObject InstructionRoot { get; }
        public bool English { get; private set; }
        public void SetLanguage(bool english) => English = english;
        public bool IsDisposed { get; private set; }

        public void SetVisible(bool visible)
        {
            if (DisplayRoot != null)
                DisplayRoot.gameObject.SetActive(visible);
            if (InstructionRoot != null)
                InstructionRoot.SetActive(visible);
        }

        public void DisposeOnce()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            m_Dispose?.Invoke();
        }
    }
}
