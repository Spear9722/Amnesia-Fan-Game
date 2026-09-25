using UnityEngine;

///<Summary>
/// Amnesia-style first-person controller.
/// Attach a capsule GameObject with a CharacterController Component
/// The player Camera should be a child object, positioned ate eye level (assign it below)
/// 
/// Design notes (matches the feel of Amnesia: The Dark Descent):
///     - No Jumping.
///     - Walking is slow and deliberate; sprint exits but drains a stamina meter fast
///       and recovers slowly, so it can't be relied on constantly.
///     - Crouch lowers the capsule height, camera height, and moveent speed/noise
///       (useful for sneaking past monsters and fitting through gaps).
///     - Lean lets you peek around corners/doorways without exposing your whole body.
///       A raycast prevents the camera from leaning through walls. 
///     - Subtle head bob while moving, for that unsteady, immersive feel. 
///</Summary>

[RequireComponent(typeof(CharacterController))]
public class AmnesiaFirstPersonController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The child camera transform used for looking and head bob/lean.")]
    public Transform cameraTransform;

    [Header("Movement")]
    public float walkSpeed = 1.8f;
    public float sprintSpeed = 3.2f;
    public float crouchSpeed = 0.9f;
    [Tooltip("How quickly velocity ramps toward the target speed. Lower = heavier/more sluggish.")]
    public float acceleration = 6f;
    public float gravity = -18f;

    [Header("Stamina (limits sprinting)")]
    public float maxStamina = 5f;
    public float staminaDrainPerSecond = 1f;
    public float staminaRegenPerSecond = 0.5f;
    [Tooltip("Must regen back above this before you're allowed to sprint again once exhausted.")]
    public float staminaRegenThreshold = 1.5f;
    private float currentStamina;
    private bool staminaExhausted;

    [Header("Mouse Look")]
    public float mouseSensitivity = 2f;
    public float minPitch = -85f;
    public float maxPitch = 85f;
    private float pitch;

    [Header("Crouch")]
    public float standHeight = 1.8f;
    public float crouchHeight = 1.0f;
    public float standCameraY = 1.6f;
    public float crouchCameraY = 0.75f;
    public float crouchTransitionSpeed = 8f;
    private bool isCrouching;

    [Header("Lean")]
    public float maxLeanAngle = 15f;
    public float maxLeanOffset = 0.5f;
    public float leanSpeed = 6f;
    public LayerMask leanCollisionMask = ~0;
    private float currentLean; // -1 (left) to 1 (right)

    [Header("Head Bob")]
    public float bobFrequency = 1.6f;
    public float bobAmplitude = 0.035f;
    private float bobTimer;

    [Header("Footsteps (optional)")]
    public AudioSource footstepSource;
    public AudioClip[] walkFootsteps;
    public AudioClip[] crouchFootsteps;
    public float walkStepInterval = 0.55f;
    public float crouchStepInterval = 0.75f;
    private float stepTimer;

    private CharacterController controller;
    private Vector3 currentVelocity;
    private float verticalVelocity;
    private Vector3 cameraBasePosition;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        currentStamina = maxStamina;

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        controller.height = standHeight;
        controller.center = new Vector3(0, standHeight / 2f, 0);

        if (cameraTransform != null)
            cameraBasePosition = cameraTransform.localPosition;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        HandleMouseLook();
        HandleCrouchInput();
        HandleLeanInput();
        HandleMovement();
        HandleHeadBobAndFootsteps();
    }

    // ---------------- MOUSE LOOK ----------------

     void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        transform.Rotate(Vector3.up * mouseX);

        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(
                pitch, 
                0f, 
                cameraTransform.localEulerAngles.z
                );
    }

    // ---------------- MOVEMENT ----------------
    void HandleMovement()
    {
        float inputX = Input.GetAxisRaw("Horizontal");
        float inputZ = Input.GetAxisRaw("Vertical");
        Vector3 inputDir = (transform.right * inputX + transform.forward * inputZ);

        if (inputDir.sqrMagnitude > 1f) inputDir.Normalize();

        bool wantsSprint = Input.GetKey(KeyCode.LeftShift) && !isCrouching && inputDir.sqrMagnitude > 0.01f;
        bool isSprinting = wantsSprint && HandleStamina(true);
        if (!wantsSprint) HandleStamina(false);

        float targetSpeed = isCrouching ? crouchSpeed : (isSprinting ? sprintSpeed: walkSpeed);
        Vector3 targetVelocity = inputDir * targetSpeed;

        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, acceleration * Time.deltaTime);

        //Gravity/Grounding
        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity -= 1f; // small stick-to-ground value
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = currentVelocity;
        motion.y = verticalVelocity;
        controller.Move(motion * Time.deltaTime);
    }

    /// <Summary>
    /// Drains or regenerates stamina. Returns whether sprinting is currently allowed.
    /// </Summary>
    bool HandleStamina(bool draining)
    {
        if(draining)
        {
            if (staminaExhausted) return false;
            currentStamina -= staminaDrainPerSecond * Time.deltaTime;
            if (currentStamina <= 0f)
            {
                currentStamina = 0f;
                staminaExhausted = true;
                return false;
            }
            return true;
        }
        else
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenPerSecond * Time.deltaTime);
            if (currentStamina <= 0f && currentStamina >= staminaRegenThreshold)
                staminaExhausted = false;
            return false;
        }
    }

    // ---------------- CROUCH ----------------
    void HandleCrouchInput()
    {
        if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.C))
            isCrouching = !isCrouching;

        float targetHeight = isCrouching ? crouchHeight : standHeight;
        controller.height = Mathf.Lerp(controller.height, targetHeight, crouchTransitionSpeed * Time.deltaTime);
        controller.center = new Vector3(0, controller.height / 2f, 0);

        if (cameraTransform != null)
        {
            float targetCamY = isCrouching ? crouchCameraY : standCameraY;
            Vector3 pos = cameraBasePosition;
            pos.y = Mathf.Lerp(cameraTransform.localPosition.y, targetCamY, crouchTransitionSpeed * Time.deltaTime);
            cameraBasePosition = new Vector3(cameraBasePosition.x, pos.y, cameraBasePosition.z);
        }
    }

    // ---------------- LEAN ----------------
    void HandleLeanInput()
    {
        float leanInput = 0f;
        if (Input.GetKey(KeyCode.Q)) leanInput = -1f;
        else if (Input.GetKey(KeyCode.E)) leanInput = 1f;

        currentLean = Mathf.Lerp(currentLean, leanInput, leanSpeed * Time.deltaTime);

        if (cameraTransform == null) return;

        // Prevent leaning through walls with a quick raycast from the capsule center
        float desiredOffset = currentLean * maxLeanOffset;
        Vector3 rayOrigin = transform.position + Vector3.up * (controller.height * 0.6f);
        Vector3 rayDir = transform.right * Mathf.Sign(desiredOffset == 0 ? 1 : desiredOffset);
        float rayDist = Mathf.Abs(desiredOffset) + 0.2f;

        if(Mathf.Abs(desiredOffset) > 0.01f && Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, rayDist, leanCollisionMask))
        {
            desiredOffset = Mathf.Sign(desiredOffset) * Mathf.Max(0f, hit.distance - 0.2f);
        }

        Vector3 localPos = cameraBasePosition;
        localPos.x = desiredOffset;
        cameraTransform.localPosition = localPos + GetBobOffset();
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, -currentLean*maxLeanAngle);
    }

    // ---------------- HEAD BOB + FOOTSTEPS ----------------
    Vector3 GetBobOffset()
    {
        Vector2 flatVel = new Vector2(currentVelocity.x, currentVelocity.z);

        if (flatVel.magnitude < 0.1f || !controller.isGrounded)
        {
            bobTimer = 0f;
            return Vector3.zero;
        }

        bobTimer += Time.deltaTime * bobFrequency * (isCrouching ? 0.7f : 1f) * flatVel.magnitude;
        float bobY = Mathf.Sin(bobTimer * Mathf.PI * 2f) * bobAmplitude;
        float bobX = Mathf.Cos(bobTimer * Mathf.PI) * bobAmplitude * 0.5f;
        return new Vector3(bobX, Mathf.Abs(bobY), 0f);
    }

    void HandleHeadBobAndFootsteps()
    {
        // Re-apply position each frame in case lean didn't run (camera null-safety already handled there).
        if (cameraTransform == null) return;
        Vector2 flatVel = new Vector2(currentVelocity.x, currentVelocity.z);
        bool isMoving = flatVel.magnitude > 0.01f && controller.isGrounded;

        if(isMoving)
        {
            stepTimer = 0f;
            return;
        }

        float interval = isCrouching ? crouchStepInterval : walkStepInterval;
        stepTimer += Time.deltaTime;
        if (stepTimer >= interval)
        {
            stepTimer = 0f;
            PlayFootstep();
        }
    }

    void PlayFootstep()
    {
        if (footstepSource == null) return;
        AudioClip[] clips = isCrouching ? crouchFootsteps : walkFootsteps;
        if (clips == null || clips.Length == 0) return;

        AudioClip clip = clips[Random.Range(0, clips.Length)];
        footstepSource.PlayOneShot(clip);
    }

    // ---------------- PUBLIC HELPERS ----------------
    /// <summary>0-1 stamina fraction, handy for a UI meter.</summary>
    public float StaminaFraction => currentStamina / maxStamina;
    public bool IsCrouching => isCrouching;
    public bool IsSprintExhausted => staminaExhausted;
}