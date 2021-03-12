using System;
using System.Collections;
using UnityEngine;

public class FollowingAudioSource : MonoBehaviour
{
    private Transform target;
    public AudioSource src;

    public void Begin(Transform target, AudioClip clip)
    {
        this.target = target;
        src.clip = clip;
        StartCoroutine(FollowerCoroutine());
    }

    private IEnumerator FollowerCoroutine()
    {
        src.Play();
        
        while (src.isPlaying)
        {
            if (target != null)
            {
                transform.position = target.position;
            }
            yield return null;
        }

        Destroy(gameObject);
    }
}