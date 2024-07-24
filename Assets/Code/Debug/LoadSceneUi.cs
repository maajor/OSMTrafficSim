using System.IO;
using System.Linq;
using Newtonsoft.Json;
using OSMTrafficSim;
using TMPro;
using Unity.Entities;
using UnityEngine;

public class LoadSceneUi : MonoBehaviour
{
    public TMP_Dropdown Dropdown;
    public RoadGraph RoadGraph;

    public SceneOption[] scenes =
    {
        new()
        {
            GeojsonPath = "Resources/Cities/Guangzhou/myd_guangzhou_2000.geojson",
            Name = "Guangzhou",
            Origin = new Vector2(113.37691f, 23.1387672f),
            TerrainModelPath = "Cities/Guangzhou/Venue"
        },
        new()
        {
            GeojsonPath = "Resources/Cities/RioDeJaneiro/myd_riodejaneiro_2000.geojson",
            Name = "RioDeJaneiro",
            Origin = new Vector2(-43.187042f, -22.9534571f),
            TerrainModelPath = "Cities/RioDeJaneiro/Venue_Terrain"
        },
        new()
        {
            GeojsonPath = "Resources/Cities/Seattle/myd_seattle_2000.geojson",
            Name = "Seattle",
            Origin = new Vector2(-122.33657f, 47.6221712f),
            TerrainModelPath = "Cities/Seattle/Venue_Terrain"
        },
        new()
        {
            GeojsonPath = "Resources/Cities/Vienna/myd_vienna_2000.geojson",
            Name = "Vienna",
            Origin = new Vector2(16.416546f, 48.19311432f),
            TerrainModelPath = "Cities/Vienna/Venue_Terrain"
        }
    };

    // Start is called before the first frame update
    private void Start()
    {
        Dropdown.options = scenes.Select(option => new TMP_Dropdown.OptionData(option.Name)).ToList();
        Load();
    }

    /// <summary>
    ///     For UI debug, binding it with UI Button to load from dropdown options
    /// </summary>
    public void Load()
    {
        SceneOption scene = scenes[Dropdown.value];
        LoadScene(scene);
    }

    public void LoadScene(SceneOption scene)
    {
        CleanupPreviousScene();

        LoadAndSetupVenue(scene.TerrainModelPath);

        ReloadGeojsonData(scene.GeojsonPath, scene.Origin);

        RestartSystems();
    }

    private void CleanupPreviousScene()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            transform.GetChild(i).gameObject.SetActive(false);
            Destroy(transform.GetChild(i).gameObject);
        }
    }

    private GeoJson LoadGeoJson(string geojsonPath)
    {
        string geoJsonAbsolutePath = Path.Join(Application.dataPath, geojsonPath);
        string text = File.ReadAllText(geoJsonAbsolutePath);
        return JsonConvert.DeserializeObject<GeoJson>(text);
    }

    private void LoadAndSetupVenue(string terrainModelPath)
    {
        GameObject terrain = Resources.Load<GameObject>(terrainModelPath);
        GameObject terrainGameObject = Instantiate(terrain, transform);
        terrainGameObject.transform.localScale = Vector3.one * 100.0f;
        terrainGameObject.transform.rotation = Quaternion.Euler(0, 180, 0);
        MeshCollider meshCollider = terrainGameObject.AddComponent<MeshCollider>();
        // setup collider for later raycast and get heights
        meshCollider.sharedMesh = terrainGameObject.GetComponent<MeshFilter>().sharedMesh;
    }

    private void ReloadGeojsonData(string geojsonPath, Vector2 origin)
    {
        GeoJson rawData = LoadGeoJson(geojsonPath);

        // I have no idea why we need a cos latitude scale here, but it match the venue
        RoadGraph.Init(rawData, origin, Mathf.Cos(Mathf.Deg2Rad * origin.y));
    }

    private void RestartSystems()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        EntityManager entityManager = world.EntityManager;
        entityManager.DestroyEntity(entityManager.UniversalQuery);

        TrafficLightSystem lightSystem = world.GetExistingSystemManaged<TrafficLightSystem>();
        lightSystem.Restart();
        VehicleSystem vehicleSystem = world.GetExistingSystemManaged<VehicleSystem>();
        vehicleSystem.Restart();
    }
}

public struct SceneOption
{
    public string Name;

    public Vector2 Origin;

    /* need to start without Resources and has no extension, since we will load with Resources.Load */
    public string TerrainModelPath;

    /* need to start with Resources and end with .geojson, since Unity does not recognize geojson as TextAsset, so we have to do File.ReadAllText */
    public string GeojsonPath;
}