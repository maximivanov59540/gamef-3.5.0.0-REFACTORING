using System.Collections.Generic;
using UnityEngine;

public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    [Header("Стартовые Лимиты")]
    [Tooltip("Начальный лимит для всех ресурсов (до постройки складов)")]
    public float baseResourceLimit = 50f;
    public Dictionary<ResourceType, StorageData> GlobalStorage = new Dictionary<ResourceType, StorageData>();

    // --- Событие для UI ---
    // (UIResourceDisplay сможет подписаться на него, чтобы обновляться не каждый кадр)
    public event System.Action<ResourceType> OnResourceChanged;

    // --- Population & Workforce Data (ранее PopulationManager + WorkforceManager) ---
    public PopulationData Population { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }

        // Инициализируем Population (объединяет PopulationManager + WorkforceManager)
        Population = new PopulationData();

        InitializeResources();
    }

    void Update()
    {
        // Обновляем Population (для Inspector values)
        if (Population != null)
        {
            Population.UpdateInspectorValues();
        }
    }
    public bool CanAfford(List<ResourceCost> costs)
    {
        if (costs == null || costs.Count == 0) return true; // (Бесплатный апгрейд)

        foreach (var cost in costs)
        {
            if (GetResourceAmount(cost.resourceType) < cost.amount)
            {
                return false;
            }
        }
        return true;
    }

    public void SpendResources(List<ResourceCost> costs)
    {
        if (costs == null) return;

        foreach (var cost in costs)
        {
            SpendResources(cost.resourceType, cost.amount);
        }
    }

    private void InitializeResources()
    {
        GlobalStorage.Clear();
        foreach (ResourceType resourceType in System.Enum.GetValues(typeof(ResourceType)))
        {
            // Создаем "слот" на складе с базовым лимитом
            GlobalStorage.Add(resourceType, new StorageData(0, baseResourceLimit));
        }

        // Выдаем стартовые ресурсы
        GlobalStorage[ResourceType.Wood].currentAmount = 100f;
        GlobalStorage[ResourceType.Stone].currentAmount = 50f;
        
        // Сразу "заполняем" лимит (если стартовые ресурсы > лимита)
        if (GlobalStorage[ResourceType.Wood].currentAmount > GlobalStorage[ResourceType.Wood].maxAmount)
             GlobalStorage[ResourceType.Wood].currentAmount = GlobalStorage[ResourceType.Wood].maxAmount;
             
        if (GlobalStorage[ResourceType.Stone].currentAmount > GlobalStorage[ResourceType.Stone].maxAmount)
             GlobalStorage[ResourceType.Stone].currentAmount = GlobalStorage[ResourceType.Stone].maxAmount;
    }

    public void IncreaseGlobalLimit(float amount)
    {
        foreach (var slot in GlobalStorage.Values)
        {
            slot.maxAmount += amount;
            if (slot.maxAmount < 0) slot.maxAmount = 0; // Защита от отрицательного лимита
        }
        
        // (Можно добавить вызов общего события, чтобы UI обновил лимиты)
        OnResourceChanged?.Invoke(ResourceType.Wood); // Просто "пинаем" UI
    }

    public float AddToStorage(ResourceType type, float amount)
    {
        if (!GlobalStorage.ContainsKey(type)) return 0;

        StorageData slot = GlobalStorage[type];
        float spaceAvailable = slot.maxAmount - slot.currentAmount;

        if (spaceAvailable <= 0) return 0; // Склад полон

        float amountToAdd = Mathf.Min(amount, spaceAvailable);
        slot.currentAmount += amountToAdd;

        OnResourceChanged?.Invoke(type); // Уведомляем UI
        return amountToAdd;
    }

    public float TakeFromStorage(ResourceType type, float amount)
    {
        if (!GlobalStorage.ContainsKey(type)) return 0;

        StorageData slot = GlobalStorage[type];

        if (slot.currentAmount <= 0) return 0; // Пусто

        float amountToTake = Mathf.Min(amount, slot.currentAmount);
        slot.currentAmount -= amountToTake;
        
        OnResourceChanged?.Invoke(type); // Уведомляем UI
        return amountToTake;
    }

    public void SpendResources(ResourceType type, int amount)
    {
        // Просто вызываем наш новый float-метод
        TakeFromStorage(type, (float)amount);
    }
    
    // --- ОБНОВЛЕННЫЕ МЕТОДЫ (для совместимости) ---

    public float GetResourceAmount(ResourceType type)
    {
        if (GlobalStorage.ContainsKey(type))
        {
            return GlobalStorage[type].currentAmount;
        }
        return 0;
    }
    
    public float GetResourceLimit(ResourceType type)
    {
        if (GlobalStorage.ContainsKey(type))
        {
            return GlobalStorage[type].maxAmount;
        }
        return 0;
    }

    public bool CanAfford(BuildingData data)
    {
        if (data.costs == null) return true;

        foreach (var cost in data.costs)
        {
            // Проверяем по ТЕКУЩЕМУ количеству на складе
            if (GetResourceAmount(cost.resourceType) < cost.amount)
            {
                return false;
            }
        }
        return true;
    }

    public void SpendResources(BuildingData data)
    {
        if (data.costs == null) return;

        foreach (var cost in data.costs)
        {
            SpendResources(cost.resourceType, cost.amount);
        }
    }
}

