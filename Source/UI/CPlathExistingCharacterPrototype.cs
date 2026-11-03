using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UBOAT.Game.Scene.Camera;
using UnityEngine;
using UnityEngine.Rendering;

namespace UBOAT.Mods.RadioNavigation
{
    /// <summary>
    /// Prototype: use the character's own first-person hand from the FPP Body
    /// mesh, not an authored asset. F9 extracts the hand/forearm triangles from
    /// the live FPP Body mesh, attaches them to the sextant and hides the rest of
    /// the FPP body. Comma/period and page keys tune the grip.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class CPlathExistingCharacterPrototype : MonoBehaviour
    {
        private const string BundleName = "cplath_sextant";
        private const string SextantPath = "assets/sextantvalidation/prefabs/c_plath_uboat.prefab";
        private object bundle;
        private GameObject holder;
        private GameObject sextant;
        private GameObject rightHand;
        private GameObject leftHand;
        private SkinnedMeshRenderer fppBody;
        private bool fppBodyWasEnabled;
        private bool held;
        private float distance = 0.42f;
        private float height = -0.02f;
        private float lateral;
        private float yaw;
        private float pitch;
        private float roll;
        private Vector3 rightOffset = new Vector3(0.00f, 0.00f, 0.00f);
        private Vector3 leftOffset = new Vector3(-0.12f, -0.08f, 0.02f);
        private Vector3 rightEuler = Vector3.zero;
        private Vector3 leftEuler = Vector3.zero;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (held) Stow();
                else Take();
                return;
            }
            if (!held) return;
            if (Input.GetKeyDown(KeyCode.Comma)) yaw -= 90f;
            if (Input.GetKeyDown(KeyCode.Period)) yaw += 90f;
            if (Input.GetKeyDown(KeyCode.Semicolon)) distance += 0.02f;
            if (Input.GetKeyDown(KeyCode.Quote)) distance -= 0.02f;
            if (Input.GetKeyDown(KeyCode.PageUp)) height += 0.01f;
            if (Input.GetKeyDown(KeyCode.PageDown)) height -= 0.01f;
            if (Input.GetKeyDown(KeyCode.Insert)) pitch += 2f;
            if (Input.GetKeyDown(KeyCode.Delete)) pitch -= 2f;
            if (Input.GetKeyDown(KeyCode.Home)) roll -= 2f;
            if (Input.GetKeyDown(KeyCode.End)) roll += 2f;
        }

        private void Take()
        {
            GameObject prefab = LoadPrefab(SextantPath);
            if (prefab == null) return;

            object main = MainCamera.Instance;
            object fpp = Read(main, "FPPController");
            fppBody = Read(fpp, "FPPBody") as SkinnedMeshRenderer;
            if (fppBody == null || fppBody.sharedMesh == null)
            {
                Debug.LogWarning("[RadioNav][CPlathHands] no FPP body mesh");
                return;
            }

            holder = new GameObject("CPlath_ExistingHands_Holder");
            sextant = Instantiate(prefab);
            sextant.name = "CPlath_ExistingHands_Sextant";
            sextant.transform.SetParent(holder.transform, false);

            rightHand = BuildHand("CPlath_ExistingHands_Right", fppBody, false, out Vector3 rightCentroid);
            leftHand = BuildHand("CPlath_ExistingHands_Left", fppBody, false, out Vector3 leftCentroid);
            if (rightHand == null || leftHand == null)
            {
                Stow();
                return;
            }
            rightHand.transform.SetParent(holder.transform, false);
            leftHand.transform.SetParent(holder.transform, false);
            rightHand.transform.localPosition = new Vector3(0.00f, 0.02f, -0.06f) - rightCentroid + rightOffset;
            rightHand.transform.localRotation = Quaternion.Euler(rightEuler);
            leftHand.transform.localPosition = new Vector3(-0.08f, -0.10f, -0.02f) - leftCentroid + leftOffset;
            leftHand.transform.localRotation = Quaternion.Euler(leftEuler);

            fppBodyWasEnabled = fppBody.enabled;
            fppBody.enabled = false;
            int layer = ResolveLayer();
            SetLayer(holder, layer);
            held = true;
            Debug.Log(string.Format(
                "[RadioNav][CPlathHands] extracted existing hands from {0} ({1} verts) layer={2}",
                fppBody.sharedMesh.name, fppBody.sharedMesh.vertexCount, layer));
        }

        private static GameObject BuildHand(string name, SkinnedMeshRenderer source, bool right, out Vector3 centroid)
        {
            centroid = Vector3.zero;
            Mesh mesh = source.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;
            if (triangles.Length == 0) return null;

            var map = new int[vertices.Length];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            var newVertices = new List<Vector3>();
            var newNormals = new List<Vector3>();
            var newUv = new List<Vector2>();
            var newTriangles = new List<int>();
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Vector3 centre = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                // The FPP body stores the right arm+hand on +X and the detached
                // left hand on -X; the centre strip is the body and is skipped.
                if (right ? centre.x < 0.055f : centre.x > -0.055f) continue;
                Add(a, vertices, normals, uv, map, newVertices, newNormals, newUv, newTriangles);
                Add(b, vertices, normals, uv, map, newVertices, newNormals, newUv, newTriangles);
                Add(c, vertices, normals, uv, map, newVertices, newNormals, newUv, newTriangles);
            }
            if (newTriangles.Count == 0) return null;
            foreach (Vector3 vertex in newVertices) centroid += vertex;
            centroid /= newVertices.Count;
            var handMesh = new Mesh();
            handMesh.name = name + "_Mesh";
            handMesh.SetVertices(newVertices);
            handMesh.SetNormals(newNormals);
            handMesh.SetUVs(0, newUv);
            handMesh.SetTriangles(newTriangles, 0);
            handMesh.RecalculateBounds();

            GameObject go = new GameObject(name);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = handMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = source.sharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        private static void Add(int vertex, Vector3[] vertices, Vector3[] normals, Vector2[] uv,
            int[] map, List<Vector3> outVertices, List<Vector3> outNormals, List<Vector2> outUv,
            List<int> outTriangles)
        {
            if (map[vertex] < 0)
            {
                map[vertex] = outVertices.Count;
                outVertices.Add(vertices[vertex]);
                outNormals.Add(normals.Length > vertex ? normals[vertex] : Vector3.up);
                outUv.Add(uv.Length > vertex ? uv[vertex] : Vector2.zero);
            }
            outTriangles.Add(map[vertex]);
        }

        private void LateUpdate()
        {
            if (!held || holder == null) return;
            Camera view = ResolveViewCamera();
            if (view == null) return;
            Quaternion rotation = view.transform.rotation * Quaternion.Euler(pitch, yaw, roll);
            holder.transform.SetPositionAndRotation(
                view.transform.position
                    + view.transform.forward * distance
                    + view.transform.up * height
                    + view.transform.right * lateral,
                rotation);
        }

        private void Stow()
        {
            held = false;
            Debug.Log(string.Format(
                "[RadioNav][CPlathHands] pose distance={0:F2} height={1:F2} lateral={2:F2} yaw={3:F0} pitch={4:F0} roll={5:F0}",
                distance, height, lateral, yaw, pitch, roll));
            if (fppBody != null) fppBody.enabled = fppBodyWasEnabled;
            fppBody = null;
            if (holder != null) Destroy(holder);
            holder = null;
            sextant = null;
            rightHand = null;
            leftHand = null;
        }

        private static int ResolveLayer()
        {
            object main = MainCamera.Instance;
            return main != null && Read(main, "IsInsideBoat") is bool inside && inside
                ? LayerMask.NameToLayer("Interior")
                : LayerMask.NameToLayer("Default");
        }

        private static Camera ResolveViewCamera()
        {
            object main = MainCamera.Instance;
            if (main == null) return Camera.main;
            object interior = Read(main, "InteriorCamera");
            Camera camera = interior != null ? Read(interior, "Camera") as Camera : null;
            if (camera != null && camera.isActiveAndEnabled) return camera;
            object fpp = Read(main, "FPPController");
            camera = fpp != null ? Read(fpp, "localCamera") as Camera : null;
            return camera != null ? camera : Camera.main;
        }

        private static object Read(object instance, string name)
        {
            if (instance == null) return null;
            Type type = instance.GetType();
            while (type != null)
            {
                PropertyInfo property = type.GetProperty(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property != null && property.CanRead) return property.GetValue(instance, null);
                FieldInfo field = type.GetField(name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null) return field.GetValue(instance);
                type = type.BaseType;
            }
            return null;
        }

        private static void SetLayer(GameObject root, int layer)
        {
            if (layer < 0) return;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        private GameObject LoadPrefab(string assetPath)
        {
            try
            {
                Type type = GameTypeCache.Resolve("UnityEngine.AssetBundle", "UnityEngine.AssetBundleModule");
                if (type == null) throw new InvalidOperationException("AssetBundle API unavailable.");
                if (bundle == null) bundle = FindLoaded(type);
                if (bundle == null)
                {
                    string path = Path.Combine(Application.persistentDataPath,
                        "Mods", "RadioNavigation", "CPlath", BundleName);
                    if (!File.Exists(path)) throw new FileNotFoundException("C. Plath bundle missing", path);
                    bundle = Method(type, "LoadFromFile", true, 1).Invoke(null, new object[] { path });
                }
                if (bundle == null) throw new InvalidOperationException("C. Plath bundle failed to load.");
                GameObject prefab = Method(type, "LoadAsset", false, 1).Invoke(bundle,
                    new object[] { assetPath }) as GameObject;
                if (prefab == null) throw new InvalidOperationException("Missing asset: " + assetPath);
                return prefab;
            }
            catch (Exception e)
            {
                Debug.LogError("[RadioNav][CPlathHands] load failed: " + e);
                return null;
            }
        }

        private static MethodInfo Method(Type type, string name, bool isStatic, int count)
        {
            BindingFlags flags = BindingFlags.Public | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            foreach (MethodInfo method in type.GetMethods(flags))
                if (method.Name == name && !method.IsGenericMethod && method.GetParameters().Length == count)
                    return method;
            throw new MissingMethodException(type.FullName, name);
        }

        private static object FindLoaded(Type type)
        {
            var bundles = Method(type, "GetAllLoadedAssetBundles", true, 0).Invoke(null, null) as IEnumerable;
            if (bundles == null) return null;
            foreach (object candidate in bundles)
            {
                var obj = candidate as UnityEngine.Object;
                if (obj != null && obj.name == BundleName) return candidate;
            }
            return null;
        }

        private void OnDisable()
        {
            if (held) Stow();
        }
    }
}
