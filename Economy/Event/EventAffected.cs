using UnityEngine;

/// <summary>
/// Компонент для зданий, которые могут быть подвержены событиям (пандемия, бунт)
/// Добавляется на жилые и производственные здания
/// </summary>
[RequireComponent(typeof(BuildingIdentity))]
public class EventAffected : MonoBehaviour
{
    [Header("Настройки")]
    [Tooltip("Может ли здание быть подвержено пандемии")]
    public bool canGetPandemic = true;

    [Tooltip("Может ли здание бунтовать")]
    public bool canRiot = true;

    [Header("Текущее Событие")]
    [SerializeField] private BuildingEvent _currentEvent = new BuildingEvent();

    // --- Публичные Свойства ---

    /// <summary>
    /// Есть ли активное событие в данный момент
    /// </summary>
    public bool HasActiveEvent => _currentEvent != null && _currentEvent.IsActive();

    /// <summary>
    /// Тип текущего события
    /// </summary>
    public EventType CurrentEventType => _currentEvent?.eventType ?? EventType.None;

    // --- Публичные Методы ---

    void Start()
    {
        // Регистрируем здание в EventManager
        if (EventManager.Instance != null)
        {
            EventManager.Instance.RegisterBuilding(this);
        }
    }

    void OnDestroy()
    {
        // Снимаем регистрацию при уничтожении
        if (EventManager.Instance != null)
        {
            EventManager.Instance.UnregisterBuilding(this);
        }
    }

    void Update()
    {
        // Автоматически завершаем событие, если время истекло
        if (HasActiveEvent && !_currentEvent.IsActive())
        {
            EndEvent();
        }
    }

    /// <summary>
    /// Начинает новое событие в здании
    /// </summary>
    /// <param name="eventType">Тип события</param>
    /// <param name="durationSeconds">Длительность в секундах</param>
    /// <returns>True, если событие успешно начато</returns>
    public bool StartEvent(EventType eventType, float durationSeconds)
    {
        // Проверяем, можно ли начать это событие
        if (eventType == EventType.Pandemic && !canGetPandemic)
        {
            Debug.LogWarning($"[EventAffected] {name}: Не может быть подвержено пандемии!");
            return false;
        }

        if (eventType == EventType.Riot && !canRiot)
        {
            Debug.LogWarning($"[EventAffected] {name}: Не может бунтовать!");
            return false;
        }

        // Проверяем, нет ли уже активного события
        if (HasActiveEvent)
        {
            Debug.LogWarning($"[EventAffected] {name}: Уже есть активное событие {CurrentEventType}!");
            return false;
        }

        // Начинаем событие
        _currentEvent.Start(eventType, durationSeconds);
        Debug.Log($"[EventAffected] {name}: Начато событие {eventType} на {durationSeconds} секунд");

        // Применяем эффекты события
        ApplyEventEffects();

        return true;
    }

    /// <summary>
    /// Завершает текущее событие
    /// </summary>
    public void EndEvent()
    {
        if (!HasActiveEvent) return;

        EventType endedEventType = CurrentEventType;
        _currentEvent.End();

        Debug.Log($"[EventAffected] {name}: Событие {endedEventType} завершено");

        // Убираем эффекты события
        RemoveEventEffects();
    }

    /// <summary>
    /// Принудительно завершает все события
    /// </summary>
    public void ForceEndAllEvents()
    {
        if (HasActiveEvent)
        {
            EndEvent();
        }
    }

    /// <summary>
    /// Применяет эффекты события (например, останавливает производство при бунте)
    /// </summary>
    private void ApplyEventEffects()
    {
        switch (CurrentEventType)
        {
            case EventType.Pandemic:
                // Пандемия: снижение счастья, возможно снижение налогов
                ApplyPandemicEffects();
                break;

            case EventType.Riot:
                // Бунт: остановка производства, снижение счастья
                ApplyRiotEffects();
                break;
        }
    }

    /// <summary>
    /// Убирает эффекты события
    /// </summary>
    private void RemoveEventEffects()
    {
        // Восстанавливаем нормальное состояние здания
        var producer = GetComponent<ResourceProducer>();
        if (producer != null)
        {
            producer.ResumeProduction(); // Возобновляем производство
        }
    }

    /// <summary>
    /// Применяет эффекты пандемии
    /// </summary>
    private void ApplyPandemicEffects()
    {
        // TODO: Реализовать снижение счастья/налогов при пандемии
        Debug.Log($"[EventAffected] {name}: Применены эффекты пандемии");
    }

    /// <summary>
    /// Применяет эффекты бунта
    /// </summary>
    private void ApplyRiotEffects()
    {
        // Останавливаем производство при бунте
        var producer = GetComponent<ResourceProducer>();
        if (producer != null)
        {
            producer.PauseProduction();
            Debug.Log($"[EventAffected] {name}: Производство остановлено из-за бунта");
        }

        // TODO: Реализовать снижение счастья при бунте
        Debug.Log($"[EventAffected] {name}: Применены эффекты бунта");
    }

    /// <summary>
    /// Возвращает оставшееся время текущего события
    /// </summary>
    public float GetRemainingEventTime()
    {
        return _currentEvent?.RemainingTime() ?? 0f;
    }
}