using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// "Входной" инвентарь (буфер сырья) для производственного здания.
/// Может ПРИНИМАТЬ ресурсы (IResourceReceiver).
/// </summary>
[RequireComponent(typeof(BuildingIdentity))]
public class BuildingInputInventory : MonoBehaviour, IResourceReceiver
{
    [Tooltip("Список требуемого сырья и его вместимость (настраивается в Инспекторе)")]
    public List<StorageData> requiredResources;
    
    [Header("Логистика Запросов")]
    [Tooltip("Приоритет доставки (1-5). Тележки выберут того, у кого '5'")]
    [Range(1, 5)] public int priority = 3;

    [Tooltip("Создать 'Запрос', когда склад опустеет до этого % (0.0 - 1.0)")]
    [Range(0f, 1f)] public float requestThresholdPercent = 0.25f; // 25%

    [Tooltip("Снять 'Запрос', когда склад заполнится до этого % (0.0 - 1.0)")]
    [Range(0f, 1f)] public float fulfillThresholdPercent = 0.8f; // 80%

    private Dictionary<ResourceType, ResourceRequest> _activeRequests = new Dictionary<ResourceType, ResourceRequest>();

    // ISSUE #8 FIX: Кэшированный словарь для O(1) lookup вместо O(n) FirstOrDefault
    private Dictionary<ResourceType, StorageData> _resourceLookup = new Dictionary<ResourceType, StorageData>();

    private BuildingIdentity _identity;
    private RoadManager _roadManager; // UPDATED: LogisticsManager объединен с RoadManager
    public bool IsRequesting { get; private set; } = false;

    // ════════════════════════════════════════════════════════════════
    //                      ИНИЦИАЛИЗАЦИЯ
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _identity = GetComponent<BuildingIdentity>();

