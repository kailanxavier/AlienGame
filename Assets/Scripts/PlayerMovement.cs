using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem.Editor;

public class PlayerMovement : MonoBehaviour {

    
    public bool CanMove { get; private set; } = true;
    private bool IsSprinting => canSprint && Input.GetKey(sprintKey);
    private bool ShouldJump => Input.GetKeyDown(jumpKey) && characterController.isGrounded;
    private bool ShouldCrouch => Input.GetKeyDown(crouchKey) && !duringCrouchAnimation && characterController.isGrounded;

    [Header("Functional Options: ")]
    [SerializeField] private bool canSprint = true;
    [SerializeField] private bool canJump = true;
    [SerializeField] private bool canCrouch = true;
    [SerializeField] private bool canUseHeadbob = true;
    [SerializeField] private bool willSlideOnSlopes = true;
    [SerializeField] private bool canZoom = true;


    [Header("Controls: ")]
    [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private KeyCode crouchKey = KeyCode.LeftControl;
    [SerializeField] private KeyCode zoomKey = KeyCode.Mouse1;

    [Header("Movement Parameters: ")]
    [SerializeField] private float walkSpeed = 3.0f;
    [SerializeField] private float sprintSpeed = 6.0f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float slopeSpeed = 5.0f;

    [Header("Sprint Parameters: ")]
    [SerializeField] private float timeToSprint = 0.4f;
    [SerializeField] private float sprintFOV = 90.0f;

    private Coroutine sprintRoutine;

    [Header("Look Parameters: ")]
    [SerializeField, Range(1, 10)] private float lookSpeedX = 2.0f;
    [SerializeField, Range(1, 10)] private float lookSpeedY = 2.0f;
    [SerializeField, Range(1, 180)] private float upperLookLimit = 80.0f;
    [SerializeField, Range(1, 180)] private float lowerLookLimit = 80.0f;

    [Header("Jumping Parameters: ")]
    [SerializeField] private float jumpForce = 8.0f;
    [SerializeField] private float gravity = 30.0f;

    [Header("Crouch Parameters: ")]
    [SerializeField] private float crouchHeight = 0.5f;
    [SerializeField] private float standingHeight = 2.0f;
    [SerializeField] private float timeToCrouch = 0.25f;
    [SerializeField] private Vector3 crouchingCenter = new Vector3(0, 0.5f, 0);
    [SerializeField] private Vector3 standingCenter = new Vector3(0, 0, 0);
    private bool isCrouching;
    private bool duringCrouchAnimation;

    [Header("Headbob Parameters: ")]
    [SerializeField] private float walkBobSpeed = 14.0f;
    [SerializeField] private float walkBobAmount = 0.05f;
    [SerializeField] private float sprintBobSpeed = 18.0f;
    [SerializeField] private float sprintBobAmount = 0.1f;
    [SerializeField] private float crouchBobSpeed = 8.0f;
    [SerializeField] private float crouchBobAmount = 0.025f;
    private float defaultYPos = 0;
    private float timer;

    [Header("Zoom Parameters: ")]
    [SerializeField] private float timeToZoom = 0.3f;
    [SerializeField] private float zoomFOV = 30f;

    private float defaultFOV = 70.0f;
    private Coroutine zoomRoutine;

    // Sliding Parameters: 
    private Vector3 hitPointNormal;

    private bool isSliding {
        get {
            if (characterController.isGrounded && Physics.Raycast(transform.position, Vector3.down, out RaycastHit slopeHit, 2.0f)) {
                hitPointNormal = slopeHit.normal;
                return Vector3.Angle(hitPointNormal, Vector3.up) > characterController.slopeLimit;
            } else {
                return false;
            }

        }
    }


    private Camera playerCamera;
    private CharacterController characterController;

    private Vector3 moveDirection;
    private Vector2 currentInput;

    private float rotationX = 0;

    private void Awake() {
        playerCamera = GetComponentInChildren<Camera>();
        characterController = GetComponent<CharacterController>();
        defaultYPos = playerCamera.transform.localPosition.y;
        defaultFOV = playerCamera.fieldOfView;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }


    private void Update() {
        if (CanMove) {
            HandleMovementInput();
            HandleMouseLook();

            if (canJump)
                HandleJump();

            if (canCrouch)
                HandleCrouch();

            if (canUseHeadbob)
                HandleHeadbob();

            if (canZoom)
                HandleZoom();

            ApplyFinalMovement();
        }
    }

    private void HandleMovementInput() {

        // Decide speed of the player based on the state (Sprinting, Walking, Crouching)
        currentInput = new Vector2((isCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed) * Input.GetAxisRaw("Vertical"), (isCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed) * Input.GetAxisRaw("Horizontal"));

        float moveDirectionY = moveDirection.y;
        moveDirection = (transform.TransformDirection(Vector3.forward) * currentInput.x) + (transform.TransformDirection(Vector3.right) * currentInput.y);
        moveDirection.y = moveDirectionY;


            if (Input.GetKeyUp(sprintKey)) {
                if (sprintRoutine != null) {
                    StopCoroutine(sprintRoutine);
                    sprintRoutine = null;
                }

                sprintRoutine = StartCoroutine(SprintFOV(true));
            }

            if (Input.GetKeyDown(sprintKey)) {
                if (sprintRoutine != null) {
                    StopCoroutine(sprintRoutine);
                    sprintRoutine = null;
                }

            sprintRoutine = StartCoroutine(SprintFOV(false));
        }
    }


    private void HandleMouseLook() {

        // Look up and down
        rotationX -= Input.GetAxisRaw("Mouse Y") * lookSpeedY;
        rotationX = Mathf.Clamp(rotationX, -upperLookLimit, lowerLookLimit);
        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);

        // Look left and right
        transform.rotation *= Quaternion.Euler(0, Input.GetAxisRaw("Mouse X") * lookSpeedX, 0);
    }