/// <summary>
/// Класс для управления населением и рынком труда
/// Объединяет функциональность PopulationManager и WorkforceManager
/// Система работает по принципу Anno 1800:
/// - Farmers (смерды) - низший класс
/// - Craftsmen (посадские) - средний класс
/// - Artisans (цеховые) - высший класс
/// - WhiteClergy (белое духовенство)
/// - BlackClergy (черное духовенство)
/// </summary>
[System.Serializable]
public class PopulationData
{
    // FIX #17: Кешируем Enum.GetValues для избежания аллокаций
    private static readonly PopulationTier[] AllTiers = (PopulationTier[])System.Enum.GetValues(typeof(PopulationTier));

    // 🔔 PERF FIX: События для уведомления UI об изменениях населения
    public event System.Action<PopulationTier> OnPopulationChanged;
    public event System.Action OnAnyPopulationChanged;

    // --- Population Data (ранее PopulationManager) ---
    private Dictionary<PopulationTier, int> _currentPopulation = new Dictionary<PopulationTier, int>();
    private Dictionary<PopulationTier, int> _maxPopulation = new Dictionary<PopulationTier, int>();

    // --- Workforce Data (ранее WorkforceManager) ---
    [UnityEngine.Tooltip("Включить/Выключить всю систему 'Рынка Труда'")]
    public bool workforceSystemEnabled = true;

    private Dictionary<PopulationTier, int> _totalRequiredWorkforce = new Dictionary<PopulationTier, int>();
    private Dictionary<PopulationTier, int> _totalAvailableWorkforce = new Dictionary<PopulationTier, int>();
    private List<ResourceProducer> _allProducers = new List<ResourceProducer>();

    // Для отображения в инспекторе (так как Dictionary не сериализуется)
    [UnityEngine.Header("Статистика Farmers (Смерды)")]
    public int farmersCurrent = 0;
    public int farmersMax = 0;
    public int farmersRequired = 0;
    public int farmersAvailable = 0;
    public float farmersRatio = 1.0f;

    [UnityEngine.Header("Статистика Craftsmen (Посадские)")]
    public int craftsmenCurrent = 0;
    public int craftsmenMax = 0;
    public int craftsmenRequired = 0;
    public int craftsmenAvailable = 0;
    public float craftsmenRatio = 1.0f;

    [UnityEngine.Header("Статистика Artisans (Цеховые)")]
    public int artisansCurrent = 0;
    public int artisansMax = 0;
    public int artisansRequired = 0;
    public int artisansAvailable = 0;
    public float artisansRatio = 1.0f;

    [UnityEngine.Header("Статистика WhiteClergy (Белое духовенство)")]
    public int whiteClergyCurrent = 0;
    public int whiteClergyMax = 0;
    public int whiteClergyRequired = 0;
    public int whiteClergyAvailable = 0;
    public float whiteClergyRatio = 1.0f;

    [UnityEngine.Header("Статистика BlackClergy (Черное духовенство)")]
    public int blackClergyCurrent = 0;
    public int blackClergyMax = 0;
    public int blackClergyRequired = 0;
    public int blackClergyAvailable = 0;
    public float blackClergyRatio = 1.0f;

