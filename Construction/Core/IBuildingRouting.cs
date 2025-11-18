using UnityEngine;

/// <summary>
/// Интерфейс для компонентов, управляющих маршрутизацией ресурсов здания.
/// Определяет, КУДА отвозить Output и ОТКУДА брать Input.
///
/// Реализуется: BuildingResourceRouting
///
/// ЦЕЛЬ: Избежать прямых зависимостей от конкретного класса BuildingResourceRouting
/// через GetComponent<BuildingResourceRouting>(). Вместо этого используем
/// GetComponent<IBuildingRouting>().
///
/// Это снижает coupling между системами (CartAgent, ResourceProducer)
/// и маршрутизацией, упрощает тестирование и позволяет создавать
/// альтернативные реализации маршрутизации.
/// </summary>
public interface IBuildingRouting
{
    /// <summary>
    /// Получатель продукции (Output). Может быть null если не настроен
    /// </summary>
    IResourceReceiver outputDestination { get; }

    /// <summary>
    /// Поставщик сырья (Input). Может быть null если не настроен
    /// </summary>
    IResourceProvider inputSource { get; }

    /// <summary>
    /// Transform целевого здания для Output (визуальная настройка)
    /// </summary>
    Transform outputDestinationTransform { get; set; }

    /// <summary>
    /// Transform источника для Input (визуальная настройка)
    /// </summary>
    Transform inputSourceTransform { get; set; }

    /// <summary>
    /// Обновляет маршруты (повторный поиск складов/производителей)
    /// </summary>
    void RefreshRoutes();

    /// <summary>
    /// Устанавливает получателя продукции
    /// </summary>
    /// <param name="destination">Transform целевого здания</param>
    void SetOutputDestination(Transform destination);

    /// <summary>
    /// Устанавливает источник сырья
    /// </summary>
    /// <param name="source">Transform здания-источника</param>
    void SetInputSource(Transform source);

    /// <summary>
    /// Проверяет, настроены ли маршруты (Input и Output)
    /// </summary>
    /// <returns>true если оба маршрута настроены</returns>
    bool IsConfigured();

    /// <summary>
    /// Есть ли настроенный получатель продукции?
    /// </summary>
    /// <returns>true если outputDestination != null</returns>
    bool HasOutputDestination();

    /// <summary>
    /// Есть ли настроенный источник сырья?
    /// </summary>
    /// <returns>true если inputSource != null</returns>
    bool HasInputSource();

    /// <summary>
    /// Уведомляет маршрутизатор о завершении доставки
    /// (используется для round-robin распределения)
    /// </summary>
    void NotifyDeliveryCompleted();

    /// <summary>
    /// Transform компонента (для доступа к GameObject)
    /// </summary>
    Transform transform { get; }

    /// <summary>
    /// GameObject, к которому прикреплен этот компонент
    /// </summary>
    GameObject gameObject { get; }
}
