OSMTrafficSim
========

![gif](Recordings/gif_animation_001.gif)
![gif](Recordings/gif_animation_002.gif)
![gif](Recordings/gif_animation_003.gif)

**OSMTrafficSim** is project to simulate city traffic. 

Taking advantage of Unity's recent [Entity-Componet-System](https://github.com/Unity-Technologies/EntityComponentSystemSamples) technology, this project exploits the protential of multi-core processors, simulating 4k cars at 30fps on my laptop.

![profile](Recordings/profile.png)

The city's model is generated using OSM OpenStreetMap. I takes some tools form Houdini's [GameDevelopmentTool](https://github.com/sideeffects/GameDevelopmentToolset) to generate the city's static model. Please see ArtSource folder for project files.  
Road's abstract graph is also generated using OpenStreetMap data, an example data is places in Assets\OSM folder. You can right click on it in Unity's Project window and select **ParseToRoadGraph** to convert it to data in Unity.

See [my blog](http://ma-yidong.com/2018/11/04/unity-ecs-and-traffic-simulation/) for more detail

System requirements
-------------------

- Unity 2018.2 or later
