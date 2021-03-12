using System;
using System.Collections;
using System.Linq;
using MLAPI;
using MLAPI.Messaging;
using MLAPI.NetworkedVar;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zenject;
using Random = UnityEngine.Random;


public class PlayerLogic : NetworkedBehaviour
{
    // Serialized fields
    [Header("Movement")]
    [SerializeField] private float movementSpeed = 400f;
    [SerializeField] private float runningSpeed = 400f;
    [SerializeField] private float maneuverSpeed = 100f;
    [SerializeField] private float maxManeuverSpeed = 10f;
    [SerializeField] private float jumpPower = 10f;
    [SerializeField] private float runHoldTime = 0.4f;
    [SerializeField] private float runSoundCooldown = 0.25f;
    
    [Header("Dash")]
    [SerializeField] private float dashCooldown = 3f;
    [SerializeField] private float dashPower = 10f;
    [SerializeField] private float dashLength = 0.2f;
    [SerializeField] private float afterDashSpeed = 10f;
    [SerializeField] private LayerMask groundMask;
    
    [Header("Abilities")]
    [SerializeField] private float teleportDistance = 30f;

    [Header("Debuffs")]
    [SerializeField] private Color stunColor = new Color(1, 0, 1, 0.75f);
    [SerializeField] private Color deathColor = new Color(1, 0, 0, 0.75f);

    [Header("Cooldown Times")]
    [SerializeField] private float respawnDuration = 5f;
    [SerializeField] private float stunDuration = 0.6f;
    [SerializeField] private float invulnerableTime = 3f;
    [SerializeField] private float dashInvulnerableTime = 0.5f;

    [Header("Throwing knifes")]
    [SerializeField] private float randomBulletSpread = 0.05f;
    [SerializeField] private float knifeCooldown = 10f;
    [SerializeField] private int knifeCount = 3;
    [SerializeField] private float knifeBurstTime = 0.1f;

    // Networked variables
    private readonly NetworkedVarColor bodyColor = new NetworkedVarColor(NetVarPerm.Server2Everyone);
    private readonly NetworkedVarBool useMelee = new NetworkedVarBool(NetVarPerm.Owner2Everyone);
    private readonly NetworkedVarBool dead = new NetworkedVarBool(NetVarPerm.Server2Everyone);
    private readonly NetworkedVarBool invulnerable = new NetworkedVarBool(NetVarPerm.Server2Everyone);
    private readonly NetworkedVarBool canThrowKnife = new NetworkedVarBool(NetVarPerm.Server2Everyone, true);
    private readonly NetworkedVarBool canDash = new NetworkedVarBool(NetVarPerm.Server2Everyone, true);
    
    // Public fields
    public bool CanDealDamage { get; private set; }
    
    // Private fields
    private Rigidbody rb;
    private GameObject knife;
    private Image playerStatusImage;

    private float vAxis;
    private float hAxis;
    private bool dashRequested;
    private bool jumpRequested;

    private Transform[] spawnPoints;
    private bool warpToSpawn = true;
    private bool stunned;
    private ulong lastKnifeStabber = UInt64.MaxValue;

    private new Transform transform;

    private Coroutine stunCoroutine;
    private Coroutine meleeCoroutine;
    private Coroutine statusCoroutine;

    private bool shiftPressed = false;
    private bool dashServerAllowed = true;
    private float shiftPressTime;
    private float lastAttackTime;
    private float stunRemainingTime = 0f;
    private float lastRunSoundTime = 0f;

    private bool canJump = true;

    private Collider myCollider;
    private bool Grounded { get; set; }

    private bool _isDashing;
    private bool Dashing
    {
        get => _isDashing;
        set
        {
            if (_isDashing == value)
                return;
            
            _isDashing = value;
            UpdateFriction();
        }
    }
    
    private bool _isMoving;
    private bool Moving
    {
        get => _isMoving;
        set
        {
            if (_isMoving == value)
                return;
            
            _isMoving = value;
            UpdateFriction();
        }
    }

