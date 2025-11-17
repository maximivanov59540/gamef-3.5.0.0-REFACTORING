using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Менеджер населения с поддержкой трех уровней (градаций) населения
/// Система работает по принципу Anno 1800:
/// - Farmers (смерды) - низший класс
/// - Craftsmen (посадские) - средний класс
/// - Artisans (цеховые) - высший класс
/// </summary>
public class PopulationManager : MonoBehaviour
{
    public static PopulationManager Instance { get; private set; }

    [Header("Население по уровням")]
    [Tooltip("Текущее население по каждому уровню")]
    [SerializeField] private Dictionary<PopulationTier, int> _currentPopulation = new Dictionary<PopulationTier, int>();

    [Tooltip("Максимальное население (лимит жилья) по каждому уровню")]
    [SerializeField] private Dictionary<PopulationTier, int> _maxPopulation = new Dictionary<PopulationTier, int>();

    // Для отображения в инспекторе (так как Dictionary не сериализуется)
    [Header("Статистика (только для чтения)")]
    [SerializeField] private int _farmersCurrent = 0;
    [SerializeField] private int _farmersMax = 0;
    [SerializeField] private int _craftsmenCurrent = 0;
    [SerializeField] private int _craftsmenMax = 0;
    [SerializeField] private int _artisansCurrent = 0;
    [SerializeField] private int _artisansMax = 0;
    [SerializeField] private int _whiteClergyCurrent = 0;
    [SerializeField] private int _whiteClergyMax = 0;
    [SerializeField] private int _blackClergyCurrent = 0;
    [SerializeField] private int _blackClergyMax = 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Инициализируем словари для всех уровней
        InitializePopulationDictionaries();
    }

    void Start()
    {
        // Сообщаем WorkforceManager о начальном населении
        UpdateWorkforceManager();
    }

    void Update()
    {
        // Обновляем отображаемые значения для инспектора
        UpdateInspectorValues();
    }

    /// <summary>
    /// Инициализирует словари для всех уровней населения
    /// </summary>
    private void InitializePopulationDictionaries()
    {
        _currentPopulation.Clear();
        _maxPopulation.Clear();

        foreach (PopulationTier tier in System.Enum.GetValues(typeof(PopulationTier)))
        {
            _currentPopulation[tier] = 0;
            _maxPopulation[tier] = 0;
        }
    }

    /// <summary>
    /// Добавляет лимит жилья для конкретного уровня населения
    /// </summary>
    /// <param name="tier">Уровень населения</param>
    /// <param name="amount">Количество мест</param>
    public void AddHousingCapacity(PopulationTier tier, int amount)
    {
        if (!_maxPopulation.ContainsKey(tier))
        {
            Debug.LogError($"[PopulationManager] Неизвестный уровень населения: {tier}");
            return;
        }

        _maxPopulation[tier] += amount;
        Debug.Log($"[PopulationManager] Лимит жилья для {tier} увеличен на {amount}. Новый лимит: {_maxPopulation[tier]}");

        UpdateWorkforceManager();
    }

    /// <summary>
    /// Удаляет лимит жилья для конкретного уровня населения
    /// </summary>
    /// <param name="tier">Уровень населения</param>
    /// <param name="amount">Количество мест</param>
    public void RemoveHousingCapacity(PopulationTier tier, int amount)
    {
        if (!_maxPopulation.ContainsKey(tier))
        {
            Debug.LogError($"[PopulationManager] Неизвестный уровень населения: {tier}");
            return;
        }

        _maxPopulation[tier] -= amount;
        if (_maxPopulation[tier] < 0)
        {
            _maxPopulation[tier] = 0;
        }
        Debug.Log($"[PopulationManager] Лимит жилья для {tier} уменьшен на {amount}. Новый лимит: {_maxPopulation[tier]}");

        UpdateWorkforceManager();
    }

    /// <summary>
    /// Устанавливает текущее население для конкретного уровня
    /// Вызывается из Residence при пересчете жителей на основе удовлетворения потребностей
    /// </summary>
    /// <param name="tier">Уровень населения</param>
    /// <param name="amount">Количество жителей</param>
    public void SetCurrentPopulation(PopulationTier tier, int amount)
    {
        if (!_currentPopulation.ContainsKey(tier))
        {
            Debug.LogError($"[PopulationManager] Неизвестный уровень населения: {tier}");
            return;
        }

        int oldAmount = _currentPopulation[tier];
        _currentPopulation[tier] = Mathf.Clamp(amount, 0, _maxPopulation[tier]);

        if (oldAmount != _currentPopulation[tier])
        {
            Debug.Log($"[PopulationManager] Текущее население {tier} изменено: {oldAmount} -> {_currentPopulation[tier]}");
            UpdateWorkforceManager();
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

    /// <summary>
    /// Обновляет WorkforceManager о доступных работниках каждого типа
    /// Население конвертируется 1 к 1 в работников
    /// </summary>
    private void UpdateWorkforceManager()
    {
        if (WorkforceManager.Instance == null) return;

        // Передаем максимальное население по каждому уровню
        // (в реальности можно использовать текущее население, если нужна динамика)
        WorkforceManager.Instance.UpdateAvailableWorkforce(
            _maxPopulation[PopulationTier.Farmers],
            _maxPopulation[PopulationTier.Craftsmen],
            _maxPopulation[PopulationTier.Artisans],
            _maxPopulation[PopulationTier.WhiteClergy],
            _maxPopulation[PopulationTier.BlackClergy]
        );
    }

    /// <summary>
    /// Обновляет значения для отображения в инспекторе
    /// </summary>
    private void UpdateInspectorValues()
    {
        _farmersCurrent = GetCurrentPopulation(PopulationTier.Farmers);
        _farmersMax = GetMaxPopulation(PopulationTier.Farmers);
        _craftsmenCurrent = GetCurrentPopulation(PopulationTier.Craftsmen);
        _craftsmenMax = GetMaxPopulation(PopulationTier.Craftsmen);
        _artisansCurrent = GetCurrentPopulation(PopulationTier.Artisans);
        _artisansMax = GetMaxPopulation(PopulationTier.Artisans);
        _whiteClergyCurrent = GetCurrentPopulation(PopulationTier.WhiteClergy);
        _whiteClergyMax = GetMaxPopulation(PopulationTier.WhiteClergy);
        _blackClergyCurrent = GetCurrentPopulation(PopulationTier.BlackClergy);
        _blackClergyMax = GetMaxPopulation(PopulationTier.BlackClergy);
    }

    // --- УСТАРЕВШИЕ МЕТОДЫ (для обратной совместимости) ---
    // Эти методы оставлены для совместимости со старым кодом
    // Они работают только с уровнем Farmers

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