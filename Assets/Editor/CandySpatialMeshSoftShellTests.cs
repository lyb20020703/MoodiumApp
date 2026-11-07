using Moodium.Reality;
using NUnit.Framework;

public sealed class CandySpatialMeshSoftShellTests
{
    [Test]
    public void SoftShellPress_IsStrongestAtTheContactCenter()
    {
        var center = CandySpatialMeshTransformationController.ComputeSoftPressDisplacement(0f, .14f, .024f);
        var edge = CandySpatialMeshTransformationController.ComputeSoftPressDisplacement(.14f, .14f, .024f);

        Assert.That(center, Is.EqualTo(-.024f).Within(.0001f));
        Assert.That(edge, Is.EqualTo(0f).Within(.0001f));
    }

    [Test]
    public void SoftShellPress_FallsOffSmoothlyInsideItsRadius()
    {
        var halfway = CandySpatialMeshTransformationController.ComputeSoftPressDisplacement(.07f, .14f, .024f);

        Assert.That(halfway, Is.LessThan(0f));
        Assert.That(halfway, Is.GreaterThan(-.024f));
    }

    [Test]
    public void SoftShellPress_IsDisabledWhenTheRadiusOrStrengthIsZero()
    {
        Assert.That(CandySpatialMeshTransformationController.ComputeSoftPressDisplacement(.01f, 0f, .024f), Is.Zero);
        Assert.That(CandySpatialMeshTransformationController.ComputeSoftPressDisplacement(.01f, .14f, 0f), Is.Zero);
    }
}
