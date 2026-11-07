using UnityEditor;
using UnityEngine;
using Interaction;

namespace Interaction.Editor
{
    [CustomPropertyDrawer(typeof(TriggerType))]
    public class TriggerTypeDrawer : PropertyDrawer
    {
        private static readonly GUIContent[] Options =
        {
            new GUIContent("时间"),
            new GUIContent("进入"),
            new GUIContent("离开"),
            new GUIContent("触摸"),
            new GUIContent("长按"),
            new GUIContent("触摸导游")
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.enumValueIndex < 0)
                property.enumValueIndex = 0;
            property.enumValueIndex = EditorGUI.Popup(position, label, property.enumValueIndex, Options);
        }
    }

    [CustomPropertyDrawer(typeof(AppearEffectType))]
    public class AppearEffectTypeDrawer : PropertyDrawer
    {
        private static readonly GUIContent[] Options =
        {
            new GUIContent("直接显示"),
            new GUIContent("透明度渐变"),
            new GUIContent("材质动画"),
            new GUIContent("程序化动画"),
            new GUIContent("关键帧动画"),
            new GUIContent("直接隐藏")
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            property.enumValueIndex = EditorGUI.Popup(position, label, property.enumValueIndex, Options);
        }
    }

    [CustomPropertyDrawer(typeof(DisappearEffectType))]
    public class DisappearEffectTypeDrawer : PropertyDrawer
    {
        private static readonly GUIContent[] Options =
        {
            new GUIContent("直接隐藏"),
            new GUIContent("透明度渐变"),
            new GUIContent("材质动画"),
            new GUIContent("程序化动画"),
            new GUIContent("关键帧动画"),
            new GUIContent("直接显示")
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            property.enumValueIndex = EditorGUI.Popup(position, label, property.enumValueIndex, Options);
        }
    }

    [CustomPropertyDrawer(typeof(DragReleaseType))]
    public class DragReleaseTypeDrawer : PropertyDrawer
    {
        private static readonly GUIContent[] Options =
        {
            new GUIContent("无"),
            new GUIContent("归位"),
            new GUIContent("固定"),
            new GUIContent("物理碰撞")
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            property.enumValueIndex = EditorGUI.Popup(position, label, property.enumValueIndex, Options);
        }
    }

    [CustomPropertyDrawer(typeof(GuideMoveStyle))]
    public class GuideMoveStyleDrawer : PropertyDrawer
    {
        private static readonly GuideMoveStyle[] Values =
        {
            GuideMoveStyle.Straight,
            GuideMoveStyle.Waypoints
        };

        private static readonly GUIContent[] Options =
        {
            new GUIContent("直线"),
            new GUIContent("路径点")
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var current = (GuideMoveStyle)property.enumValueIndex;
            int currentIndex = System.Array.IndexOf(Values, current);
            if (currentIndex < 0) currentIndex = 0;

            int selected = EditorGUI.Popup(position, label, currentIndex, Options);
            if (selected >= 0 && selected < Values.Length)
                property.enumValueIndex = (int)Values[selected];
        }
    }

    [CustomPropertyDrawer(typeof(GuidePathInterpolation))]
    public class GuidePathInterpolationDrawer : PropertyDrawer
    {
        private static readonly GUIContent[] Options =
        {
            new GUIContent("折线"),
            new GUIContent("平滑曲线")
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            property.enumValueIndex = EditorGUI.Popup(position, label, property.enumValueIndex, Options);
        }
    }
}
