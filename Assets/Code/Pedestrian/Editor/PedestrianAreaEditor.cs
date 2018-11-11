using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using Random = Unity.Mathematics.Random;
using OSMTrafficSim.BVH;
using UnityEditor;
using WalkablePatch = System.UInt64;

namespace OSMTrafficSim
{
    [CustomEditor(typeof(PedestrianArea))]
    public class PedestrianAreaEditor : Editor
    {
        public  override void OnInspectorGUI()
        {
            PedestrianArea myTarget = (PedestrianArea)target;

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("bDebug"), new GUIContent("Debug?"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Size"), new GUIContent("Size"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("PatchResolution"), new GUIContent("PatchResolution"));

            bool changed = EditorGUI.EndChangeCheck();
            if (GUILayout.Button("Build!", GUILayout.Height(100)))
            {
                myTarget.BuildWalkableArea();
            }
            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
            }

        }
    }
}
