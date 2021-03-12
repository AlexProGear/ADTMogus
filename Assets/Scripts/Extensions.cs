using UnityEngine;

public static class Extensions
{
    public static Vector3 GetHorizontalVelocity(this Rigidbody rb)
    {
        Vector3 velocity = rb.velocity;
        return new Vector3(velocity.x, 0, velocity.z);
    }

    public static Color WithAlpha(this Color color, float alpha)
    {
        return new Color(color.r, color.g, color.b, alpha);
    }
}