    /// <summary>
    /// Конструктор - инициализирует все словари
    /// </summary>
    public PopulationData()
    {
        InitializeDictionaries();
    }

    /// <summary>
    /// Инициализирует словари для всех уровней населения и работников
    /// </summary>
    private void InitializeDictionaries()
    {
        _currentPopulation.Clear();
        _maxPopulation.Clear();
        _totalRequiredWorkforce.Clear();
        _totalAvailableWorkforce.Clear();

        foreach (PopulationTier tier in AllTiers)
        {
            _currentPopulation[tier] = 0;
            _maxPopulation[tier] = 0;
            _totalRequiredWorkforce[tier] = 0;
            _totalAvailableWorkforce[tier] = 0;
        }
    }

    // ==================== POPULATION METHODS (ранее PopulationManager) ====================

    /// <summary>
    /// Добавляет лимит жилья для конкретного уровня населения
    /// </summary>
    public void AddHousingCapacity(PopulationTier tier, int amount)
    {
        if (!_maxPopulation.ContainsKey(tier))
        {
            Debug.LogError($"[PopulationData] Неизвестный уровень населения: {tier}");
            return;
        }

        _maxPopulation[tier] += amount;
        Debug.Log($"[PopulationData] Лимит жилья для {tier} увеличен на {amount}. Новый лимит: {_maxPopulation[tier]}");

        UpdateWorkforce();

        OnPopulationChanged?.Invoke(tier);
        OnAnyPopulationChanged?.Invoke();
    }

    /// <summary>
    /// Удаляет лимит жилья для конкретного уровня населения
    /// </summary>
    public void RemoveHousingCapacity(PopulationTier tier, int amount)
    {
        if (!_maxPopulation.ContainsKey(tier))
        {
            Debug.LogError($"[PopulationData] Неизвестный уровень населения: {tier}");
            return;
        }

        _maxPopulation[tier] -= amount;
        if (_maxPopulation[tier] < 0)
        {
            _maxPopulation[tier] = 0;
        }
        Debug.Log($"[PopulationData] Лимит жилья для {tier} уменьшен на {amount}. Новый лимит: {_maxPopulation[tier]}");

        UpdateWorkforce();

        OnPopulationChanged?.Invoke(tier);
        OnAnyPopulationChanged?.Invoke();
    }

    /// <summary>
    /// Устанавливает текущее население для конкретного уровня
    /// </summary>
    public void SetCurrentPopulation(PopulationTier tier, int amount)
    {
        if (!_currentPopulation.ContainsKey(tier))
        {
            Debug.LogError($"[PopulationData] Неизвестный уровень населения: {tier}");
            return;
        }

        int oldAmount = _currentPopulation[tier];
        _currentPopulation[tier] = UnityEngine.Mathf.Clamp(amount, 0, _maxPopulation[tier]);

        if (oldAmount != _currentPopulation[tier])
        {
            Debug.Log($"[PopulationData] Текущее население {tier} изменено: {oldAmount} -> {_currentPopulation[tier]}");
            UpdateWorkforce();

            OnPopulationChanged?.Invoke(tier);
            OnAnyPopulationChanged?.Invoke();
        }
    }

    /// <summary>
    /// Возвращает текущее население для конкретного уровня
    /// </summary>
    public int GetCurrentPopulation(PopulationTier tier)
    {
        return _currentPopulation.ContainsKey(tier) ? _currentPopulation[tier] : 0;
    }

    /// <summary>
    /// Возвращает максимальное население (лимит жилья) для конкретного уровня
    /// </summary>
    public int GetMaxPopulation(PopulationTier tier)
    {
        return _maxPopulation.ContainsKey(tier) ? _maxPopulation[tier] : 0;
    }

    /// <summary>
    /// Возвращает общее текущее население (все уровни)
    /// </summary>
    public int GetTotalCurrentPopulation()
    {
        int total = 0;
        foreach (var pop in _currentPopulation.Values)
        {
            total += pop;
        }
        return total;
    }

    /// <summary>
    /// Возвращает общий лимит жилья (все уровни)
    /// </summary>
    public int GetTotalMaxPopulation()
    {
        int total = 0;
        foreach (var max in _maxPopulation.Values)
        {
            total += max;
        }
        return total;
    }

