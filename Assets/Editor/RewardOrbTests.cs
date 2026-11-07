using Moodium.Rewards;
using NUnit.Framework;
using UnityEngine;

public sealed class RewardOrbTests
{
    sealed class UnwantedRewardBehaviour : MonoBehaviour { }

    [Test]
    public void PrepareMiniatureForOrb_DisablesSimulationAndKeepsFittedRenderer()
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.transform.localScale = Vector3.one * 2f;
        var collider = root.GetComponent<Collider>();
        var body = root.AddComponent<Rigidbody>();
        var particles = root.AddComponent<ParticleSystem>();
        var animator = root.AddComponent<Animator>();
        var behaviour = root.AddComponent<UnwantedRewardBehaviour>();

        try
        {
            RewardOrb.PrepareMiniatureForOrb(root, .045f);

            Assert.That(root.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(collider.enabled, Is.False);
            Assert.That(body.detectCollisions, Is.False);
            Assert.That(particles.emission.enabled, Is.False);
            Assert.That(animator.enabled, Is.False);
            Assert.That(behaviour.enabled, Is.False);
            Assert.That(root.GetComponent<Renderer>().bounds.size.magnitude, Is.LessThanOrEqualTo(.0451f));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