    private void HandleJump() {
        if (ShouldJump)
            moveDirection.y = jumpForce;
    }

    private void HandleCrouch() {
        if (ShouldCrouch)
            StartCoroutine(CrouchStand());
    }

    private void HandleHeadbob() {
        if (!characterController.isGrounded)
            return;

        if (Mathf.Abs(moveDirection.x) > 0.1f || Mathf.Abs(moveDirection.z) > 0.1f) {
            timer += Time.deltaTime * (isCrouching ? crouchBobSpeed : IsSprinting ? sprintBobSpeed : walkBobSpeed);
            playerCamera.transform.localPosition = new Vector3(
                playerCamera.transform.localPosition.x,
                defaultYPos + Mathf.Sin(timer) * (isCrouching ? crouchBobAmount : IsSprinting ? sprintBobAmount : walkBobAmount),
                playerCamera.transform.localPosition.z
                );
        }
    }

    private void HandleZoom() {

        if (playerCamera.fieldOfView <= 70) {
            if (Input.GetKeyUp(zoomKey)) {
                if (zoomRoutine != null) {
                    StopCoroutine(zoomRoutine);
                    zoomRoutine = null;
                }

                zoomRoutine = StartCoroutine(ToggleZoom(true));
            }

            if (Input.GetKeyDown(zoomKey)) {
                if (zoomRoutine != null) {
                    StopCoroutine(zoomRoutine);
                    zoomRoutine = null;
                }

                zoomRoutine = StartCoroutine(ToggleZoom(false));
            }
        }
    }

    private void ApplyFinalMovement() {
        if (!characterController.isGrounded)
            moveDirection.y -= gravity * Time.deltaTime;

        if (willSlideOnSlopes && isSliding)
            moveDirection += new Vector3(hitPointNormal.x, -hitPointNormal.y, hitPointNormal.z) * slopeSpeed;

        characterController.Move(moveDirection * Time.deltaTime);
    }

    private IEnumerator CrouchStand() {
        if (isCrouching && Physics.Raycast(playerCamera.transform.position, Vector3.up, 1f))
            yield break;

        duringCrouchAnimation = true;

        float timeElapsed = 0;
        float targetHeight = isCrouching ? standingHeight : crouchHeight;
        float currentHeight = characterController.height;
        Vector3 targetCenter = isCrouching ? standingCenter : crouchingCenter;
        Vector3 currentCenter = characterController.center;

        while (timeElapsed < timeToCrouch) {
            characterController.height = Mathf.Lerp(currentHeight, targetHeight, timeElapsed / timeToCrouch);
            characterController.center = Vector3.Lerp(currentCenter, targetCenter, timeElapsed / timeToCrouch);
            timeElapsed += Time.deltaTime;
            yield return null;
        }

        characterController.height = targetHeight;
        characterController.center = targetCenter;

        isCrouching = !isCrouching;

        duringCrouchAnimation = false;
    }

    private IEnumerator ToggleZoom(bool isEnter) {
        float targetFOV = isEnter ? defaultFOV : zoomFOV;
        float startingFOV = playerCamera.fieldOfView;
        float timeElapsed = 0;

        while (timeElapsed < timeToZoom) {
            playerCamera.fieldOfView = Mathf.Lerp(startingFOV, targetFOV, timeElapsed / timeToZoom);
            timeElapsed += Time.deltaTime;
            yield return false;
        }

        playerCamera.fieldOfView = targetFOV;
        zoomRoutine = null;
    }

    private IEnumerator SprintFOV(bool isEnter) {


        float targetFOV = isEnter ? defaultFOV : sprintFOV;
        float startingFOV = playerCamera.fieldOfView;
        float timeElapsed = 0;

                while (timeElapsed < timeToSprint) {
                    playerCamera.fieldOfView = Mathf.Lerp(startingFOV, targetFOV, timeElapsed / timeToSprint);
                    timeElapsed += Time.deltaTime;
                    yield return false;
                }

            playerCamera.fieldOfView = targetFOV;
            sprintRoutine = null;
    }
}