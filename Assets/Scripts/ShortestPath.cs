using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using UnityEngine.UI;
using System.Threading;
using System.Reflection;

public class Crs
{
    public string type { get; set; }
    public Properties properties { get; set; }
}

public class Feature
{
    public string type { get; set; }
    public Properties properties { get; set; }
    public Geometry geometry { get; set; }
}

public class Geometry
{
    public string type { get; set; }
    public List<List<List<float>>> coordinates { get; set; }
}

public class Properties
{
    public string name { get; set; }
    public string osm_id { get; set; }
    public string highway { get; set; }
    public object waterway { get; set; }
    public object aerialway { get; set; }
    public object barrier { get; set; }
    public object man_made { get; set; }
    public int z_order { get; set; }
    public string other_tags { get; set; }
    public float z_zmean { get; set; }
}

public class Root
{
    public string type { get; set; }
    public string name { get; set; }
    public Crs crs { get; set; }
    public List<Feature> features { get; set; }
}

public class DuplicateKeyComparer<TKey>
                :
             IComparer<TKey> where TKey : IComparable
{
    #region IComparer<TKey> Members
    public int Compare(TKey x, TKey y)
    {
        int result = x.CompareTo(y);

        if (result == 0)
            return 1; // Handle equality as being greater. Note: this will break Remove(key) or
        else          // IndexOfKey(key) since the comparer never returns 0 to signal key equality
            return result;
    }
    #endregion
}

public class Node
{
    public float x;
    public float y;
    public float z;
    public Node(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
    public override int GetHashCode()
    {
        return Convert.ToInt32(this.x * 1000);
    }
    public override bool Equals(object obj)
    {
        return Equals(obj as Node);
    }
    public bool Equals(Node obj)
    {
        return obj != null && obj.x == this.x && obj.y == this.y && obj.z == this.z;
    }
}
public class Graph
{
    Dictionary<Node, LinkedList<Node>> graph;

    public Graph()
    {
        graph = new Dictionary<Node, LinkedList<Node>>();
    }

    // doublic directed
    public void addEdge(Node n1, Node n2)
    {
        if (n1 == null) { return; }
        if (!graph.ContainsKey(n1))
        {
            graph.Add(n1, new LinkedList<Node>());
        }
        graph[n1].AddLast(n2);

        if (!graph.ContainsKey(n2))
        {
            graph.Add(n2, new LinkedList<Node>());
        }
        graph[n2].AddLast(n1);
    }

    public void addOneWayEdge(Node n1, Node n2)
    {
        if (n1 == null) { return; }
        if (!graph.ContainsKey(n1))
        {
            graph.Add(n1, new LinkedList<Node>());
        }
        graph[n1].AddLast(n2);
        // n2 add to graph, but not add edge
        if (!graph.ContainsKey(n2))
        {
            graph.Add(n2, new LinkedList<Node>());
        }
    }
    public void printGraph()
    {
        foreach (var u in graph)
        {
            Console.Write(u.Key.x + " " + u.Key.y + " " + u.Key.z);
            foreach (var v in u.Value)
            {
                Console.Write(" -> " + v.x + " " + v.y + " " + v.z);
            }
            Console.WriteLine();
        }
    }

    public float weight(Node n1, Node n2)
    {
        return (float)(Math.Sqrt(Math.Pow(n2.x - n1.x, 2) + Math.Pow(n2.y - n1.y, 2) + Math.Pow(n2.z - n1.z, 2)));
    }

    public Dictionary<Node, Node> dijkstra(GameObject startIns, GameObject endIns)
    {
        var stPos = startIns.transform.position;
        Node start = new Node(stPos.x, stPos.y, stPos.z);
        var endPos = endIns.transform.position;
        Node end = new Node(endPos.x, endPos.y, endPos.z);
        if (!graph.ContainsKey(start) && !graph.ContainsKey(end))
        {
            Debug.Log("the Node start or end isn't exist");
            return null;
        }

        // init
        int len = graph.Count;
        // <postNode, preNode>
        Dictionary<Node, Node> preNodeDict = new Dictionary<Node, Node>(len);
        Dictionary<Node, float> distTo = new Dictionary<Node, float>(len);
        foreach (var node in graph)
        {
            preNodeDict.Add(node.Key, null);
            distTo.Add(node.Key, float.MaxValue);
        }
        distTo[start] = 0;

        SortedList<float, Node> sl = new SortedList<float, Node>(new DuplicateKeyComparer<float>());
        sl.Add(0, start);

        // run
        // if list.Count == 0, then terminate the algo 
        while (sl.Count != 0)
        {
            // get first ele
            var curPair = sl.First();
            Node curNode = curPair.Value;
            float curDist = curPair.Key;
            // delete first ele
            sl.RemoveAt(0);
            if (curNode.Equals(end))
            {
                Debug.Log("distance: " + curDist);
                return preNodeDict;
            }
            if (curDist > distTo[curNode])
            {
                continue;
            }
            foreach (Node adjNode in graph[curNode])
            {
                float distToNextNode = distTo[curNode] + weight(curNode, adjNode);
                if (distTo[adjNode] > distToNextNode)
                {
                    preNodeDict[adjNode] = curNode;
                    distTo[adjNode] = distToNextNode;
                    sl.Add(distToNextNode, adjNode);
                }
            }
        }
        // start can't go to end, then return -1
        return null;
    }

