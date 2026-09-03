#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Helpers;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace FishingMod.Editor
{
    internal static class FishingModChecks
    {
        private static int _checks;

        internal static int Run()
        {
            _checks = 0;
            CheckRodRenderingConfiguration();
            CheckGameplayConfiguration();
            CheckHappinessBehavior();
            CheckWaterDetection();
            CheckShoreResolution();
            Debug.Log("[FishingMod.Checks] PASS " + _checks + "/" + _checks + ".");
            return _checks;
        }

        private static void CheckGameplayConfiguration()
        {
            Check(FishingHappinessService.HasNativeContract, "native happiness modifier contract available");
            Check(FishingHappinessService.FishingActivityAmount == 10
                && FishingHappinessService.FishingActivityHours == 48,
                "fishing activity grants +10 happiness for 48 hours");
            Check(FishingHappinessService.CatchBonusHours == 72,
                "caught fish happiness lasts 72 hours");
            Check(FishingFishCatalog.All.Count == 6, "six weighted fish available");
            Check(Mathf.Approximately(FishingQteSession.FailureMeters, FishingQteSession.SuccessMeters * 0.5f),
                "QTE mistake loses half a successful pull");
        }

        private static void CheckHappinessBehavior()
        {
            FieldInfo modifiersField = typeof(HappinessHelper).GetField(
                "Modifiers",
                BindingFlags.Static | BindingFlags.NonPublic);
            object originalModifiers = modifiersField.GetValue(null);
            GameInstance originalSave = SaveGameManager.Current;
            Dictionary<string, HappinessModifier> testModifiers = new Dictionary<string, HappinessModifier>();
            modifiersField.SetValue(null, testModifiers);
            SaveGameManager.Current = new GameInstance { gameVariables = new GameVariables() };

            try
            {
                FishingHappinessService service = new FishingHappinessService();
                service.Initialize();
                Check(service.ApplyFishingActivity(), "fishing activity modifier applied");
                Check(SaveGameManager.Current.happinessModifiers.Count == 1
                    && SaveGameManager.Current.happinessModifiers[0].hoursLeft == 48,
                    "fishing activity stores one 48-hour modifier");
                service.ApplyFishingActivity();
                Check(SaveGameManager.Current.happinessModifiers.Count == 1,
                    "repeated fishing does not stack activity modifiers");
                Check(Mathf.Approximately(SaveGameManager.Current.Happiness, 10f),
                    "native happiness includes fishing activity amount");

                FishingFish roach = FishingFishCatalog.All[0];
                FishingFish sturgeon = FishingFishCatalog.All[FishingFishCatalog.All.Count - 1];
                FishingCatchBonusResult bestResult = service.ApplyCatch(sturgeon);
                Check(bestResult.CountedFish == sturgeon && CountCatchModifiers() == 1,
                    "best catch creates exactly one catch modifier");
                Check(Mathf.Approximately(SaveGameManager.Current.Happiness, 24f),
                    "native happiness combines activity and best catch");

                HappinessModifierData sturgeonData = FindCatchModifier(sturgeon);
                sturgeonData.hoursLeft = 19;
                FishingCatchBonusResult worseResult = service.ApplyCatch(roach);
                Check(worseResult.CountedFish == sturgeon && sturgeonData.hoursLeft == 19,
                    "worse catch neither replaces nor refreshes best bonus");
                Check(CountCatchModifiers() == 1, "worse catch cannot stack a second bonus");

                service.ApplyCatch(sturgeon);
                Check(FindCatchModifier(sturgeon).hoursLeft == 72,
                    "same best catch refreshes bonus to 72 hours");
                Check(CountCatchModifiers() == 1, "refreshed catch bonus remains unique");
            }
            finally
            {
                foreach (HappinessModifier modifier in testModifiers.Values)
                    if (modifier != null) UnityEngine.Object.DestroyImmediate(modifier);
                SaveGameManager.Current = originalSave;
                modifiersField.SetValue(null, originalModifiers);
            }
        }

        private static int CountCatchModifiers()
        {
            int count = 0;
            List<HappinessModifierData> modifiers = SaveGameManager.Current.happinessModifiers;
            for (int i = 0; i < modifiers.Count; i++)
                if (modifiers[i] != null && FishingFishCatalog.FindByModifierType(modifiers[i].type) != null)
                    count++;
            return count;
        }

        private static HappinessModifierData FindCatchModifier(FishingFish fish)
        {
            List<HappinessModifierData> modifiers = SaveGameManager.Current.happinessModifiers;
            for (int i = 0; i < modifiers.Count; i++)
                if (modifiers[i] != null && modifiers[i].type == fish.HappinessModifierType)
                    return modifiers[i];
            throw new InvalidOperationException("Expected catch modifier was not found: " + fish.Id);
        }

        private static void CheckRodRenderingConfiguration()
        {
            GameObject rodObject = new GameObject("Fishing Test Rod");
            LineRenderer rod = rodObject.AddComponent<LineRenderer>();
            FishingCastVisual.ConfigureRodWidth(rod, 1.35f);
            Keyframe[] keys = rod.widthCurve.keys;
            Check(Mathf.Approximately(rod.widthMultiplier, 1f), "rod width multiplier remains absolute");
            Check(keys.Length == 3, "rod taper has three controlled widths");
            Check(keys[0].value <= 0.034f && keys[0].value > keys[keys.Length - 1].value,
                "rod base stays slender");
            Check(keys[keys.Length - 1].value <= 0.009f, "rod tip stays slender");

            Shader shader = Shader.Find("HDRP/Unlit");
            Check(shader != null, "HDRP unlit shader available");
            Material material = new Material(shader);
            Color expected = new Color(0.08f, 0.13f, 0.08f, 1f);
            FishingCastVisual.ApplyMaterialColor(material, expected);
            Check(material.HasProperty("_UnlitColor")
                && Approximately(material.GetColor("_UnlitColor"), expected), "HDRP rod color applied");

            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(rodObject);
        }

        private static void CheckWaterDetection()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject waterRoot = new GameObject("Ocean Water");
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Cube);
            water.name = "Water Tile A";
            water.transform.position = new Vector3(0f, 0f, 10f);
            water.transform.localScale = new Vector3(12f, 0.1f, 12f);
            water.transform.SetParent(waterRoot.transform, true);
            GameObject secondWaterTile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            secondWaterTile.name = "Water Tile B";
            secondWaterTile.transform.position = new Vector3(12f, 0f, 10f);
            secondWaterTile.transform.localScale = new Vector3(12f, 0.1f, 12f);
            secondWaterTile.transform.SetParent(waterRoot.transform, true);
            Physics.SyncTransforms();

            FishingWaterDetector detector = new FishingWaterDetector();
            detector.ForceRefresh();
            Check(detector.SurfaceCount == 1, "root water tiles share one height group");
            Check(detector.IndexedTileCount == 2, "root water tiles keep local bounds");
            Ray directRay = new Ray(new Vector3(0f, 5f, 0f), new Vector3(0f, -0.5f, 1f).normalized);
            Check(detector.TryGetWaterPoint(directRay, null, out Vector3 directPoint), "direct water collider hit");
            Check(Mathf.Abs(directPoint.y) < 0.2f, "direct water height");
            Check(detector.CacheBuildCount == 1, "water cache reused on click");

            UnityEngine.Object.DestroyImmediate(waterRoot);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Material tiledWaterMaterial = new Material(Shader.Find("HDRP/Unlit")) { name = "Harbor Water" };
            GameObject plane = CreateWaterPlane("Surface Tile Low", new Vector3(0f, 0f, 0f), tiledWaterMaterial);
            GameObject highLeft = CreateWaterPlane("Surface Tile High Left", new Vector3(-40f, 10f, 0f), tiledWaterMaterial);
            GameObject highRight = CreateWaterPlane("Surface Tile High Right", new Vector3(40f, 10f, 0f), tiledWaterMaterial);
            Physics.SyncTransforms();

            detector.ForceRefresh();
            Check(detector.SurfaceCount == 2, "water tiles indexed by elevation");
            Check(detector.IndexedTileCount == 3, "material water tiles keep local bounds");
            Ray fallbackRay = new Ray(new Vector3(0f, 20f, 0f), Vector3.down);
            Check(detector.TryGetWaterPoint(fallbackRay, null, out Vector3 fallbackPoint), "renderer plane fallback");
            Check(Mathf.Abs(fallbackPoint.y) < 0.05f, "distant higher tiles do not create false water");

            GameObject blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "Concrete Quay";
            blocker.transform.position = new Vector3(0f, 15f, 0f);
            blocker.transform.localScale = new Vector3(8f, 1.5f, 8f);
            Physics.SyncTransforms();
            Check(!detector.TryGetWaterPoint(fallbackRay, null, out _), "occluded water rejected");

            UnityEngine.Object.DestroyImmediate(blocker);
            UnityEngine.Object.DestroyImmediate(plane);
            UnityEngine.Object.DestroyImmediate(highLeft);
            UnityEngine.Object.DestroyImmediate(highRight);
            UnityEngine.Object.DestroyImmediate(tiledWaterMaterial);
        }

        private static GameObject CreateWaterPlane(string name, Vector3 position, Material material)
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = name;
            plane.transform.position = position;
            plane.transform.localScale = new Vector3(2f, 1f, 2f);
            plane.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(plane.GetComponent<Collider>());
            return plane;
        }

        private static void CheckShoreResolution()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Test Shore";
            ground.transform.position = new Vector3(0f, 0f, 10f);
            ground.transform.localScale = new Vector3(1f, 1f, 2f);

            Mesh mesh = ground.GetComponent<MeshFilter>().sharedMesh;
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
            List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    sourceObject = mesh,
                    transform = ground.transform.localToWorldMatrix,
                    area = 0
                }
            };
            Bounds bounds = new Bounds(new Vector3(0f, 0f, 10f), new Vector3(30f, 10f, 40f));
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            Check(data != null, "test NavMesh built");
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);

            NavMeshQueryFilter filter = new NavMeshQueryFilter
            {
                agentTypeID = settings.agentTypeID,
                areaMask = NavMesh.AllAreas
            };
            Check(NavMesh.SamplePosition(new Vector3(0f, 0f, 2f), out NavMeshHit startHit, 2f, filter),
                "test start sampled on NavMesh");

            FishingShoreResolver resolver = new FishingShoreResolver();
            Vector3 waterPoint = new Vector3(0f, 0f, 25f);
            Check(resolver.TryFindClosestReachable(settings.agentTypeID, NavMesh.AllAreas, startHit.position, waterPoint,
                out Vector3 shore, out float routeLength), "reachable shoreline resolved");
            Check(shore.z > 17f && shore.z < 21f, "shoreline remains near navigable edge");
            Check(routeLength > 10f && routeLength < 25f, "complete route length measured");

            instance.Remove();
            UnityEngine.Object.DestroyImmediate(data);
            UnityEngine.Object.DestroyImmediate(ground);
        }

        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            _checks++;
            Debug.Log("[FishingMod.Checks] PASS " + description);
        }

        private static bool Approximately(Color left, Color right)
        {
            return Mathf.Abs(left.r - right.r) < 0.001f
                && Mathf.Abs(left.g - right.g) < 0.001f
                && Mathf.Abs(left.b - right.b) < 0.001f
                && Mathf.Abs(left.a - right.a) < 0.001f;
        }
    }
}
#endif
