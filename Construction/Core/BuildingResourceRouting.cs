using UnityEngine;

/// <summary>
/// Управляет маршрутизацией ресурсов для производственного здания.
/// Определяет, КУДА отвозить Output и ОТКУДА брать Input.
/// 
/// Использование:
/// - Добавьте на производственное здание
/// - Настройте маршруты в Inspector:
///   * outputDestinationTransform - куда везти продукцию (или null для автопоиска склада)
///   * inputSourceTransform - откуда брать сырьё (или null для автопоиска склада)
/// </summary>
[RequireComponent(typeof(BuildingIdentity))]
public class BuildingResourceRouting : MonoBehaviour
{
    [Header("Output Routing (куда отвозить продукцию)")]
    [Tooltip("Целевое здание для Output. Оставьте пустым для автопоиска ближайшего склада")]
    public Transform outputDestinationTransform;
    
    [Header("Input Routing (откуда брать сырьё)")]
    [Tooltip("Источник для Input. Оставьте пустым для автопоиска ближайшего склада")]
    public Transform inputSourceTransform;
    
    [Header("Дебаг (только для чтения)")]
    [SerializeField] private string _outputDestinationName = "не настроен";
    [SerializeField] private string _inputSourceName = "не настроен";
    
    [Header("Автообновление")]
    [Tooltip("Интервал повторной проверки маршрутов (сек), если они не настроены")]
    [SerializeField] private float _retryInterval = 5.0f;

    [Header("Приоритеты (только для чтения)")]
    [Tooltip("Предпочитать прямые поставки от производителей вместо склада (для Input)")]
    [SerializeField] private bool _preferDirectSupply = true;

    [Tooltip("Предпочитать прямые поставки к потребителям вместо склада (для Output)")]
    [SerializeField] private bool _preferDirectDelivery = true;

    [Header("Round-Robin Распределение")]
    [Tooltip("Включить равномерное распределение между потребителями (переключаться после каждых N доставок)")]
    [SerializeField] private bool _enableRoundRobin = true;

    [Tooltip("Количество доставок к одному потребителю перед переключением на следующего (1 = переключать после каждой доставки)")]
    [SerializeField] private int _deliveriesBeforeRotation = 1;

    [Header("Координация Производителей")]
    [Tooltip("Использовать координацию с другими производителями в сети (избегать дублирования поставок)")]
    [SerializeField] private bool _enableCoordination = true;

    // Счетчик доставок к текущему потребителю
    private int _deliveryCountToCurrentConsumer = 0;

    // Кэшированные интерфейсы
    public IResourceReceiver outputDestination { get; private set; }
    public IResourceProvider inputSource { get; private set; }
    private BuildingIdentity _identity;
    private float _retryTimer = 0f;

    // Кэшированные системы для поиска путей
    private GridSystem _gridSystem;
    private RoadManager _roadManager;
    
    void Awake()
    {
        _identity = GetComponent<BuildingIdentity>();
        
        if (_identity == null)
        {
            Debug.LogError($"[BuildingResourceRouting] {gameObject.name} не имеет BuildingIdentity!");
        }
    }
    
