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
    // 싱글톤 인스턴스 정의
    private static NetworkManager instance;

    // 싱글톤 인스턴스 접근자
    public static NetworkManager Instance
    {
        get
        {
            if (instance == null)
            {
                // 현재 씬에서 NetworkManager 찾기
                instance = FindObjectOfType<NetworkManager>();
                if (instance == null)
                {
                    // 없으면 새로운 게임 오브젝트 생성 후 NetworkManager 추가
                    GameObject singleton = new GameObject(typeof(NetworkManager).ToString());
                    instance = singleton.AddComponent<NetworkManager>();
                    // 씬 전환 시 파괴되지 않도록 설정
                    DontDestroyOnLoad(singleton);
                }
            }
            return instance;
        }
    }

    // 플레이어 프리팹
    [SerializeField] private GameObject playerPrefab;
    // 별 프리팹
    [SerializeField] private GameObject starPrefab;
    // NPC 프리팹
    [SerializeField] private GameObject npcPrefab;

    // 빨간색 메터리얼
    [SerializeField] private Material redMaterial;
    // 초록색 메터리얼
    [SerializeField] private Material greenMaterial;
    // 파란색 메터리얼
    [SerializeField] private Material blueMaterial;

    // 씬 이름을 표시할 텍스트
    [SerializeField] private TextMeshProUGUI sceneNameText;
    // 플레이어 이름을 표시할 텍스트
    [SerializeField] private TextMeshProUGUI playerNameText;
    // 플레이어 점수를 표시할 텍스트
    [SerializeField] private TextMeshProUGUI playerScoreText;

    // 클라이언트 ID
    public uint clientId;
    // 월드 상태 정보
    public WorldState worldState = new WorldState();

    // TCP 클라이언트
    private TcpClient client;
    // 네트워크 스트림
    private NetworkStream stream;
    // 서버 IP 주소
    private const string serverIP = "127.0.0.1";
    // 서버 포트 번호
    private const int serverPort = 3100;
    // 수신 데이터 버퍼
    private byte[] readBuffer = new byte[4096];
    // 메시지 조합을 위한 StringBuilder
    private StringBuilder messageBuilder = new StringBuilder();

    // 플레이어 오브젝트 관리 딕셔너리
    private Dictionary<uint, GameObject> playerObjects = new Dictionary<uint, GameObject>();
    // 별 오브젝트 관리 딕셔너리
    private Dictionary<uint, GameObject> starObjects = new Dictionary<uint, GameObject>();
    // NPC 오브젝트 관리 딕셔너리
    private Dictionary<uint, GameObject> npcObjects = new Dictionary<uint, GameObject>();

    // 초기 설정 메서드
    void Awake()
    {
        // 싱글톤 패턴 구현
        if (instance == null)
        {
            instance = this;
            // 씬 전환 시 파괴되지 않도록 설정
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            // 이미 인스턴스가 존재하면 현재 오브젝트를 파괴
            Destroy(gameObject);
        }
    }

    // 게임 시작 시 호출되는 메서드
    void Start()
    {
        // 서버에 연결 시도
        ConnectToServer();
    }

    // 매 프레임마다 호출되는 메서드
    void Update()
    {
        // 서버와 연결되어 있고 스트림이 유효한 경우
        if (client != null && client.Connected && stream != null)
        {
            // 서버로부터 데이터 수신
            ReceiveDataFromServer();
            // 사용자 입력 처리
            HandleInput();
        }

        // UI 업데이트를 위한 플레이어 점수 정보 수집
        SortedDictionary<string, int> playerScores = new SortedDictionary<string, int>();

        // 월드 상태에서 플레이어 정보 수집
        foreach (var world in worldState.Worlds)
        {
            foreach (var player in world.Value.Players)
            {
                playerScores[player.Value.Name] = player.Value.Score;
            }
        }

        // 플레이어 이름과 점수를 표시하기 위한 StringBuilder
        StringBuilder playerNamesBuilder = new StringBuilder();
        StringBuilder playerScoresBuilder = new StringBuilder();

        // 플레이어 이름과 점수 추가
        foreach (var playerScore in playerScores)
        {
            playerNamesBuilder.AppendLine(playerScore.Key);
            playerScoresBuilder.AppendLine(playerScore.Value.ToString());
        }

        // UI 텍스트 업데이트
        playerNameText.text = playerNamesBuilder.ToString();
        playerScoreText.text = playerScoresBuilder.ToString();
    }

    // 애플리케이션 종료 시 호출되는 메서드
    private void OnApplicationQuit()
    {
        // 클라이언트가 존재하면 연결 종료
        if (client != null)
        {
            client.Close();
        }
    }

    // 서버에 연결하는 비동기 메서드
    private async void ConnectToServer()
    {
        try
        {
            // TcpClient 인스턴스 생성
            client = new TcpClient();
            // 서버에 연결
            await client.ConnectAsync(serverIP, serverPort);
            // 네트워크 스트림 설정
            stream = client.GetStream();
            Debug.Log("서버에 연결되었습니다.");
        }
        catch (Exception ex)
        {
            Debug.LogError("서버 연결 중 오류 발생: " + ex.Message);
        }
    }

    // 서버로부터 데이터를 수신하는 메서드
    private void ReceiveDataFromServer()
    {
        // 데이터가 수신 가능한지 확인
        if (stream.DataAvailable)
        {
            // 데이터를 읽어들임
            int byteCount = stream.Read(readBuffer, 0, readBuffer.Length);
            if (byteCount > 0)
            {
                // 수신된 데이터를 압축 해제
                byte[] decompressedData = DecompressData(readBuffer, byteCount);
                if (decompressedData != null)
                {
                    // 문자열로 변환하여 메시지 빌더에 추가
                    string received = Encoding.UTF8.GetString(decompressedData);
                    messageBuilder.Append(received);

                    // 완전한 메시지가 있는지 확인 후 처리
                    string completeData = messageBuilder.ToString();
                    int delimiterIndex;
                    while ((delimiterIndex = completeData.IndexOf('\n')) >= 0)
                    {
                        // 한 줄의 메시지를 추출
                        string singleMessage = completeData.Substring(0, delimiterIndex).Trim();
                        // 메시지 빌더에서 해당 부분 제거
                        messageBuilder.Remove(0, delimiterIndex + 1);

                        // 수신된 메시지 처리
                        ProcessReceivedMessage(singleMessage);
                        completeData = messageBuilder.ToString();
                    }
                }
            }
        }
    }

    // GZip을 통해 데이터를 압축 해제하는 메서드
    private byte[] DecompressData(byte[] data, int length)
    {
        try
        {
            using (MemoryStream compressedStream = new MemoryStream(data, 0, length))
            using (GZipStream gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            using (MemoryStream decompressedStream = new MemoryStream())
            {
                // 압축된 데이터를 압축 해제하여 복사
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

    // 수신된 메시지를 처리하는 메서드
    private void ProcessReceivedMessage(string message)
    {
        if (message.StartsWith("DATA:"))
        {
            // 데이터 부분 추출
            string jsonData = message.Substring("DATA:".Length);
            // 월드 상태 처리
            ProcessWorldState(jsonData);
        }
        else if (message.StartsWith("ID:"))
        {
            // 클라이언트 ID 추출
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

    // 월드 상태 데이터를 처리하는 메서드
    private void ProcessWorldState(string data)
    {
        try
        {
            // JSON 데이터를 객체로 역직렬화
            worldState = JsonConvert.DeserializeObject<WorldState>(data);
            // 게임 오브젝트 업데이트
            UpdateGameObjects();
        }
        catch (Exception ex)
        {
            Debug.LogError("월드 상태 처리 중 오류 발생: " + ex.Message);
        }
    }

    // 게임 오브젝트를 업데이트하는 메서드
    private void UpdateGameObjects()
    {
        // 현재 씬 이름 가져오기
        string currentSceneName = SceneManager.GetActiveScene().name;
        bool sceneChanged = false;

        // 각 월드의 씬 데이터 순회
        foreach (var world in worldState.Worlds)
        {
            // 플레이어 데이터 순회
            foreach (var player in world.Value.Players)
            {
                // 자신의 플레이어이고 씬이 변경된 경우
                if (player.Value.NetworkId == clientId && player.Value.SceneName != currentSceneName)
                {
                    // 씬 변경 처리
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

            // 플레이어 오브젝트 업데이트
            UpdateEntities(world.Value.Players, playerObjects, playerPrefab, currentSceneName);
            // 별 오브젝트 업데이트
            UpdateEntities(world.Value.Stars, starObjects, starPrefab, currentSceneName);
            // NPC 오브젝트 업데이트
            UpdateEntities(world.Value.Npcs, npcObjects, npcPrefab, currentSceneName);
        }
    }

    // 엔티티 데이터를 기반으로 오브젝트를 업데이트하는 제네릭 메서드
    private void UpdateEntities<T>(Dictionary<uint, T> entities, Dictionary<uint, GameObject> objectPool, GameObject prefab, string currentSceneName) where T : BaseNetworkEntityData
    {
        foreach (var entity in entities)
        {
            // 현재 씬에 있는 엔티티인지 확인
            if (entity.Value.SceneName == currentSceneName)
            {
                // 오브젝트 풀이 해당 엔티티를 포함하지 않으면 생성
                if (!objectPool.ContainsKey(entity.Key))
                {
                    GameObject entityObject = Instantiate(prefab);
                    entityObject.name = $"{typeof(T).Name}_{entity.Key}";

                    // 플레이어인 경우 메터리얼 설정
                    if (typeof(T) == typeof(PlayerData))
                    {
                        // 자신의 플레이어는 파란색, 다른 플레이어는 빨간색으로 표시
                        entityObject.GetComponent<MeshRenderer>().material = entity.Key == clientId ? blueMaterial : redMaterial;
                    }

                    objectPool[entity.Key] = entityObject;
                }

                if (objectPool[entity.Key] != null)
                {
                    // 오브젝트 위치 업데이트
                    objectPool[entity.Key].transform.position = new Vector3(entity.Value.X, entity.Value.Y, 0);
                }
            }
            else if (objectPool.ContainsKey(entity.Key))
            {
                // 현재 씬에 없으면 오브젝트 제거
                if (objectPool[entity.Key] != null)
                {
                    Destroy(objectPool[entity.Key]);
                }
                objectPool.Remove(entity.Key);
            }
        }
    }

    // 사용자 입력을 처리하는 메서드
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

    // 서버로 메시지를 전송하는 비동기 메서드
    private async void SendMessageToServer(string message)
    {
        if (client != null && client.Connected)
        {
            try
            {
                // 메시지를 UTF8 인코딩하여 전송
                byte[] messageBytes = Encoding.UTF8.GetBytes(message + "\n");
                await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
            }
            catch (Exception ex)
            {
                Debug.LogError("서버로 메시지 전송 중 오류 발생: " + ex.Message);
            }
        }
    }

    // 네트워크 엔티티의 기본 클래스
    public class BaseNetworkEntityData
    {
        public uint NetworkId { get; set; }     // 엔티티의 고유 ID
        public float X { get; set; }            // X 좌표
        public float Y { get; set; }            // Y 좌표
        public string SceneName { get; set; }   // 속한 씬 이름
    }

    // 플레이어 데이터 클래스
    public class PlayerData : BaseNetworkEntityData
    {
        public string Name { get; set; }    // 플레이어 이름
        public int Score { get; set; }      // 플레이어 점수
        public float Speed { get; set; }    // 이동 속도
    }

    // 별 데이터 클래스
    public class StarData : BaseNetworkEntityData { }

    // NPC 데이터 클래스
    public class NpcData : BaseNetworkEntityData
    {
        public float Speed { get; set; }    // 이동 속도
    }

    // 씬 별 데이터 관리 클래스
    public class SceneData
    {
        public Dictionary<uint, PlayerData> Players { get; set; } = new Dictionary<uint, PlayerData>(); // 플레이어 목록
        public Dictionary<uint, StarData> Stars { get; set; } = new Dictionary<uint, StarData>();       // 별 목록
        public Dictionary<uint, NpcData> Npcs { get; set; } = new Dictionary<uint, NpcData>();          // NPC 목록
    }

    // 월드 상태를 나타내는 클래스
    public class WorldState
    {
        public Dictionary<string, SceneData> Worlds { get; set; } = new Dictionary<string, SceneData>(); // 씬 이름별 데이터
    }
}