    // ==================== WORKFORCE METHODS (ранее WorkforceManager) ====================

    /// <summary>
    /// Регистрирует производителя и его требования к работникам
    /// </summary>
    public void RegisterProducer(ResourceProducer producer)
    {
        if (!workforceSystemEnabled || producer == null) return;

        if (!_allProducers.Contains(producer))
        {
            _allProducers.Add(producer);
        }

        PopulationTier requiredTier = producer.requiredWorkerType;
        int requiredAmount = producer.workforceRequired;

        if (_totalRequiredWorkforce.ContainsKey(requiredTier))
        {
            _totalRequiredWorkforce[requiredTier] += requiredAmount;
            Debug.Log($"[Workforce] Зарегистрирован: {producer.name} (Требует: {requiredAmount} x {requiredTier}). " +
                      $"ОБЩАЯ ПОТРЕБНОСТЬ {requiredTier}: {_totalRequiredWorkforce[requiredTier]}");
        }
    }

    /// <summary>
    /// Снимает регистрацию производителя
    /// </summary>
    public void UnregisterProducer(ResourceProducer producer)
    {
        if (!workforceSystemEnabled || producer == null) return;

        _allProducers.Remove(producer);

        PopulationTier requiredTier = producer.requiredWorkerType;
        int requiredAmount = producer.workforceRequired;

        if (_totalRequiredWorkforce.ContainsKey(requiredTier))
        {
            _totalRequiredWorkforce[requiredTier] -= requiredAmount;
            if (_totalRequiredWorkforce[requiredTier] < 0)
                _totalRequiredWorkforce[requiredTier] = 0;

            Debug.Log($"[Workforce] Снят с регистрации: {producer.name}. " +
                      $"ОБЩАЯ ПОТРЕБНОСТЬ {requiredTier}: {_totalRequiredWorkforce[requiredTier]}");
        }
    }

    /// <summary>
    /// Возвращает коэффициент доступности работников для конкретного типа (0.0 - 1.0)
    /// </summary>
    public float GetWorkforceRatio(PopulationTier tier)
    {
        if (!workforceSystemEnabled)
            return 1.0f;

        if (!_totalRequiredWorkforce.ContainsKey(tier) || !_totalAvailableWorkforce.ContainsKey(tier))
            return 1.0f;

        int required = _totalRequiredWorkforce[tier];
        if (required <= 0)
            return 1.0f;

        int available = _totalAvailableWorkforce[tier];
        float ratio = (float)available / (float)required;

        return UnityEngine.Mathf.Clamp01(ratio);
    }

    /// <summary>
    /// Возвращает количество доступных работников конкретного типа
    /// </summary>
    public int GetAvailableWorkforce(PopulationTier tier)
    {
        return _totalAvailableWorkforce.ContainsKey(tier) ? _totalAvailableWorkforce[tier] : 0;
    }

    /// <summary>
    /// Возвращает количество требуемых работников конкретного типа
    /// </summary>
    public int GetRequiredWorkforce(PopulationTier tier)
    {
        return _totalRequiredWorkforce.ContainsKey(tier) ? _totalRequiredWorkforce[tier] : 0;
    }

    /// <summary>
    /// Возвращает список всех производителей
    /// </summary>
    public List<ResourceProducer> GetAllProducers()
    {
        return _allProducers;
    }

    // ==================== INTERNAL HELPERS ====================

    /// <summary>
    /// Обновляет доступных работников на основе максимального населения
    /// Вызывается автоматически при изменении населения
    /// </summary>
    private void UpdateWorkforce()
    {
        // Население конвертируется 1 к 1 в работников
        _totalAvailableWorkforce[PopulationTier.Farmers] = _maxPopulation[PopulationTier.Farmers];
        _totalAvailableWorkforce[PopulationTier.Craftsmen] = _maxPopulation[PopulationTier.Craftsmen];
        _totalAvailableWorkforce[PopulationTier.Artisans] = _maxPopulation[PopulationTier.Artisans];
        _totalAvailableWorkforce[PopulationTier.WhiteClergy] = _maxPopulation[PopulationTier.WhiteClergy];
        _totalAvailableWorkforce[PopulationTier.BlackClergy] = _maxPopulation[PopulationTier.BlackClergy];

        Debug.Log($"[Workforce] Доступные работники обновлены на основе населения");
    }

