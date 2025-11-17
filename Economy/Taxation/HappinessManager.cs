using UnityEngine;

/// <summary>
/// Менеджер счастья населения.
/// Отслеживает общий уровень счастья и влияет на налоги, события и другие механики.
/// </summary>
public class HappinessManager : MonoBehaviour
{
    // --- Синглтон ---
    public static HappinessManager Instance { get; private set; }

    [Header("=== Настройки Счастья ===")]
    [Tooltip("Текущий уровень счастья (может быть отрицательным)")]
    [SerializeField] private float _currentHappiness = 0f;

    [Tooltip("Минимальный уровень счастья (для UI)")]
    public float minHappiness = -100f;

    [Tooltip("Максимальный уровень счастья (для UI)")]
    public float maxHappiness = 100f;

    // === События счастья ===")]
    public event System.Action<float> OnHappinessChanged;

    // --- Unity Lifecycle ---

    void Awake()
    {
        // Синглтон
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // --- Публичные методы ---

    /// <summary>
    /// Добавляет счастье (положительное или отрицательное значение)
    /// </summary>
    public void AddHappiness(float amount)
    {
        _currentHappiness += amount;

        // Ограничиваем диапазон (опционально)
        // _currentHappiness = Mathf.Clamp(_currentHappiness, minHappiness, maxHappiness);

        // Уведомляем подписчиков
        OnHappinessChanged?.Invoke(_currentHappiness);

        Debug.Log($"[HappinessManager] Счастье изменено на {amount:+0.0;-0.0}. Текущее: {_currentHappiness:F1}");
    }

    /// <summary>
    /// Устанавливает счастье на конкретное значение
    /// </summary>
    public void SetHappiness(float value)
    {
        _currentHappiness = value;
        OnHappinessChanged?.Invoke(_currentHappiness);

        Debug.Log($"[HappinessManager] Счастье установлено на {_currentHappiness:F1}");
    }

    /// <summary>
    /// Возвращает текущий уровень счастья
    /// </summary>
    public float GetCurrentHappiness()
    {
        return _currentHappiness;
    }

    /// <summary>
    /// Возвращает нормализованное счастье (0.0 - 1.0)
    /// где 0.0 = minHappiness, 1.0 = maxHappiness
    /// </summary>
    public float GetNormalizedHappiness()
    {
        // Преобразуем диапазон [minHappiness, maxHappiness] в [0, 1]
        float range = maxHappiness - minHappiness;
        if (range <= 0) return 0.5f;

        float normalized = (_currentHappiness - minHappiness) / range;
        return Mathf.Clamp01(normalized);
    }

    /// <summary>
    /// Возвращает модификатор счастья для событий (0.0 - 2.0)
    /// Низкое счастье = высокий модификатор (больше шанс событий)
    /// Высокое счастье = низкий модификатор (меньше шанс событий)
    ///
    /// Примеры:
    /// - Счастье = -100 → модификатор = 2.0 (вдвое больше шансов на события)
    /// - Счастье = 0 → модификатор = 1.0 (базовый шанс)
    /// - Счастье = 100 → модификатор = 0.0 (минимальный шанс событий)
    /// </summary>
    public float GetEventChanceModifier()
    {
        // Нормализуем счастье (0.0 - 1.0)
        float normalized = GetNormalizedHappiness();

        // Инвертируем: низкое счастье → высокий модификатор
        // normalized = 0.0 (очень несчастливы) → модификатор = 2.0
        // normalized = 0.5 (нейтральные) → модификатор = 1.0
        // normalized = 1.0 (очень счастливы) → модификатор = 0.0
        float modifier = 2.0f * (1.0f - normalized);

        return modifier;
    }
}