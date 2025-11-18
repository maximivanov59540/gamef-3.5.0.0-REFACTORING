using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Центральный менеджер для управления событиями (пандемия, бунт)
/// Singleton паттерн
/// </summary>
public class EventManager : MonoBehaviour
{
    public static EventManager Instance { get; private set; }

    [Header("Настройки Системы Событий")]
    [Tooltip("Включить/выключить систему событий")]
    public bool eventsEnabled = true;

    [Tooltip("Интервал проверки событий (в минутах)")]
    [Range(0.5f, 30f)]
    public float eventCheckIntervalMinutes = 1f;

    [Header("Разблокировка Событий")]
    [Tooltip("Пандемии разблокированы (обычно после постройки первой больницы)")]
    public bool pandemicsUnlocked = false;

    [Tooltip("Бунты разблокированы (обычно после постройки первого полицейского участка)")]
    public bool riotsUnlocked = false;

    [Header("Базовые Шансы Событий")]
    [Tooltip("Базовый шанс пандемии при проверке (0-1, например 0.07 = 7%)")]
    [Range(0f, 1f)]
    public float basePandemicChance = 0.07f;

    [Tooltip("Базовый шанс бунта при проверке (0-1, например 0.07 = 7%)")]
    [Range(0f, 1f)]
    public float baseRiotChance = 0.07f;

    [Header("Длительность Событий")]
    [Tooltip("Длительность пандемии (в секундах)")]
    public float pandemicDurationSeconds = 300f; // 5 минут

    [Tooltip("Длительность бунта (в секундах)")]
    public float riotDurationSeconds = 180f; // 3 минуты

    [Header("Влияние Счастья")]
    [Tooltip("Множитель влияния счастья на шанс событий (чем выше счастье, тем ниже шанс)")]
    [Range(0f, 5f)]
    public float happinessMultiplier = 1.5f;

    [Tooltip("При счастье = 100, шанс события умножается на это значение (например, 0.1 = -90% к шансу)")]
    [Range(0f, 1f)]
    public float maxHappinessReduction = 0.1f;

    [Header("Статистика (только для чтения)")]
    [SerializeField] private int _totalBuildings = 0;
    [SerializeField] private int _buildingsWithPandemic = 0;
    [SerializeField] private int _buildingsWithRiot = 0;
    [SerializeField] private float _nextCheckTime = 0f;

    // --- Внутреннее Состояние ---

    private List<EventAffected> _allBuildings = new List<EventAffected>();
    private float _lastCheckTime = 0f;

