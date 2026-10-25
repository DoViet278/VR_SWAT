using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SetupNamesByMeshes
{
    [MenuItem("GameObject/Utils/Setup names by meshes names")]
    public static void SetupNames()
    {
        GameObject[] targets = Selection.gameObjects;
        foreach(GameObject t in targets)
        {
            MeshFilter m = t.GetComponent<MeshFilter>();
            t.name = m.sharedMesh.name;
        }
    }
}
