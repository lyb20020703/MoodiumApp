using System.IO;
using Moodium.NatureWorld;
using NUnit.Framework;
using UnityEngine;
public class BubblePlayTests
{
 [Test]public void RippleTimingAndCooldown(){Assert.That(BubbleLightRipple.Arrival(12),Is.EqualTo(2));Assert.That(BubbleLightRipple.CanTrigger(3,0,true),Is.False);Assert.That(BubbleLightRipple.CanTrigger(2,0,false),Is.False);Assert.That(BubbleLightRipple.CanTrigger(3,0,false),Is.True);}
 [Test]public void CatchRequiresOpenPalmAndSpace(){Assert.That(BubblePlayHands.CanCatch(Vector3.zero,Vector3.up,Vector3.up*.03f,4),Is.True);Assert.That(BubblePlayHands.CanCatch(Vector3.zero,Vector3.down,Vector3.up*.03f,0),Is.False);Assert.That(BubblePlayHands.CanCatch(Vector3.zero,Vector3.up,Vector3.up*.03f,5),Is.False);Assert.That(BubblePlayHands.CanCatch(Vector3.zero,Vector3.up,Vector3.up*.08f,0),Is.False);}
 [Test]public void PinchHasHysteresis(){Assert.That(BubblePlayHands.PinchState(false,.02f),Is.False);Assert.That(BubblePlayHands.PinchState(true,.02f),Is.True);}
 [Test]public void MaterialsStayOnTheStaticPointPath(){var m=BubbleSplatMaterial.Create(Texture2D.whiteTexture,1.3f,false);try{Assert.That(m.shader.name,Is.EqualTo("Universal Render Pipeline/Unlit"));}finally{Object.DestroyImmediate(m);}}
}
