using UnityEngine;
using Newtonsoft.Json;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace OSMTrafficSim
{
    public class OSMParser
    {

#if UNITY_EDITOR
        [MenuItem("Assets/ParseToRoadGraph")]
        private static void Parse()
        {
            string selectionPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            selectionPath = selectionPath.Replace("Assets", Application.dataPath);
            Parse(selectionPath);
        }
#endif

        public static void Parse(string filePath)
        {
            Debug.Log(filePath);
            if (!filePath.EndsWith("geojson")) return;
            string text = System.IO.File.ReadAllText(filePath);
            GeoJson rawData = JsonConvert.DeserializeObject<GeoJson>(text);
            RoadGraph.Instance.Init(rawData);
        }
    }

    [System.Serializable]
    public class GeoJson
    {
        public string type;
        public GeoFeature[] features;
    }
    [System.Serializable]
    public class GeoFeature
    {
        public string type;
        public GeoProperty properties;
        public GeoGeometry geometry;
        public string id;
    }
    [System.Serializable]
    public class GeoProperty
    {
        public string highway;
        public string name;
        public string lanes;
        public string oneway;
    }
    [System.Serializable]
    public class GeoGeometry
    {
        public string type;
        public float[,] coordinates;
    }
}

