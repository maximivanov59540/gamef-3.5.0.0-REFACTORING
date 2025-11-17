using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ✅ НОВАЯ СИСТЕМА КООРДИНАЦИИ
/// Централизованный менеджер для координации производителей и потребителей в рамках дорожной сети.
///
/// Основная идея:
/// - Когда Рудник #1 начинает снабжать Кузницу #1, он "резервирует" её
/// - Рудник #2 видит резервирование и выбирает незарезервированную Кузницу #2
/// - Результат: идеальное распределение вместо хаотичного round-robin
///
/// Работает в рамках одной дорожной сети (road network island).
/// </summary>
public class ResourceCoordinator : MonoBehaviour
{
    public static ResourceCoordinator Instance { get; private set; }

    /// <summary>
    /// Структура для хранения информации о связи производитель → потребитель
    /// </summary>
    private class SupplyRoute
    {
        public MonoBehaviour producer;           // Здание-производитель
        public MonoBehaviour consumer;           // Здание-потребитель
        public ResourceType resourceType;        // Тип ресурса
        public Vector2Int producerGridPos;       // Позиция производителя (для проверки дорожной сети)
        public float lastUpdateTime;             // Время последнего обновления (для очистки устаревших)

        public SupplyRoute(MonoBehaviour prod, MonoBehaviour cons, ResourceType res, Vector2Int prodPos)
        {
            producer = prod;
            consumer = cons;
            resourceType = res;
            producerGridPos = prodPos;
            lastUpdateTime = Time.time;
        }
    }

    // Основное хранилище связей: ключ = потребитель, значение = производитель, который его снабжает
    private Dictionary<MonoBehaviour, SupplyRoute> _activeSupplyRoutes = new Dictionary<MonoBehaviour, SupplyRoute>();

    // Для быстрого поиска по производителю
    private Dictionary<MonoBehaviour, MonoBehaviour> _producerToConsumer = new Dictionary<MonoBehaviour, MonoBehaviour>();

    // Время после которого связь считается устаревшей (если не обновлялась)
    private const float ROUTE_TIMEOUT = 30f;

    private GridSystem _gridSystem;
    private RoadManager _roadManager;

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
        _gridSystem = FindFirstObjectByType<GridSystem>();
        _roadManager = RoadManager.Instance;

