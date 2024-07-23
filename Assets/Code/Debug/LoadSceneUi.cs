using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OSMTrafficSim;
using TMPro;
using UnityEngine;

public class LoadSceneUi : MonoBehaviour
{
    public SceneOption[] scenes = new SceneOption[4]
    {
        new SceneOption()
        {
            GeojsonPath = "Resources/Cities/Guangzhou/myd_guangzhou_2000.geojson",
            Name = "Guangzhou",
            Origin = new Vector2(113.37691f, 23.1387672f),
            TerrainModelPath = "Cities/Guangzhou/Venue",
        },
        new SceneOption(){
            GeojsonPath = "Resources/Cities/RioDeJaneiro/myd_riodejaneiro_2000.geojson",
            Name = "RioDeJaneiro",
            Origin = new Vector2(-43.187042f, -22.9534571f),
            TerrainModelPath = "Cities/RioDeJaneiro/Venue_Terrain",
        },
        new SceneOption(){
            GeojsonPath = "Resources/Cities/Seattle/myd_seattle_2000.geojson",
            Name = "Seattle",
            Origin = new Vector2(-122.33657f, 47.6221712f),
            TerrainModelPath = "Cities/Seattle/Venue_Terrain",
        },
        new SceneOption(){
            GeojsonPath = "Resources/Cities/Vienna/myd_vienna_2000.geojson",
            Name = "Vienna",
            Origin = new Vector2(16.416546f, 48.19311432f),
            TerrainModelPath = "Cities/Vienna/Venue_Terrain",
        }
    };

    public TMP_Dropdown Dropdown;
    public RoadGraph RoadGraph;
    // Start is called before the first frame update
    void Start()
    {
        Dropdown.options = scenes.Select(option => new TMP_Dropdown.OptionData(option.Name)).ToList();
    }

    public void Load()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject.Destroy(transform.GetChild(i).gameObject);
        }

        var scene = scenes[Dropdown.value];

        var geoJsonAbsolutePath = Path.Join(Application.dataPath, scene.GeojsonPath);
        string text = System.IO.File.ReadAllText(geoJsonAbsolutePath);
        GeoJson rawData = JsonConvert.DeserializeObject<GeoJson>(text);

        var terrain = Resources.Load<GameObject>(scene.TerrainModelPath);
        var terrainGameObject = Instantiate(terrain, transform);
        terrainGameObject.transform.localScale = Vector3.one * 100.0f;
        terrainGameObject.transform.rotation = Quaternion.Euler(0, 180, 0);
        var collider = terrainGameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = terrainGameObject.GetComponent<MeshFilter>().sharedMesh;
        // I have no idea why we need a cos latitude scale here, but it match the venue
        RoadGraph.Init(rawData, scene.Origin, Mathf.Cos(Mathf.Deg2Rad * scene.Origin.y));
    }


}

public struct SceneOption
{
    public string Name;
    public Vector2 Origin;
    public string TerrainModelPath;
    public string GeojsonPath;
}