    public void JsonToGraph(string path, List<List<Vector3>> roadArr, Terrain waterLevel, Terrain dem)
    {
        using (StreamReader r = new StreamReader(path))
        {
            // read json
            string json = r.ReadToEnd();
            Root root = JsonConvert.DeserializeObject<Root>(json);

            // highway attribute filter
            HashSet<string> PropertyForRemove = new HashSet<string>()
            {
                "construction","cycleway","footway","path","pedestrian","proposed","steps"
            };
            HashSet<string> PropertyForAdd = new HashSet<string>()
            {
                "motorway", "trunk"
            };
            TerrainData waterLevelData = waterLevel.terrainData;

            float xPos = waterLevel.GetPosition()[0];
            float yPos = waterLevel.GetPosition()[2];
            float xMulti = waterLevelData.size.x / waterLevelData.heightmapResolution;
            float yMulti = waterLevelData.size.z / waterLevelData.heightmapResolution;

            TerrainData demData = dem.terrainData;
            float demxPos = waterLevel.GetPosition()[0];
            float demyPos = waterLevel.GetPosition()[2];
            float demxMulti = waterLevelData.size.x / waterLevelData.heightmapResolution;
            float demyMulti = waterLevelData.size.z / waterLevelData.heightmapResolution;

            foreach (var feature in root.features)
            {
                ////
                List<Vector3> road = new List<Vector3>();
                // use to save last time's Node
                Node preNode = null;
                // filter
                string highway = feature.properties.highway;
                string tag = feature.properties.other_tags;
                bool tagContainBridge = false;
                bool tagContainOneway = false;
                if (tag != null && tag.Contains("bridge")) { tagContainBridge = true; }
                if (tag != null && tag.Contains("oneway")) { tagContainOneway = true; }

                // not road
                if (highway == null) { continue; }
                // not road that car can drive
                else if (PropertyForRemove.Contains(highway)) { continue; }
                // road that must can drive
                else if (PropertyForAdd.Contains(highway) || tagContainBridge == true)
                {
                    foreach (var coor in feature.geometry.coordinates[0])
                    {
                        road.Add(new Vector3(coor[0], coor[2], coor[1]));
                        if (tagContainOneway == true)
                        {
                            this.addOneWayEdge(preNode, new Node(coor[0], coor[2], coor[1]));
                        }
                        else if (tagContainOneway == false)
                        {
                            this.addEdge(preNode, new Node(coor[0], coor[2], coor[1]));
                        }
                        preNode = new Node(coor[0], coor[2], coor[1]);
                    }
                    ////
                    roadArr.Add(road);
                    continue;
                }
                // road that need to determinate
                else
                {
                    foreach (var coor in feature.geometry.coordinates[0])
                    {
                        /*if (coor[2] <
                            waterLevelData.GetHeight(   (int)((coor[0] - xPos) / xMulti),
                                                        (int)((coor[1] - yPos) / yMulti)   ))
                        {
                            tmp = null;
                            roadArr.Add(road);
                            road = new List<Vector3>();
                            continue; 
                        }*/

                        // road node higher than water level
                        if (coor[2] >
                            waterLevelData.GetHeight((int)((coor[0] - xPos) / xMulti),
                                                        (int)((coor[1] - yPos) / yMulti)))
                        {
                            // compute the point between two node
                            if (preNode != null)
                            {
                                float part = weight(preNode, new Node(coor[0], coor[2], coor[1]));
                                float part_x = (preNode.x - coor[0]) / part;
                                float part_y = (preNode.z - coor[1]) / part;
                                for (int i = 1; i <= part; ++i)
                                {
                                    float dist_x = part_x * i + coor[0];
                                    float dist_y = part_y * i + coor[1];
                                    if (demData.GetHeight((int)((dist_x - xPos) / xMulti),
                                                            (int)((dist_y - yPos) / yMulti)) <
                                        waterLevelData.GetHeight((int)((dist_x - demxPos) / demxMulti),
                                                            (int)((dist_y - demyPos) / demyMulti)))
                                    {
                                        preNode = new Node(coor[0], coor[2], coor[1]);
                                        roadArr.Add(road);
                                        road = new List<Vector3>();
                                        break;
                                    }
                                }
                            }

                            // road that can drive
                            road.Add(new Vector3(coor[0], coor[2], coor[1]));
                            if (tagContainOneway == true)
                            {
                                this.addOneWayEdge(preNode, new Node(coor[0], coor[2], coor[1]));
                            }
                            else if (tagContainOneway == false)
                            {
                                this.addEdge(preNode, new Node(coor[0], coor[2], coor[1]));
                            }
                            preNode = new Node(coor[0], coor[2], coor[1]);
                        }
                    }
                    ////
                    roadArr.Add(road);
                }
            }
        }
    }
}
public class ShortestPath : MonoBehaviour
{
    List<List<Vector3>> roadArr;
    public LineRenderer prefabResLr;
    public LineRenderer prefabLr;
    public GameObject prefabNode;
    public Terrain dem;
    public Terrain waterLevel;
    public Graph g;

