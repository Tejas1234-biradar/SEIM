using UnityEngine;

/// <summary>
/// Simple free-fly camera controller for exploring the city scene.
/// Attach directly to your Camera GameObject.
///
/// Controls:
///   W/A/S/D  - move forward/left/back/right
///   Q/E      - move down/up
///   Shift    - sprint (faster movement)
///   Mouse    - look around
///   Esc      - unlock cursor (click back into Game view to re-lock)
///
/// NOTE: Uses the legacy Input Manager (Input.GetAxis / Input.GetKey).
/// If your project has "Active Input Handling" set to "Input System Package (New)"
/// only (Edit > Project Settings > Player > Active Input Handling), this script
/// won't receive input. Set it to "Both" to use this without extra setup.
/// </summary>
public class CameraFlyController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;
    public float sprintMultiplier = 3f;
    public float verticalSpeed = 8f; // Q/E up-down speed

    [Header("Mouse Look")]
    public float mouseSensitivity = 2.5f;
    public bool invertY = false;

    private float yaw;
    private float pitch;
    private bool cursorLocked = true;

    void Start()
    {
        // Initialize yaw/pitch from the object's current rotation so it doesn't snap on play
        Vector3 startEuler = transform.eulerAngles;
        yaw = startEuler.y;
        pitch = startEuler.x;

        LockCursor(true);
    }

    void Update()
    {
        HandleCursorToggle();

        if (cursorLocked)
        {
            HandleMouseLook();
        }

        HandleMovement();
    }

    private void HandleCursorToggle()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            LockCursor(false);
        }
        // Re-lock when clicking back into the Game view
        if (!cursorLocked && Input.GetMouseButtonDown(0))
        {
            LockCursor(true);
        }
    }

    private void LockCursor(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void HandleMouseLook()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * (invertY ? 1f : -1f);

        yaw += mouseX;
        pitch += mouseY;
        pitch = Mathf.Clamp(pitch, -89f, 89f); // prevent flipping over

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private void HandleMovement()
    {
        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? sprintMultiplier : 1f);

        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += transform.forward;
        if (Input.GetKey(KeyCode.S)) move -= transform.forward;
        if (Input.GetKey(KeyCode.A)) move -= transform.right;
        if (Input.GetKey(KeyCode.D)) move += transform.right;

        // Q/E move straight up/down in world space, independent of look direction
        float vertical = 0f;
        if (Input.GetKey(KeyCode.E)) vertical += 1f;
        if (Input.GetKey(KeyCode.Q)) vertical -= 1f;

        transform.position += move.normalized * speed * Time.deltaTime;
        transform.position += Vector3.up * vertical * verticalSpeed * Time.deltaTime;
    }
}
