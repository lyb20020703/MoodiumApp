using Moodium.Interaction;
using NUnit.Framework;
using UnityEngine;

public sealed class PalmPressCapsuleInteractionTests
{
    GameObject m_CapsuleObject;

    [TearDown]
    public void TearDown()
    {
        if (m_CapsuleObject != null)
            Object.DestroyImmediate(m_CapsuleObject);
    }

    [Test]
    public void PalmContactUsesExistingCapsuleFeedbackEventAndRearmsAfterRelease()
    {
        m_CapsuleObject = new GameObject("Test Tracked Chocolate Capsule");
        var capsule = m_CapsuleObject.AddComponent<ChocolateCapsuleInteraction>();
        capsule.SetInteractionEnabled(true);
        var eventCount = 0;
        void CountActivation(ChocolateCapsuleInteraction _, Vector3 __) => eventCount++;
        ChocolateCapsuleInteraction.AnyCapsulePinched += CountActivation;

        try
        {
            capsule.PalmContactStarted();
            capsule.PalmContactStarted();

            Assert.That(capsule.IsPinched, Is.True);
            Assert.That(eventCount, Is.EqualTo(1));

            capsule.PalmContactEnded();
            capsule.PalmContactStarted();

            Assert.That(capsule.IsPinched, Is.True);
            Assert.That(eventCount, Is.EqualTo(2));
        }
        finally
        {
            ChocolateCapsuleInteraction.AnyCapsulePinched -= CountActivation;
        }
    }
}
