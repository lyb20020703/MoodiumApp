using Moodium.CandyWorld;
using Moodium.Interaction;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;

public sealed class NatureLeafParticleControllerTests
{
    [Test]
    public void IndependentLeafEmittersDoNotShareACadenceAndPreserveTotalBudget()
    {
        var first = NatureLeafParticleController.GetIndependentEmissionRate(18f, 3, 0);
        var second = NatureLeafParticleController.GetIndependentEmissionRate(18f, 3, 1);
        var third = NatureLeafParticleController.GetIndependentEmissionRate(18f, 3, 2);
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(second, Is.Not.EqualTo(third));
        Assert.That(first + second + third, Is.EqualTo(18f).Within(.001f));
    }
    [Test]
    public void LeavesSurviveLongEnoughToReachViewerHeight()
    {
        var root = new GameObject("Leaf regression");
        var particles = new GameObject("Particles").AddComponent<ParticleSystem>();
        particles.transform.SetParent(root.transform);
        particles.Stop();
        root.AddComponent<NatureLeafParticleController>().Configure(particles);
        Assert.That(particles.main.startLifetime.constantMin, Is.GreaterThanOrEqualTo(35f));
        Assert.That(particles.main.cullingMode, Is.EqualTo(ParticleSystemCullingMode.AlwaysSimulate));
        Object.DestroyImmediate(root);
    }

    [Test]
    public void CompletedLeavesEnableNativeAutomaticEmission()
    {
        var root = new GameObject("Native emission regression");
        var particles = new GameObject("Particles").AddComponent<ParticleSystem>();
        particles.transform.SetParent(root.transform);
        particles.Stop();
        var controller = root.AddComponent<NatureLeafParticleController>();
        controller.Configure(particles);
        controller.StartLeaves();
        Assert.That(particles.emission.enabled, Is.False);
        controller.SetOrigin(Vector3.zero);
        controller.UpdateLeafIntensity(1f);
        Assert.That(particles.emission.enabled, Is.True);
        Assert.That(particles.emission.rateOverTime.constant, Is.EqualTo(18f));
        Assert.That(particles.shape.enabled, Is.True);
        controller.StopLeaves();
        Object.DestroyImmediate(root);
    }
    [Test]
    public void Leaves_StartOnlyWhenNatureWorldReachesCompletion()
    {
        Assert.That(NatureLeafParticleController.ShouldEmitLeaves(.99f), Is.False);
        Assert.That(NatureLeafParticleController.ShouldEmitLeaves(1f), Is.True);
    }

    [Test]
    public void EmptyVariantList_IsInitializedWhenLeavesStart()
    {
        Assert.That(NatureLeafParticleController.NeedsVariantInitialization(0), Is.True);
        Assert.That(NatureLeafParticleController.NeedsVariantInitialization(3), Is.False);
    }

    [Test]
    public void Release_IsOfferedOnlyAfterCompletionAndBeforeItHasBeenUsed()
    {
        Assert.That(EnergyReleasePolicy.ShouldOffer(.99f, false), Is.False);
        Assert.That(EnergyReleasePolicy.ShouldOffer(1f, false), Is.True);
        Assert.That(EnergyReleasePolicy.ShouldOffer(1f, true), Is.False);
    }

    [Test]
    public void ReleaseToggle_RemainsAvailableAfterItHasBeenStarted()
    {
        Assert.That(EnergyReleasePolicy.ShouldShowToggle(.99f), Is.False);
        Assert.That(EnergyReleasePolicy.ShouldShowToggle(1f), Is.True);
    }

    [Test]
    public void LeafVariantsSplitTheTotalEmissionBudgetInsteadOfTriplingIt()
    {
        Assert.That(NatureLeafParticleController.GetEmissionRatePerVariant(18f, 3), Is.EqualTo(6f));
        Assert.That(NatureLeafParticleController.GetEmissionRatePerVariant(18f, 0), Is.EqualTo(0f));
    }

    [Test]
    public void EachLeafEffectGetsItsOwnShareOfTheInitialEmission()
    {
        Assert.That(NatureLeafParticleController.GetInitialEmissionCount(16, 3, 0), Is.EqualTo(6));
        Assert.That(NatureLeafParticleController.GetInitialEmissionCount(16, 3, 1), Is.EqualTo(5));
        Assert.That(NatureLeafParticleController.GetInitialEmissionCount(16, 3, 2), Is.EqualTo(5));
    }

    [Test]
    public void NatureLeavesUseOneVariantPerTexture()
    {
        Assert.That(NatureLeafParticleController.GetLeafSystemCount(1), Is.EqualTo(1));
        Assert.That(NatureLeafParticleController.GetLeafSystemCount(3), Is.EqualTo(3));
    }

    [Test]
    public void ReleaseRainUsesWorldSpecificScaleMultipliers()
    {
        Assert.That(CandyRainRewardController.ReleaseScaleMultiplier(false), Is.EqualTo(.15f));
        Assert.That(CandyRainRewardController.ReleaseScaleMultiplier(true), Is.EqualTo(.375f));
        Assert.That(CandyRainRewardController.GetReleaseScaleMultiplier(false, "Chocolate_Capsule"), Is.EqualTo(.75f));
        Assert.That(CandyRainRewardController.GetReleaseScaleMultiplier(false, "StarPrefab"), Is.EqualTo(.15f));
    }

    [Test]
    public void ReleaseRainKeepsThirtyFiveRewardsAndExpiresGroundedRewardsAfterFiveSeconds()
    {
        Assert.That(CandyRainRewardController.DefaultMaxActiveRewards, Is.EqualTo(35));
        Assert.That(ReleaseRewardLifecycle.ShouldExpireAfterLanding(4.99f, 5f), Is.False);
        Assert.That(ReleaseRewardLifecycle.ShouldExpireAfterLanding(5f, 5f), Is.True);
    }

    [TestCase("Chocolate_Capsule", true)]
    [TestCase("StarPrefab", false)]
    [TestCase("MacaronPrefab", false)]
    public void CandyRelease_OnlyChocolateCapsulesUseTheShellCrackTouchPath(
        string prefabName, bool expected)
    {
        Assert.That(CandyRainRewardController.IsShellCrackReward(prefabName), Is.EqualTo(expected));
    }

    [Test]
    public void SoftSqueezeFeedbackCreatesTheExistingWhiteCircularBurstAtTheSoftCenter()
    {
        var expectedCenter = new Vector3(.24f, 1.15f, -.72f);

        var burst = CandyWorldManager.SpawnSoftSqueezeFeedback(expectedCenter);

        Assert.That(burst, Is.Not.Null);
        Assert.That(burst.name, Is.EqualTo("Moodium White Candy Burst"));
        Assert.That(burst.transform.position, Is.EqualTo(expectedCenter));
        Object.DestroyImmediate(burst);
    }

    [Test]
    public void WhiteBurstCircleTextureHasTransparentCornersAndAnOpaqueCenter()
    {
        var texture = ParticleBurstEffect.CreateRuntimeCircleTexture(16);

        Assert.That(texture.GetPixel(0, 0).a, Is.EqualTo(0f).Within(.001f));
        Assert.That(texture.GetPixel(8, 8).a, Is.GreaterThan(.95f));

        Object.DestroyImmediate(texture);
    }
}