    private void UpdateFriction()
    {
        bool setEnabled = !Dashing && !Moving;
        PhysicMaterial material = myCollider.material;
        material.staticFriction = setEnabled ? 5f : 0;
        material.dynamicFriction = setEnabled ? 5f : 0;
        material.frictionCombine = setEnabled ? PhysicMaterialCombine.Average : PhysicMaterialCombine.Minimum;
    }

    public override void NetworkStart()
    {
        if (IsServer)
        {
            bodyColor.Value = Random.ColorHSV(0, 1, 1, 1, 1, 1);
        }
        
        GetComponent<Renderer>().material.color = bodyColor.Value;
        
        transform = gameObject.transform;
        knife = transform.Find("Knife").gameObject;
        knife.SetActive(useMelee.Value);
        
        useMelee.OnValueChanged += UseMeleeReaction;
        dead.OnValueChanged += DeadChanged;
        bodyColor.OnValueChanged += UpdateColor;
        canDash.OnValueChanged += CanDashChanged;
        
        rb = GetComponent<Rigidbody>();

        if (IsOwner)
        {
            CommonFunctions.CursorVisible = false;
            Physics.gravity = Vector3.down * 30;
            
            myCollider = GetComponent<Collider>();
            playerStatusImage = GameObject.FindWithTag("StatusPanel").GetComponent<Image>();
            spawnPoints = GameObject.FindGameObjectsWithTag("SpawnPoint").Select(g => g.transform).ToArray();
        }
    }

