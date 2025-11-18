using UnityEngine;

/// <summary>
/// Выходной инвентарь производственного здания.
/// Может ОТДАВАТЬ ресурсы (IResourceProvider).
/// </summary>
public class BuildingOutputInventory : MonoBehaviour, IResourceProvider
{
    [Tooltip("Какой ресурс производим и его вместимость (настраивается в Инспекторе)")]
    public StorageData outputResource;

    public event System.Action OnFull;
    public event System.Action OnSpaceAvailable;

    private bool _wasFull = false;

    // === НОВОЕ ПОЛЕ ДЛЯ ИНТЕРФЕЙСА ===
    private BuildingIdentity _identity;

    // === ПУБЛИЧНОЕ СВОЙСТВО ДЛЯ ДОСТУПА К ТИПУ РЕСУРСА ===
    public ResourceType resourceType => outputResource.resourceType;

    // ════════════════════════════════════════════════════════════════
    //                      ИНИЦИАЛИЗАЦИЯ
    // ════════════════════════════════════════════════════════════════
    
    void Awake()
    {
        _identity = GetComponent<BuildingIdentity>();

        if (_identity == null)
        {
            Debug.LogWarning($"[BuildingOutputInventory] {gameObject.name} не имеет BuildingIdentity!");
        }
    }

    // 🚀 PERFORMANCE FIX: Автоматическая регистрация в BuildingRegistry
    private void OnEnable()
    {
        BuildingRegistry.Instance?.RegisterOutput(this);
    }

    private void OnDisable()
    {
        BuildingRegistry.Instance?.UnregisterOutput(this);
    }

    // ════════════════════════════════════════════════════════════════
    //                   СТАРЫЕ МЕТОДЫ (НЕ МЕНЯЕМ)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Проверяет, есть ли место (вызывается из ResourceProducer).
    /// </summary>
    public bool HasSpace(int amountToAdd)
    {
        return outputResource.currentAmount + amountToAdd <= outputResource.maxAmount;
    }

    /// <summary>
    /// Добавляет готовую продукцию (вызывается из ResourceProducer).
    /// </summary>
    public void AddResource(int amount)
    {
        outputResource.currentAmount += amount;
        
        if (outputResource.currentAmount >= outputResource.maxAmount)
        {
            outputResource.currentAmount = outputResource.maxAmount;
            
            if (!_wasFull)
            {
                _wasFull = true;
                OnFull?.Invoke(); // Сообщаем: "Я ПОЛОН!"
            }
        }
    }

    /// <summary>
    /// Забирает продукцию (вызывается тележкой CartAgent).
    /// </summary>
    /// <returns>Сколько РЕАЛЬНО удалось забрать.</returns>
    public int TakeResource(int amountToTake)
    {
        // Округляем ВНИЗ то, что лежит на складе, до целого
        int amountAvailable = Mathf.FloorToInt(outputResource.currentAmount);
        
        int amountTaken = Mathf.Min(amountToTake, amountAvailable);
        if (amountTaken <= 0) return 0;
        
        outputResource.currentAmount -= amountTaken;
        
        if (_wasFull && outputResource.currentAmount < outputResource.maxAmount)
        {
            _wasFull = false;
            OnSpaceAvailable?.Invoke(); // Сообщаем: "ЕСТЬ МЕСТО!"
        }
        
        return amountTaken;
    }

    /// <summary>
    /// Используется тележкой, чтобы решить, стоит ли ехать.
    /// (Проверяем, что есть хотя бы 1.0)
    /// </summary>
    public bool HasAtLeastOneUnit()
    {
        return outputResource.currentAmount >= 1f;
    }

    /// <summary>
    /// Используется тележкой (старый метод от BuildingInventory).
    /// </summary>
    public int TakeAllResources()
    {
        // Берем все, что есть, округляя до целого
        int amountToTake = Mathf.FloorToInt(outputResource.currentAmount);
        return TakeResource(amountToTake);
    }
    
    /// <summary>
    /// Используется тележкой (старый метод от BuildingInventory).
    /// </summary>
    public ResourceType GetResourceType()
    {
        return outputResource.resourceType;
    }
    
    public bool TryAddResource(int amountToAdd)
    {
        // 1. Проверяем, есть ли место
        if (!HasSpace(amountToAdd))
        {
            // Места нет. Вызываем OnFull (если еще не вызывали)
            if (!_wasFull)
            {
                _wasFull = true;
                OnFull?.Invoke();
            }
            return false;
        }

        // 2. Место есть. Добавляем.
        outputResource.currentAmount += amountToAdd;

        // 3. Проверяем, не заполнили ли мы его *только что*
        if (outputResource.currentAmount >= outputResource.maxAmount)
        {
            outputResource.currentAmount = outputResource.maxAmount;
            
            if (!_wasFull)
            {
                _wasFull = true;
                OnFull?.Invoke(); // Сообщаем: "Я ПОЛОН!"
            }
        }
        
        return true; // Успех
    }

    // ════════════════════════════════════════════════════════════════
    //              РЕАЛИЗАЦИЯ IResourceProvider (ОТДАТЬ)
    // ════════════════════════════════════════════════════════════════

    public Vector2Int GetGridPosition()
    {
        if (_identity == null)
            _identity = GetComponent<BuildingIdentity>();
        
        return _identity != null ? _identity.rootGridPosition : Vector2Int.zero;
    }

    public ResourceType GetProvidedResourceType()
    {
        return outputResource.resourceType;
    }

    public float GetAvailableAmount(ResourceType type)
    {
        // Проверяем, что запрашиваемый тип совпадает с нашим
        if (type != outputResource.resourceType)
            return 0f;
        
        // Возвращаем количество, округлённое вниз
        return Mathf.Floor(outputResource.currentAmount);
    }

    public float TryTakeResource(ResourceType type, float amount)
    {
        // Проверяем тип
        if (type != outputResource.resourceType)
            return 0f;
        
        // Используем существующий метод TakeResource
        int amountToTake = Mathf.FloorToInt(Mathf.Min(amount, outputResource.currentAmount));
        return TakeResource(amountToTake);
    }

    public bool CanAcceptCart()
    {
        // Выходной инвентарь может принять тележку, если есть что забрать
        return HasAtLeastOneUnit();
    }

    /// <summary>
    /// Возвращает текущее количество ресурса (для сохранения состояния при апгрейде)
    /// </summary>
    public float GetCurrentAmount()
    {
        return outputResource.currentAmount;
    }

    /// <summary>
    /// Возвращает вместимость склада (для сохранения состояния при апгрейде)
    /// </summary>
    public float GetCapacity()
    {
        return outputResource.maxAmount;
    }
}