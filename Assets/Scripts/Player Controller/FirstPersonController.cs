using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.VisualScripting.ReorderableList;
using UnityEngine;

[DisallowMultipleComponent]
public class FirstPersonController : MonoBehaviour, ICanBeDamaged
{
    GameObject playerObject;
    public bool CanMove { get; private set; } = true;
    private bool ShouldJump => canJump && Input.GetKeyDown(jumpKey) && characterController.isGrounded;
    private bool ShouldCrouch => canCrouch && Input.GetKeyDown(crouchKey) &&
        !duringCrouchAnimation && characterController.isGrounded;
    private bool shouldSprint => canSprint && Input.GetKey(sprintKey);

    [Header("Functional Options")]
    [SerializeField] private bool canJump = true;
    [SerializeField] private bool canCrouch = true;
    [SerializeField] private bool canUseHeadbob = true;
    [SerializeField] private bool canZoom = true;
    [SerializeField] private bool canInteract = true;
    [SerializeField] private bool canClimb = true;
    [SerializeField] private bool canSprint = true;

    [Header("Controls")]
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private KeyCode crouchKey = KeyCode.C;
    [SerializeField] private KeyCode zoomKey = KeyCode.Mouse1;
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;

    [Header("Movement Parameters")]
    [SerializeField] private float walkSpeed = 6.0f;
    [SerializeField] private float crouchSpeed = 3.0f;
    [SerializeField] private float sprintSpeed = 12.0f;

    [Header("Look Parameters")]
    [SerializeField, Range(1, 10)] private float lookSpeedX = 2.0f;
    [SerializeField, Range(1, 10)] private float lookSpeedY = 2.0f;
    [SerializeField, Range(1, 180)] private float upperLookLimit = 80.0f;
    [SerializeField, Range(1, 180)] private float lowerLookLimit = 80.0f;

    [Header("Health Parameters")]
    [SerializeField] private float maxHealth = 100;
    [SerializeField] private float timeBeforeRegenStarts = 3;
    [SerializeField] private float healthValueIncrement = 1;
    [SerializeField] private float healthTimeIncrement = 0.1f;
    private float currentHealth;
    private Coroutine regeneratingHealth;
    public static Action<float> OnTakeDamage;
    public static Action<float> OnDamage;
    public static Action<float> OnHeal;

    [Header("Jumping Parameters")]
    [SerializeField] private float jumpForce = 8.0f;
    [SerializeField] private float gravity = 30.0f;

    [Header("Crouching Parameters")]
    [SerializeField] private float crouchHeight = 0.5f;
    [SerializeField] private float standingHeight = 2.5f;
    [SerializeField] private float timeToCrouch = 0.25f;
    [SerializeField] private Vector3 crouchingCenter = new Vector3(0, 0.5f, 0);
    [SerializeField] private Vector3 standingCenter = new Vector3(0, 0, 0);
    private bool isCrouching;
    private bool duringCrouchAnimation;

    [Header("Headbob Parameters")]
    [SerializeField] private float walkBobSpeed = 14f;
    [SerializeField] private float walkBobAmount = 0.05f;
    [SerializeField] private float crouchBobSpeed = 7f;
    [SerializeField] private float crouchBobAmount = 0.025f;
    private float defaultYpos = 0f;
    private float timer;

    [Header("Zoom Parameters")]
    [SerializeField] private float timeToZoom = 0.3f;
    [SerializeField] private float zoomFOV = 30f;
    private float defaultFOV;
    private Coroutine zoomRoutine;

    [Header("Interaction")]
    [SerializeField] private Vector3 interactionRayPoint = default;
    [SerializeField] private float interactionDistance = default;
    [SerializeField] private LayerMask interactionLayer = default;
    private Interactable currentInteractable;

    [Header("Climbing")]
    [SerializeField] private float avoidFloorDistance = .1f;
    [SerializeField] private float ladderGrabDistance = 1f;
    [SerializeField] private float ladderFloorDropDistance = 1f;
    private bool isClimbingLadder;
    private Vector3 lastGrabLadderDirection;

    private Camera playerCamera;
    private CharacterController characterController;

    private Vector3 moveDirection;
    private Vector2 currentInput;

    private float rotationX = 0f;

    private void OnEnable()
    {
        OnTakeDamage += ApplyDamage;
    }

    private void OnDisable()
    {
        OnTakeDamage -= ApplyDamage;
    }


