using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Синглтон для управления рынком труда с типизированными работниками
/// Поддерживает три типа работников: Farmers, Craftsmen, Artisans
/// Каждое здание требует конкретный тип работника
/// </summary>
public class WorkforceManager : MonoBehaviour
{
    public static WorkforceManager Instance { get; private set; }

    [Tooltip("Включить/Выключить всю систему 'Рынка Труда'")]
    public bool workforceSystemEnabled = true;

    // Требуемые работники по типам
    private Dictionary<PopulationTier, int> _totalRequiredWorkforce = new Dictionary<PopulationTier, int>();

    // Доступные работники по типам
    private Dictionary<PopulationTier, int> _totalAvailableWorkforce = new Dictionary<PopulationTier, int>();

    // Все зарегистрированные производители
    private List<ResourceProducer> _allProducers = new List<ResourceProducer>();

    // Для отображения в инспекторе
    [Header("Статистика Farmers (Смерды)")]
    [SerializeField] private int _farmersRequired = 0;
    [SerializeField] private int _farmersAvailable = 0;
    [SerializeField] private float _farmersRatio = 1.0f;

    [Header("Статистика Craftsmen (Посадские)")]
    [SerializeField] private int _craftsmenRequired = 0;
    [SerializeField] private int _craftsmenAvailable = 0;
    [SerializeField] private float _craftsmenRatio = 1.0f;

    [Header("Статистика Artisans (Цеховые)")]
    [SerializeField] private int _artisansRequired = 0;
    [SerializeField] private int _artisansAvailable = 0;
    [SerializeField] private float _artisansRatio = 1.0f;

    [Header("Статистика WhiteClergy (Белое духовенство)")]
    [SerializeField] private int _whiteClergyRequired = 0;
    [SerializeField] private int _whiteClergyAvailable = 0;
    [SerializeField] private float _whiteClergyRatio = 1.0f;

    [Header("Статистика BlackClergy (Черное духовенство)")]
    [SerializeField] private int _blackClergyRequired = 0;
    [SerializeField] private int _blackClergyAvailable = 0;
    [SerializeField] private float _blackClergyRatio = 1.0f;

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

