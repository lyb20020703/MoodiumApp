using System.Linq;
using UnityEditor;
using UnityEngine;
using Moodium.NatureWorld;
public static class WishChimeSetup
{
 [MenuItem("Moodium/Bubble World/Bake Wish Paper Frames")]
 public static void Bake()
 {
  foreach(var world in new[]{"Candy","Nature"})
  {
   string path="Assets/Resources/MoodiumBubblePlay/WishChime_"+world+".fbx";var importer=(ModelImporter)AssetImporter.GetAtPath(path);bool readable=importer.isReadable;
   try{
    importer.isReadable=true;importer.SaveAndReimport();
    var root=AssetDatabase.LoadAssetAtPath<GameObject>(path);var strip=root.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Strip");var mesh=strip.GetComponent<MeshFilter>().sharedMesh;var uv=mesh.uv;var vertices=mesh.vertices;
    var assetPath="Assets/Resources/MoodiumBubblePlay/PaperFrame_"+world+".asset";var frame=AssetDatabase.LoadAssetAtPath<WishPaperFrame>(assetPath);
    if(frame==null){frame=ScriptableObject.CreateInstance<WishPaperFrame>();AssetDatabase.CreateAsset(frame,assetPath);}
    frame.grid=new Vector3[225];
    for(int y=0;y<25;y++)for(int x=0;x<9;x++){var q=new Vector2(x/8f,y/24f);int index=0;float best=float.MaxValue;for(int i=0;i<uv.Length;i++){var d=(uv[i]-q).sqrMagnitude;if(d<best){best=d;index=i;}}if(best>.001f)throw new System.InvalidOperationException("Missing paper UV sample");frame.grid[y*9+x]=vertices[index];}
    EditorUtility.SetDirty(frame);
   }finally{importer.isReadable=readable;importer.SaveAndReimport();}
  }
  AssetDatabase.SaveAssets();
 }
}
