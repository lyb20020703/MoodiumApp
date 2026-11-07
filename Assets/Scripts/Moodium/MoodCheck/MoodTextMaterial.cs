using TMPro;
using UnityEngine;

namespace Moodium.MoodCheck
{
    // Own the material per label so cleaning a panel never changes another UI.
    public sealed class MoodTextMaterial : MonoBehaviour
    {
        Material owned;
        public static void Apply(TMP_Text text)
        {
            if (text == null || text.font == null) return;
            var owner = text.GetComponent<MoodTextMaterial>() ?? text.gameObject.AddComponent<MoodTextMaterial>();
            var previous = owner.owned;
            var material = new Material(text.font.material);
            owner.owned = material;
            var shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
            if (shader != null) material.shader = shader;
            material.DisableKeyword("UNDERLAY_ON");
            material.DisableKeyword("UNDERLAY_INNER");
            material.DisableKeyword("OUTLINE_ON");
            material.DisableKeyword("GLOW_ON");
            material.SetFloat("_OutlineWidth", 0f);
            material.SetColor("_OutlineColor", Color.clear);
            material.SetColor("_UnderlayColor", Color.clear);
            if (material.HasProperty("_FaceTex")) material.SetTexture("_FaceTex", Texture2D.whiteTexture);
            material.SetColor("_FaceColor", Color.white);
            text.fontSharedMaterial = material;
            if (previous != null) Destroy(previous);
        }
        void OnDestroy() { if (owned != null) Destroy(owned); }
    }
}
