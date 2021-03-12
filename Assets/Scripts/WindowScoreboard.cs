using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MLAPI;
using MLAPI.Messaging;
using MLAPI.NetworkedVar;
using MLAPI.NetworkedVar.Collections;
using MLAPI.Serialization;
using MLAPI.Serialization.Pooled;
using TMPro;
using UnityEngine;

public class WindowScoreboard : NetworkedBehaviour
{
    [SerializeField] private TMP_Text scoresField;
    [SerializeField] private GameObject crownPrefab;
    private GameObject crown;
    
    public struct UserData : IBitWritable
    {
        #region IBitWritable
        public void Read(Stream stream)
        {
            using (PooledBitReader reader = PooledBitReader.Get(stream))
            {
                nickname = reader.ReadString(false).ToString();
                Kills = reader.ReadInt32();
                Deaths = reader.ReadInt32();
                IsNameSet = reader.ReadBool();
            }
        }

        public void Write(Stream stream)
        {
            using (PooledBitWriter writer = PooledBitWriter.Get(stream))
            {
                writer.WriteString(nickname);
                writer.WriteInt32(Kills);
                writer.WriteInt32(Deaths);
                writer.WriteBool(IsNameSet);
            }
        }
        #endregion

        public UserData(ulong id)
        {
            nickname = $"User{id}";
            Kills = 0;
            Deaths = 0;
            IsNameSet = false;
        }

        public bool IsNameSet { get; private set; }

        private string nickname;
        public string Nickname
        {
            get => nickname;
            set
            {
                nickname = value;
                IsNameSet = true;
            }
        }

        public int Kills { get; set; }
        public int Deaths { get; set; }
    }
    
    public readonly NetworkedDictionary<ulong, UserData> Players = new NetworkedDictionary<ulong, UserData>(NetVarPerm.Server2Everyone);
    private readonly NetworkedVarULong bestPlayerID = new NetworkedVarULong(NetVarPerm.Server2Everyone, UInt64.MaxValue);
    
    private readonly Dictionary<ulong, UserData> disconnectedPlayers = new Dictionary<ulong, UserData>();

    public static WindowScoreboard Instance;

    public void AddKill(ulong id)
    {
        if (Players.ContainsKey(id))
        {
            UserData updatedData = Players[id];
            updatedData.Kills++;
            Players[id] = updatedData;
        }
        else
        {
            UserData newPlayer = new UserData(id) {Kills = 1};
            Players.Add(id, newPlayer);
        }

        Players.IsDirty();
    }
    
    public void AddDeath(ulong id)
    {
        if (Players.ContainsKey(id))
        {
            UserData updatedData = Players[id];
            updatedData.Deaths++;
            Players[id] = updatedData;
        }
        else
        {
            UserData newPlayer = new UserData(id) {Deaths = 1};
            Players.Add(id, newPlayer);
        }
    }

    private void Awake()
    {
        Instance = this;
        transform.SetParent(FindObjectOfType<Canvas>().transform, false);
        RectTransform rectTransform = GetComponent<RectTransform>();
        rectTransform.anchoredPosition = new Vector2(0, -10);
        scoresField.text = "";
    }
    
    public override void NetworkStart()
    {
        bestPlayerID.OnValueChanged += BestPlayerChanged;
        Players.OnDictionaryChanged += DictionaryChanged;
        if (IsServer)
        {
            NetworkingManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            NetworkingManager.Singleton.OnClientConnectedCallback += OnClientConnect;
            OnClientConnect(0);
        }
        RebuildScoreboard();
    }

    private void OnDestroy()
    {
        if (NetworkingManager.Singleton != null)
        {
            NetworkingManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            NetworkingManager.Singleton.OnClientConnectedCallback -= OnClientConnect;
        }
    }

    private void OnClientDisconnect(ulong id)
    {
        disconnectedPlayers.Add(id, Players[id]);
        Players.Remove(id);
    }
    
    private void OnClientConnect(ulong id)
    {
        if (disconnectedPlayers.ContainsKey(id))
        {
            Players.Add(id, disconnectedPlayers[id]);
            disconnectedPlayers.Remove(id);
        }
        else
        {
            Players.Add(id, new UserData(id));
        }
    }

    private void DictionaryChanged(NetworkedDictionaryEvent<ulong, UserData> changeEvent)
    {
        RebuildScoreboard();
    }

    private void BestPlayerChanged(ulong oldBest, ulong newBest)
    {
        if (newBest == UInt64.MaxValue)
        {
            if (crown != null)
            {
                Destroy(crown);
            }
        }
    }

    [ClientRPC]
    private void WearCrown(NetworkedObject wearer)
    {
        if (crown == null)
        {
            crown = Instantiate(crownPrefab, Vector3.down * 20, Quaternion.identity);
        }
        crown.transform.SetParent(wearer.transform, false);
        crown.transform.localPosition = Vector3.zero;
    }
    
    private void RebuildScoreboard()
    {
        List<UserData> scoreboard =
            Players.Values
                .OrderByDescending(userData => userData.Kills)
                .ThenByDescending(userData => userData.Deaths)
                .ThenBy(userData => userData.Nickname).ToList();

        scoresField.text = "";
        foreach (UserData player in scoreboard)
        {
            StringBuilder scoreString = new StringBuilder()
                .Append(player.Nickname)
                .Append(" | K ")
                .Append(player.Kills)
                .Append(" | D ")
                .Append(player.Deaths);
            
            string separator = scoresField.text == "" ? "" : "\n";
            scoresField.text += separator + scoreString;
        }

        if (IsServer && Players.Count > 0)
        {
            int maxKills = Players.Max(pair => pair.Value.Kills);
            if (bestPlayerID.Value == UInt64.MaxValue ||      // Not initialized
                !Players.ContainsKey(bestPlayerID.Value) ||   // Player disconnected
                maxKills > Players[bestPlayerID.Value].Kills) // New leader
            {
                if (maxKills > 0)
                {
                    ulong bestKiller = Players.First(pair => pair.Value.Kills == maxKills).Key;
                    bestPlayerID.Value = bestKiller;
                    NetworkedObject best = NetworkingManager.Singleton.ConnectedClients[bestKiller].PlayerObject;
                    InvokeClientRpcOnEveryone(WearCrown, best);
                }
                else
                {
                    bestPlayerID.Value = UInt64.MaxValue;
                }
            }
        }
    }
}