        if (_gridSystem == null)
            Debug.LogWarning("[ResourceCoordinator] GridSystem не найден!");
        if (_roadManager == null)
            Debug.LogWarning("[ResourceCoordinator] RoadManager не найден!");
    }

    void Update()
    {
        // Периодическая очистка устаревших связей
        if (Time.frameCount % 300 == 0) // Каждые ~5 секунд при 60 FPS
        {
            CleanupStaleRoutes();
        }
    }

    /// <summary>
    /// ✅ РЕГИСТРАЦИЯ: Производитель сообщает, что начал снабжать потребителя
    /// </summary>
    public void RegisterSupplyRoute(MonoBehaviour producer, MonoBehaviour consumer, ResourceType resourceType)
    {
        if (producer == null || consumer == null)
        {
            Debug.LogWarning("[ResourceCoordinator] RegisterSupplyRoute: producer или consumer == null!");
            return;
        }

        // Получаем позицию производителя
        var producerIdentity = producer.GetComponent<BuildingIdentity>();
        if (producerIdentity == null)
        {
            Debug.LogWarning($"[ResourceCoordinator] {producer.name} не имеет BuildingIdentity!");
            return;
        }

        Vector2Int producerPos = producerIdentity.rootGridPosition;

        // Проверяем, не занят ли уже этот потребитель другим производителем
        if (_activeSupplyRoutes.TryGetValue(consumer, out SupplyRoute existingRoute))
        {
            // Если это тот же производитель - просто обновляем время
            if (existingRoute.producer == producer)
            {
                existingRoute.lastUpdateTime = Time.time;
                Debug.Log($"[ResourceCoordinator] 🔄 Обновлена связь: {producer.name} → {consumer.name} ({resourceType})");
                return;
            }

            // Другой производитель! Вытесняем старого
            Debug.Log($"[ResourceCoordinator] ⚠️ КОНФЛИКТ: {consumer.name} был занят {existingRoute.producer.name}, теперь {producer.name} ({resourceType})");

            // Удаляем старую связь
            if (_producerToConsumer.ContainsKey(existingRoute.producer))
                _producerToConsumer.Remove(existingRoute.producer);
        }

        // Создаем новую связь
        var route = new SupplyRoute(producer, consumer, resourceType, producerPos);
        _activeSupplyRoutes[consumer] = route;
        _producerToConsumer[producer] = consumer;

        Debug.Log($"[ResourceCoordinator] ✅ ЗАРЕГИСТРИРОВАНА связь: {producer.name} → {consumer.name} ({resourceType})");
    }

    /// <summary>
    /// ✅ ОТМЕНА РЕГИСТРАЦИИ: Производитель сообщает, что перестал снабжать потребителя
    /// </summary>
    public void UnregisterSupplyRoute(MonoBehaviour producer, MonoBehaviour consumer)
    {
        if (producer == null || consumer == null)
            return;

        // Проверяем, что это действительно наша связь
        if (_activeSupplyRoutes.TryGetValue(consumer, out SupplyRoute route))
        {
            if (route.producer == producer)
            {
                _activeSupplyRoutes.Remove(consumer);
                _producerToConsumer.Remove(producer);
                Debug.Log($"[ResourceCoordinator] ❌ УДАЛЕНА связь: {producer.name} → {consumer.name}");
            }
        }
    }

    /// <summary>
    /// ✅ ПРОВЕРКА: Занят ли этот потребитель другим производителем?
    /// </summary>
    public bool IsConsumerReserved(MonoBehaviour consumer, MonoBehaviour requestingProducer)
    {
        if (consumer == null || requestingProducer == null)
            return false;

        if (!_activeSupplyRoutes.TryGetValue(consumer, out SupplyRoute route))
            return false; // Не зарезервирован

        // Если это мы сами - не считается резервированием
        if (route.producer == requestingProducer)
            return false;

        // Проверяем, не устарела ли связь
        if (Time.time - route.lastUpdateTime > ROUTE_TIMEOUT)
        {
            Debug.Log($"[ResourceCoordinator] Связь {route.producer.name} → {consumer.name} устарела, удаляю");
            _activeSupplyRoutes.Remove(consumer);
            _producerToConsumer.Remove(route.producer);
            return false;
        }

        // ✅ ВАЖНАЯ ПРОВЕРКА: Находятся ли производитель и потребитель в одной дорожной сети?
        if (!AreInSameRoadNetwork(requestingProducer, route.producer))
        {
            // Разные дорожные сети - резервирование не действует
            return false;
        }

        // Да, зарезервирован другим производителем в той же сети
        Debug.Log($"[ResourceCoordinator] 🚫 {consumer.name} зарезервирован {route.producer.name} (запрос от {requestingProducer.name})");
        return true;
    }

    /// <summary>
    /// ✅ ИНФОРМАЦИЯ: Получить список всех зарезервированных потребителей для данного типа ресурса
    /// </summary>
    public List<MonoBehaviour> GetReservedConsumers(ResourceType resourceType, MonoBehaviour requestingProducer)
    {
        var reserved = new List<MonoBehaviour>();

        foreach (var kvp in _activeSupplyRoutes)
        {
            var route = kvp.Value;

            // Пропускаем другие типы ресурсов
            if (route.resourceType != resourceType)
                continue;

            // Пропускаем самого себя
            if (route.producer == requestingProducer)
                continue;

            // Пропускаем если не в одной дорожной сети
            if (!AreInSameRoadNetwork(requestingProducer, route.producer))
                continue;

            // Пропускаем устаревшие
            if (Time.time - route.lastUpdateTime > ROUTE_TIMEOUT)
                continue;

            reserved.Add(kvp.Key); // kvp.Key = consumer
        }

        return reserved;
    }

    /// <summary>
    /// ✅ ИНФОРМАЦИЯ: Какого потребителя снабжает данный производитель?
    /// </summary>
    public MonoBehaviour GetConsumerForProducer(MonoBehaviour producer)
    {
        if (_producerToConsumer.TryGetValue(producer, out MonoBehaviour consumer))
        {
            // Проверяем актуальность
            if (_activeSupplyRoutes.TryGetValue(consumer, out SupplyRoute route))
            {
                if (Time.time - route.lastUpdateTime <= ROUTE_TIMEOUT)
                {
                    return consumer;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Проверяет, находятся ли два здания в одной дорожной сети
    /// </summary>
    private bool AreInSameRoadNetwork(MonoBehaviour building1, MonoBehaviour building2)
    {
        if (_gridSystem == null || _roadManager == null)
            return true; // Fallback: считаем что в одной сети

        var identity1 = building1.GetComponent<BuildingIdentity>();
        var identity2 = building2.GetComponent<BuildingIdentity>();

        if (identity1 == null || identity2 == null)
            return true; // Fallback

        var roadGraph = _roadManager.GetRoadGraph();
        if (roadGraph == null || roadGraph.Count == 0)
            return true; // Нет дорог - все в одной "сети"

        // Находим точки доступа к дорогам для обоих зданий
        var access1 = LogisticsPathfinder.FindAllRoadAccess(identity1.rootGridPosition, _gridSystem, roadGraph);
        var access2 = LogisticsPathfinder.FindAllRoadAccess(identity2.rootGridPosition, _gridSystem, roadGraph);

        if (access1.Count == 0 || access2.Count == 0)
            return false; // Одно из зданий не у дороги

        // Проверяем достижимость через BFS
        var distances = LogisticsPathfinder.Distances_BFS_Multi(access1, 10000, roadGraph);

        foreach (var point in access2)
        {
            if (distances.ContainsKey(point))
                return true; // Достижимо = в одной сети
        }

        return false; // Недостижимо = разные дорожные сети
    }

    /// <summary>
    /// ✅ НОВОЕ: Определяет, нужно ли использовать жесткое резервирование 1:1 или разрешить многопоточность
    /// </summary>
    /// <param name="producer">Производитель, который делает запрос</param>
    /// <param name="resourceType">Тип ресурса</param>
    /// <returns>true = использовать жесткое резервирование 1:1, false = разрешить многопоточность</returns>
    public bool ShouldUseExclusiveReservation(MonoBehaviour producer, ResourceType resourceType)
    {
        if (producer == null)
            return true;

        // Находим всех производителей и потребителей данного ресурса в нашей сети
        var producers = GetAllProducersInNetwork(producer, resourceType);
        var consumers = GetAllConsumersInNetwork(producer, resourceType);

        int producerCount = producers.Count;
        int consumerCount = consumers.Count;

        Debug.Log($"[ResourceCoordinator] Соотношение {resourceType}: {producerCount} производителей, {consumerCount} потребителей");

        // Если производителей >= потребителей → жесткое резервирование 1:1
        // Пример: 2 рудника, 2 кузницы → каждый рудник обслуживает свою кузницу
        if (producerCount >= consumerCount)
        {
            Debug.Log($"[ResourceCoordinator] {producer.name}: Производителей >= потребителей → жесткое резервирование 1:1");
            return true;
        }

        // Если производителей < потребителей → разрешить многопоточность
        // Пример: 1 рудник, 2 кузницы → рудник может обслуживать обе кузницы
        Debug.Log($"[ResourceCoordinator] {producer.name}: Производителей < потребителей → многопоточность разрешена");
        return false;
    }

    /// <summary>
    /// ✅ НОВОЕ: Находит всех производителей данного ресурса в той же дорожной сети
    /// </summary>
    private List<MonoBehaviour> GetAllProducersInNetwork(MonoBehaviour referenceBuilding, ResourceType resourceType)
    {
        var producers = new List<MonoBehaviour>();

        // 🚀 PERFORMANCE FIX: Используем BuildingRegistry вместо FindObjectsByType
        var allOutputs = BuildingRegistry.Instance?.GetAllOutputs();
        if (allOutputs == null || allOutputs.Count == 0)
            return producers;

        for (int i = 0; i < allOutputs.Count; i++)
        {
            var output = allOutputs[i];

            // Проверяем тип ресурса
            if (output.GetProvidedResourceType() != resourceType)
                continue;

            // Проверяем, что в той же дорожной сети
            if (!AreInSameRoadNetwork(referenceBuilding, output))
                continue;

            producers.Add(output);
        }

        return producers;
    }

    /// <summary>
    /// ✅ НОВОЕ: Находит всех потребителей данного ресурса в той же дорожной сети
    /// </summary>
    private List<MonoBehaviour> GetAllConsumersInNetwork(MonoBehaviour referenceBuilding, ResourceType resourceType)
    {
        var consumers = new List<MonoBehaviour>();

        // 🚀 PERFORMANCE FIX: Используем BuildingRegistry вместо FindObjectsByType
        var allInputs = BuildingRegistry.Instance?.GetAllInputs();
        if (allInputs == null || allInputs.Count == 0)
            return consumers;

        for (int i = 0; i < allInputs.Count; i++)
        {
            var input = allInputs[i];

            // Проверяем, требует ли это здание данный ресурс
            bool needsResource = false;
            if (input.requiredResources != null)
            {
                for (int j = 0; j < input.requiredResources.Count; j++)
                {
                    if (input.requiredResources[j].resourceType == resourceType)
                    {
                        needsResource = true;
                        break;
                    }
                }
            }

            if (!needsResource)
                continue;

            // Проверяем, что в той же дорожной сети
            if (!AreInSameRoadNetwork(referenceBuilding, input))
                continue;

            consumers.Add(input);
        }

        return consumers;
    }

    /// <summary>
    /// Очищает устаревшие связи
    /// 🚀 MEMORY LEAK FIX: Улучшенная очистка с проверкой Unity fake null
    /// </summary>
    private void CleanupStaleRoutes()
    {
        var toRemove = new List<MonoBehaviour>();

        foreach (var kvp in _activeSupplyRoutes)
        {
            var consumer = kvp.Key;
            var route = kvp.Value;

            // Удаляем если устарела
            if (Time.time - route.lastUpdateTime > ROUTE_TIMEOUT)
            {
                toRemove.Add(consumer);
                continue;
            }

            // 🚀 FIX: Проверка Unity fake null (когда объект уничтожен но ссылка не null)
            // Используем ReferenceEquals для проверки настоящего null
            bool producerDestroyed = route.producer == null || !route.producer;
            bool consumerDestroyed = consumer == null || !consumer;

            if (producerDestroyed || consumerDestroyed)
            {
                toRemove.Add(consumer);
            }
        }

        // Удаляем все найденные устаревшие связи
        foreach (var consumer in toRemove)
        {
            if (_activeSupplyRoutes.TryGetValue(consumer, out SupplyRoute route))
            {
                // Удаляем из обоих словарей
                if (route.producer != null)
                {
                    _producerToConsumer.Remove(route.producer);
                }
                _activeSupplyRoutes.Remove(consumer);

                Debug.Log($"[ResourceCoordinator] 🧹 Очищена устаревшая связь: {route.producer?.name ?? "null"} → {consumer?.name ?? "null"}");
            }
        }

        // 🚀 FIX: Дополнительная очистка _producerToConsumer от уничтоженных производителей
        var deadProducers = new List<MonoBehaviour>();
        foreach (var kvp in _producerToConsumer)
        {
            var producer = kvp.Key;
            var consumer = kvp.Value;

            bool producerDestroyed = producer == null || !producer;
            bool consumerDestroyed = consumer == null || !consumer;

            if (producerDestroyed || consumerDestroyed)
            {
                deadProducers.Add(producer);
            }
        }

        foreach (var producer in deadProducers)
        {
            _producerToConsumer.Remove(producer);
        }

        if (toRemove.Count > 0 || deadProducers.Count > 0)
        {
            Debug.Log($"[ResourceCoordinator] 🧹 Очистка завершена: удалено {toRemove.Count} маршрутов, {deadProducers.Count} производителей");
        }
    }

    /// <summary>
    /// ДЕБАГ: Вывести все активные связи
    /// </summary>
    public void DebugPrintRoutes()
    {
        Debug.Log($"[ResourceCoordinator] === АКТИВНЫЕ СВЯЗИ ({_activeSupplyRoutes.Count}) ===");
        foreach (var kvp in _activeSupplyRoutes)
        {
            var route = kvp.Value;
            float age = Time.time - route.lastUpdateTime;
            Debug.Log($"  {route.producer.name} → {route.consumer.name} ({route.resourceType}) [возраст: {age:F1}с]");
        }
    }
}