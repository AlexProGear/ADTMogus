using System.Net;
using MLAPI;
using MLAPI.Transports.UNET;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WindowConnection : MonoBehaviour
{
    [SerializeField] private TMP_InputField textField;
    [SerializeField] private Button host;
    [SerializeField] private Button join;

    [RuntimeInitializeOnLoadMethod]
    private static void InitWindow()
    {
        GameObject menuPrefab = Resources.Load<GameObject>("Prefabs/WindowConnection");
        Instantiate(menuPrefab, FindObjectOfType<Canvas>().transform);
    }
    
    private void Start()
    {
        host.onClick.AddListener(Host);
        join.onClick.AddListener(Join);
        NetworkingManager.Singleton.OnServerStarted += ServerStarted;
        NetworkingManager.Singleton.OnClientConnectedCallback += ClientJoined;
        NetworkingManager.Singleton.OnClientDisconnectCallback += ClientDisconnected;
    }

    private void Host()
    {
        NetworkingManager.Singleton.StartHost();
    }

    private void Join()
    {
        IPAddress[] hostAddresses = Dns.GetHostAddresses(textField.text);
        NetworkingManager.Singleton.GetComponent<UnetTransport>().ConnectAddress = hostAddresses[0].ToString();
        NetworkingManager.Singleton.StartClient();
    }

    private void ServerStarted()
    {
        SpawnChat();
        SpawnScoreboard();
        CameraLogic.Active = true;
        gameObject.SetActive(false);
    }

    private void ClientJoined(ulong id)
    {
        if (NetworkingManager.Singleton.LocalClientId != id)
            return;

        CameraLogic.Active = true;
        gameObject.SetActive(false);
    }

    private void ClientDisconnected(ulong id)
    {
        if (NetworkingManager.Singleton.LocalClientId != id)
            return;
        
        CameraLogic.Active = false;
        gameObject.SetActive(true);
    }

    private void SpawnChat()
    {
        GameObject chatPrefab = Resources.Load<GameObject>("Prefabs/WindowChat");
        GameObject chatInstance = Instantiate(chatPrefab);
        chatInstance.GetComponent<NetworkedObject>().Spawn();
    }

    private void SpawnScoreboard()
    {
        GameObject scoreboardPrefab = Resources.Load<GameObject>("Prefabs/WindowScoreboard");
        GameObject scoreboardInstance = Instantiate(scoreboardPrefab);
        scoreboardInstance.GetComponent<NetworkedObject>().Spawn();
    }
}
