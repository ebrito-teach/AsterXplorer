using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ShipInput : MonoBehaviour
{
    public float Steer { get; private set; }      // -1 izquierda ... 1 derecha
    public bool ThrustHeld { get; private set; }
    public bool BrakeHeld { get; private set; }

    float uiSteer;
    bool uiThrust, uiBrake;

    // Los llama la UI táctil
    public void SetSteer(float value) => uiSteer = Mathf.Clamp(value, -1f, 1f);
    public void SetThrust(bool held) => uiThrust = held;
    public void SetBrake(bool held) => uiBrake = held;

    void Update()
    {
        float kbSteer = 0f;
        bool kbThrust = false, kbBrake = false;

#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null)
        {
            kbSteer = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f)
                    - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            kbThrust = k.wKey.isPressed || k.upArrowKey.isPressed;
            kbBrake = k.sKey.isPressed || k.downArrowKey.isPressed || k.spaceKey.isPressed;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        kbSteer = Input.GetAxisRaw("Horizontal");
        kbThrust = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        kbBrake = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.Space);
#endif

        Steer = Mathf.Abs(kbSteer) > 0.01f ? kbSteer : uiSteer;
        ThrustHeld = uiThrust || kbThrust;
        BrakeHeld = uiBrake || kbBrake;
    }
}