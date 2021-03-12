using System;
using System.Collections;
using UnityEngine;

public static class CommonFunctions
{
    public static bool CursorVisible
    {
        get => Cursor.visible;
        set
        {
            Cursor.visible = value;
            Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }

    /// <summary>
    /// Cooldown coroutine with setter and callback (any can be null)
    /// </summary>
    /// <param name="length">Time between setting startValue and !startValue</param>
    /// <param name="startEndAction">Called on start and end of coroutine</param>
    /// <param name="startValue">First setter call argument</param>
    /// <param name="onProgress">Called every tick with percentage as parameter</param>
    public static IEnumerator CooldownCoroutine(float length, Action<bool> startEndAction = null, bool startValue = false, Action<float> onProgress = null)
    {
        if (startEndAction == null && onProgress == null)
        {
            Debug.LogWarning("BoolCooldown called without getter or setter!");
            yield break;
        }

        // Initial call
        startEndAction?.Invoke(startValue);
        float cooldownTime = 0;
        
        // Main loop
        if (onProgress != null)
        {
            while (cooldownTime < length)
            {
                onProgress.Invoke(cooldownTime / length);
                yield return null;
                cooldownTime += Time.deltaTime;
            }
        }
        else
        {
            yield return new WaitForSeconds(length);
        }

        // Final call
        onProgress?.Invoke(1);
        startEndAction?.Invoke(!startValue);
    }
}