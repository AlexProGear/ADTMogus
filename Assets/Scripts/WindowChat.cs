using MLAPI;
using MLAPI.Messaging;
using MLAPI.NetworkedVar.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WindowChat : NetworkedBehaviour
{
    // Serialized Fields
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private TMP_Text messagesField;
    
    // Networking
    private readonly NetworkedList<string> messages = new NetworkedList<string>(NetVarPerm.Server2Everyone);

    // Client side
    private bool inputNicknameMode = true;

    private void Awake()
    {
        transform.SetParent(FindObjectOfType<Canvas>().transform, false);
        RectTransform rectTransform = GetComponent<RectTransform>();
        rectTransform.anchoredPosition = new Vector2(10, -10);
        
        messagesField.text = "";
    }

    public override void NetworkStart()
    {
        for (int i = 0; i < messages.Count; i++)
        {
            string separator = i > 0 ? "\n" : "";
            messagesField.text += separator + messages[i];
        }
        Canvas.ForceUpdateCanvases();
        scrollRect.verticalNormalizedPosition = 0;
        Canvas.ForceUpdateCanvases();

        if (WindowScoreboard.Instance != null
            && WindowScoreboard.Instance.Players.TryGetValue(NetworkingManager.Singleton.LocalClientId, out WindowScoreboard.UserData data) 
            && data.IsNameSet)
        {
            inputNicknameMode = false;
            inputField.placeholder.GetComponent<TMP_Text>().text = "Enter your message";
        }
    }

    private void OnEnable()
    {
        messages.OnListChanged += AddMessage;
        inputField.onEndEdit.AddListener(SendMessageToServer);
    }

    private void OnDisable()
    {
        messages.OnListChanged -= AddMessage;
        inputField.onEndEdit.RemoveListener(SendMessageToServer);
    }

    private void AddMessage(NetworkedListEvent<string> changeevent)
    {
        bool wasOnBottom = scrollRect.verticalScrollbar.value < 0.01f;

        string separator = messagesField.text == "" ? "" : "\n";
        messagesField.text += separator + changeevent.value;
        
        if (wasOnBottom)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0;
            Canvas.ForceUpdateCanvases();
        }
    }
    
    private void SendMessageToServer(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            InvokeServerRpc(ReceiveMessage, $"{message.Trim()}");
            if (inputNicknameMode)
            {
                inputNicknameMode = false;
                inputField.placeholder.GetComponent<TMP_Text>().text = "Enter your message";
            }
        }
        inputField.text = "";
    }

    [ServerRPC(RequireOwnership = false)]
    private void ReceiveMessage(string message)
    {
        ulong id = ExecutingRpcSender;
        if (WindowScoreboard.Instance.Players.ContainsKey(id))
        {
            WindowScoreboard.UserData player = WindowScoreboard.Instance.Players[id];
            if (player.IsNameSet)
            {
                messages.Add($"{player.Nickname}: {message}");
            }
            else
            {
                player.Nickname = message;
                WindowScoreboard.Instance.Players[id] = player;
            }
        }
        else
        {
            WindowScoreboard.UserData newPlayer = new WindowScoreboard.UserData(id) {Nickname = message};
            WindowScoreboard.Instance.Players.Add(id, newPlayer);
        }
    }
}
