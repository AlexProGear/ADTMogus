using System;
using System.Collections.Generic;
using MLAPI;
using MLAPI.Messaging;
using UnityEngine;
using Random = UnityEngine.Random;

public class SFXManager : NetworkedBehaviour
{
    // Serialized fields
    [SerializeField] private GameObject audioSourceTemplate;
    [Header("Sounds")]
    [SerializeField] private AudioClip[] deathSounds;
    [SerializeField] private AudioClip[] dashSounds;
    [SerializeField] private AudioClip[] teleportSounds;
    [SerializeField] private AudioClip[] attackSounds;
    [SerializeField] private AudioClip[] jumpSounds;
    [SerializeField] private AudioClip[] stunSounds;
    [SerializeField] private AudioClip[] runSounds;

    // Public fields
    public static SFXManager Singleton;

    public enum SoundEffectType
    {
        Death,
        Dash,
        Teleport,
        Attack,
        Jump,
        Stun,
        Run
    }
    
    // Private fields
    private readonly Dictionary<SoundEffectType, AudioClip[]> typeToArray = new Dictionary<SoundEffectType, AudioClip[]>();

    public override void NetworkStart()
    {
        Singleton = this;
        typeToArray.Add(SoundEffectType.Death, deathSounds);
        typeToArray.Add(SoundEffectType.Dash, dashSounds);
        typeToArray.Add(SoundEffectType.Teleport, teleportSounds);
        typeToArray.Add(SoundEffectType.Attack, attackSounds);
        typeToArray.Add(SoundEffectType.Jump, jumpSounds);
        typeToArray.Add(SoundEffectType.Stun, stunSounds);
        typeToArray.Add(SoundEffectType.Run, runSounds);
    }

    [ServerRPC(RequireOwnership = false)]
    public void PlaySoundEffectOnMe(SoundEffectType effectType)
    {
        NetworkedObject player = NetworkingManager.Singleton.ConnectedClients[ExecutingRpcSender].PlayerObject;
        int index = Random.Range(0, typeToArray[effectType].Length - 1);
        InvokeClientRpcOnEveryone(PlaySoundEffectOnObject, player, effectType, index);
    }
    
    [ServerRPC(RequireOwnership = false)]
    public void PlaySoundEffectOnMe(SoundEffectType effectType, int index)
    {
        NetworkedObject player = NetworkingManager.Singleton.ConnectedClients[ExecutingRpcSender].PlayerObject;
        InvokeClientRpcOnEveryone(PlaySoundEffectOnObject, player, effectType, index);
    }
    
    [ServerRPC]
    public void PlaySoundEffectOnObject(NetworkedObject netObject, SoundEffectType effectType)
    {
        int index = Random.Range(0, typeToArray[effectType].Length - 1);
        InvokeClientRpcOnEveryone(PlaySoundEffectOnObject, netObject, effectType, index);
    }

    [ClientRPC]
    private void PlaySoundEffectOnObject(NetworkedObject netObject, SoundEffectType effectType, int index)
    {
        AudioClip clip = typeToArray[effectType][index];
        GameObject audioSrc = Instantiate(audioSourceTemplate, transform, true);
        FollowingAudioSource follower = audioSrc.GetComponent<FollowingAudioSource>();
        follower.Begin(netObject.transform, clip);
    }
}