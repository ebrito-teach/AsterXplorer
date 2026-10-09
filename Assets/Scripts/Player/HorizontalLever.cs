using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

public class HorizontalLever : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [System.Serializable] public class FloatEvent : UnityEvent<float> { }

    [Header("Referencias")]
    [SerializeField] private RectTransform track;   // vacío = usa este mismo objeto
    [SerializeField] private RectTransform handle;  // la perilla

    [Header("Ajustes")]
    [SerializeField] private float deadZone = 0.08f;
    [SerializeField] private float returnSpeed = 6f;

    [Header("Sprites de la perilla")]
    [SerializeField] private Sprite handleNormal;
    [SerializeField] private Sprite handleActive;

    [SerializeField] private FloatEvent onValueChanged;

    Image handleImage;
    float value;
    bool held;

    void Awake()
    {
        if (track == null) track = transform as RectTransform;
        if (handle != null)
        {
            handleImage = handle.GetComponent<Image>();
            if (handleImage != null) handleImage.preserveAspect = true;
        }
        SetActiveVisual(false);
    }

    public void OnPointerDown(PointerEventData e) { held = true; SetActiveVisual(true); UpdateFrom(e); }
    public void OnDrag(PointerEventData e) => UpdateFrom(e);
    public void OnPointerUp(PointerEventData e) { held = false; SetActiveVisual(false); }

    void UpdateFrom(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            track, e.position, e.pressEventCamera, out Vector2 local);

        // El punto local es relativo al PIVOTE; se resta el centro del rectángulo
        // para que 0 sea siempre el medio del riel, sea cual sea el pivote.
        float centerX = track.rect.center.x;
        SetValue(Mathf.Clamp((local.x - centerX) / HalfRange(), -1f, 1f));
    }

    void Update()
    {
        if (!held && Mathf.Abs(value) > 0.001f)
            SetValue(Mathf.MoveTowards(value, 0f, returnSpeed * Time.unscaledDeltaTime));
    }

    float HalfRange() =>
        Mathf.Max(1f, track.rect.width * 0.5f - handle.rect.width * 0.5f);

    void SetValue(float v)
    {
        value = v;
        handle.anchoredPosition = new Vector2(value * HalfRange(), 0f);
        onValueChanged?.Invoke(Mathf.Abs(value) < deadZone ? 0f : value);
    }

    void SetActiveVisual(bool active)
    {
        if (handleImage == null) return;
        if (handleNormal != null && handleActive != null)
            handleImage.sprite = active ? handleActive : handleNormal;
    }

    void OnDisable() { held = false; SetActiveVisual(false); SetValue(0f); }
}