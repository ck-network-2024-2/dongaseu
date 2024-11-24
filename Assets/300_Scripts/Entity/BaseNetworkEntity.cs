using UnityEngine;

public class BaseNetworkEntity : MonoBehaviour
{
    public uint NetworkId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public string SceneName { get; set; }
}
