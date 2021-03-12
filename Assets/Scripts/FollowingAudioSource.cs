using System;
using UnityEngine;

public class FollowingAudioSource : MonoBehaviour
{
    public Transform target;
    public AudioSource src;
    public bool start;

    private void Update()
    {
        if (!start)
            return;

        if (target != null)
        {
            transform.position = target.position;
        }
        
        if (!src.isPlaying)
        {
            Destroy(gameObject);
        }
    }
}