using UnityEngine;

/// <summary>
/// Менеджер времени для управления скоростью игры.
/// Поддерживает 4 режима: пауза (f=0), замедленно (f=0.5), обычная скорость (f=1), ускоренно (f=2)
/// </summary>
public class TimeManager : MonoBehaviour
{
    public static TimeManager Instance { get; private set; }

    public enum TimeScale
    {
        Paused = 0,      // f = 0
        Slow = 1,        // f = 0.5
        Normal = 2,      // f = 1
        Fast = 3         // f = 2
    }

    [Header("Настройки времени")]
    [Tooltip("Текущий режим времени")]
    [SerializeField] private TimeScale _currentTimeScale = TimeScale.Normal;

    [Header("Значения множителей")]
    [Tooltip("Множитель для паузы")]
    public float pausedMultiplier = 0f;
    [Tooltip("Множитель для замедленного режима")]
    public float slowMultiplier = 0.5f;
    [Tooltip("Множитель для обычного режима")]
    public float normalMultiplier = 1f;
    [Tooltip("Множитель для ускоренного режима")]
    public float fastMultiplier = 2f;

    public TimeScale CurrentTimeScale => _currentTimeScale;
    public float CurrentMultiplier { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Применяем начальную скорость
        ApplyTimeScale(_currentTimeScale);
    }

    /// <summary>
    /// Устанавливает новый режим времени
    /// </summary>
    public void SetTimeScale(TimeScale newScale)
    {
        if (_currentTimeScale != newScale)
        {
            _currentTimeScale = newScale;
            ApplyTimeScale(newScale);
            Debug.Log($"[TimeManager] Режим времени изменен на {newScale} (множитель: {CurrentMultiplier})");
        }
    }

    /// <summary>
    /// Применяет множитель времени в зависимости от режима
    /// </summary>
    private void ApplyTimeScale(TimeScale scale)
    {
        switch (scale)
        {
            case TimeScale.Paused:
                CurrentMultiplier = pausedMultiplier;
                Time.timeScale = pausedMultiplier;
                break;
            case TimeScale.Slow:
                CurrentMultiplier = slowMultiplier;
                Time.timeScale = slowMultiplier;
                break;
            case TimeScale.Normal:
                CurrentMultiplier = normalMultiplier;
                Time.timeScale = normalMultiplier;
                break;
            case TimeScale.Fast:
                CurrentMultiplier = fastMultiplier;
                Time.timeScale = fastMultiplier;
                break;
        }
    }

    /// <summary>
    /// Переключает на следующий режим времени по циклу
    /// </summary>
    public void CycleTimeScale()
    {
        int nextIndex = ((int)_currentTimeScale + 1) % 4;
        SetTimeScale((TimeScale)nextIndex);
    }

    /// <summary>
    /// Устанавливает паузу
    /// </summary>
    public void Pause()
    {
        SetTimeScale(TimeScale.Paused);
    }

    /// <summary>
    /// Снимает паузу и возвращает к обычной скорости
    /// </summary>
    public void Unpause()
    {
        SetTimeScale(TimeScale.Normal);
    }

    /// <summary>
    /// Проверяет, стоит ли игра на паузе
    /// </summary>
    public bool IsPaused()
    {
        return _currentTimeScale == TimeScale.Paused;
    }

    /// <summary>
    /// Возвращает скорректированное время с учетом timeScale
    /// Для систем, которые используют unscaledDeltaTime
    /// </summary>
    public float GetScaledDeltaTime()
    {
        return Time.unscaledDeltaTime * CurrentMultiplier;
    }
}