using TMPro;
using UnityEngine;

/// El velocímetro: un número pequeño que casi nunca miras, y que cuando cambia lo es todo.
///
/// POR QUÉ SE ENCIENDE AL ESTAR A SALVO Y NO AL ESTAR EN PELIGRO
///
/// La velocidad letal coincide con la de crucero, así que el jugador pasa la partida entera
/// por encima del umbral. Un indicador que avisara del peligro estaría rojo el noventa por
/// ciento del tiempo y el ojo lo aprendería a ignorar en dos niveles.
///
/// Invertido funciona: lo normal es ir a velocidad mortal, y lo excepcional es poder tocar
/// algo. El número se enciende en el momento raro —cuando frenas o entras en preciso— y lo
/// que comunica no es "cuidado" sino "ahora sí puedes rozar esa roca". Que es exactamente la
/// decisión que el jugador tiene que tomar.
///
/// Va en un objeto del Canvas con un TMP_Text.
public class SpeedReadout : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Vacío = lo busca en la escena")]
    [SerializeField] private PlayerController player;
    [Tooltip("Vacío = el TMP_Text de este objeto")]
    [SerializeField] private TMP_Text label;

    [Header("Unidades")]
    [Tooltip("Metros por unidad de mundo. A la escala del juego, 1 unidad son 100 m, así que " +
             "la velocidad de crucero (5 u/s) se lee como 500 m/s")]
    [SerializeField] private float metersPerUnit = 100f;
    [SerializeField] private string suffix = " m/s";
    [Tooltip("Redondeo del número. 10 = de diez en diez, para que no baile un dígito cada " +
             "fotograma y acabe siendo ruido en vez de información")]
    [SerializeField] private float roundTo = 10f;

    [Header("Aspecto")]
    [Tooltip("Color normal: vas a velocidad mortal, que es casi siempre")]
    [SerializeField] private Color fastColor = new Color(1f, 1f, 1f, 0.45f);
    [Tooltip("Color cuando puedes rozar una roca quieta sin morir. Este es el que importa")]
    [SerializeField] private Color safeColor = new Color(0.45f, 1f, 0.75f, 0.95f);
    [Tooltip("Lo rápido que cambia de color. Un corte seco se lee como un parpadeo de error")]
    [SerializeField] private float fadeSpeed = 8f;
    [Tooltip("Margen por debajo del umbral antes de considerarte a salvo. Evita que el color " +
             "parpadee cuando la velocidad oscila justo en la raya")]
    [SerializeField] private float safeMargin = 0.3f;

    Color current;
    bool safe;

    void Awake()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (label == null) label = GetComponent<TMP_Text>();
        if (label == null)
        {
            Debug.LogWarning("SpeedReadout: no hay ningún TMP_Text donde escribir.", this);
            enabled = false;
            return;
        }
        current = fastColor;
        label.color = current;
    }

    void Update()
    {
        if (player == null || label == null) return;

        float v = player.Speed;

        // Histéresis: para pasar a "a salvo" hay que bajar del umbral MENOS el margen, y para
        // salir basta con alcanzarlo. Sin esto, una velocidad que oscila en la raya enciende y
        // apaga el color varias veces por segundo.
        float umbral = player.LethalSpeed;
        safe = safe ? v < umbral : v < umbral - safeMargin;

        float mostrado = v * metersPerUnit;
        if (roundTo > 0.01f) mostrado = Mathf.Round(mostrado / roundTo) * roundTo;
        label.text = Mathf.RoundToInt(mostrado).ToString() + suffix;

        Color quiero = safe ? safeColor : fastColor;
        current = Color.Lerp(current, quiero, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
        label.color = current;
    }
}
