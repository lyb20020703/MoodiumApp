using System.Collections;
using System.Reflection;
using Moodium.Opening;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class OpeningGestureGuideTests
{
    GameObject m_OpeningInstance;

    [TearDown]
    public void TearDown()
    {
        if (m_OpeningInstance != null)
            Object.DestroyImmediate(m_OpeningInstance);
    }

    [Test]
    public void OpeningPrefabShowsGestureGuideThreeSecondsAfterCandyInFrontOfCandy()
    {
        var openingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/prefab/MoodiumOpening/MoodiumOpening.prefab");
        var gesturePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/prefab/Gesture.prefab");
        m_OpeningInstance = Object.Instantiate(openingPrefab);
        var manager = m_OpeningInstance.GetComponent<OpeningManager>();
        var candy = m_OpeningInstance.GetComponentInChildren<CandyInteraction>(true);
        var serializedManager = new SerializedObject(manager);

        var prefabProperty = serializedManager.FindProperty("m_GestureGuidePrefab");
        var delayProperty = serializedManager.FindProperty("m_GestureGuideDelay");
        Assert.That(prefabProperty, Is.Not.Null);
        Assert.That(prefabProperty.objectReferenceValue, Is.SameAs(gesturePrefab));
        Assert.That(delayProperty, Is.Not.Null);
        Assert.That(delayProperty.floatValue, Is.EqualTo(3f).Within(0.001f));

        var routineMethod = typeof(OpeningManager).GetMethod(
            "ShowGestureGuideAfterDelay",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(routineMethod, Is.Not.Null);
        var routine = routineMethod.Invoke(manager, null) as IEnumerator;
        Assert.That(routine, Is.Not.Null);
        Assert.That(routine.MoveNext(), Is.True);
        Assert.That(routine.Current, Is.InstanceOf<WaitForSeconds>());
        Assert.That(routine.MoveNext(), Is.False);

        var instanceField = typeof(OpeningManager).GetField(
            "m_GestureGuideInstance",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var guide = instanceField?.GetValue(manager) as GameObject;
        Assert.That(guide, Is.Not.Null);
        Assert.That(guide.transform.parent, Is.SameAs(candy.transform));
        Assert.That(guide.transform.localPosition, Is.EqualTo(new Vector3(0f, 0f, -0.18f)));
        Assert.That(guide.activeSelf, Is.True);

        var hideMethod = typeof(OpeningManager).GetMethod(
            "HideGestureGuide",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(hideMethod, Is.Not.Null);
        hideMethod.Invoke(manager, null);
        Assert.That(guide.activeSelf, Is.False);
    }
}
