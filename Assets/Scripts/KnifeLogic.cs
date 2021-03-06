using System.Collections;
using MLAPI;
using UnityEngine;
using Zenject;

public class KnifeLogic : NetworkedBehaviour
{
    // Serialized fields
    [SerializeField] private bool permanent;
    [SerializeField] private float movementSpeed = 5f;
    [SerializeField] private GameObject audioSourcePrefab;
    [SerializeField] private AudioClip audioClip;
    
    // Public fields
    public PlayerLogic owner;
    public bool flying;

    // Private fields
    private Rigidbody rb;
    
    public override void NetworkStart()
    {
        if (flying)
        {
            if (IsServer)
            {
                rb = GetComponent<Rigidbody>();
                StartCoroutine(ItsRainingKnives());
            }

            GameObject followingAudioSource = Instantiate(audioSourcePrefab);
            FollowingAudioSource audioSource = followingAudioSource.GetComponent<FollowingAudioSource>();
            audioSource.Begin(transform, audioClip);
        }
    }

    private IEnumerator ItsRainingKnives()
    {
        float lifeTime = 0;
        while (lifeTime < 60)
        {
            rb.position += transform.forward * (Time.deltaTime * movementSpeed);
            lifeTime += Time.deltaTime;
            yield return null;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsOwner)
            return;
        PlayerLogic player = other.GetComponentInParent<PlayerLogic>();
        if (player != null)
        {
            if (player != owner)
            {
                if (flying || owner.CanDealDamage)
                {
                    player.InvokeServerRpc(player.KnifeCollisionServer, this);
                }
                if (!permanent)
                {
                    Destroy(gameObject);
                }
            }
        }
        else if (!permanent)
        {
            KnifeLogic otherKnife = other.GetComponentInParent<KnifeLogic>();
            if (otherKnife == null || otherKnife.owner != owner)
            {
                Destroy(gameObject);
            }
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsOwner)
            return;

        PlayerLogic player = other.GetComponentInParent<PlayerLogic>();
        if (player != null && player != owner && owner.CanDealDamage)
        {
            player.InvokeServerRpc(player.KnifeCollisionServer, this);
        }
    }
}
