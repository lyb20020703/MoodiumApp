using System.Collections.Generic;
using UnityEngine;

namespace Moodium.NatureWorld
{
    public static class BubbleRingPlacement
    {
        public struct GroundHit
        {
            public Vector3 Point, Normal;
            public GroundHit(Vector3 point, Vector3 normal) { Point = point; Normal = normal; }
        }
        public static float HorizontalDistance(Vector3 a, Vector3 b) => new Vector2(a.x-b.x,a.z-b.z).magnitude;
        public static bool FloorInside(Vector3 head, Vector3 center, bool wasInside) => HorizontalDistance(head,center) <= (wasInside ? .7f : .45f);
        public static bool PalmInside(Vector3 palm, Vector3 center) => palm.y >= center.y && palm.y <= center.y+.15f && HorizontalDistance(palm,center)<=.08f;
        public static bool CanTrigger(bool explore, bool onHand, bool help, bool bubble) => explore && !onHand && !help && !bubble;
        public static bool FloorRing(Vector3 head, IReadOnlyList<GroundHit> hits, IReadOnlyList<Vector3> steps, IReadOnlyList<Vector3> occupied, out Vector3 point)
        {
            point=default; bool found=false;
            for(int i=0;i<hits.Count;i++) if(hits[i].Normal.y>=.85f && hits[i].Point.y<=head.y-1f && (!found || hits[i].Point.y<point.y)) { point=hits[i].Point; found=true; }
            if(!found) return false;
            if(Clear(point,occupied)) return true;
            float travelled=0; var previous=point;
            for(int i=steps.Count-1;i>=0;i--) { travelled+=HorizontalDistance(previous,steps[i]); previous=steps[i]; if(travelled>1.5f) break; if(Clear(steps[i],occupied)) { point=steps[i]; return true; } }
            return false;
        }
        static bool Clear(Vector3 p,IReadOnlyList<Vector3> occupied) { for(int i=0;i<occupied.Count;i++) if(HorizontalDistance(p,occupied[i])<.5f) return false; return true; }
        public static bool TableRing(Bounds? bag,float height,Vector3 forward,IReadOnlyList<Bounds> obstacles,out Vector3 point)
        {
            point=default; if(!bag.HasValue) return false;
            forward=Vector3.ProjectOnPlane(forward,Vector3.up).normalized; if(forward.sqrMagnitude<.1f) forward=Vector3.forward;
            var right=Vector3.Cross(Vector3.up,forward); var b=bag.Value;
            foreach(var dir in new[]{right,-right,forward}) {
                float extent=Mathf.Abs(dir.x)*b.extents.x+Mathf.Abs(dir.z)*b.extents.z;
                var p=b.center+dir*(extent+.12f+.07f); p.y=height;
                bool clear=true;
                for(int i=0;i<obstacles.Count;i++) { var expanded=obstacles[i]; expanded.Expand(.06f); var near=expanded.ClosestPoint(p); if(HorizontalDistance(near,p)<.07f && p.y>=expanded.min.y-.03f && p.y<=expanded.max.y+.03f) {clear=false;break;} }
                if(clear) {point=p;return true;}
            } return false;
        }
    }
}