    /// <summary>
    /// Обновляет значения для отображения в инспекторе
    /// Вызывается из ResourceManager.Update()
    /// </summary>
    public void UpdateInspectorValues()
    {
        // Population stats
        farmersCurrent = GetCurrentPopulation(PopulationTier.Farmers);
        farmersMax = GetMaxPopulation(PopulationTier.Farmers);
        craftsmenCurrent = GetCurrentPopulation(PopulationTier.Craftsmen);
        craftsmenMax = GetMaxPopulation(PopulationTier.Craftsmen);
        artisansCurrent = GetCurrentPopulation(PopulationTier.Artisans);
        artisansMax = GetMaxPopulation(PopulationTier.Artisans);
        whiteClergyCurrent = GetCurrentPopulation(PopulationTier.WhiteClergy);
        whiteClergyMax = GetMaxPopulation(PopulationTier.WhiteClergy);
        blackClergyCurrent = GetCurrentPopulation(PopulationTier.BlackClergy);
        blackClergyMax = GetMaxPopulation(PopulationTier.BlackClergy);

        // Workforce stats
        farmersRequired = GetRequiredWorkforce(PopulationTier.Farmers);
        farmersAvailable = GetAvailableWorkforce(PopulationTier.Farmers);
        farmersRatio = GetWorkforceRatio(PopulationTier.Farmers);

        craftsmenRequired = GetRequiredWorkforce(PopulationTier.Craftsmen);
        craftsmenAvailable = GetAvailableWorkforce(PopulationTier.Craftsmen);
        craftsmenRatio = GetWorkforceRatio(PopulationTier.Craftsmen);

        artisansRequired = GetRequiredWorkforce(PopulationTier.Artisans);
        artisansAvailable = GetAvailableWorkforce(PopulationTier.Artisans);
        artisansRatio = GetWorkforceRatio(PopulationTier.Artisans);

        whiteClergyRequired = GetRequiredWorkforce(PopulationTier.WhiteClergy);
        whiteClergyAvailable = GetAvailableWorkforce(PopulationTier.WhiteClergy);
        whiteClergyRatio = GetWorkforceRatio(PopulationTier.WhiteClergy);

        blackClergyRequired = GetRequiredWorkforce(PopulationTier.BlackClergy);
        blackClergyAvailable = GetAvailableWorkforce(PopulationTier.BlackClergy);
        blackClergyRatio = GetWorkforceRatio(PopulationTier.BlackClergy);
    }

    // ==================== OBSOLETE METHODS (для обратной совместимости) ====================

    [System.Obsolete("Используйте AddHousingCapacity(PopulationTier, int) вместо этого")]
    public void AddHousingCapacity(int amount)
    {
        AddHousingCapacity(PopulationTier.Farmers, amount);
    }

    [System.Obsolete("Используйте RemoveHousingCapacity(PopulationTier, int) вместо этого")]
    public void RemoveHousingCapacity(int amount)
    {
        RemoveHousingCapacity(PopulationTier.Farmers, amount);
    }

    /// <summary>
    /// УСТАРЕВШИЙ метод для обратной совместимости
    /// Возвращает общий коэффициент (среднее по всем типам)
    /// </summary>
    [System.Obsolete("Используйте GetWorkforceRatio(PopulationTier) для конкретного типа работников")]
    public float GetWorkforceRatio()
    {
        if (!workforceSystemEnabled)
            return 1.0f;

        float totalRatio = 0f;
        int count = 0;

        foreach (PopulationTier tier in AllTiers)
        {
            totalRatio += GetWorkforceRatio(tier);
            count++;
        }

        return (count > 0) ? (totalRatio / count) : 1.0f;
    }

    // Публичные свойства для обратной совместимости
    public int currentPopulation
    {
        get => GetCurrentPopulation(PopulationTier.Farmers);
        set => SetCurrentPopulation(PopulationTier.Farmers, value);
    }

    public int maxPopulation
    {
        get => GetMaxPopulation(PopulationTier.Farmers);
    }
}