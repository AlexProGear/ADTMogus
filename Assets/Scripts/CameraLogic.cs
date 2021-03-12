using MLAPI;
using UnityEngine;

public class CameraLogic : MonoBehaviour
{
    [SerializeField] private Vector3 rotationPivotOffset;
    [SerializeField] private LayerMask layerMask;
    [SerializeField] private float followSpeed = 5f;

    private Transform playerTransform;
    public static Transform Transform;
    public static bool Active = false;
    
    private float rotationY;
    private float rotationX;

    private Vector3 targetPosition;

    public static Camera Camera;
    public static Vector3 Forward
    {
        get
        {
            Vector3 fwd = Transform.forward;
            return new Vector3(fwd.x, 0, fwd.z).normalized;
        }
    }

    private void Start()
    {
        NetworkingManager.Singleton.OnServerStarted += ConnectCameraServer;
        NetworkingManager.Singleton.OnClientConnectedCallback += ConnectCameraClient;

        Transform = gameObject.transform;
        targetPosition = transform.position;
        Camera = Camera.main;
    }

    private void LateUpdate()
    {
        if (Active)
        {
            UpdateCameraTargetPosition();
            UpdateCameraPosition();
        }
    }

    void UpdateCameraTargetPosition()
    {
        if (!CommonFunctions.CursorVisible)
        {
            rotationY += Input.GetAxis ("Mouse Y");
            rotationX += Input.GetAxis ("Mouse X");

            rotationY = Mathf.Clamp (rotationY, -89.9f, 89.9f);
            Transform.localEulerAngles = new Vector3(-rotationY, rotationX, Transform.localEulerAngles.z);
        }

        Vector3 camPos = playerTransform.position + Vector3.up;
        Vector3 offsetDir = Transform.localToWorldMatrix.MultiplyVector(rotationPivotOffset);
        camPos +=
            Physics.SphereCast(new Ray(camPos, offsetDir), 0.5f, out RaycastHit hitInfo1, offsetDir.magnitude, layerMask)
                ? offsetDir.normalized * hitInfo1.distance
                : offsetDir;

        targetPosition = camPos;
    }

    private void UpdateCameraPosition()
    {
        Transform.position = Vector3.Lerp(Transform.position, targetPosition, Time.deltaTime * followSpeed);
    }

    private void ConnectCameraServer()
    {
        ConnectCameraClient(NetworkingManager.Singleton.LocalClientId);
    }

    private void ConnectCameraClient(ulong id)
    {
        // Our connection
        if (NetworkingManager.Singleton.LocalClientId == id)
        {
            playerTransform = NetworkingManager.Singleton.ConnectedClients[id].PlayerObject.transform;
            // transform.SetParent(playerTransform);
            UpdateCameraTargetPosition();
        }
    }
}