    // --- Unity Lifecycle ---

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        _lastCheckTime = Time.time;
        _nextCheckTime = Time.time + (eventCheckIntervalMinutes * 60f);
    }

    void Update()
    {
        if (!eventsEnabled) return;

        UpdateStatistics();

        // Проверяем, пора ли запускать проверку событий
        if (Time.time >= _nextCheckTime)
        {
            CheckForEvents();
            _lastCheckTime = Time.time;
            _nextCheckTime = Time.time + (eventCheckIntervalMinutes * 60f);
        }
    }

    // --- Регистрация Зданий ---

    /// <summary>
    /// Регистрирует здание в системе событий
    /// </summary>
    public void RegisterBuilding(EventAffected building)
    {
        if (building == null) return;

        if (!_allBuildings.Contains(building))
        {
            _allBuildings.Add(building);
            Debug.Log($"[EventManager] Зарегистрировано здание: {building.name}. Всего: {_allBuildings.Count}");
        }
    }

    /// <summary>
    /// Снимает регистрацию здания
    /// </summary>
    public void UnregisterBuilding(EventAffected building)
    {
        if (building == null) return;

        _allBuildings.Remove(building);
        Debug.Log($"[EventManager] Снято с регистрации: {building.name}. Осталось: {_allBuildings.Count}");
    }

    // --- Проверка Событий ---

    /// <summary>
    /// Главная логика проверки событий
    /// </summary>
    private void CheckForEvents()
    {
        Debug.Log($"[EventManager] Проверка событий ({_allBuildings.Count} зданий)...");

        // FIX #16: Используем кешированные счетчики вместо LINQ .Any()
        bool hasActivePandemic = _buildingsWithPandemic > 0;
        bool hasActiveRiot = _buildingsWithRiot > 0;

        // Определяем, какое событие может произойти
        bool canTriggerPandemic = pandemicsUnlocked && !hasActiveRiot;
        bool canTriggerRiot = riotsUnlocked && !hasActivePandemic;

        // Если оба типа событий могут произойти, делаем отдельные проверки
        EventType eventToTrigger = EventType.None;

        if (canTriggerPandemic && canTriggerRiot)
        {
            // Оба события доступны - проверяем оба шанса
            float pandemicRoll = Random.value;
            float riotRoll = Random.value;

            bool pandemicTriggered = pandemicRoll < basePandemicChance;
            bool riotTriggered = riotRoll < baseRiotChance;

            // Если оба сработали - выбираем случайно
            if (pandemicTriggered && riotTriggered)
            {
                eventToTrigger = Random.value < 0.5f ? EventType.Pandemic : EventType.Riot;
                Debug.Log($"[EventManager] Оба события выбраны! Случайный выбор: {eventToTrigger}");
            }
            else if (pandemicTriggered)
            {
                eventToTrigger = EventType.Pandemic;
            }
            else if (riotTriggered)
            {
                eventToTrigger = EventType.Riot;
            }
        }
        else if (canTriggerPandemic)
        {
            // Только пандемия доступна
            if (Random.value < basePandemicChance)
            {
                eventToTrigger = EventType.Pandemic;
            }
        }
        else if (canTriggerRiot)
        {
            // Только бунт доступен
            if (Random.value < baseRiotChance)
            {
                eventToTrigger = EventType.Riot;
            }
        }

        // Запускаем выбранное событие
        if (eventToTrigger != EventType.None)
        {
            TriggerEvent(eventToTrigger);
        }
        else
        {
            Debug.Log($"[EventManager] Событие не сработало");
        }
    }

    /// <summary>
    /// Запускает событие на случайном здании
    /// </summary>
    private void TriggerEvent(EventType eventType)
    {
        // FIX #15: Заменили LINQ на простой цикл (убираем GC аллокации)
        List<EventAffected> eligibleBuildings = new List<EventAffected>();

        foreach (var b in _allBuildings)
        {
            // Пропускаем null и здания с активными событиями
            if (b == null || b.HasActiveEvent)
                continue;

            // Проверяем по типу события
            bool canAffect = eventType == EventType.Pandemic ? b.canGetPandemic : b.canRiot;
            if (!canAffect)
                continue;

            // Дополнительная фильтрация для пандемии (только жилые здания)
            if (eventType == EventType.Pandemic)
            {
                // Кешируем Residence в EventAffected.Awake для избежания GetComponent здесь
                if (b.GetComponent<Residence>() == null)
                    continue;
            }

            eligibleBuildings.Add(b);
        }

        if (eligibleBuildings.Count == 0)
        {
            Debug.LogWarning($"[EventManager] Нет подходящих зданий для события {eventType}");
            return;
        }

        // Выбираем случайное здание с учетом модификаторов
        EventAffected targetBuilding = SelectBuildingWithModifiers(eligibleBuildings, eventType);

        if (targetBuilding == null)
        {
            Debug.LogWarning($"[EventManager] Не удалось выбрать целевое здание для {eventType}");
            return;
        }

        // Запускаем событие
        float duration = eventType == EventType.Pandemic ? pandemicDurationSeconds : riotDurationSeconds;
        bool success = targetBuilding.StartEvent(eventType, duration);

        if (success)
        {
            Debug.Log($"[EventManager] ✅ Событие {eventType} начато в {targetBuilding.name}!");
        }
    }

    /// <summary>
    /// Выбирает здание с учетом модификаторов шанса (счастье, ауры, потребности)
    /// </summary>
    private EventAffected SelectBuildingWithModifiers(List<EventAffected> buildings, EventType eventType)
    {
        if (buildings.Count == 0) return null;

        // Вычисляем вес для каждого здания
        Dictionary<EventAffected, float> weights = new Dictionary<EventAffected, float>();

        foreach (var building in buildings)
        {
            float weight = CalculateBuildingEventChance(building, eventType);
            weights[building] = weight;
        }

        // Выбираем здание с учетом весов (здания с большим шансом выбираются чаще)
        float totalWeight = weights.Values.Sum();
        if (totalWeight <= 0f)
        {
            // Все шансы = 0, выбираем случайно
            return buildings[Random.Range(0, buildings.Count)];
        }

        float randomValue = Random.value * totalWeight;
        float cumulativeWeight = 0f;

        foreach (var kvp in weights)
        {
            cumulativeWeight += kvp.Value;
            if (randomValue <= cumulativeWeight)
            {
                return kvp.Key;
            }
        }

        // Fallback
        return buildings[Random.Range(0, buildings.Count)];
    }

    /// <summary>
    /// Вычисляет итоговый шанс события для конкретного здания
    /// Учитывает счастье, ауры, потребности
    /// </summary>
    private float CalculateBuildingEventChance(EventAffected building, EventType eventType)
    {
        float baseChance = eventType == EventType.Pandemic ? basePandemicChance : baseRiotChance;

        // 1. Влияние счастья (получаем из HappinessManager)
        float happinessModifier = GetHappinessModifier();

        // 2. Влияние аур (больницы/полицейские участки)
        float auraModifier = GetAuraModifier(building, eventType);

        // 3. Влияние потребностей (для жилых зданий)
        float needsModifier = GetNeedsModifier(building, eventType);

        // Итоговый шанс = базовый шанс * модификаторы
        float finalChance = baseChance * happinessModifier * auraModifier * needsModifier;

        Debug.Log($"[EventManager] Шанс {eventType} для {building.name}: {finalChance:F4} " +
                  $"(base={baseChance}, happiness={happinessModifier:F2}, aura={auraModifier:F2}, needs={needsModifier:F2})");

        return Mathf.Clamp01(finalChance);
    }

    /// <summary>
    /// Возвращает модификатор от счастья (0-1, где 1 = нет изменений, <1 = снижение шанса)
    /// </summary>
    private float GetHappinessModifier()
    {
        if (HappinessManager.Instance == null) return 1f;

        // Используем специальный метод для событий, который учитывает диапазон счастья
        float modifier = HappinessManager.Instance.GetEventChanceModifier();

        return Mathf.Clamp(modifier, 0.01f, 10f);
    }

    /// <summary>
    /// Возвращает модификатор от аур (больницы/полицейские участки)
    /// </summary>
    private float GetAuraModifier(EventAffected building, EventType eventType)
    {
        if (AuraManager.Instance == null) return 1f;

        var identity = building.GetComponent<BuildingIdentity>();
        if (identity == null) return 1f;

        Vector2Int buildingPos = identity.rootGridPosition;

        // Ищем ауры нужного типа
        AuraType auraType = eventType == EventType.Pandemic
            ? AuraType.Hospital
            : AuraType.Police;

        // Получаем все излучатели нужного типа
        var emitters = FindObjectsByType<AuraEmitter>(FindObjectsSortMode.None)
            .Where(e => e.type == auraType && e.IsActive())
            .ToList();

        float totalReduction = 0f;

        foreach (var emitter in emitters)
        {
            // Проверяем, находится ли здание в радиусе ауры
            if (emitter.IsBuildingInRange(buildingPos))
            {
                totalReduction += emitter.eventChanceReduction;
            }
        }

        // Модификатор = 1 - суммарное снижение (например, 0.3 снижения = модификатор 0.7)
        float modifier = Mathf.Clamp01(1f - totalReduction);

        return modifier;
    }

    /// <summary>
    /// Возвращает модификатор от потребностей (только для Residence)
    /// </summary>
    private float GetNeedsModifier(EventAffected building, EventType eventType)
    {
        var residence = building.GetComponent<Residence>();
        if (residence == null) return 1f;

        // Получаем снижение шанса события от удовлетворенных потребностей
        float reduction = 0f;

        if (eventType == EventType.Pandemic)
        {
            // Например, "Мешочек целебных трав" снижает шанс пандемии
            reduction = residence.GetPandemicChanceReduction();
        }
        else if (eventType == EventType.Riot)
        {
            // Например, "Развлечения" снижают шанс бунта
            reduction = residence.GetRiotChanceReduction();
        }

        // Модификатор = 1 - снижение (например, 0.2 снижения = модификатор 0.8)
        float modifier = Mathf.Clamp01(1f - reduction);

        return modifier;
    }

    // --- Утилиты ---

    /// <summary>
    /// Обновляет статистику для Inspector
    /// </summary>
    private void UpdateStatistics()
    {
        _totalBuildings = _allBuildings.Count;
        _buildingsWithPandemic = _allBuildings.Count(b => b.CurrentEventType == EventType.Pandemic);
        _buildingsWithRiot = _allBuildings.Count(b => b.CurrentEventType == EventType.Riot);
    }

    /// <summary>
    /// Разблокирует пандемии (вызывается при постройке первой больницы)
    /// </summary>
    public void UnlockPandemics()
    {
        pandemicsUnlocked = true;
        Debug.Log("[EventManager] Пандемии разблокированы!");
    }

    /// <summary>
    /// Разблокирует бунты (вызывается при постройке первого полицейского участка)
    /// </summary>
    public void UnlockRiots()
    {
        riotsUnlocked = true;
        Debug.Log("[EventManager] Бунты разблокированы!");
    }

    /// <summary>
    /// Принудительно завершает все события в городе (для отладки)
    /// </summary>
    public void ForceEndAllEvents()
    {
        foreach (var building in _allBuildings)
        {
            if (building != null && building.HasActiveEvent)
            {
                building.EndEvent();
            }
        }

        Debug.Log("[EventManager] Все события принудительно завершены");
    }
}