    // Start is called before the first frame update
    void Awake()
    {
        playerCamera = GetComponentInChildren<Camera>();
        characterController = GetComponent<CharacterController>();
        defaultYpos = playerCamera.transform.localPosition.y;
        defaultFOV = playerCamera.fieldOfView;
        currentHealth = maxHealth;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Update is called once per frame
    public void Update()
    {
        if (CanMove)
        {
            HandleMovementInput();
            HandleMouseLook(); 
            
            if(canJump)
                HandleJump();

            if (canCrouch)
                HandleCrouch();

            if (canClimb)
                HandleClimbing();

            if (canUseHeadbob)
                HandleHeadBob();

            if (canZoom)
                HandleZoom();

            if (canInteract)
            {
                HandleInteractionCheck();
                HandleInteractionInput();
            }

            ApplyFinalMovements();

        }
    }

    private void HandleMovementInput()
    {
        {
            currentInput = new Vector2(
                (isCrouching ? crouchSpeed : shouldSprint ? sprintSpeed : walkSpeed) * Input.GetAxis("Vertical"),
                (isCrouching ? crouchSpeed : shouldSprint ? sprintSpeed : walkSpeed) * Input.GetAxis("Horizontal"));

            float moveDirectionY = moveDirection.y;

            moveDirection = (transform.TransformDirection(Vector3.forward) * currentInput.x) +
                (transform.TransformDirection(Vector3.right) * currentInput.y);

            moveDirection.y = moveDirectionY;
        }
    }

    private void HandleClimbing()
    {
        if (!isClimbingLadder)
        {
            
            //Not Climbing Ladder
            if (Physics.Raycast(transform.position + Vector3.up * avoidFloorDistance, moveDirection,
              out RaycastHit raycastHit, ladderGrabDistance))
            {
                if (raycastHit.transform.TryGetComponent(out Climbable climbable))
                {
                    GrabLadder(moveDirection);
                    Debug.Log("Climbing");
                }
            }
        }

        else
        {

            //Is climbing ladder already.
            if (Physics.Raycast(transform.position + Vector3.up * avoidFloorDistance, lastGrabLadderDirection,
               out RaycastHit raycastHit, ladderGrabDistance))
            { 
                if (!raycastHit.transform.TryGetComponent(out Climbable climbable))
                {
                    DropLadder();
                    moveDirection.y = jumpForce;
                    Debug.Log("StopClimbing");
                } 
            }

            else
            {
                DropLadder();
                moveDirection.y = jumpForce;
                Debug.Log("StopClimbing2");
            }
                

            if (Vector3.Dot(moveDirection, lastGrabLadderDirection) < 0)
            {
                
                //Climbing Down Ladder
                if (Physics.Raycast(transform.position, transform.TransformDirection(Vector3.down), out RaycastHit FloorraycastHit, ladderFloorDropDistance))
                {
                    DropLadder();
                    Debug.Log("StopClimbingOnGround");
                    Debug.Log(FloorraycastHit);
                }
            }
        }

        if (isClimbingLadder)
        {
            moveDirection = (transform.TransformDirection(Vector3.up) * currentInput.x) +
                (transform.TransformDirection(Vector3.right) * currentInput.y);
        }

    }
    private void GrabLadder(Vector3 lastGrabLadderDirection)
    {
        isClimbingLadder = true;
        this.lastGrabLadderDirection = lastGrabLadderDirection;
        canCrouch = false;
        canSprint = false;

    }

    private void DropLadder()
    {
        isClimbingLadder = false;
        canCrouch = true;
        canSprint = true;
    }
    private void HandleMouseLook()
    {
        rotationX -= Input.GetAxis("Mouse Y") * lookSpeedY;
        rotationX = Mathf.Clamp(rotationX, -upperLookLimit, lowerLookLimit);
        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
        transform.rotation *= Quaternion.Euler(0, Input.GetAxis("Mouse X") * lookSpeedX, 0);
    }

    private void HandleJump()
    {
        if (ShouldJump)
            moveDirection.y = jumpForce;
    }
    private void HandleCrouch()
    {
        if (ShouldCrouch)
            StartCoroutine(CrouchStand());
    }

    private void HandleHeadBob()
    {
        if (!characterController.isGrounded) 
            return;

        if(Mathf.Abs(moveDirection.x) > 0.1f || Mathf.Abs(moveDirection.z) > 0.1f)
        {
            timer += Time.deltaTime * (isCrouching ? crouchBobSpeed : walkBobSpeed);
            playerCamera.transform.localPosition = new Vector3(
                playerCamera.transform.localPosition.x,
                defaultYpos + Mathf.Sin(timer) * (isCrouching ? crouchBobAmount : walkBobAmount),
                playerCamera.transform.localPosition.z);
        }
    }

    private void HandleZoom()
    {
        if (Input.GetKeyDown(zoomKey))
        {
            if(zoomRoutine != null)
            {
                StopCoroutine(zoomRoutine);
                zoomRoutine = null;
            }

            zoomRoutine = StartCoroutine(ToggleZoom(true));
        }
        if (Input.GetKeyUp(zoomKey))
        {
            if (zoomRoutine != null)
            {
                StopCoroutine(zoomRoutine);
                zoomRoutine = null;
            }

            zoomRoutine = StartCoroutine(ToggleZoom(false));
        }
    }
    private void HandleInteractionCheck()
    {
        Ray viewpointRay = playerCamera.ViewportPointToRay(interactionRayPoint);

        if (Physics.Raycast(viewpointRay, 
            out RaycastHit hit, interactionDistance))
        {
            if(hit.collider.gameObject.layer == 9 && (currentInteractable == null || 
                hit.collider.gameObject.GetInstanceID() != currentInteractable.GetInstanceID()))
            {
                hit.collider.TryGetComponent(out currentInteractable);

                if (currentInteractable)
                    currentInteractable.OnFocus();
            }
        }
        else if (currentInteractable)
        {
            currentInteractable.OnLoseFocus();
            currentInteractable = null;
        }
    }

    private void HandleInteractionInput()
    {
        Ray viewpointRay = playerCamera.ViewportPointToRay(interactionRayPoint);

        if (Input.GetKeyDown(interactKey) && 
            currentInteractable != null && 
            Physics.Raycast(viewpointRay, out RaycastHit hit, interactionDistance, interactionLayer))
        {
            currentInteractable.OnInteract();
        }
    }

    private void ApplyDamage(float dmg)
    {
        currentHealth -= dmg;
        OnDamage?.Invoke(currentHealth);

        if (currentHealth <= 0)
            KillPlayer();

        else if (regeneratingHealth != null)
            StopCoroutine(regeneratingHealth);

        regeneratingHealth = StartCoroutine(RegenerateHealth());
    }

    private void KillPlayer()
    {
        currentHealth = 0;
        if (regeneratingHealth != null)
            StopCoroutine(regeneratingHealth);

        print("Dead");
    }

    public void TakeDamage(float Damage)
    {
        ApplyDamage(Damage);
        if (currentHealth >= maxHealth)
        {
            currentHealth = maxHealth;
        }
    }

    private void ApplyFinalMovements()
    {
        if(!characterController.isGrounded)
        
            moveDirection.y -= gravity * Time.deltaTime;

            characterController.Move(moveDirection * Time.deltaTime);
        
    }

    private IEnumerator CrouchStand()
    {
        float timeElapsed = 0;
        float targetHeight = isCrouching ? standingHeight : crouchHeight;
        float currentHeight = characterController.height;
        Vector3 targetCentre = isCrouching ? standingCenter : crouchingCenter;
        Vector3 currentCentre = characterController.center;

        if (isCrouching && Physics.Raycast(playerCamera.transform.position, Vector3.up, 1f))
            yield break;

        duringCrouchAnimation = true;

        while(timeElapsed < timeToCrouch)
        {
            characterController.height = Mathf.Lerp(currentHeight, targetHeight, timeElapsed / timeToCrouch);
            characterController.center = Vector3.Lerp(currentCentre, targetCentre, timeElapsed/ timeToCrouch);
            timeElapsed += Time.deltaTime;
            yield return null;
        }

        characterController.height = targetHeight;
        characterController.center = targetCentre;

        isCrouching = !isCrouching;

        duringCrouchAnimation = false;
    }

    private IEnumerator ToggleZoom(bool isEnter)
    {
        float targetFOV = isEnter ? zoomFOV : defaultFOV;
        float startingFOV = playerCamera.fieldOfView;
        float timeElapsed = 0;

        while(timeElapsed < timeToZoom)
        {
            playerCamera.fieldOfView = Mathf.Lerp(startingFOV, targetFOV, timeElapsed / timeToZoom);
            timeElapsed += Time.deltaTime;
            yield return null;
        }

        playerCamera.fieldOfView = targetFOV;
        zoomRoutine = null;
    }

    private IEnumerator RegenerateHealth()
    {
        yield return new WaitForSeconds(timeBeforeRegenStarts);
        WaitForSeconds timeToWait = new WaitForSeconds(healthTimeIncrement);

        while(currentHealth < maxHealth)
        {
            currentHealth += healthValueIncrement;

            if (currentHealth > maxHealth)
                currentHealth = maxHealth;

            OnHeal?.Invoke(currentHealth);
            yield return timeToWait;
        }

        regeneratingHealth = null;
    }


}
