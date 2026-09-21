#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FishingMod.Editor
{
    internal static class FishingDirectCastChecks
    {
        internal static int Run()
        {
            int checks=0;
            Action<bool,string> check=(ok,why)=>{if(!ok)throw new InvalidOperationException(why);checks++;};
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);
            Vector3 water=new Vector3(-1698.6f,-2.8f,-889.8f);
            blocker.transform.position=water+Vector3.up*3;
            blocker.transform.localScale=new Vector3(10,1,10);Physics.SyncTransforms();
            var detector=new FishingWaterDetector();Ray ray=new Ray(water+Vector3.up*40,Vector3.down);
            foreach(float height in new[]{-2.8f,0f,25f,60f})
            {
                check(detector.IsPlayerNearWater(new Vector3(-1689.7f,height,-885.8f),out float shoreDistance)
                    && Mathf.Abs(shoreDistance-.95817f)<.002f,"quay proximity uses X/Z, independent of height");
                check(!detector.IsPlayerNearWater(new Vector3(-1490.9f,height,-1319.5f),out float landDistance)
                    && landDistance>290f,"far inland player cannot cast even toward valid water");
            }
            check(!detector.IsPlayerNearWater(new Vector3(float.NaN,0,0),out _),"invalid player coordinates rejected");
            check(!detector.TryGetWaterPoint(ray,null,out _),"ordinary cast preserves solid visibility");
            check(!detector.TryGetWaterPoint(ray,null,out _),
                "keyboard cast shares solid visibility validation");
            check(!detector.TryGetWaterPoint(new Ray(new Vector3(-2600,30,-1100),Vector3.down),null,out _),
                "keyboard cast cannot turn inland into water");
            UnityEngine.Object.DestroyImmediate(blocker);
            foreach(float distance in new[]{.5f,3f,28f,80f,250f})
            foreach(float elevation in new[]{0f,25f,60f})
            {
                Vector3 target=new Vector3(distance,-2.8f,17f);
                Vector3 launch=new Vector3(0,elevation+2,0);
                Vector3 landing=FishingCastGeometry.LandingPoint(target);
                check(landing.x==target.x && landing.z==target.z,"landing never clamps or offsets clicked X/Z");
                check(Vector3.Distance(FishingCastGeometry.FlightPoint(launch,landing,0,1),launch)<.001f,"flight starts at tip");
                check(Vector3.Distance(FishingCastGeometry.FlightPoint(launch,landing,1,1),landing)<.001f,"flight ends at exact target");
                float impact=FishingMath.ReleaseTime+FishingMath.FlightSeconds(Vector3.Distance(launch,target));
                var timer=new FishingBiteTimer(20);
                timer.AdvanceCast(0,impact,impact);check(timer.ElapsedSeconds==0,"no bite time before actual impact");
                timer.AdvanceCast(impact,impact+.5f,impact);check(Mathf.Abs(timer.ElapsedSeconds-.5f)<.001f,"bite timing follows actual impact");
            }
            var csv=new List<string>{"scale,time,rx,ry,rz,lx,ly,lz,dx,dy,dz"};
            foreach(float scale in new[]{.78f,1f,1.35f})
            for(int frame=0;frame<=335;frame++)
            {
                float time=frame*.01f;
                var pose=FishingCastVisual.EvaluatePose(time);
                Vector3 direction=new Vector3(0,pose.RodUp,pose.RodForward).normalized;
                Vector3 desired=new Vector3(pose.HandSide,pose.HandUp,pose.HandForward)*scale;
                FishingCastGeometry.GripFrames(direction, Vector3.up, Vector3.right, out Quaternion rightFrame, out Quaternion leftFrame);
                Vector3 rightPalm=rightFrame*(Vector3.forward*.07f*scale);
                Vector3 leftPalm=leftFrame*(Vector3.forward*.07f*scale);
                desired-=rightPalm;
                Vector3 offset=-direction*.20f*scale+rightPalm-leftPalm;
                Vector3 rightShoulder=new Vector3(.18f,.05f,0)*scale,leftShoulder=new Vector3(-.18f,.05f,0)*scale;
                Vector3 right=FishingCastGeometry.ConstrainGrip(desired,offset,rightShoulder,leftShoulder,.57f*scale,.57f*scale);
                Vector3 left=right+offset;
                check(Vector3.Distance(right,rightShoulder)<=.5701f*scale && Vector3.Distance(left,leftShoulder)<=.5701f*scale,
                    "both arm targets remain reachable across full cast");
                check(Vector3.Distance((right+rightPalm)-(left+leftPalm), direction*.20f*scale)<.0001f,"palms maintain handle spacing");
                check(Mathf.Abs(Vector3.Dot(rightFrame*Vector3.forward,direction))<.0001f,"fingers wrap across rod");
                check(right.x>left.x,"wrists remain on their own side of handle");
                foreach (bool isRight in new[] { true, false })
                {
                    Vector3 shoulder = isRight ? rightShoulder : leftShoulder;
                    Vector3 wrist = isRight ? right : left;
                    Vector3 hint = (isRight ? Vector3.right : Vector3.left) - Vector3.up*.65f;
                    Vector3 elbow = FishingCastGeometry.ElbowPosition(shoulder,wrist,hint,.30f*scale,.30f*scale);
                    check(Mathf.Abs(Vector3.Distance(shoulder,elbow)-.30f*scale)<.0001f,"upper arm keeps bone length");
                    check(Mathf.Abs(Vector3.Distance(wrist,elbow)-.30f*scale)<.0001f,"forearm reaches wrist without stretching");
                    Vector3 bend = Vector3.ProjectOnPlane(elbow-shoulder,wrist-shoulder);
                    check(Vector3.Dot(bend,hint)>0f,"elbow bends toward outward hint");
                }
                csv.Add(string.Join(",",new[]{scale,time,right.x,right.y,right.z,left.x,left.y,left.z,direction.x,direction.y,direction.z}
                    .ConvertAllInvariant()));
            }
            string preview=Environment.GetEnvironmentVariable("FISHING_PREVIEW_OUTPUT");
            if(!string.IsNullOrEmpty(preview)) File.WriteAllLines(Path.Combine(preview,"cast-poses.csv"),csv);
            Debug.Log("[FishingMod.DirectCastChecks] PASS "+checks+"; force-water, exact targets, flight timing, full grip sweep.");
            return checks;
        }

        private static string[] ConvertAllInvariant(this float[] values)
            => Array.ConvertAll(values,v=>v.ToString("R",CultureInfo.InvariantCulture));
    }
}
#endif
