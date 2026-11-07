using System.Linq;
using Moodium.Flow;
using UnityEditor;
using UnityEngine;

namespace Moodium.EditorTools
{
    /// <summary>Creates the runtime clip-reference asset for Moodi on the sticker selection UI.</summary>
    public static class MoodiSelectionMascotSetup
    {
        const string ModelPath = "Assets/Model/IP_Unity_3Clips.fbx";
        const string ConfigFolder = "Assets/Resources/MoodiumSelection";
        const string ConfigPath = ConfigFolder + "/MoodiSelectionAnimationConfig.asset";

        [MenuItem("Moodium/Setup Selection Moodi Animation")]
        public static void CreateOrUpdateConfig()
        {
            var modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelRoot == null || modelRoot.GetComponentInChildren<Animator>(true) == null)
            {
                Debug.LogError($"[Moodium Selection Moodi] {ModelPath} has no runtime Animator on its imported model hierarchy.");
                return;
            }

            var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().ToArray();
            var idle = clips.FirstOrDefault(clip => clip.name == "Idle_Blink");
            var shySmile = clips.FirstOrDefault(clip => clip.name == "Shy_Smile");
            var wink = clips.FirstOrDefault(clip => clip.name == "Wink");
            if (idle == null || shySmile == null || wink == null)
            {
                Debug.LogError($"[Moodium Selection Moodi] Required clips were not found in {ModelPath}. " +
                    $"Found: {string.Join(", ", clips.Select(clip => clip.name))}");
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(ConfigFolder))
                AssetDatabase.CreateFolder("Assets/Resources", "MoodiumSelection");

            var config = AssetDatabase.LoadAssetAtPath<MoodiSelectionAnimationConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<MoodiSelectionAnimationConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            config.Configure(idle, shySmile, wink);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Moodium Selection Moodi] Animation config ready: Idle={idle.name}, Shy={shySmile.name}, Wink={wink.name}.");
        }
    }
}