    LineRenderer lr;
    // Start is called before the first frame update
    [Obsolete]
    void Start()
    {
        g = new Graph();
        roadArr = new List<List<Vector3>>();
        g.JsonToGraph(Application.dataPath + "\\road_osm_json.json", roadArr, waterLevel, dem);
        //g.JsonToGraph("C:\\Users\\mark3\\test6\\Assets\\road_osm_json.json", roadArr, waterLevel, dem);
        // draw road
        for (int i = 0; i < roadArr.Count; ++i)
        {
            // get a set of points
            var tmp = roadArr[i].ToArray();
            lr = Instantiate<LineRenderer>(prefabLr);
            lr.tag = "road";
            lr.positionCount = tmp.Length;
            lr.startColor = Color.red;
            lr.SetPositions(tmp);
            lr.SetWidth(10, 10);
            lr.transform.parent = gameObject.transform;
            foreach (var j in tmp)
            {
                GameObject go = Instantiate<GameObject>(prefabNode);
                go.tag = "point";
                go.transform.position = j;
                go.transform.parent = gameObject.transform;
            }
        }
    }

    bool hasExecute = false;
    public GameObject startIns, endIns;
    // Update is called once per frame
    [Obsolete]
    void Update()
    {
        // dijkstra
        if (Input.GetKey(KeyCode.D) && hasExecute == false)
        {
            showShortestPathBYDijkstra(startIns, endIns);
            hasExecute = true;
        }
        // stop
        else if (Input.GetKey(KeyCode.S) && hasExecute == true)
        {
            ClearLog();
            Destroy(resLr);
            hasExecute = false;
        }
        // retart
        if (Input.GetKey(KeyCode.R))
        {
            Debug.Log("restart...");
            // if startIns is null, don't need to new
            Vector3 startPos = new Vector3(0, 0, 0);
            Vector3 endPos = new Vector3(0, 0, 0);
            bool insIsNull = true;
            if (startIns != null && endIns != null)
            {
                insIsNull = false;
                var tmpStart = startIns.transform.position;
                startPos = new Vector3(tmpStart.x, tmpStart.y, tmpStart.z);
                Destroy(startIns);
                var tmpEnd = endIns.transform.position;
                endPos = new Vector3(tmpEnd.x, tmpEnd.y, tmpEnd.z);
                Destroy(endIns);
            }


            var points = GameObject.FindGameObjectsWithTag("point");
            foreach (var i in points)
            {
                Destroy(i);
            }
            var roads = GameObject.FindGameObjectsWithTag("road");
            foreach (var i in roads)
            {
                Destroy(i);
            }
            Start();

            // instance is not null, new go
            if (insIsNull == false)
            {
                startIns = new GameObject();
                startIns.name = "newStartNode";
                startIns.transform.parent = gameObject.transform;
                startIns.transform.position = startPos;

                endIns = new GameObject();
                endIns.name = "newEndNode";
                endIns.transform.parent = gameObject.transform;
                endIns.transform.position = endPos;
            }


            Debug.Log("restart success");
        }
        // a_start
        if (Input.GetKey(KeyCode.A))
        {
            // 修改 weight 計算方式即可
            //showShortestPathBYAStart(startIns, endIns);
        }
    }

    LineRenderer resLr;
    [Obsolete]
    void showShortestPathBYDijkstra(GameObject startIns, GameObject endIns)
    {

        Dictionary<Node, Node> preNodeDict = g.dijkstra(startIns, endIns);
        if (preNodeDict == null)
        {
            Debug.Log("no way");
            return;
        }
        var endPos = endIns.transform.position;
        Node pathNode = new Node(endPos.x, endPos.y, endPos.z);
        resLr = Instantiate<LineRenderer>(prefabResLr);
        int count = 0;
        while (pathNode != null)
        {
            pathNode = preNodeDict[pathNode];
            count++;
        }
        resLr.positionCount = count;
        count = 0;
        pathNode = new Node(endPos.x, endPos.y, endPos.z);
        while (pathNode != null)
        {
            //Debug.Log(count);
            resLr.startColor = Color.green;
            resLr.SetPosition(count++, new Vector3(pathNode.x, pathNode.y + 5, pathNode.z));
            resLr.SetWidth(70, 70);
            resLr.transform.parent = gameObject.transform;
            //Debug.Log(pathNode.x + " " + pathNode.y);
            pathNode = preNodeDict[pathNode];
        }
        return;
    }

    public void ClearLog()
    {
        var assembly = Assembly.GetAssembly(typeof(UnityEditor.Editor));
        var type = assembly.GetType("UnityEditor.LogEntries");
        var method = type.GetMethod("Clear");
        method.Invoke(new object(), null);
    }
}
