#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FishingMod.Editor
{
    internal static class FishingNativeWaterChecks
    {
        [Serializable] private sealed class Fixture
        {
            public string path;
            public int layer;
            public bool box, renderer;
            public float[] center, size, matrix, localCenter, localSize, vertices, probe;
            public int[] triangles;
        }
        [Serializable] private sealed class Fixtures { public Fixture[] layers; public float[] probes; public double[] atlasProbes, rejectedAtlasProbes; public bool[] obliqueSafe; }
        private static Vector3 V(float[] a, int i = 0) => new Vector3(a[i], a[i+1], a[i+2]);

        internal static int Run()
        {
            var data=JsonUtility.FromJson<Fixtures>(File.ReadAllText(Path.Combine(Application.dataPath,
                "Mods/FishingMod/Editor/NativeWaterFixtures.json")));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var nodes=new Dictionary<string,GameObject>();
            var meshes=new List<Mesh>();
            var colliders=new List<Collider>();
            int count=0;
            Action<bool,string> check=(ok,reason)=> { if(!ok) throw new InvalidOperationException(reason); count++; };
            try
            {
                foreach(var f in data.layers)
                {
                    string path=""; Transform parent=null;
                    foreach(string name in f.path.Split('/'))
                    {
                        path=path.Length==0?name:path+"/"+name;
                        if(!nodes.TryGetValue(path,out var node))
                        { node=new GameObject(name); node.transform.SetParent(parent); nodes.Add(path,node); }
                        parent=node.transform;
                    }
                    var obj=parent.gameObject; obj.layer=f.layer;
                    if(f.renderer) obj.AddComponent<MeshRenderer>().enabled=false;
                    Collider collider;
                    if(f.box)
                    {
                        var m=new Matrix4x4();
                        for(int r=0;r<4;r++) for(int c=0;c<4;c++) m[r,c]=f.matrix[r*4+c];
                        parent.position=m.GetColumn(3); parent.rotation=m.rotation; parent.localScale=m.lossyScale;
                        var box=obj.AddComponent<BoxCollider>();box.center=V(f.localCenter);box.size=V(f.localSize);collider=box;
                    }
                    else
                    {
                        var mesh=new Mesh();var vertices=new Vector3[f.vertices.Length/3];
                        for(int i=0;i<vertices.Length;i++) vertices[i]=V(f.vertices,i*3);
                        mesh.vertices=vertices;mesh.triangles=f.triangles;mesh.RecalculateBounds();meshes.Add(mesh);
                        var mc=obj.AddComponent<MeshCollider>();mc.sharedMesh=mesh;collider=mc;
                    }
                    colliders.Add(collider);
                }
                Physics.SyncTransforms();
                foreach(var collider in colliders)
                {
                    check(FishingNativeWaterOccluder.IsVerifiedSupport(collider),"native support signature mismatch: "+collider.name);
                    string name=collider.name;collider.name="Unrelated support";
                    check(!FishingNativeWaterOccluder.IsVerifiedSupport(collider),"unrelated geometry exempted");collider.name=name;
                }
                var detector=new FishingWaterDetector();
                var atlas=StaticWater.GameWaterAtlas.Create();
                for(int i=0;i<data.atlasProbes.Length;i+=2)
                    check(atlas.TryGetCandidate(data.atlasProbes[i],data.atlasProbes[i+1],out _),
                        "each atlas sub-polygon keeps its interior candidate");
                for(int i=0;i<data.rejectedAtlasProbes.Length;i+=2)
                {
                    double x=data.rejectedAtlasProbes[i], z=data.rejectedAtlasProbes[i+1];
                    check(!atlas.TryGetCandidate(x,z,out _), "wooden pontoon footprint excluded");
                    var ray=new Ray(new Vector3((float)x,35f,(float)z),Vector3.down);
                    check(!detector.TryGetWaterPoint(ray,null,out _), "wooden pontoon rejects cast without a physics collider");
                }
                for(int i=0;i<data.probes.Length;i+=3)
                {
                    Vector3 target=V(data.probes,i);
                    foreach(var offset in new[]{new Vector3(0,35,0),new Vector3(12,35,-16)})
                    {
                        if(offset.x!=0f && !data.obliqueSafe[i/3]) continue;
                        Ray ray=new Ray(target+offset,-offset);
                        check(detector.TryGetWaterPoint(ray,null,out var water) && Vector3.Distance(water,target)<.02f,
                            "atlas native support regression at "+target+": "+detector.LastFailureReason+" / "+detector.LastBlockingCollider);
                    }
                }
                // Real rendererless docks must still block even when on Ground.
                var real=new GameObject("Real dock");real.layer=LayerMask.NameToLayer("Ground");
                nodes.Add("real",real);real.transform.position=new Vector3(-1946,0,-945.7f);
                real.AddComponent<BoxCollider>().size=new Vector3(8,1,8);Physics.SyncTransforms();
                Ray dockRay=new Ray(real.transform.position+Vector3.up*30,Vector3.down);
                check(!detector.TryGetWaterPoint(dockRay,null,out _),"real invisible dock must block");
                real.layer=LayerMask.NameToLayer("Vehicles");
                check(!detector.TryGetWaterPoint(dockRay,null,out _),"boat must block");
                foreach(var collider in colliders)
                {
                    var renderer=collider.GetComponent<Renderer>();
                    if(renderer==null) renderer=collider.gameObject.AddComponent<MeshRenderer>();
                    renderer.enabled=true;
                    check(!FishingNativeWaterOccluder.IsVerifiedSupport(collider),"visible geometry must not be exempted");
                }
                Debug.Log("[FishingMod.NativeWaterChecks] PASS "+count+" checks; "+data.layers.Length+
                    " native support geometries, "+data.probes.Length/3+" atlas targets, vertical and oblique rays.");
                return count;
            }
            finally
            {
                foreach(var node in nodes.Values) if(node!=null && node.transform.parent==null) UnityEngine.Object.DestroyImmediate(node);
                foreach(var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
#endif
