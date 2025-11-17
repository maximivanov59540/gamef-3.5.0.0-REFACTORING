using UnityEngine;

/// <summary>
/// Данные о событии, происходящем в здании
/// Хранит информацию о типе события и его длительности
/// </summary>
[System.Serializable]
public class BuildingEvent
{
    public EventType eventType = EventType.None;
    public float startTime;              // Время начала события (Time.time)
    public float duration;               // Длительность события в секундах

    /// <summary>
    /// Проверяет, активно ли событие в данный момент
    /// </summary>
    public bool IsActive()
    {
        if (eventType == EventType.None) return false;

        float elapsed = Time.time - startTime;
        return elapsed < duration;
    }

    /// <summary>
    /// Оставшееся время события в секундах
    /// </summary>
    public float RemainingTime()
    {
        if (!IsActive()) return 0f;

        float elapsed = Time.time - startTime;
        return Mathf.Max(0f, duration - elapsed);
    }

    /// <summary>
    /// Начинает новое событие
    /// </summary>
    public void Start(EventType type, float durationSeconds)
    {
        eventType = type;
        startTime = Time.time;
        duration = durationSeconds;
    }

    /// <summary>
    /// Завершает текущее событие
    /// </summary>
    public void End()
    {
        eventType = EventType.None;
        startTime = 0f;
        duration = 0f;
    }
}