    private void CanDashChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            dashServerAllowed = true;
        }
    }

    private void OnDestroy()
    {
        if (IsOwner)
        {
            CommonFunctions.CursorVisible = true;
        }
    }

    #region Melee logic
    private void UseMeleeReaction(bool wasActive, bool isActive)
    {
        knife.SetActive(isActive);
        if (!wasActive && isActive && IsOwner)
        {
            if (meleeCoroutine != null)
            {
                StopCoroutine(meleeCoroutine);
            }

            meleeCoroutine = StartCoroutine(MeleeAttack());
        }
    }

    private IEnumerator MeleeAttack()
    {
        // Position, rotation, time, play sound, deal damage
        ValueTuple<Vector3, Vector3, float, bool, bool>[][] actions = new (Vector3, Vector3, float, bool, bool)[3][];
        // Stage 1
        actions[0] = new[]
        {
            (new Vector3(0, 0, 0), new Vector3(0, 20, 0), 0, false, false),
            (new Vector3(0.34f, 0, 0.94f), new Vector3(0, 20, 0), 0.3f, false, false),
            (new Vector3(0.7f, 0, 0.7f), new Vector3(0, 45, 0), 0.02f, true, true),
            (new Vector3(0.94f, 0, 0.34f), new Vector3(0, 70, 0), 0.02f, false, true),
            (new Vector3(1f, 0, 0f), new Vector3(0, 90, 0), 0.02f, true, true),
            (new Vector3(0.94f, 0, -0.34f), new Vector3(0, 110, 0), 0.02f, false, true),
            (new Vector3(0.7f, 0, -0.7f), new Vector3(0, 135, 0), 0.02f, true, true),
            (new Vector3(0, 0, 0), new Vector3(0, 135, 0), 0.2f, false, false),
        };
        // Stage 2
        actions[1] = new[]
        {
            (new Vector3(0, 0, 0), new Vector3(0, 135, 0), 0.2f, false, false),
            (new Vector3(0.7f, 0, -0.7f), new Vector3(0, 135, 0), 0.1f, true, false),
            (new Vector3(0.94f, 0, -0.34f), new Vector3(0, 110, 0), 0.02f, false, true),
            (new Vector3(1f, 0, 0f), new Vector3(0, 90, 0), 0.02f, false, true),
            (new Vector3(0.94f, 0, 0.34f), new Vector3(0, 70, 0), 0.02f, true, true),
            (new Vector3(0.7f, 0, 0.7f), new Vector3(0, 45, 0), 0.02f, false, true),
            (new Vector3(0.34f, 0, 0.94f), new Vector3(0, 20, 0), 0.02f, false, true),
            (new Vector3(0, 0, 1), new Vector3(0, 20, 0), 0.02f, true, true),
            (new Vector3(-0.34f, 0, 0.94f), new Vector3(0, -20, 0), 0.02f, false, true),
            (new Vector3(-0.7f, 0, 0.7f), new Vector3(0, -45, 0), 0.02f, false, true),
            (new Vector3(-0.94f, 0, 0.34f), new Vector3(0, -70, 0), 0.02f, false, true),
            (new Vector3(0, 0, 0), new Vector3(0, -70, 0), 0.2f, false, false),
        };
        // Stage 3
        actions[2] = new[]
        {
            (new Vector3(0, 0, 0), new Vector3(0, -70, 0), 0.2f, false, false),
            (new Vector3(0, 0, 0), new Vector3(0, 0, 0), 0f, false, false),
            (new Vector3(0, 0, 1), new Vector3(0, 0, 0), 0.1f, true, true),
            (new Vector3(0, 0, 1), new Vector3(0, 0, 0), 0.0f, true, true),
            (new Vector3(0, 0, 1), new Vector3(0, 0, 0), 0.0f, true, true),
            (new Vector3(0, 0, 1), new Vector3(0, 0, 0), 0.5f, false, false),
        };

        Transform knifeTransform = knife.transform;

        int stage = 0;
        transform.rotation = Quaternion.LookRotation(CameraLogic.Forward);

        for (int i = 1; i < actions[stage].Length; i++)
        {
            Vector3 startPos = actions[stage][i-1].Item1;
            Vector3 startRot = actions[stage][i-1].Item2;
            Vector3 targetPos = actions[stage][i].Item1;
            Vector3 targetRot = actions[stage][i].Item2;
            float duration = actions[stage][i].Item3;

            void MoveKnife(float progress)
            {
                knifeTransform.localPosition = Vector3.Lerp(startPos, targetPos, progress);
                knifeTransform.localRotation = Quaternion.Euler(Vector3.Lerp(startRot, targetRot, progress));
            }

            CanDealDamage = actions[stage][i].Item5;

            if (actions[stage][i].Item4)
            {
                SFXManager.Singleton.InvokeServerRpc(SFXManager.Singleton.PlaySoundEffectOnMe, SFXManager.SoundEffectType.Attack);
            }

            yield return CommonFunctions.CooldownCoroutine(duration, onProgress: MoveKnife);

            if (stage == 2 && i == 2)
            {
                // rb.AddRelativeForce((Vector3.forward + Vector3.up * 0.2f).normalized * attackDashPower, ForceMode.VelocityChange);
                // StartCoroutine(CommonFunctions.CooldownCoroutine(1f, x => Dashing = x, true));
                DashClient(true);
                InvokeServerRpc(AttackInvulnerability);
            }

            // Next stage exists and we're approaching it
            if (stage + 1 < actions.Length && actions[stage].Length == i + 1)
            {
                // Last click was not too long ago
                if (Time.realtimeSinceStartup - lastAttackTime < 0.3f)
                {
                    stage++;
                    i = 1;
                    transform.rotation = Quaternion.LookRotation(CameraLogic.Forward);
                }
                else
                {
                    break;
                }
            }
        }

        useMelee.Value = false;
    }


    [ServerRPC]
    private void AttackInvulnerability()
    {
        StartCoroutine(CommonFunctions.CooldownCoroutine(dashInvulnerableTime, x => invulnerable.Value = x, true));
    }
    
    #endregion

    private void DeadChanged(bool wasDead, bool isDead)
    {
        if (isDead)
        {
            if (IsServer)
            {
                bodyColor.Value = Random.ColorHSV(0, 1, 1, 1, 1, 1);
                WindowScoreboard.Instance.AddDeath(OwnerClientId);
                if (lastKnifeStabber != UInt64.MaxValue)
                {
                    WindowScoreboard.Instance.AddKill(lastKnifeStabber);
                    lastKnifeStabber = UInt64.MaxValue;
                }
            }

            if (IsOwner)
            {
                bool rare = Random.Range(1, 100) >= 90;
                SFXManager.Singleton.InvokeServerRpc(SFXManager.Singleton.PlaySoundEffectOnMe, SFXManager.SoundEffectType.Death, rare ? 1 : 0);
            }
            
            Vector3 oldPos = transform.position;
            transform.position = new Vector3(oldPos.x, oldPos.y - 0.5f, oldPos.z);

            CanDealDamage = false;
            Moving = false;
            if (meleeCoroutine != null)
            {
                StopCoroutine(meleeCoroutine);
            }
            useMelee.Value = false;
        }
        else
        {
            if (IsServer)
            {
                StartCoroutine(CommonFunctions.CooldownCoroutine(invulnerableTime, x => invulnerable.Value = x, true));
            }
            warpToSpawn = true;
        }
        // Death "animation"
        transform.rotation = isDead ? Quaternion.Euler(new Vector3(-90, 0, 0)) : Quaternion.Euler(Vector3.zero);
    }

    private void UpdateColor(Color oldColor, Color newColor)
    {
        GetComponent<Renderer>().material.color = newColor;
    }

    private void Update()
    {
        if (IsOwner)
        {
            MyUpdate();
            LockUnlockCursorInput();
        }
    }

    private static void LockUnlockCursorInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CommonFunctions.CursorVisible = true;
        }

        if (Input.GetKeyDown(KeyCode.Mouse0) && !EventSystem.current.IsPointerOverGameObject())
        {
            CommonFunctions.CursorVisible = false;
        }
    }

    private void FixedUpdate()
    {
        if (IsOwner)
        {
            MyFixedUpdate();
        }
    }

    private void MyFixedUpdate()
    {
        if (dead.Value)
            return;

        Grounded = Physics.SphereCast(new Ray(transform.position, Vector3.down),
            0.48f, 0.85f, groundMask);

        // Warp after respawn
        if (warpToSpawn)
        {
            Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length - 1)];
            transform.position = spawnPoint.position + new Vector3(0, 0.5f, 0);
            warpToSpawn = false;
        }
        
        // Movement
        Moving = !stunned && Mathf.Abs(vAxis) + Mathf.Abs(hAxis) > 0.01;
        if (Moving && !Dashing)
        {
            float speedModifier = useMelee.Value ? 0.5f : 1f;
            transform.rotation = Quaternion.LookRotation(CameraLogic.Forward);
            Vector3 dirVector = transform.forward * vAxis + transform.right * hAxis;
            if (dirVector.magnitude > 1)
                dirVector.Normalize();
            // Controls on the land
            if (Grounded)
            {
                bool run = shiftPressed && Time.time - shiftPressTime > runHoldTime;
                if (run && Time.time - lastRunSoundTime > runSoundCooldown)
                {
                    lastRunSoundTime = Time.time;
                    SFXManager.Singleton.InvokeServerRpc(SFXManager.Singleton.PlaySoundEffectOnMe, SFXManager.SoundEffectType.Run);
                }
                float speed = run && !useMelee.Value ? runningSpeed : movementSpeed;
                Vector3 moveVector = dirVector * (speed * speedModifier * Time.deltaTime);
                moveVector = Vector3.Lerp(rb.GetHorizontalVelocity(), moveVector, 0.3f);
                rb.velocity = new Vector3(moveVector.x, rb.velocity.y, moveVector.z);
            }
            // Controls in the air
            else
            {
                Vector3 moveVector = (transform.forward * (vAxis * 0.2f) + transform.right * hAxis) * (maneuverSpeed * speedModifier * Time.deltaTime);
                Vector3 currentSpeed = rb.GetHorizontalVelocity();
                Vector3 newSpeed = currentSpeed + moveVector;
                float factor = Mathf.Clamp01(maxManeuverSpeed / Mathf.Pow(newSpeed.magnitude, 2));
                newSpeed = currentSpeed + moveVector * factor;
                rb.velocity = new Vector3(newSpeed.x, rb.velocity.y, newSpeed.z);
            }
        }
        
        // Jump
        if (jumpRequested)
        {
            jumpRequested = false;
            if (rb.velocity.y < 0)
            {
                rb.velocity = rb.GetHorizontalVelocity();
            }
            Vector3 dirVector = (transform.forward * vAxis + transform.right * hAxis);
            rb.AddForce((dirVector * 0.2f + Vector3.up).normalized * jumpPower, ForceMode.VelocityChange);
            StartCoroutine(CommonFunctions.CooldownCoroutine(0.5f, x => canJump = x));
            SFXManager.Singleton.InvokeServerRpc(SFXManager.Singleton.PlaySoundEffectOnMe, SFXManager.SoundEffectType.Jump);
        }

        // Dash
        if (dashRequested)
        {
            dashRequested = false;
            DashClient();
        }
    }

    private void MyUpdate()
    {
        if (dead.Value)
            return;

        // Check if out of bounds
        if (transform.position.magnitude > 60)
        {
            warpToSpawn = true;
        }

        // User input section
        vAxis = CommonFunctions.CursorVisible ? 0 : Input.GetAxis("Vertical");
        hAxis = CommonFunctions.CursorVisible ? 0 : Input.GetAxis("Horizontal");

        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            shiftPressTime = Time.time;
            shiftPressed = true;
        }

        if (Input.GetKeyUp(KeyCode.LeftShift))
        {
            shiftPressed = false;
            if (Moving && !CommonFunctions.CursorVisible && Time.time - shiftPressTime < runHoldTime
                && !useMelee.Value && !stunned && Grounded && canDash.Value && !dead.Value && dashServerAllowed)
            {
                dashRequested = true;
                dashServerAllowed = false;
            }
        }

        if (Input.GetKeyDown(KeyCode.Space) && !CommonFunctions.CursorVisible
                                            && !useMelee.Value && !stunned && Grounded && canJump && !dead.Value && !Dashing)
        {
            jumpRequested = true;
        }

        bool shouldUseMelee = Input.GetKeyDown(KeyCode.Mouse0) && !CommonFunctions.CursorVisible && !stunned && !Dashing;

        if (shouldUseMelee)
        {
            lastAttackTime = Time.realtimeSinceStartup;

            if (!useMelee.Value)
            {
                useMelee.Value = true;
            }
        }

        if (Input.GetKeyDown(KeyCode.Mouse1) && !CommonFunctions.CursorVisible && !useMelee.Value && !stunned && canThrowKnife.Value)
        {
            ThrowKnifeClient();
        }
        
        #if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.K) && !CommonFunctions.CursorVisible && !dead.Value)
        {
            InvokeServerRpc(Suicide);
        }
        #endif
    }

    #if UNITY_EDITOR
    [ServerRPC]
    private void Suicide()
    {
        StartCoroutine(CommonFunctions.CooldownCoroutine(respawnDuration, x => dead.Value = x, true));
        
        if (statusCoroutine != null)
        {
            StopCoroutine(statusCoroutine);
        }
        
        statusCoroutine = StartCoroutine(CommonFunctions.CooldownCoroutine(respawnDuration,
            onProgress: x => playerStatusImage.color = Color.Lerp(deathColor, deathColor.WithAlpha(0), x)));
    }
    #endif
    
    private IEnumerator BoolCooldownIcon(float maxTime, string iconTag, Action<bool> setter = null, bool reverse = false)
    {
        Action<float> action = null;
        if (IsOwner)
        {
            Image icon = GameObject.FindWithTag(iconTag).GetComponent<Image>();
            action = x => icon.fillAmount = reverse ? 1 - x : x;
        }
        yield return CommonFunctions.CooldownCoroutine(maxTime, setter, reverse, action);
    }

    #region Throwing knife logic
    private void ThrowKnifeClient()
    {
        Vector3 targetPoint = Vector3.zero;
        float maxShootRayDist = 100f;
        Ray shootRay = new Ray(CameraLogic.Transform.position, CameraLogic.Transform.forward);
        RaycastHit[] raycastHits = Physics.RaycastAll(shootRay, maxShootRayDist);
        bool found = false;
        foreach (RaycastHit raycastHit in raycastHits)
        {
            if (raycastHit.collider.gameObject != gameObject)
            {
                targetPoint = raycastHit.point;
                found = true;
                break;
            }
        }
        if (!found)
        {
            targetPoint = shootRay.origin + shootRay.direction.normalized * maxShootRayDist;
        }

        StartCoroutine(BoolCooldownIcon(knifeCooldown, "KnifeIcon"));
        InvokeServerRpc(ThrowKnifeServer, targetPoint);
    }

    [ServerRPC]
    private void ThrowKnifeServer(Vector3 target)
    {
        if (!canThrowKnife.Value)
            return;
        
        StartCoroutine(ThrowKnifeCoroutine(target));
        StartCoroutine(CommonFunctions.CooldownCoroutine(knifeCooldown, x => canThrowKnife.Value = x));
    }

    private IEnumerator ThrowKnifeCoroutine(Vector3 target)
    {
        Vector3 origin = NetworkingManager.Singleton.ConnectedClients[ExecutingRpcSender].PlayerObject.transform.position;
        GameObject knifePrefab = Resources.Load<GameObject>("Prefabs/Knife");

        for (int i = 0; i < knifeCount; i++)
        {
            Vector3 lookVector = (target - origin).normalized + Random.insideUnitSphere * randomBulletSpread;
            GameObject spawnedKnife = Instantiate(knifePrefab, origin, Quaternion.LookRotation(lookVector));

            KnifeLogic knifeLogic = spawnedKnife.GetComponent<KnifeLogic>();
            knifeLogic.owner = this;
            knifeLogic.flying = true;
            spawnedKnife.GetComponent<NetworkedObject>().Spawn();
            yield return new WaitForSeconds(knifeBurstTime);
        }
    }
    #endregion

    #region Dash logic
    private void DashClient(bool attack = false)
    {
        // rb.AddRelativeForce((Vector3.forward + Vector3.up * 0.2f).normalized * dashPower, ForceMode.VelocityChange);
        // rb.AddForce(dirVector.normalized * dashPower, ForceMode.VelocityChange);

        Vector3 direction;
        if (!attack)
        {
            transform.rotation = Quaternion.LookRotation(CameraLogic.Forward);
            direction = (transform.forward * vAxis + transform.right * hAxis).normalized;
        }
        else
        {
            direction = transform.forward;
        }

        void ApplyVelocity(float progress)
        {
            if (progress < 1f)
            {
                rb.velocity = direction * dashPower;
            }
            else
            {
                rb.velocity = direction * afterDashSpeed;
            }

            if (progress < 0.7f)
            {
                float param = Mathf.InverseLerp(0, 0.7f, progress);
                CameraLogic.Camera.fieldOfView = Mathf.Lerp(60, 65, param);
            }
            else if (progress > 0.7f)
            {
                float param = Mathf.InverseLerp(0.7f, 1f, progress);
                CameraLogic.Camera.fieldOfView = Mathf.Lerp(65, 60, param);
            }
        }

        StartCoroutine(CommonFunctions.CooldownCoroutine(dashLength, x => Dashing = x, true, ApplyVelocity));

        if (!attack)
        {
            StartCoroutine(BoolCooldownIcon(dashCooldown, "DashIcon"));
        }
        InvokeServerRpc(DashServer, attack);
        SFXManager.Singleton.InvokeServerRpc(SFXManager.Singleton.PlaySoundEffectOnMe, SFXManager.SoundEffectType.Dash);
    }

    [ServerRPC]
    private void DashServer(bool attack)
    {
        if (!attack)
        {
            StartCoroutine(CommonFunctions.CooldownCoroutine(dashCooldown, x => canDash.Value = x));
        }
        StartCoroutine(CommonFunctions.CooldownCoroutine(dashInvulnerableTime, x => invulnerable.Value = x, true));
    }
    #endregion

    #region Teleport logic

    private void TeleportClient()
    {
        Vector3 camPos = CameraLogic.Transform.position;
        Vector3 camFwd = CameraLogic.Transform.forward;
        Vector3 newPosition;
        
        if (Physics.SphereCast(new Ray(camPos + camFwd, camFwd), 0.2f, out RaycastHit raycastHit, teleportDistance, groundMask))
        {
            if (raycastHit.normal.y > 0.2f)
            {
                newPosition = raycastHit.point + Vector3.up;
            }
            else
            {
                newPosition = camPos + camFwd * (raycastHit.distance - 1) + Vector3.up;
            }
        }
        else
        {
            newPosition = camPos + camFwd * teleportDistance + Vector3.up;
        }
        
        transform.position = newPosition;
    }
    

    #endregion

    #region Knife collision logic
    [ServerRPC(RequireOwnership = false)]
    public void KnifeCollisionServer(KnifeLogic source)
    {
        lastKnifeStabber = source.owner.OwnerClientId;
        InvokeClientRpcOnOwner(KnifeCollisionClient, source);
        if (!invulnerable.Value && !dead.Value && !source.flying)
        {
            StartCoroutine(CommonFunctions.CooldownCoroutine(respawnDuration, x => dead.Value = x, true));
        }
    }

    [ClientRPC]
    void KnifeCollisionClient(KnifeLogic source)
    {
        if (!invulnerable.Value && !dead.Value)
        {
            if (statusCoroutine != null)
            {
                StopCoroutine(statusCoroutine);
            }

            if (!source.flying)
            {
                // Kill
                useMelee.Value = false;
                dashRequested = false;
                jumpRequested = false;
                stunRemainingTime = 0;
                
                StartCoroutine(BoolCooldownIcon(respawnDuration, "RespawnIcon", reverse: true));

                statusCoroutine = StartCoroutine(CommonFunctions.CooldownCoroutine(respawnDuration,
                    onProgress: x => playerStatusImage.color = Color.Lerp(deathColor, deathColor.WithAlpha(0), x)));
            }
            else
            {
                // Stun
                SFXManager.Singleton.InvokeServerRpc(SFXManager.Singleton.PlaySoundEffectOnMe, SFXManager.SoundEffectType.Stun);
                Moving = false;
                if (meleeCoroutine != null)
                {
                    StopCoroutine(meleeCoroutine);
                }
                useMelee.Value = false;
                
                float stunAccumulated = stunRemainingTime + stunDuration;
                
                if (stunCoroutine != null)
                {
                    StopCoroutine(stunCoroutine);
                }
                stunCoroutine = StartCoroutine(BoolCooldownIcon(stunAccumulated, "StunIcon", x => stunned = x, true));
                
                if (statusCoroutine != null)
                {
                    StopCoroutine(statusCoroutine);
                }
                
                statusCoroutine = StartCoroutine(CommonFunctions.CooldownCoroutine(stunAccumulated,
                    onProgress: x =>
                    {
                        stunRemainingTime = (1 - x) * stunAccumulated;
                        playerStatusImage.color = Color.Lerp(stunColor, stunColor.WithAlpha(0), x);
                    }));
            }
        }
    }
    #endregion
}
