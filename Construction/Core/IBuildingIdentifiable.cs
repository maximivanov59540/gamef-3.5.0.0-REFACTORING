using UnityEngine;

/// <summary>
/// Интерфейс для компонентов, которые идентифицируют здание в сетке.
/// Реализуется: BuildingIdentity
///
/// ЦЕЛЬ: Избежать прямых зависимостей от конкретного класса BuildingIdentity
/// через GetComponent<BuildingIdentity>(). Вместо этого используем
/// GetComponent<IBuildingIdentifiable>().
///
/// Это снижает coupling и упрощает тестирование.
/// </summary>
public interface IBuildingIdentifiable
{
    /// <summary>
    /// Данные конфигурации здания (тип, размер, стоимость и т.д.)
    /// </summary>
    BuildingData buildingData { get; }

    /// <summary>
    /// Позиция корня здания в сетке
    /// </summary>
    Vector2Int rootGridPosition { get; }

    /// <summary>
    /// Поворот здания в градусах (0, 90, 180, 270)
    /// </summary>
    float yRotation { get; }

    /// <summary>
    /// Является ли это зданием-чертежом (blueprint)?
    /// Чертежи не потребляют ресурсы при размещении
    /// </summary>
    bool isBlueprint { get; }

    /// <summary>
    /// Текущий tier (уровень) здания (1, 2, 3...)
    /// </summary>
    int currentTier { get; }

    /// <summary>
    /// Может ли это здание быть улучшено до следующего tier?
    /// </summary>
    /// <returns>true если есть nextTier и здание не blueprint</returns>
    bool CanUpgradeToNextTier();

    /// <summary>
    /// Возвращает данные следующего уровня здания
    /// </summary>
    /// <returns>BuildingData следующего tier или null</returns>
    BuildingData GetNextTierData();

    /// <summary>
    /// Transform компонента (для доступа к GameObject и позиции в мире)
    /// </summary>
    Transform transform { get; }

    /// <summary>
    /// GameObject, к которому прикреплен этот компонент
    /// </summary>
    GameObject gameObject { get; }
}
