using UnityEngine;
using UnityEditor;
using NUnit.Framework;
using Moodium.Calibration;
using Moodium.Flow;
public class SoftPrefabFitTests
{
    [TestCase("Candy",.4f,.25f)][TestCase("Nature",.4f,.25f)]
    [TestCase("Candy",.3f,.18f)][TestCase("Nature",.3f,.18f)]
    public void MeshAndColliderFillCalibration(string world,float width,float height)
    {
        var anchor=new GameObject("anchor");var root=new GameObject("presentation");
        try
        {
            var presentation=root.AddComponent<CalibratedSensoryBagPresentation>();
            presentation.Configure(anchor.transform,new SensoryBagCalibrationData{Width=width,Height=height,Depth=.05f,LocalOrientation=Quaternion.identity},AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/prefab/{world}Soft.prefab"));
            Assert.That(SoftModelBounds.TryGetLocalBounds(root.transform,out var b),Is.True);
            Assert.That(b.size.x,Is.EqualTo(width).Within(.001f));Assert.That(b.size.z,Is.EqualTo(height).Within(.001f));
            Assert.That(b.center.x,Is.EqualTo(0).Within(.001f));Assert.That(b.center.z,Is.EqualTo(0).Within(.001f));
            var collider=presentation.VisualRoot.GetComponentInChildren<BoxCollider>();
            Assert.That(collider,Is.Not.Null);
            Assert.That(collider.bounds.size.x,Is.EqualTo(width).Within(.002f));Assert.That(collider.bounds.size.z,Is.EqualTo(height).Within(.002f));
            Assert.That(collider.bounds.center.x,Is.EqualTo(0).Within(.002f));Assert.That(collider.bounds.center.z,Is.EqualTo(0).Within(.002f));
        }finally{Object.DestroyImmediate(root);Object.DestroyImmediate(anchor);}
    }
}
