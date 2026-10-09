using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [System.Serializable] public class BoolEvent : UnityEvent<bool> { }

    [SerializeField] private BoolEvent onHoldChanged;

    [Header("Sprites")]
    [SerializeField] private Sprite normalSprite;
    [SerializeField] private Sprite pressedSprite;

    Image image;
    bool held;

    void Awake()
    {
        image = GetComponent<Image>();
        image.preserveAspect = true;
        Apply(false);
    }

    public void OnPointerDown(PointerEventData e) { SetHeld(true); }
    public void OnPointerUp(PointerEventData e)   { SetHeld(false); }

    // Si el botón se desactiva mientras está pulsado, se suelta
    void OnDisable() { if (held) SetHeld(false); }

    void SetHeld(bool value)
    {
        if (held == value) return;
        held = value;
        Apply(value);
        onHoldChanged?.Invoke(value);
    }

    void Apply(bool pressed)
    {
        if (image == null) return;
        if (normalSprite != null && pressedSprite != null)
            image.sprite = pressed ? pressedSprite : normalSprite;
    }
}