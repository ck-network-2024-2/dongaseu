using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
	private static GameManager instance;
	public static GameManager Instance
	{
		get
		{
			if (instance == null)
			{
				instance = FindObjectOfType<GameManager>();
				if (instance == null)
				{
					GameObject singleton = new GameObject(typeof(GameManager).ToString());
					instance = singleton.AddComponent<GameManager>();
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
	public PlayerData playerData;
	public WorldState worldState;

	public GameObject playerPrefab;
	public GameObject bulletPrefab;
	public GameObject npcPrefab;

	private Dictionary<uint, GameObject> playerObjects = new Dictionary<uint, GameObject>();
	private Dictionary<uint, GameObject> bulletObjects = new Dictionary<uint, GameObject>();
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

	// 데이터를 GZipStream을 사용하여 해제압축합니다.
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
			Debug.LogError("데이터 해제압축 중 오류 발생: " + ex.Message);
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
		else if (message.StartsWith("MSG:"))
		{
			string textMessage = message.Substring("MSG:".Length);
			Debug.Log("서버 메시지: " + textMessage);
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
		if (Input.GetKeyDown(KeyCode.Space))
		{
			SendMessageToServer("SPACE");
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
			UpdateEntities(world.Value.Bullets, bulletObjects, bulletPrefab, currentSceneName);
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
					objectPool[entity.Key] = entityObject;
				}
				objectPool[entity.Key].transform.position = new Vector3(entity.Value.X, entity.Value.Y, 0);
			}
			else if (objectPool.ContainsKey(entity.Key))
			{
				Destroy(objectPool[entity.Key]);
				objectPool.Remove(entity.Key);
			}
		}
	}

	private void OnApplicationQuit()
	{
		if (client != null)
		{
			client.Close();
		}
	}

	// 엔티티 데이터 클래스들 (서버와 동일하게 정의)
	public class BaseNetworkEntityData
	{
		public uint NetworkId { get; set; }
		public float X { get; set; }
		public float Y { get; set; }
		public string SceneName { get; set; }
	}

	public class PlayerData : BaseNetworkEntityData
	{
		// 추가적인 플레이어 속성이나 메서드가 필요하다면 여기에 정의
	}

	public class BulletData : BaseNetworkEntityData
	{
		public uint OwnerId { get; set; }
	}

	public class NpcData : BaseNetworkEntityData
	{
		// 추가적인 NPC 속성이나 메서드가 필요하다면 여기에 정의
	}

	public class SceneData
	{
		public Dictionary<uint, PlayerData> Players { get; set; } = new Dictionary<uint, PlayerData>();
		public Dictionary<uint, BulletData> Bullets { get; set; } = new Dictionary<uint, BulletData>();
		public Dictionary<uint, NpcData> Npcs { get; set; } = new Dictionary<uint, NpcData>();
	}

	public class WorldState
	{
		public Dictionary<string, SceneData> Worlds { get; set; } = new Dictionary<string, SceneData>();
	}
}
