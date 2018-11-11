using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WalkableArea : MonoBehaviour
{
    
    public Vector3 Extent;

    [HideInInspector] public BoxCollider Col;

    void OnDrawGizmos()
    {
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, Extent);
        Gizmos.matrix = Matrix4x4.identity;
    }

    public void OnBuild()
    {
        Col = gameObject.AddComponent<BoxCollider>();
        Col.size = Extent;
        gameObject.layer = 31;
    }

    public Bounds GetBounds()
    {
        var bound = Col.bounds;
        return bound;
    }

    public void OnFinish()
    {
        Col = gameObject.GetComponent<BoxCollider>();
        if(Col != null) DestroyImmediate(Col);
    }
}