        // Инициализируем словари
        InitializeWorkforceDictionaries();
    }

    void Update()
    {
        // Обновляем отображаемые значения для инспектора
        UpdateInspectorValues();
    }

    /// <summary>
    /// Инициализирует словари для всех типов работников
    /// </summary>
    private void InitializeWorkforceDictionaries()
    {
        _totalRequiredWorkforce.Clear();
        _totalAvailableWorkforce.Clear();

        foreach (PopulationTier tier in System.Enum.GetValues(typeof(PopulationTier)))
        {
            _totalRequiredWorkforce[tier] = 0;
            _totalAvailableWorkforce[tier] = 0;
        }
    }

    /// <summary>
    /// Регистрирует производителя и его требования к работникам
    /// </summary>
    public void RegisterProducer(ResourceProducer producer)
    {
        if (!workforceSystemEnabled || producer == null) return;

        // Добавляем в список производителей
        if (!_allProducers.Contains(producer))
        {
            _allProducers.Add(producer);
        }

        // Добавляем требование к работникам конкретного типа
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
    /// Обновляет доступных работников по типам (вызывается из PopulationManager)
    /// </summary>
    public void UpdateAvailableWorkforce(int farmers, int craftsmen, int artisans, int whiteClergy, int blackClergy)
    {
        _totalAvailableWorkforce[PopulationTier.Farmers] = farmers;
        _totalAvailableWorkforce[PopulationTier.Craftsmen] = craftsmen;
        _totalAvailableWorkforce[PopulationTier.Artisans] = artisans;
        _totalAvailableWorkforce[PopulationTier.WhiteClergy] = whiteClergy;
        _totalAvailableWorkforce[PopulationTier.BlackClergy] = blackClergy;

        Debug.Log($"[Workforce] Доступные работники обновлены: Farmers={farmers}, Craftsmen={craftsmen}, Artisans={artisans}, WhiteClergy={whiteClergy}, BlackClergy={blackClergy}");
    }

    /// <summary>
    /// Возвращает коэффициент доступности работников для конкретного типа (0.0 - 1.0)
    /// </summary>
    public float GetWorkforceRatio(PopulationTier tier)
    {
        if (!workforceSystemEnabled)
            return 1.0f; // Система выключена, лимита нет

        if (!_totalRequiredWorkforce.ContainsKey(tier) || !_totalAvailableWorkforce.ContainsKey(tier))
            return 1.0f;

        int required = _totalRequiredWorkforce[tier];
        if (required <= 0)
            return 1.0f; // Никто не требует этих работников

        int available = _totalAvailableWorkforce[tier];
        float ratio = (float)available / (float)required;

        return Mathf.Clamp01(ratio);
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

        // Возвращаем среднее значение по всем типам
        float totalRatio = 0f;
        int count = 0;

        foreach (PopulationTier tier in System.Enum.GetValues(typeof(PopulationTier)))
        {
            totalRatio += GetWorkforceRatio(tier);
            count++;
        }

        return (count > 0) ? (totalRatio / count) : 1.0f;
    }

    /// <summary>
    /// УСТАРЕВШИЙ метод для обратной совместимости (3 параметра)
    /// </summary>
    [System.Obsolete("Используйте UpdateAvailableWorkforce(int, int, int, int, int) вместо этого")]
    public void UpdateAvailableWorkforce(int farmers, int craftsmen, int artisans)
    {
        // Для обратной совместимости вызываем новую версию с нулями для духовенства
        UpdateAvailableWorkforce(farmers, craftsmen, artisans, 0, 0);
    }

    /// <summary>
    /// УСТАРЕВШИЙ метод для обратной совместимости (1 параметр)
    /// </summary>
    [System.Obsolete("Используйте UpdateAvailableWorkforce(int, int, int, int, int) вместо этого")]
    public void UpdateAvailableWorkforce(int totalPopulation)
    {
        // Для обратной совместимости считаем всех работников как Farmers
        UpdateAvailableWorkforce(totalPopulation, 0, 0, 0, 0);
    }

    /// <summary>
    /// Возвращает список всех производителей
    /// </summary>
    public List<ResourceProducer> GetAllProducers()
    {
        return _allProducers;
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
    /// Обновляет значения для отображения в инспекторе
    /// </summary>
    private void UpdateInspectorValues()
    {
        _farmersRequired = GetRequiredWorkforce(PopulationTier.Farmers);
        _farmersAvailable = GetAvailableWorkforce(PopulationTier.Farmers);
        _farmersRatio = GetWorkforceRatio(PopulationTier.Farmers);

        _craftsmenRequired = GetRequiredWorkforce(PopulationTier.Craftsmen);
        _craftsmenAvailable = GetAvailableWorkforce(PopulationTier.Craftsmen);
        _craftsmenRatio = GetWorkforceRatio(PopulationTier.Craftsmen);

        _artisansRequired = GetRequiredWorkforce(PopulationTier.Artisans);
        _artisansAvailable = GetAvailableWorkforce(PopulationTier.Artisans);
        _artisansRatio = GetWorkforceRatio(PopulationTier.Artisans);

        _whiteClergyRequired = GetRequiredWorkforce(PopulationTier.WhiteClergy);
        _whiteClergyAvailable = GetAvailableWorkforce(PopulationTier.WhiteClergy);
        _whiteClergyRatio = GetWorkforceRatio(PopulationTier.WhiteClergy);

        _blackClergyRequired = GetRequiredWorkforce(PopulationTier.BlackClergy);
        _blackClergyAvailable = GetAvailableWorkforce(PopulationTier.BlackClergy);
        _blackClergyRatio = GetWorkforceRatio(PopulationTier.BlackClergy);
    }
}