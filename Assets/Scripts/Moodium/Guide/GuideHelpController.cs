using System.Collections;
using Moodium.Squeeze;
using UnityEngine;

namespace Moodium.Guide
{
    /// <summary>
    /// "Ask when you need it" tutorial (design boards 「玩法教学 · 不再抢戏」 and 「玩法教学 · 规格」):
    /// - Nothing pops up when a world starts. The "?" next to the mode buttons opens the tutorial card, docked on the
    ///   table behind the button row (WorldGuideCards.ShowDocked). Pressing a mode button while it is open turns the
    ///   card to that mode's page.
    /// - The first time a world is entered on this device, the "?" pulses twice once Moodi has asked for help.
    /// - At 100% an open card closes (Moodi's moment), and after <see cref="ExploreHintDelay"/> the "?" gets a dot and
    ///   a one-line hint appears beside it for 6 s. The card then starts with the "explore freely" page.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GuideHelpController : MonoBehaviour
    {
        /// <summary>After 100%: Moodi's thank-you (K10) and palm teaching end ≈ 9 s, the bubble world appears at 10 s.</summary>
        public const float ExploreHintDelay = 11f;

        SqueezeModeController m_Modes;
        WorldGuideCards m_Cards;
        SqueezeTableView m_View;
        bool m_Nature, m_English, m_Running;
        bool m_PulsePending, m_HintPending;
        float m_HintDeadline;
        /// <summary>If the table UI is not visible this long after unlocking, skip the hint line (the dot stays).</summary>
        public const float HintGiveUpSeconds = 20f;
        Coroutine m_Unlock;

        public bool ExploreUnlocked { get; private set; }
        public bool HasNewContent { get; private set; }
        public bool IsOpen => m_Cards != null && m_Cards.IsDockedOpen;

        public static string FirstVisitKey(bool nature) => "Moodium.WorldGuide.HelpPulse." + (nature ? "nature" : "candy");

        /// <summary>Which tutorial page explains a mode button.</summary>
        public static GuideCard CardFor(SqueezeMode mode) => mode switch
        {
            SqueezeMode.Rhythm => GuideCard.Rhythm,
            SqueezeMode.Breath => GuideCard.Breath,
            _ => GuideCard.Free
        };

        /// <summary>Is this the first visit to the world on this device? (marks it as visited)</summary>
        public static bool ConsumeFirstVisit(bool nature)
        {
            var key = FirstVisitKey(nature);
            if (PlayerPrefs.GetInt(key, 0) == 1) return false;
            PlayerPrefs.SetInt(key, 1);
            return true;
        }

        public void Begin(SqueezeModeController modes, WorldGuideCards cards, bool natureWorld, bool english)
        {
            Stop();
            m_Modes = modes; m_Cards = cards; m_Nature = natureWorld; m_English = english;
            ExploreUnlocked = HasNewContent = false;
            m_PulsePending = m_HintPending = false;
            m_Running = true;
        }

        /// <summary>Moodi finished asking for help (or the world started without the rescue story).</summary>
        public void OnIntroFinished()
        {
            if (!m_Running) return;
            if (ConsumeFirstVisit(m_Nature)) m_PulsePending = true;
        }

        /// <summary>Energy reached 100%.</summary>
        bool m_Suspended;
        public void SetSuspended(bool value) { m_Suspended = value; if (value && IsOpen) m_Cards.CloseDocked(); }
        public void OnFirstRingShown() { if (m_Running) { HasNewContent = true; m_PulsePending = true; } }

        public void OnEnergyFull(float delay = ExploreHintDelay)
        {
            if (!m_Running) return;
            if (IsOpen) m_Cards.CloseDocked();
            if (m_Unlock != null) StopCoroutine(m_Unlock);
            m_Unlock = StartCoroutine(UnlockExploreAfter(delay));
        }

        IEnumerator UnlockExploreAfter(float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            m_Unlock = null;
            ExploreUnlocked = true;
            HasNewContent = true;
            m_HintPending = true;   // shown as soon as the table UI is visible
            m_HintDeadline = Time.time + HintGiveUpSeconds;
        }

        /// <summary>The ? was pressed: open or close the docked card.</summary>
        public void Toggle()
        {
            if (m_Suspended) return;
            if (!m_Running || m_Cards == null) return;
            if (IsOpen) { m_Cards.CloseDocked(); return; }
            if (m_View == null || m_View.HelpDock == null) return;
            m_Cards.ShowDocked(m_View.HelpDock, m_Nature, m_English, ExploreUnlocked);
            HasNewContent = false;
        }

        void OnModePressed(SqueezeMode mode)
        {
            if (IsOpen) m_Cards.JumpTo(CardFor(mode));
        }

        void Update()
        {
            if (!m_Running || m_Suspended) return;
            var view = m_Modes != null ? m_Modes.View : null;
            if (view != m_View)
            {
                Unhook();
                // The card stands on the old table; a new table means a new place for it.
                if (m_Cards != null && m_Cards.IsDockedOpen) m_Cards.Hide(true);
                m_View = view;
                if (m_View != null) { m_View.HelpPressed += Toggle; m_View.ModePressed += OnModePressed; }
            }
            if (m_HintPending && Time.time > m_HintDeadline) m_HintPending = false;
            if (m_View == null || !m_View.HasHelp) return;
            if (m_PulsePending) { m_View.PulseHelp(); m_PulsePending = false; }
            if (m_HintPending && m_View.ShowExploreHint()) { m_View.PulseHelp(); m_HintPending = false; }
            m_View.SetHelpState(IsOpen, HasNewContent);
        }

        void Unhook()
        {
            if (m_View == null) return;
            m_View.HelpPressed -= Toggle;
            m_View.ModePressed -= OnModePressed;
        }

        public void Stop()
        {
            m_Running = false;
            if (m_Unlock != null) StopCoroutine(m_Unlock);
            m_Unlock = null;
            Unhook();
            m_View = null;
            if (m_Cards != null && m_Cards.IsDockedOpen) m_Cards.Hide(true);
        }

        void OnDisable() => Stop();
    }
}
