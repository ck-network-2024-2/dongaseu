using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour
{
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject starPrefab;
    [SerializeField] private GameObject npcPrefab;

    [SerializeField] private Material redMaterial;
    [SerializeField] private Material greenMaterial;
    [SerializeField] private Material blueMaterial;

    [SerializeField] private TextMeshProUGUI sceneNameText;
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI playerScoreText;

    private static NetworkManager instance;
    public static NetworkManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<NetworkManager>();
                if (instance == null)
                {
                    GameObject singleton = new GameObject(typeof(NetworkManager).ToString());
                    instance = singleton.AddComponent<NetworkManager>();
                    DontDestroyOnLoad(singleton);
                }
            }
            return instance;
        }
    }

    private TcpClient client;
    private NetworkStream stream;
    private const string serverIP = "127.0.0.1";
    private const int serverPort = 3100;
    private byte[] readBuffer = new byte[4096];
    private StringBuilder messageBuilder = new StringBuilder();

    public uint clientId;
    public WorldState worldState = new WorldState();

    private Dictionary<uint, GameObject> playerObjects = new Dictionary<uint, GameObject>();
    private Dictionary<uint, GameObject> starObjects = new Dictionary<uint, GameObject>();
    private Dictionary<uint, GameObject> npcObjects = new Dictionary<uint, GameObject>();

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        ConnectToServer();
    }

    void Update()
    {
        if (client != null && client.Connected && stream != null)
        {
            ReceiveDataFromServer();
            HandleInput();
        }

        // UI 업데이트
        SortedDictionary<string, int> playerScores = new SortedDictionary<string, int>();

        // 데이터 추가
        foreach (var world in worldState.Worlds)
        {
            foreach (var player in world.Value.Players)
            {
                playerScores[player.Value.Name] = player.Value.Score;
            }
        }

        StringBuilder playerNamesBuilder = new StringBuilder();
        StringBuilder playerScoresBuilder = new StringBuilder();

        // playerScores의 Key 값에 따라 데이터 추가
        foreach (var playerScore in playerScores)
        {
            playerNamesBuilder.AppendLine(playerScore.Key);
            playerScoresBuilder.AppendLine(playerScore.Value.ToString());
        }

        // 텍스트 업데이트
        playerNameText.text = playerNamesBuilder.ToString();
        playerScoreText.text = playerScoresBuilder.ToString();
    }

    private void OnApplicationQuit()
    {
        if (client != null)
        {
            client.Close();
        }
    }

    // 서버로부터 데이터를 수신합니다.
    private void ReceiveDataFromServer()
    {
        if (stream.DataAvailable)
        {
            int byteCount = stream.Read(readBuffer, 0, readBuffer.Length);
            if (byteCount > 0)
            {
                byte[] decompressedData = DecompressData(readBuffer, byteCount);
                if (decompressedData != null)
                {
                    string received = Encoding.UTF8.GetString(decompressedData);
                    messageBuilder.Append(received);

                    string completeData = messageBuilder.ToString();
                    int delimiterIndex;
                    while ((delimiterIndex = completeData.IndexOf('\n')) >= 0)
                    {
                        string singleMessage = completeData.Substring(0, delimiterIndex).Trim();
                        messageBuilder.Remove(0, delimiterIndex + 1);

                        ProcessReceivedMessage(singleMessage);
                        completeData = messageBuilder.ToString();
                    }
                }
            }
        }
    }

    // 데이터를 GZipStream을 사용하여 압축해제합니다.
    private byte[] DecompressData(byte[] data, int length)
    {
        try
        {
            using (MemoryStream compressedStream = new MemoryStream(data, 0, length))
            using (GZipStream gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            using (MemoryStream decompressedStream = new MemoryStream())
            {
                gzipStream.CopyTo(decompressedStream);
                return decompressedStream.ToArray();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("데이터 해제 압축 중 오류 발생: " + ex.Message);
            return null;
        }
    }

    // 수신된 메시지를 처리합니다.
    private void ProcessReceivedMessage(string message)
    {
        if (message.StartsWith("DATA:"))
        {
            string jsonData = message.Substring("DATA:".Length);
            ProcessWorldState(jsonData);
        }
        else if (message.StartsWith("ID:"))
        {
            string clientIdString = message.Substring("ID:".Length);
            if (uint.TryParse(clientIdString, out uint clientId))
            {
                this.clientId = clientId;
                Debug.Log("클라이언트 ID: " + clientId);
            }
            else
            {
                Debug.LogWarning("잘못된 클라이언트 ID 형식: " + clientIdString);
            }
        }
        else
        {
            Debug.LogWarning("알 수 없는 메시지 형식: " + message);
        }
    }

    // 서버에 연결을 시도합니다.
    private async void ConnectToServer()
    {
        try
        {
            client = new TcpClient();
            await client.ConnectAsync(serverIP, serverPort);
            stream = client.GetStream();
            Debug.Log("서버에 연결되었습니다.");
        }
        catch (Exception ex)
        {
            Debug.LogError("서버 연결 중 오류 발생: " + ex.Message);
        }
    }

    // 플레이어 입력을 처리합니다.
    private void HandleInput()
    {
        if (Input.GetKey(KeyCode.UpArrow))
        {
            SendMessageToServer("UP");
        }
        if (Input.GetKey(KeyCode.DownArrow))
        {
            SendMessageToServer("DOWN");
        }
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            SendMessageToServer("LEFT");
        }
        if (Input.GetKey(KeyCode.RightArrow))
        {
            SendMessageToServer("RIGHT");
        }
    }

    // 서버로 메시지를 전송합니다.
    private async void SendMessageToServer(string message)
    {
        if (client != null && client.Connected)
        {
            try
            {
                byte[] messageBytes = Encoding.UTF8.GetBytes(message + "\n");
                await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            }
            catch (Exception ex)
            {
                Debug.LogError("서버로 메시지 전송 중 오류 발생: " + ex.Message);
            }
        }
    }

    // 수신된 월드 상태 데이터를 처리합니다.
    private void ProcessWorldState(string data)
    {
        try
        {
            worldState = JsonConvert.DeserializeObject<WorldState>(data);
            UpdateGameObjects();
        }
        catch (Exception ex)
        {
            Debug.LogError("월드 상태 처리 중 오류 발생: " + ex.Message);
        }
    }

    // 월드 상태 데이터를 바탕으로 게임 오브젝트를 업데이트합니다.
    private void UpdateGameObjects()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        bool sceneChanged = false;

        foreach (var world in worldState.Worlds)
        {
            foreach (var player in world.Value.Players)
            {
                if (player.Value.NetworkId == clientId && player.Value.SceneName != currentSceneName)
                {
                    sceneNameText.text = player.Value.SceneName;
                    playerObjects.Clear();
                    starObjects.Clear();
                    npcObjects.Clear();
                    SceneManager.LoadScene(player.Value.SceneName);
                    sceneChanged = true;
                    break;
                }
            }

            if (sceneChanged)
            {
                break;
            }

            UpdateEntities(world.Value.Players, playerObjects, playerPrefab, currentSceneName);
            UpdateEntities(world.Value.Stars, starObjects, starPrefab, currentSceneName);
            UpdateEntities(world.Value.Npcs, npcObjects, npcPrefab, currentSceneName);
        }
    }

    // 엔티티 데이터를 바탕으로 게임 오브젝트를 업데이트합니다.
    private void UpdateEntities<T>(Dictionary<uint, T> entities, Dictionary<uint, GameObject> objectPool, GameObject prefab, string currentSceneName) where T : BaseNetworkEntityData
    {
        foreach (var entity in entities)
        {
            if (entity.Value.SceneName == currentSceneName)
            {
                if (!objectPool.ContainsKey(entity.Key))
                {
                    GameObject entityObject = Instantiate(prefab);
                    entityObject.name = $"{typeof(T).Name}_{entity.Key}";

                    // 자기 자신은 파란색, 다른 플레이어라면 빨간색으로 표시
                    if (typeof(T) == typeof(PlayerData))
                    {
                        entityObject.GetComponent<MeshRenderer>().material = entity.Key == clientId ? blueMaterial : redMaterial;
                    }

                    objectPool[entity.Key] = entityObject;
                }

                if (objectPool[entity.Key] != null)
                {
                    objectPool[entity.Key].transform.position = new Vector3(entity.Value.X, entity.Value.Y, 0);
                }
            }
            else if (objectPool.ContainsKey(entity.Key))
            {
                if (objectPool[entity.Key] != null)
                {
                    Destroy(objectPool[entity.Key]);
                }
                objectPool.Remove(entity.Key);
            }
        }
    }

    // 모든 네트워크 엔티티의 기본 클래스
    public class BaseNetworkEntityData
    {
        public uint NetworkId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public string SceneName { get; set; }
    }

    // 플레이어 엔티티 데이터 클래스
    public class PlayerData : BaseNetworkEntityData
    {
        public string Name { get; set; }
        public int Score { get; set; }
        public float Speed { get; set; }
    }

    // 별 엔티티 데이터 클래스
    public class StarData : BaseNetworkEntityData { }

    // NPC 엔티티 데이터 클래스
    public class NpcData : BaseNetworkEntityData
    {
        public float Speed { get; set; }
    }

    // 씬별 엔티티 데이터를 관리하는 클래스
    public class SceneData
    {
        public Dictionary<uint, PlayerData> Players { get; set; } = new Dictionary<uint, PlayerData>();
        public Dictionary<uint, StarData> Stars { get; set; } = new Dictionary<uint, StarData>();
        public Dictionary<uint, NpcData> Npcs { get; set; } = new Dictionary<uint, NpcData>();
    }

    // 모든 엔티티를 포함하는 월드 상태 클래스
    public class WorldState
    {
        public Dictionary<string, SceneData> Worlds { get; set; } = new Dictionary<string, SceneData>();
    }
}