    void Start()
    {
        // Инициализируем системы для поиска путей
        _gridSystem = FindFirstObjectByType<GridSystem>();
        _roadManager = RoadManager.Instance;

        if (_gridSystem == null)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: GridSystem не найден!");
        }
        if (_roadManager == null)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: RoadManager не найден!");
        }

        RefreshRoutes();
    }

    void OnDestroy()
    {
        // ✅ НОВОЕ: Отменяем регистрацию при уничтожении здания
        if (_enableCoordination && ResourceCoordinator.Instance != null && outputDestination != null)
        {
            if (outputDestination is MonoBehaviour consumerMB)
            {
                ResourceCoordinator.Instance.UnregisterSupplyRoute(this, consumerMB);
            }
        }
    }
    void Update()
    {
        _retryTimer += Time.deltaTime;

        if (_retryTimer >= _retryInterval)
        {
            _retryTimer = 0f;

            // ✅ АВТООБНОВЛЕНИЕ 1: Если маршруты не настроены, повторяем проверку
            if (!IsConfigured())
            {
                Debug.Log($"[Routing] {gameObject.name}: Маршруты не настроены, повторная проверка...");
                RefreshRoutes();
                // Уведомляем ResourceProducer об изменении
                var producer = GetComponent<ResourceProducer>();
                if (producer != null)
                {
                    producer.RefreshWarehouseAccess();
                }
            }
            // ✅ НОВОЕ: АВТООБНОВЛЕНИЕ 2: Динамическое переключение между потребителями
            else if (_preferDirectDelivery && outputDestination != null && outputDestinationTransform == null)
            {
                // Проверяем только автоматически выбранные маршруты (не ручные)
                if (outputDestination is BuildingInputInventory consumer)
                {
                    var outputInv = GetComponent<BuildingOutputInventory>();
                    if (outputInv != null)
                    {
                        ResourceType producedType = outputInv.GetProvidedResourceType();

                        // Причина 1: Потребитель заполнен >= 90%
                        float fillRatio = GetConsumerFillRatio(consumer, producedType);
                        if (fillRatio >= 0.9f)
                        {
                            Debug.Log($"[Routing] {gameObject.name}: Output destination заполнен на {fillRatio*100:F0}%, ищу другого потребителя...");
                            RefreshRoutes();
                        }
                        // Причина 2: Есть потребитель с меньшей нагрузкой (более справедливое распределение)
                        else if (ShouldSwitchToLessLoadedConsumer(consumer, producedType))
                        {
                            Debug.Log($"[Routing] {gameObject.name}: Найден менее нагруженный потребитель, переключаюсь для балансировки...");
                            RefreshRoutes();
                        }
                    }
                }
            }
        }
    }
    /// <summary>
    /// Обновляет маршруты (вызывать при изменении зданий на карте)
    /// </summary>
    public void RefreshRoutes()
    {
        // ✅ НОВОЕ: Отменяем старую регистрацию перед обновлением маршрута
        if (_enableCoordination && ResourceCoordinator.Instance != null && outputDestination != null)
        {
            if (outputDestination is MonoBehaviour oldConsumerMB)
            {
                ResourceCoordinator.Instance.UnregisterSupplyRoute(this, oldConsumerMB);
            }
        }

        // === OUTPUT DESTINATION ===
        if (outputDestinationTransform != null)
        {
            // Используем указанное здание
            outputDestination = outputDestinationTransform.GetComponent<IResourceReceiver>();

            if (outputDestination == null)
            {
                Debug.LogWarning($"[Routing] {gameObject.name}: {outputDestinationTransform.name} не реализует IResourceReceiver!");
                _outputDestinationName = $"{outputDestinationTransform.name} (ОШИБКА)";
            }
            else
            {
                _outputDestinationName = outputDestinationTransform.name;
                Debug.Log($"[Routing] {gameObject.name}: Output → {outputDestinationTransform.name}");
            }
        }
        else
        {
            // ✅ НОВАЯ СИСТЕМА ПРИОРИТЕТОВ: потребитель > склад
            if (_preferDirectDelivery)
            {
                // Приоритет 1: Ищем потребителя нашей продукции
                outputDestination = FindNearestConsumerForMyOutput();

                if (outputDestination != null)
                {
                    // Нашли потребителя!
                    if (outputDestination is MonoBehaviour mb)
                    {
                        _outputDestinationName = $"{mb.name} (потребитель)";
                        Debug.Log($"[Routing] {gameObject.name}: Output → потребитель {mb.name}");
                    }
                }
            }

            // Приоритет 2: Если потребителя нет → ищем склад
            if (outputDestination == null)
            {
                outputDestination = FindNearestWarehouse();

                if (outputDestination != null)
                {
                    _outputDestinationName = $"Склад (авто) на {outputDestination.GetGridPosition()}";
                    Debug.Log($"[Routing] {gameObject.name}: Output → автопоиск склада на {outputDestination.GetGridPosition()}");
                }
                else
                {
                    _outputDestinationName = "НЕ НАЙДЕН!";
                    Debug.LogWarning($"[Routing] {gameObject.name}: Output получатель НЕ НАЙДЕН! Постройте склад или потребителя.");
                }
            }
        }
        
        // === INPUT SOURCE ===
        if (inputSourceTransform != null)
        {
            // Используем указанное здание (ручная настройка)
            inputSource = inputSourceTransform.GetComponent<IResourceProvider>();

            if (inputSource == null)
            {
                Debug.LogWarning($"[Routing] {gameObject.name}: {inputSourceTransform.name} не реализует IResourceProvider!");
                _inputSourceName = $"{inputSourceTransform.name} (ОШИБКА)";
            }
            else
            {
                _inputSourceName = inputSourceTransform.name;
                Debug.Log($"[Routing] {gameObject.name}: Input ← {inputSourceTransform.name}");
            }
        }
        else
        {
            // ✅ НОВАЯ СИСТЕМА ПРИОРИТЕТОВ: производитель > склад
            if (_preferDirectSupply)
            {
                // Приоритет 1: Ищем производителя нужного ресурса
                inputSource = FindNearestProducerForMyNeeds();

                if (inputSource != null)
                {
                    // Нашли производителя!
                    if (inputSource is MonoBehaviour mb)
                    {
                        _inputSourceName = $"{mb.name} (производитель)";
                        Debug.Log($"[Routing] {gameObject.name}: Input ← производитель {mb.name}");
                    }
                }
            }

            // Приоритет 2: Если производителя нет → ищем склад
            if (inputSource == null)
            {
                inputSource = FindNearestWarehouse();

                if (inputSource != null)
                {
                    _inputSourceName = $"Склад (авто) на {inputSource.GetGridPosition()}";
                    Debug.Log($"[Routing] {gameObject.name}: Input ← автопоиск склада на {inputSource.GetGridPosition()}");
                }
                else
                {
                    _inputSourceName = "НЕ НАЙДЕН!";
                    Debug.LogWarning($"[Routing] {gameObject.name}: Input источник НЕ НАЙДЕН! Постройте склад или производителя.");
                }
            }
        }

        // ✅ НОВОЕ: Сбрасываем счетчик доставок при обновлении маршрутов
        // Это обеспечивает честный round-robin после переключения
        _deliveryCountToCurrentConsumer = 0;
    }
    
    /// <summary>
    /// ✅ НОВОЕ: Ищет ближайшего производителя нужного ресурса (с проверкой дорог)
    /// ✅ БАЛАНСИРОВКА: Учитывает нагрузку на производителей (сколько потребителей уже подключены)
    /// </summary>
    private IResourceProvider FindNearestProducerForMyNeeds()
    {
        // 1. Определяем, какой ресурс нам нужен
        var inputInv = GetComponent<BuildingInputInventory>();
        if (inputInv == null || inputInv.requiredResources == null || inputInv.requiredResources.Count == 0)
        {
            // Здание не требует Input
            return null;
        }

        // Берём первый требуемый ресурс (если их несколько, можно расширить логику)
        ResourceType neededType = inputInv.requiredResources[0].resourceType;

        Debug.Log($"[Routing] {gameObject.name}: Ищу производителя {neededType}...");

        // 2. Находим все здания с BuildingOutputInventory
        BuildingOutputInventory[] allOutputs = FindObjectsByType<BuildingOutputInventory>(FindObjectsSortMode.None);

        if (allOutputs.Length == 0)
        {
            Debug.Log($"[Routing] {gameObject.name}: Не найдено ни одного производителя на карте");
            return null;
        }

        // 3. Фильтруем по типу ресурса
        var matchingProducers = new System.Collections.Generic.List<BuildingOutputInventory>();

        foreach (var output in allOutputs)
        {
            // Проверяем, что это не мы сами
            if (output.gameObject == gameObject)
                continue;

            // Проверяем тип ресурса
            if (output.outputResource.resourceType == neededType)
            {
                matchingProducers.Add(output);
            }
        }

        if (matchingProducers.Count == 0)
        {
            Debug.Log($"[Routing] {gameObject.name}: Не найдено производителей {neededType}");
            return null;
        }

        Debug.Log($"[Routing] {gameObject.name}: Найдено {matchingProducers.Count} производителей {neededType}. Проверяю доступность по дорогам...");

        // 4. Проверяем доступность по дорогам и находим ближайшего
        if (_gridSystem == null || _roadManager == null || _identity == null)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Системы не инициализированы, выбираю с балансировкой");
            return FindBalancedProducerByDistance(matchingProducers);
        }

        var roadGraph = _roadManager.GetRoadGraph();
        if (roadGraph == null || roadGraph.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Граф дорог пуст, выбираю с балансировкой");
            return FindBalancedProducerByDistance(matchingProducers);
        }

        // Находим наши точки доступа к дорогам
        var myAccessPoints = LogisticsPathfinder.FindAllRoadAccess(_identity.rootGridPosition, _gridSystem, roadGraph);

        if (myAccessPoints.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: У меня нет доступа к дорогам!");
            return null;
        }

        // Рассчитываем расстояния от нас до всех точек дорог
        var distancesFromMe = LogisticsPathfinder.Distances_BFS_Multi(myAccessPoints, 1000, roadGraph);

        // ✅ НОВАЯ ЛОГИКА БАЛАНСИРОВКИ:
        // Собираем информацию о каждом производителе: расстояние + нагрузка
        var producerInfo = new System.Collections.Generic.List<(IResourceProvider provider, int distance, int consumerCount)>();

        foreach (var producer in matchingProducers)
        {
            var producerIdentity = producer.GetComponent<BuildingIdentity>();
            if (producerIdentity == null)
                continue;

            var producerAccessPoints = LogisticsPathfinder.FindAllRoadAccess(producerIdentity.rootGridPosition, _gridSystem, roadGraph);

            // Находим минимальное расстояние до этого производителя
            int minDistToProducer = int.MaxValue;
            foreach (var accessPoint in producerAccessPoints)
            {
                if (distancesFromMe.TryGetValue(accessPoint, out int dist) && dist < minDistToProducer)
                {
                    minDistToProducer = dist;
                }
            }

            // Если производитель недостижим - пропускаем
            if (minDistToProducer == int.MaxValue)
                continue;

            // Подсчитываем нагрузку (сколько потребителей уже подключены к этому производителю)
            int consumerCount = CountConsumersForProducer(producer);

            producerInfo.Add((producer, minDistToProducer, consumerCount));

            Debug.Log($"[Routing] {gameObject.name}: Производитель {producer.name} - дистанция: {minDistToProducer}, потребителей: {consumerCount}");
        }

        if (producerInfo.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Производители {neededType} найдены, но нет дороги к ним!");
            return null;
        }

        // ✅ ВЫБОР С БАЛАНСИРОВКОЙ:
        // Сортируем: сначала по нагрузке (меньше = лучше), затем по расстоянию (ближе = лучше)
        producerInfo.Sort((a, b) =>
        {
            // Приоритет 1: Меньше нагрузка
            int loadComparison = a.consumerCount.CompareTo(b.consumerCount);
            if (loadComparison != 0)
                return loadComparison;

            // Приоритет 2: Меньше расстояние
            return a.distance.CompareTo(b.distance);
        });

        var bestProducer = producerInfo[0];

        if (bestProducer.provider is MonoBehaviour mb)
        {
            Debug.Log($"[Routing] {gameObject.name}: ✅ ВЫБРАН производитель {neededType}: {mb.name} (дистанция: {bestProducer.distance}, нагрузка: {bestProducer.consumerCount} потребителей)");
        }

        return bestProducer.provider;
    }

    /// <summary>
    /// ✅ НОВОЕ: Подсчитывает, сколько потребителей уже используют данного производителя как inputSource
    /// </summary>
    private int CountConsumersForProducer(BuildingOutputInventory producer)
    {
        int count = 0;

        // Находим все здания с маршрутизацией
        BuildingResourceRouting[] allRoutings = FindObjectsByType<BuildingResourceRouting>(FindObjectsSortMode.None);

        foreach (var routing in allRoutings)
        {
            // Пропускаем себя
            if (routing == this)
                continue;

            // Проверяем, использует ли это здание нашего производителя как источник Input
            if ((object)routing.inputSource == (object)producer)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// ✅ НОВОЕ: Выбирает производителя с балансировкой по прямому расстоянию (fallback)
    /// </summary>
    private IResourceProvider FindBalancedProducerByDistance(System.Collections.Generic.List<BuildingOutputInventory> producers)
    {
        // Собираем информацию: производитель + расстояние + нагрузка
        var producerInfo = new System.Collections.Generic.List<(IResourceProvider provider, float distance, int consumerCount)>();

        foreach (var producer in producers)
        {
            float dist = Vector3.Distance(transform.position, producer.transform.position);
            int consumerCount = CountConsumersForProducer(producer);

            producerInfo.Add((producer, dist, consumerCount));
        }

        // Сортируем: сначала по нагрузке, затем по расстоянию
        producerInfo.Sort((a, b) =>
        {
            int loadComparison = a.consumerCount.CompareTo(b.consumerCount);
            if (loadComparison != 0)
                return loadComparison;

            return a.distance.CompareTo(b.distance);
        });

        if (producerInfo.Count > 0)
        {
            var best = producerInfo[0];
            if (best.provider is MonoBehaviour mb)
            {
                Debug.Log($"[Routing] {gameObject.name}: ✅ ВЫБРАН (по прямой) {mb.name} - расстояние: {best.distance:F1}, нагрузка: {best.consumerCount}");
            }
            return best.provider;
        }

        return null;
    }


    /// <summary>
    /// ✅ НОВОЕ: Ищет ближайшего потребителя нашей продукции (с проверкой дорог)
    /// ✅ БАЛАНСИРОВКА: Учитывает нагрузку на потребителей (сколько поставщиков уже подключены)
    /// </summary>
    private IResourceReceiver FindNearestConsumerForMyOutput()
    {
        // 1. Определяем, какой ресурс мы производим
        var outputInv = GetComponent<BuildingOutputInventory>();
        if (outputInv == null)
        {
            // Здание не производит Output
            return null;
        }

        ResourceType producedType = outputInv.GetProvidedResourceType();
        if (producedType == ResourceType.None)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: BuildingOutputInventory не производит ничего!");
            return null;
        }

        Debug.Log($"[Routing] {gameObject.name}: Ищу потребителя {producedType}...");

        // 2. Находим все здания с BuildingInputInventory
        BuildingInputInventory[] allInputs = FindObjectsByType<BuildingInputInventory>(FindObjectsSortMode.None);

        if (allInputs.Length == 0)
        {
            Debug.Log($"[Routing] {gameObject.name}: Не найдено ни одного потребителя на карте");
            return null;
        }

        // 3. Фильтруем по типу ресурса
        var matchingConsumers = new System.Collections.Generic.List<BuildingInputInventory>();

        foreach (var input in allInputs)
        {
            // Проверяем, что это не мы сами
            if (input.gameObject == gameObject)
                continue;

            // Проверяем, требует ли это здание наш ресурс
            bool needsOurResource = false;
            foreach (var slot in input.requiredResources)
            {
                if (slot.resourceType == producedType)
                {
                    needsOurResource = true;
                    break;
                }
            }

            if (needsOurResource)
            {
                matchingConsumers.Add(input);
            }
        }

        if (matchingConsumers.Count == 0)
        {
            Debug.Log($"[Routing] {gameObject.name}: Не найдено потребителей {producedType}");
            return null;
        }

        Debug.Log($"[Routing] {gameObject.name}: Найдено {matchingConsumers.Count} потребителей {producedType}. Проверяю доступность по дорогам...");

        // ✅ АДАПТИВНАЯ КООРДИНАЦИЯ: Проверяем соотношение производителей/потребителей
        if (_enableCoordination && ResourceCoordinator.Instance != null)
        {
            // Проверяем, нужно ли использовать жесткое резервирование 1:1
            bool useExclusiveReservation = ResourceCoordinator.Instance.ShouldUseExclusiveReservation(this, producedType);

            if (useExclusiveReservation)
            {
                // РЕЖИМ 1:1 - фильтруем зарезервированных (как раньше)
                var unreservedConsumers = new System.Collections.Generic.List<BuildingInputInventory>();
                var reservedConsumers = new System.Collections.Generic.List<BuildingInputInventory>();

                foreach (var consumer in matchingConsumers)
                {
                    if (ResourceCoordinator.Instance.IsConsumerReserved(consumer, this))
                    {
                        reservedConsumers.Add(consumer);
                    }
                    else
                    {
                        unreservedConsumers.Add(consumer);
                    }
                }

                // Если есть незарезервированные - работаем только с ними
                if (unreservedConsumers.Count > 0)
                {
                    Debug.Log($"[Routing] {gameObject.name}: 🎯 РЕЖИМ 1:1 - Фильтрация: {matchingConsumers.Count} всего, {unreservedConsumers.Count} незарезервированных");
                    matchingConsumers = unreservedConsumers;
                }
                else
                {
                    Debug.Log($"[Routing] {gameObject.name}: ⚠️ Все потребители зарезервированы, выбираю из всех");
                }
            }
            else
            {
                // РЕЖИМ МНОГОПОТОЧНОСТИ - НЕ фильтруем зарезервированных
                // Производитель может обслуживать нескольких потребителей
                Debug.Log($"[Routing] {gameObject.name}: 🔄 РЕЖИМ МНОГОПОТОЧНОСТИ - производитель может обслуживать всех {matchingConsumers.Count} потребителей");
            }
        }

        // 4. Проверяем доступность по дорогам и находим ближайшего
        if (_gridSystem == null || _roadManager == null || _identity == null)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Системы не инициализированы, выбираю с балансировкой");
            return FindBalancedConsumerByDistance(matchingConsumers);
        }

        var roadGraph = _roadManager.GetRoadGraph();
        if (roadGraph == null || roadGraph.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Граф дорог пуст, выбираю с балансировкой");
            return FindBalancedConsumerByDistance(matchingConsumers);
        }

        // Находим наши точки доступа к дорогам
        var myAccessPoints = LogisticsPathfinder.FindAllRoadAccess(_identity.rootGridPosition, _gridSystem, roadGraph);

        if (myAccessPoints.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: У меня нет доступа к дорогам!");
            return null;
        }

        // Рассчитываем расстояния от нас до всех точек дорог
        var distancesFromMe = LogisticsPathfinder.Distances_BFS_Multi(myAccessPoints, 1000, roadGraph);

        // ✅ НОВАЯ ЛОГИКА БАЛАНСИРОВКИ:
        // Собираем информацию о каждом потребителе: расстояние + нагрузка + заполнение склада
        var consumerInfo = new System.Collections.Generic.List<(IResourceReceiver receiver, int distance, int supplierCount, float fillRatio)>();

        foreach (var consumer in matchingConsumers)
        {
            var consumerIdentity = consumer.GetComponent<BuildingIdentity>();
            if (consumerIdentity == null)
                continue;

            var consumerAccessPoints = LogisticsPathfinder.FindAllRoadAccess(consumerIdentity.rootGridPosition, _gridSystem, roadGraph);

            // Находим минимальное расстояние до этого потребителя
            int minDistToConsumer = int.MaxValue;
            foreach (var accessPoint in consumerAccessPoints)
            {
                if (distancesFromMe.TryGetValue(accessPoint, out int dist) && dist < minDistToConsumer)
                {
                    minDistToConsumer = dist;
                }
            }

            // Если потребитель недостижим - пропускаем
            if (minDistToConsumer == int.MaxValue)
                continue;

            // Подсчитываем нагрузку (сколько поставщиков уже подключены к этому потребителю)
            int supplierCount = CountSuppliersForConsumer(consumer);

            // ✅ НОВОЕ: Проверяем заполнение склада потребителя для нашего ресурса
            float fillRatio = GetConsumerFillRatio(consumer, producedType);

            consumerInfo.Add((consumer, minDistToConsumer, supplierCount, fillRatio));

            Debug.Log($"[Routing] {gameObject.name}: Потребитель {consumer.name} - дистанция: {minDistToConsumer}, поставщиков: {supplierCount}, заполнение: {fillRatio*100:F0}%");
        }

        if (consumerInfo.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Потребители {producedType} найдены, но нет дороги к ним!");
            return null;
        }

        // ✅ ВЫБОР С БАЛАНСИРОВКОЙ:
        // Сортируем: сначала по нагрузке (меньше = лучше), затем по заполнению, затем по расстоянию
        consumerInfo.Sort((a, b) =>
        {
            // Приоритет 1: Меньше нагрузка (количество поставщиков) - САМОЕ ВАЖНОЕ!
            // Это обеспечивает равномерное распределение: 2 лесопилки → 2 плотницких (1:1)
            int loadComparison = a.supplierCount.CompareTo(b.supplierCount);
            if (loadComparison != 0)
                return loadComparison;

            // Приоритет 2: Меньше заполнение
            // При равной нагрузке выбираем менее заполненного
            int fillComparison = a.fillRatio.CompareTo(b.fillRatio);
            if (fillComparison != 0)
                return fillComparison;

            // Приоритет 3: Меньше расстояние
            return a.distance.CompareTo(b.distance);
        });

        var bestConsumer = consumerInfo[0];

        if (bestConsumer.receiver is MonoBehaviour mb)
        {
            Debug.Log($"[Routing] {gameObject.name}: ✅ ВЫБРАН потребитель {producedType}: {mb.name} (поставщиков: {bestConsumer.supplierCount}, заполнение: {bestConsumer.fillRatio*100:F0}%, дистанция: {bestConsumer.distance})");
        }

        return bestConsumer.receiver;
    }

    /// <summary>
    /// ✅ НОВОЕ: Проверяет, есть ли потребитель с меньшей нагрузкой для справедливого распределения
    /// </summary>
    private bool ShouldSwitchToLessLoadedConsumer(BuildingInputInventory currentConsumer, ResourceType producedType)
    {
        // Подсчитываем текущую нагрузку на потребителя
        int currentLoad = CountSuppliersForConsumer(currentConsumer);

        // Ищем всех потребителей данного ресурса
        BuildingInputInventory[] allInputs = FindObjectsByType<BuildingInputInventory>(FindObjectsSortMode.None);

        foreach (var input in allInputs)
        {
            // Пропускаем текущего потребителя
            if (input == currentConsumer)
                continue;

            // Пропускаем себя
            if (input.gameObject == gameObject)
                continue;

            // Проверяем, требует ли это здание наш ресурс
            bool needsOurResource = false;
            foreach (var slot in input.requiredResources)
            {
                if (slot.resourceType == producedType)
                {
                    needsOurResource = true;
                    break;
                }
            }

            if (!needsOurResource)
                continue;

            // Подсчитываем нагрузку на этого потребителя
            int otherLoad = CountSuppliersForConsumer(input);

            // Если нашли потребителя с нагрузкой хотя бы на 1 меньше - переключаемся
            if (otherLoad < currentLoad)
            {
                Debug.Log($"[Routing] {gameObject.name}: Потребитель {input.name} имеет нагрузку {otherLoad}, текущий {currentConsumer.name} - {currentLoad}");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ✅ НОВОЕ: Получает процент заполнения склада потребителя для указанного ресурса
    /// </summary>
    private float GetConsumerFillRatio(BuildingInputInventory consumer, ResourceType resourceType)
    {
        if (consumer == null || consumer.requiredResources == null)
            return 1.0f; // Если нет данных - считаем заполненным

        // Ищем слот с нужным ресурсом
        foreach (var slot in consumer.requiredResources)
        {
            if (slot.resourceType == resourceType)
            {
                if (slot.maxAmount <= 0)
                    return 1.0f; // Слот не настроен - считаем заполненным

                return slot.currentAmount / slot.maxAmount;
            }
        }

        return 1.0f; // Ресурс не найден - считаем заполненным
    }

    /// <summary>
    /// ✅ НОВОЕ: Подсчитывает, сколько поставщиков уже используют данного потребителя как outputDestination
    /// </summary>
    private int CountSuppliersForConsumer(BuildingInputInventory consumer)
    {
        int count = 0;

        // Находим все здания с маршрутизацией
        BuildingResourceRouting[] allRoutings = FindObjectsByType<BuildingResourceRouting>(FindObjectsSortMode.None);

        foreach (var routing in allRoutings)
        {
            // Пропускаем себя
            if (routing == this)
                continue;

            // Проверяем, использует ли это здание нашего потребителя как получатель Output
            if ((object)routing.outputDestination == (object)consumer)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// ✅ НОВОЕ: Выбирает потребителя с балансировкой по прямому расстоянию (fallback)
    /// </summary>
    private IResourceReceiver FindBalancedConsumerByDistance(System.Collections.Generic.List<BuildingInputInventory> consumers)
    {
        // Определяем, какой ресурс мы производим (для проверки заполнения)
        var outputInv = GetComponent<BuildingOutputInventory>();
        ResourceType producedType = ResourceType.None;
        if (outputInv != null)
        {
            producedType = outputInv.GetProvidedResourceType();
        }

        // Собираем информацию: потребитель + расстояние + нагрузка + заполнение
        var consumerInfo = new System.Collections.Generic.List<(IResourceReceiver receiver, float distance, int supplierCount, float fillRatio)>();

        foreach (var consumer in consumers)
        {
            float dist = Vector3.Distance(transform.position, consumer.transform.position);
            int supplierCount = CountSuppliersForConsumer(consumer);
            float fillRatio = GetConsumerFillRatio(consumer, producedType);

            consumerInfo.Add((consumer, dist, supplierCount, fillRatio));
        }

        // Сортируем: сначала по нагрузке, затем по заполнению, затем по расстоянию
        consumerInfo.Sort((a, b) =>
        {
            // Приоритет 1: Меньше нагрузка (количество поставщиков)
            int loadComparison = a.supplierCount.CompareTo(b.supplierCount);
            if (loadComparison != 0)
                return loadComparison;

            // Приоритет 2: Меньше заполнение
            int fillComparison = a.fillRatio.CompareTo(b.fillRatio);
            if (fillComparison != 0)
                return fillComparison;

            // Приоритет 3: Меньше расстояние
            return a.distance.CompareTo(b.distance);
        });

        if (consumerInfo.Count > 0)
        {
            var best = consumerInfo[0];
            if (best.receiver is MonoBehaviour mb)
            {
                Debug.Log($"[Routing] {gameObject.name}: ✅ ВЫБРАН (по прямой) потребитель {mb.name} - поставщиков: {best.supplierCount}, заполнение: {best.fillRatio*100:F0}%, расстояние: {best.distance:F1}");
            }
            return best.receiver;
        }

        return null;
    }

    /// <summary>
    /// ✅ ОБНОВЛЕНО: Ищет ближайший склад с проверкой дорог и балансировкой нагрузки
    /// </summary>
    private Warehouse FindNearestWarehouse()
    {
        Warehouse[] warehouses = FindObjectsByType<Warehouse>(FindObjectsSortMode.None);

        if (warehouses.Length == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: На карте нет ни одного склада!");
            return null;
        }

        Debug.Log($"[Routing] {gameObject.name}: Найдено {warehouses.Length} складов. Проверяю доступность по дорогам...");

        // Проверяем доступность дорог
        if (_gridSystem == null || _roadManager == null || _identity == null)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Системы не инициализированы, выбираю склад с балансировкой по прямой");
            return FindBalancedWarehouseByDistance(warehouses);
        }

        var roadGraph = _roadManager.GetRoadGraph();
        if (roadGraph == null || roadGraph.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Граф дорог пуст, выбираю склад с балансировкой по прямой");
            return FindBalancedWarehouseByDistance(warehouses);
        }

        // Находим наши точки доступа к дорогам
        var myAccessPoints = LogisticsPathfinder.FindAllRoadAccess(_identity.rootGridPosition, _gridSystem, roadGraph);

        if (myAccessPoints.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: У меня нет доступа к дорогам!");
            return null;
        }

        // Рассчитываем расстояния от нас до всех точек дорог
        var distancesFromMe = LogisticsPathfinder.Distances_BFS_Multi(myAccessPoints, 1000, roadGraph);

        // ✅ БАЛАНСИРОВКА СКЛАДОВ:
        // Собираем информацию о каждом складе: расстояние + нагрузка
        var warehouseInfo = new System.Collections.Generic.List<(Warehouse warehouse, int distance, int producerCount)>();

        foreach (var wh in warehouses)
        {
            var whIdentity = wh.GetComponent<BuildingIdentity>();
            if (whIdentity == null)
                continue;

            var whAccessPoints = LogisticsPathfinder.FindAllRoadAccess(whIdentity.rootGridPosition, _gridSystem, roadGraph);

            // Находим минимальное расстояние до этого склада
            int minDistToWarehouse = int.MaxValue;
            foreach (var accessPoint in whAccessPoints)
            {
                if (distancesFromMe.TryGetValue(accessPoint, out int dist) && dist < minDistToWarehouse)
                {
                    minDistToWarehouse = dist;
                }
            }

            // Если склад недостижим по дорогам - пропускаем
            if (minDistToWarehouse == int.MaxValue)
            {
                Debug.LogWarning($"[Routing] {gameObject.name}: Склад {wh.name} на {whIdentity.rootGridPosition} недостижим по дорогам!");
                continue;
            }

            // Подсчитываем нагрузку (сколько производителей уже используют этот склад)
            int producerCount = CountProducersForWarehouse(wh);

            warehouseInfo.Add((wh, minDistToWarehouse, producerCount));

            Debug.Log($"[Routing] {gameObject.name}: Склад {wh.name} - дистанция: {minDistToWarehouse}, производителей: {producerCount}");
        }

        if (warehouseInfo.Count == 0)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Склады найдены, но нет дороги к ним!");
            return null;
        }

        // ✅ ВЫБОР С БАЛАНСИРОВКОЙ:
        // Сортируем: сначала по нагрузке (меньше = лучше), затем по расстоянию (ближе = лучше)
        warehouseInfo.Sort((a, b) =>
        {
            // Приоритет 1: Меньше нагрузка
            int loadComparison = a.producerCount.CompareTo(b.producerCount);
            if (loadComparison != 0)
                return loadComparison;

            // Приоритет 2: Меньше расстояние
            return a.distance.CompareTo(b.distance);
        });

        var bestWarehouse = warehouseInfo[0];

        Debug.Log($"[Routing] {gameObject.name}: ✅ ВЫБРАН склад: {bestWarehouse.warehouse.name} (дистанция: {bestWarehouse.distance}, нагрузка: {bestWarehouse.producerCount} производителей)");

        return bestWarehouse.warehouse;
    }

    /// <summary>
    /// ✅ НОВОЕ: Подсчитывает, сколько производителей уже используют данный склад как outputDestination
    /// </summary>
    private int CountProducersForWarehouse(Warehouse warehouse)
    {
        int count = 0;

        // Находим все здания с маршрутизацией
        BuildingResourceRouting[] allRoutings = FindObjectsByType<BuildingResourceRouting>(FindObjectsSortMode.None);

        foreach (var routing in allRoutings)
        {
            // Пропускаем себя
            if (routing == this)
                continue;

            // Проверяем, использует ли это здание наш склад как получатель Output
            if ((object)routing.outputDestination == (object)warehouse)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// ✅ НОВОЕ: Выбирает склад с балансировкой по прямому расстоянию (fallback без дорог)
    /// </summary>
    private Warehouse FindBalancedWarehouseByDistance(Warehouse[] warehouses)
    {
        // Собираем информацию: склад + расстояние + нагрузка
        var warehouseInfo = new System.Collections.Generic.List<(Warehouse warehouse, float distance, int producerCount)>();

        foreach (var wh in warehouses)
        {
            float dist = Vector3.Distance(transform.position, wh.transform.position);
            int producerCount = CountProducersForWarehouse(wh);

            warehouseInfo.Add((wh, dist, producerCount));
        }

        // Сортируем: сначала по нагрузке, затем по расстоянию
        warehouseInfo.Sort((a, b) =>
        {
            int loadComparison = a.producerCount.CompareTo(b.producerCount);
            if (loadComparison != 0)
                return loadComparison;

            return a.distance.CompareTo(b.distance);
        });

        if (warehouseInfo.Count > 0)
        {
            var best = warehouseInfo[0];
            Debug.Log($"[Routing] {gameObject.name}: ✅ ВЫБРАН склад (по прямой) {best.warehouse.name} - расстояние: {best.distance:F1}, нагрузка: {best.producerCount}");
            return best.warehouse;
        }

        return null;
    }
    
    /// <summary>
    /// Устанавливает конкретный маршрут для Output (для программной настройки цепочек)
    /// </summary>
    public void SetOutputDestination(Transform destination)
    {
        outputDestinationTransform = destination;
        RefreshRoutes();
    }
    
    /// <summary>
    /// Устанавливает конкретный источник для Input (для программной настройки цепочек)
    /// </summary>
    public void SetInputSource(Transform source)
    {
        inputSourceTransform = source;
        RefreshRoutes();
    }
    
    /// <summary>
    /// Проверяет, настроены ли маршруты
    /// </summary>
    public bool IsConfigured()
    {
        // Проверяем Output (обязательно для всех зданий)
        if (outputDestination == null)
            return false;
        // ✅ ИСПРАВЛЕНИЕ: Проверяем Input только если здание требует сырьё
        var inputInv = GetComponent<BuildingInputInventory>();
        if (inputInv != null && inputInv.requiredResources != null && inputInv.requiredResources.Count > 0)
        {
            // Здание требует Input - проверяем, что источник настроен
            return inputSource != null;
        }
        // Здание не требует Input (например, лесопилка) - только Output важен
        return true;
    }
    
    /// <summary>
    /// Проверяет, настроен ли Output
    /// </summary>
    public bool HasOutputDestination()
    {
        return outputDestination != null;
    }
    
    /// <summary>
    /// Проверяет, настроен ли Input
    /// </summary>
    public bool HasInputSource()
    {
        return inputSource != null;
    }

    /// <summary>
    /// ✅ НОВОЕ: Уведомляет о доставке к текущему потребителю
    /// Вызывается CartAgent'ом после успешной разгрузки Output
    /// </summary>
    public void NotifyDeliveryCompleted()
    {
        // ✅ АДАПТИВНАЯ КООРДИНАЦИЯ: Регистрируем связь в координаторе
        if (_enableCoordination && outputDestination != null && ResourceCoordinator.Instance != null)
        {
            var outputInv = GetComponent<BuildingOutputInventory>();
            if (outputInv != null)
            {
                ResourceType producedType = outputInv.GetProvidedResourceType();
                if (outputDestination is MonoBehaviour consumerMB)
                {
                    ResourceCoordinator.Instance.RegisterSupplyRoute(this, consumerMB, producedType);
                }

                // Проверяем режим работы
                bool useExclusiveReservation = ResourceCoordinator.Instance.ShouldUseExclusiveReservation(this, producedType);

                if (!useExclusiveReservation)
                {
                    // РЕЖИМ МНОГОПОТОЧНОСТИ - разрешаем rotation
                    _deliveryCountToCurrentConsumer++;
                    Debug.Log($"[Routing] {gameObject.name}: РЕЖИМ МНОГОПОТОЧНОСТИ - Доставка #{_deliveryCountToCurrentConsumer} к {GetConsumerName(outputDestination)} завершена");

                    // Проверяем, пора ли переключаться
                    if (_deliveryCountToCurrentConsumer >= _deliveriesBeforeRotation)
                    {
                        Debug.Log($"[Routing] {gameObject.name}: Достигнут лимит доставок ({_deliveriesBeforeRotation}), переключаюсь на следующего потребителя...");
                        _deliveryCountToCurrentConsumer = 0;

                        // Ищем следующего потребителя
                        RotateToNextConsumer();
                    }
                    return; // Выход из метода
                }
                else
                {
                    // РЕЖИМ 1:1 - rotation отключен
                    Debug.Log($"[Routing] {gameObject.name}: РЕЖИМ 1:1 - доставка завершена, rotation отключен");
                    return; // Выход из метода
                }
            }
        }

        // Старая логика round-robin (если координация отключена)
        if (!_enableRoundRobin || outputDestination == null)
            return;

        _deliveryCountToCurrentConsumer++;

        Debug.Log($"[Routing] {gameObject.name}: Доставка #{_deliveryCountToCurrentConsumer} к {GetConsumerName(outputDestination)} завершена");

        // Проверяем, пора ли переключаться
        if (_deliveryCountToCurrentConsumer >= _deliveriesBeforeRotation)
        {
            Debug.Log($"[Routing] {gameObject.name}: Достигнут лимит доставок ({_deliveriesBeforeRotation}), переключаюсь на следующего потребителя...");
            _deliveryCountToCurrentConsumer = 0;

            // Ищем следующего потребителя
            RotateToNextConsumer();
        }
    }

    /// <summary>
    /// ✅ НОВОЕ: Переключается на следующего доступного потребителя для round-robin
    /// </summary>
    private void RotateToNextConsumer()
    {
        // Если это не автоматический маршрут (задан вручную), не переключаем
        if (outputDestinationTransform != null)
        {
            Debug.Log($"[Routing] {gameObject.name}: Output destination задан вручную, rotation отменен");
            return;
        }

        // ✅ АДАПТИВНАЯ КООРДИНАЦИЯ: Проверяем режим работы
        if (_enableCoordination && ResourceCoordinator.Instance != null)
        {
            // Определяем тип ресурса
            var checkOutputInv = GetComponent<BuildingOutputInventory>();
            if (checkOutputInv != null)
            {
                ResourceType checkProducedType = checkOutputInv.GetProvidedResourceType();

                // Проверяем, используем ли мы жесткое резервирование 1:1
                bool useExclusiveReservation = ResourceCoordinator.Instance.ShouldUseExclusiveReservation(this, checkProducedType);

                if (useExclusiveReservation)
                {
                    // РЕЖИМ 1:1 - НЕ переключаемся (фиксированная связь)
                    Debug.Log($"[Routing] {gameObject.name}: РЕЖИМ 1:1 - rotation отменен (используем фиксированную связь)");
                    return;
                }
                else
                {
                    // РЕЖИМ МНОГОПОТОЧНОСТИ - разрешаем rotation
                    Debug.Log($"[Routing] {gameObject.name}: РЕЖИМ МНОГОПОТОЧНОСТИ - rotation разрешен");
                    // Продолжаем выполнение rotation
                }
            }
        }
        else if (_enableCoordination)
        {
            // Координация включена, но ResourceCoordinator не найден - используем старую логику
            Debug.Log($"[Routing] {gameObject.name}: Координация включена, rotation отменен (используем фиксированную связь)");
            return;
        }

        // Сохраняем текущего потребителя
        IResourceReceiver currentConsumer = outputDestination;

        // Определяем, какой ресурс мы производим
        var outputInv = GetComponent<BuildingOutputInventory>();
        if (outputInv == null)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Нет BuildingOutputInventory, rotation отменен");
            return;
        }

        ResourceType producedType = outputInv.GetProvidedResourceType();
        if (producedType == ResourceType.None)
        {
            Debug.LogWarning($"[Routing] {gameObject.name}: Не производим ничего, rotation отменен");
            return;
        }

        // Находим всех потребителей этого ресурса
        BuildingInputInventory[] allInputs = FindObjectsByType<BuildingInputInventory>(FindObjectsSortMode.None);
        var matchingConsumers = new System.Collections.Generic.List<BuildingInputInventory>();

        foreach (var input in allInputs)
        {
            // Пропускаем себя
            if (input.gameObject == gameObject)
                continue;

            // Проверяем, требует ли это здание наш ресурс
            bool needsOurResource = false;
            foreach (var slot in input.requiredResources)
            {
                if (slot.resourceType == producedType)
                {
                    needsOurResource = true;
                    break;
                }
            }

            if (needsOurResource)
            {
                matchingConsumers.Add(input);
            }
        }

        if (matchingConsumers.Count == 0)
        {
            Debug.Log($"[Routing] {gameObject.name}: Нет других потребителей {producedType}, остаюсь с текущим");
            return;
        }

        if (matchingConsumers.Count == 1)
        {
            Debug.Log($"[Routing] {gameObject.name}: Только один потребитель {producedType}, rotation не требуется");
            return;
        }

        // Находим индекс текущего потребителя в списке
        int currentIndex = -1;
        for (int i = 0; i < matchingConsumers.Count; i++)
        {
            if ((object)matchingConsumers[i] == (object)currentConsumer)
            {
                currentIndex = i;
                break;
            }
        }

        // Выбираем следующего потребителя (круговой обход)
        int nextIndex = (currentIndex + 1) % matchingConsumers.Count;
        BuildingInputInventory nextConsumer = matchingConsumers[nextIndex];

        // Проверяем, не заполнен ли следующий потребитель
        float nextFillRatio = GetConsumerFillRatio(nextConsumer, producedType);

        // Если следующий потребитель заполнен >= 95%, ищем незаполненного
        if (nextFillRatio >= 0.95f)
        {
            Debug.Log($"[Routing] {gameObject.name}: Следующий потребитель {nextConsumer.name} заполнен на {nextFillRatio*100:F0}%, ищу незаполненного...");

            bool foundAvailable = false;
            for (int i = 0; i < matchingConsumers.Count; i++)
            {
                int checkIndex = (nextIndex + i) % matchingConsumers.Count;
                var candidateConsumer = matchingConsumers[checkIndex];
                float candidateFillRatio = GetConsumerFillRatio(candidateConsumer, producedType);

                if (candidateFillRatio < 0.95f)
                {
                    nextConsumer = candidateConsumer;
                    nextFillRatio = candidateFillRatio;
                    foundAvailable = true;
                    Debug.Log($"[Routing] {gameObject.name}: Найден незаполненный потребитель {nextConsumer.name} (заполнение: {nextFillRatio*100:F0}%)");
                    break;
                }
            }

            if (!foundAvailable)
            {
                Debug.Log($"[Routing] {gameObject.name}: Все потребители заполнены >= 95%, остаюсь с текущим");
                return;
            }
        }

        // Переключаемся на следующего потребителя
        outputDestination = nextConsumer;
        _outputDestinationName = $"{nextConsumer.name} (round-robin)";

        Debug.Log($"[Routing] {gameObject.name}: 🔄 ROTATION: {GetConsumerName(currentConsumer)} → {nextConsumer.name} (заполнение: {nextFillRatio*100:F0}%)");

        // Уведомляем ResourceProducer об изменении маршрута
        var producer = GetComponent<ResourceProducer>();
        if (producer != null)
        {
            producer.RefreshWarehouseAccess();
        }
    }

    /// <summary>
    /// Вспомогательный метод для получения имени потребителя
    /// </summary>
    private string GetConsumerName(IResourceReceiver receiver)
    {
        if (receiver == null) return "null";
        if (receiver is MonoBehaviour mb) return mb.name;
        return receiver.ToString();
    }
    
    // === ДЕБАГ ===
    
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        
        // Рисуем линии от здания к Output destination
        if (outputDestination != null)
        {
            Gizmos.color = Color.green;
            Vector3 start = transform.position + Vector3.up * 2f;
            
            // Если outputDestination - MonoBehaviour, берём его Transform
            if (outputDestination is MonoBehaviour mb)
            {
                Vector3 end = mb.transform.position + Vector3.up * 2f;
                Gizmos.DrawLine(start, end);
                Gizmos.DrawSphere(end, 0.5f);
            }
        }
        
        // Рисуем линии от Input source к зданию
        if (inputSource != null)
        {
            Gizmos.color = Color.blue;
            Vector3 end = transform.position + Vector3.up * 2f;
            
            // Если inputSource - MonoBehaviour, берём его Transform
            if (inputSource is MonoBehaviour mb)
            {
                Vector3 start = mb.transform.position + Vector3.up * 2f;
                Gizmos.DrawLine(start, end);
                Gizmos.DrawSphere(start, 0.5f);
            }
        }
    }
}