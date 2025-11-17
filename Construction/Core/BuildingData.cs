using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "New Building Data", menuName = "Building System/Building Data")]
public class BuildingData : ScriptableObject
{
    [Header("Info")]
    public string buildingName;
    public string description;
    public Sprite icon;

    [Header("Building Properties")]
    public List<ResourceCost> costs;
    public int housingCapacity = 0;
    public GameObject buildingPrefab;
    public Vector2Int size = new Vector2Int(1, 1);
    
    [Tooltip("Это 'Модуль' (Поле, Пастбище), который 'принадлежит' другому зданию?")]
    public bool isModule = false; // <-- НОВАЯ СТРОКА

    [Header("Настройки Инструмента")]
    [Tooltip("Включить 'Инструмент Улица' (Т-3) для этого здания?")]
    public bool useMassBuildTool = false;
    [Header("Экономика")]
    [Tooltip("Стоимость постройки в золоте")]
    public float moneyCost = 0;
    [Tooltip("Стоимость содержания (золота в минуту)")]
    public float upkeepCostPerMinute = 1;

    [Header("Система Апгрейдов (Tier System)")]
    [Tooltip("Текущий уровень здания (1, 2, 3...)")]
    public int currentTier = 1;

    [Tooltip("Ссылка на BuildingData следующего уровня (null = максимальный уровень)")]
    public BuildingData nextTier = null;

    [Tooltip("Стоимость апгрейда в ресурсах (для перехода на nextTier)")]
    public List<ResourceCost> upgradeCost = new List<ResourceCost>();

    [Tooltip("Стоимость апгрейда в золоте (для перехода на nextTier)")]
    public float upgradeMoneyCost = 0;

    /// <summary>
    /// Проверяет, можно ли улучшить это здание до следующего уровня
    /// </summary>
    public bool CanUpgrade()
    {
        return nextTier != null;
    }

    /// <summary>
    /// Возвращает название здания с указанием уровня
    /// </summary>
    public string GetDisplayName()
    {
        if (currentTier > 1)
            return $"{buildingName} (Ур. {currentTier})";
        return buildingName;
    }
}