        if (_identity == null)
        {
            Debug.LogWarning($"[BuildingInputInventory] {gameObject.name} не имеет BuildingIdentity!");
        }
    }

    // 🚀 PERFORMANCE FIX: Автоматическая регистрация в BuildingRegistry
    private void OnEnable()
    {
        BuildingRegistry.Instance?.RegisterInput(this);
    }

    private void OnDisable()
    {
        BuildingRegistry.Instance?.UnregisterInput(this);
    }

    private void Start()
    {
        _roadManager = RoadManager.Instance; // UPDATED: LogisticsManager теперь объединен с RoadManager

        if (_roadManager == null)
        {
            Debug.LogError($"[InputInv] {gameObject.name} не нашел RoadManager.Instance!");
        }

        // ISSUE #8 FIX: Инициализируем словарь для быстрого доступа
        RebuildResourceLookup();
    }

    /// <summary>
    /// ISSUE #8 FIX: Строит словарь для O(1) доступа к ресурсам
    /// </summary>
    private void RebuildResourceLookup()
    {
        _resourceLookup.Clear();
        foreach (var slot in requiredResources)
        {
            if (slot != null)
            {
                _resourceLookup[slot.resourceType] = slot;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //                   СТАРЫЕ МЕТОДЫ (НЕ МЕНЯЕМ)
    // ════════════════════════════════════════════════════════════════

    private void Update()
    {
        if (_logistics == null) return;
        
        // Проверяем КАЖДЫЙ слот сырья
        foreach (var slot in requiredResources)
        {
            if (slot.maxAmount <= 0) continue;

            bool isRequestActive = _activeRequests.ContainsKey(slot.resourceType);
            float fillRatio = slot.currentAmount / slot.maxAmount;

            // 1. ЛОГИКА СОЗДАНИЯ ЗАПРОСА
            if (!isRequestActive && fillRatio <= requestThresholdPercent)
            {
                CreateRequest(slot);
            }
            // 2. ЛОГИКА ОТМЕНЫ ЗАПРОСА
            else if (isRequestActive && fillRatio >= fulfillThresholdPercent)
            {
                FulfillRequest(slot);
            }
        }
    }

    private void CreateRequest(StorageData slot)
    {
        var newRequest = new ResourceRequest(
            this,
            slot.resourceType,
            priority,
            _identity.rootGridPosition
        );

        _roadManager.CreateRequest(newRequest); // UPDATED: используем RoadManager
        _activeRequests[slot.resourceType] = newRequest;
        UpdateIsRequesting();
    }

    private void FulfillRequest(StorageData slot)
    {
        if (_activeRequests.TryGetValue(slot.resourceType, out ResourceRequest request))
        {
            _roadManager.FulfillRequest(request); // UPDATED: используем RoadManager
            _activeRequests.Remove(slot.resourceType);
            UpdateIsRequesting();
        }
    }

    /// <summary>
    /// Проверяет, достаточно ли сырья для ОДНОГО цикла по "рецепту".
    /// Вызывается из ResourceProducer.
    /// </summary>
    public bool HasResources(List<ResourceCost> costs)
    {
        if (costs == null || costs.Count == 0)
        {
            return true; // Если рецепт не требует сырья (напр. Лесопилка)
        }
        
        foreach (var cost in costs)
        {
            // Ищем нужный "слот" на нашем складе
            StorageData slot = GetSlotForResource(cost.resourceType);
            if (slot == null || slot.currentAmount < cost.amount)
            {
                return false; // Не нашли слот ИЛИ в нем не хватает
            }
        }
        return true; // Все нашли, всего хватает
    }

    /// <summary>
    /// "Съедает" ресурсы за ОДИН цикл по "рецепту".
    /// Вызывается из ResourceProducer.
    /// </summary>
    public void ConsumeResources(List<ResourceCost> costs)
    {
        if (costs == null) return;
        
        foreach (var cost in costs)
        {
            StorageData slot = GetSlotForResource(cost.resourceType);
            if (slot != null)
            {
                slot.currentAmount -= cost.amount;
            }
        }
    }

    /// <summary>
    /// Добавляет сырье, привезенное тележкой.
    /// </summary>
    public float AddResource(ResourceType type, float amount)
    {
        StorageData slot = GetSlotForResource(type);
        if (slot == null)
        {
            return 0; // Этот завод не принимает такой тип ресурса
        }
        
        float spaceAvailable = slot.maxAmount - slot.currentAmount;
        if (spaceAvailable <= 0) return 0;

        float amountToAdd = Mathf.Min(amount, spaceAvailable);
        slot.currentAmount += amountToAdd;

        return amountToAdd;
    }
    
    /// <summary>
    /// Хелпер: находит слот по типу ресурса
    /// ISSUE #8 FIX: Используем Dictionary для O(1) вместо O(n) FirstOrDefault
    /// </summary>
    private StorageData GetSlotForResource(ResourceType type)
    {
        return _resourceLookup.TryGetValue(type, out var slot) ? slot : null;
    }

    private void UpdateIsRequesting()
    {
        IsRequesting = _activeRequests.Count > 0;
    }

    // ════════════════════════════════════════════════════════════════
    //             РЕАЛИЗАЦИЯ IResourceReceiver (ПРИНЯТЬ)
    // ════════════════════════════════════════════════════════════════

    public Vector2Int GetGridPosition()
    {
        if (_identity == null)
            _identity = GetComponent<BuildingIdentity>();
        
        return _identity != null ? _identity.rootGridPosition : Vector2Int.zero;
    }

    public bool AcceptsResource(ResourceType type)
    {
        // ISSUE #8 FIX: Используем Dictionary для O(1) вместо O(n) Exists
        return _resourceLookup.ContainsKey(type);
    }

    public float GetAvailableSpace(ResourceType type)
    {
        StorageData slot = GetSlotForResource(type);
        if (slot == null)
            return 0f;
        
        return Mathf.Max(0, slot.maxAmount - slot.currentAmount);
    }

    public float TryAddResource(ResourceType type, float amount)
    {
        // Используем существующий метод AddResource
        return AddResource(type, amount);
    }

    public bool CanAcceptCart()
    {
        // Может принять тележку, если есть хотя бы один незаполненный слот
        foreach (var slot in requiredResources)
        {
            if (slot.currentAmount < slot.maxAmount)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Возвращает все ресурсы в инвентаре (для сохранения состояния при апгрейде)
    /// </summary>
    public Dictionary<ResourceType, float> GetAllResources()
    {
        var resources = new Dictionary<ResourceType, float>();
        foreach (var slot in requiredResources)
        {
            if (slot.currentAmount > 0)
            {
                resources[slot.resourceType] = slot.currentAmount;
            }
        }
        return resources;
    }
}