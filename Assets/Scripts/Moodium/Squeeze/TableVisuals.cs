using System.Collections.Generic;
using Unity.PolySpatial;
using UnityEngine;
using UnityEngine.Rendering;
namespace Moodium.Squeeze
{
    public sealed class TableVisuals
    {
        public readonly Transform Root;
        public readonly Color Accent,Soft;
        public readonly string World,Language;
        public readonly Vector2 HalfSize;
        readonly VisionOSSortingGroup sorting;
        readonly List<Material> materials=new();
        readonly Dictionary<string,Sprite> sprites=new();
        public TableVisuals(Transform root,Vector2 halfSize,bool nature)
        {
            Root=root;HalfSize=halfSize;World=nature?"nature":"candy";
            Language=PlayerPrefs.GetString("Moodium.Language","Chinese")=="English"?"en":"cn";
            ColorUtility.TryParseHtmlString(nature?"#6FD39A":"#FF8DC7",out Accent);
            ColorUtility.TryParseHtmlString(nature?"#D6F8E0":"#FFD1EA",out Soft);
            sorting=root.gameObject.AddComponent<VisionOSSortingGroup>();
        }
        public Sprite Load(string key)
        {
            if(!sprites.TryGetValue(key,out var sprite))
            {
                sprite=Resources.Load<Sprite>("MoodiumSqueezeModes/"+key);sprites[key]=sprite;
                if(sprite==null)Debug.LogWarning("[Squeeze UI] Missing sprite: "+key);
            }
            return sprite;
        }
        public GameObject Node(Transform parent,string name,Vector3 position)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;return go;}
        public void Register(GameObject go,int order)=>sorting.Renderers.Add(new VisionOSSortingGroup.RendererSorting{Renderer=go,Order=order,ApplyToDescendants=false});
        public SpriteRenderer Sprite(Transform parent,string name,string key,Vector3 position,float width,int order,Color? color=null)
        {
            var go=Node(parent,name,position);var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=Load(key);
            if(renderer.sprite!=null)go.transform.localScale=Vector3.one*(width/renderer.sprite.bounds.size.x);
            go.transform.localRotation=Quaternion.Euler(90,180,0);renderer.color=color??Color.white;renderer.sortingOrder=order;Register(go,order);return renderer;
        }
        public RoundedRectBandMesh Band(string name,string texture,float y,int order,out Material material)
        {
            var go=Node(Root,name,new Vector3(0,y,0));var band=go.AddComponent<RoundedRectBandMesh>();
            material=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name=name+" Transparent"};
            material.SetFloat("_Surface",1);material.SetFloat("_Blend",0);material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);material.SetFloat("_ZWrite",0);material.SetFloat("_Cull",0);
            material.SetOverrideTag("RenderType","Transparent");material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");material.renderQueue=3000;
            material.SetTexture("_BaseMap",Resources.Load<Texture2D>("MoodiumSqueezeModes/"+texture));
            material.SetColor("_BaseColor",Accent);materials.Add(material);
            var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;
            renderer.receiveShadows=false;renderer.sortingOrder=order;Register(go,order);return band;
        }
        public static void Tint(Material material,Color color,float alpha){color.a=Mathf.Clamp01(alpha);material.SetColor("_BaseColor",color);}
        public void Dispose(){foreach(var material in materials)if(material!=null){if(Application.isPlaying)Object.Destroy(material);else Object.DestroyImmediate(material);}materials.Clear();}
    }
}
