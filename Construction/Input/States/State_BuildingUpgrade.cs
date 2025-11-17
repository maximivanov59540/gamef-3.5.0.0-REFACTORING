using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Структура для сохранения состояния здания перед апгрейдом
/// Используется для переноса данных (инвентарь, прогресс производства) на новый tier
/// </summary>
public class State_BuildingUpgrade
{
    // --- Базовая информация ---
    public BuildingData originalData;
    public BuildingData targetData;
    public UnityEngine.Vector2Int gridPosition;
    public float rotation;
    public int currentTier;

    // --- Инвентарь (Input) ---
    public Dictionary<ResourceType, float> inputInventory = new Dictionary<ResourceType, float>();

    // --- Инвентарь (Output) ---
    public ResourceType outputResourceType;
    public float outputAmount;
    public float outputCapacity;

    // --- Состояние производства ---
    public float productionProgress; // Прогресс текущего цикла (0.0 - 1.0)
    public float rampUpEfficiency;   // Эффективность "разгона"

    // --- Модули (для модульных зданий) ---
    public List<UnityEngine.Vector2Int> modulePositions = new List<UnityEngine.Vector2Int>();

    /// <summary>
    /// Сохраняет текущее состояние здания
    /// </summary>
    public static State_BuildingUpgrade CaptureState(BuildingIdentity identity)
    {
        var state = new State_BuildingUpgrade
        {
            originalData = identity.buildingData,
            targetData = identity.GetNextTierData(),
            gridPosition = identity.rootGridPosition,
            rotation = identity.yRotation,
            currentTier = identity.currentTier
        };

        // Сохраняем входной инвентарь
        var inputInv = identity.GetComponent<BuildingInputInventory>();
        if (inputInv != null)
        {
            foreach (var kvp in inputInv.GetAllResources())
            {
                state.inputInventory[kvp.Key] = kvp.Value;
            }
        }

        // Сохраняем выходной инвентарь
        var outputInv = identity.GetComponent<BuildingOutputInventory>();
        if (outputInv != null)
        {
            state.outputResourceType = outputInv.resourceType;
            state.outputAmount = outputInv.GetCurrentAmount();
            state.outputCapacity = outputInv.GetCapacity();
        }

        // Сохраняем прогресс производства 1
        var producer = identity.GetComponent<ResourceProducer>();
        if (producer != null && producer.productionData != null)
        {
            // Прогресс цикла (используем reflection для доступа к приватному полю)
            var cycleTimerField = typeof(ResourceProducer).GetField("_cycleTimer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (cycleTimerField != null)
            {
                float cycleTimer = (float)cycleTimerField.GetValue(producer);
                float cycleTime = producer.productionData.cycleTimeSeconds;
                state.productionProgress = cycleTime > 0 ? cycleTimer / cycleTime : 0f;
            }

            // Эффективность разгона
            var rampUpField = typeof(ResourceProducer).GetField("_rampUpEfficiency",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (rampUpField != null)
            {
                state.rampUpEfficiency = (float)rampUpField.GetValue(producer);
            }
        }

        // Сохраняем позиции модулей (если это модульное здание)
        var modularBuilding = identity.GetComponent<ModularBuilding>();
        if (modularBuilding != null)
        {
            var modules = modularBuilding.GetRegisteredModules();
            foreach (var module in modules)
            {
                if (module != null)
                {
                    var moduleIdentity = module.GetComponent<BuildingIdentity>();
                    if (moduleIdentity != null)
                    {
                        state.modulePositions.Add(moduleIdentity.rootGridPosition);
                    }
                }
            }
        }

        return state;
    }

    /// <summary>
    /// Восстанавливает состояние на новое здание после апгрейда
    /// </summary>
    public void RestoreState(BuildingIdentity newIdentity)
    {
        // Восстанавливаем tier
        newIdentity.currentTier = currentTier + 1;

        // Восстанавливаем входной инвентарь
        var inputInv = newIdentity.GetComponent<BuildingInputInventory>();
        if (inputInv != null && inputInventory.Count > 0)
        {
            foreach (var kvp in inputInventory)
            {
                inputInv.AddResource(kvp.Key, kvp.Value);
            }
        }

        // Восстанавливаем выходной инвентарь
        var outputInv = newIdentity.GetComponent<BuildingOutputInventory>();
        if (outputInv != null && outputAmount > 0)
        {
            outputInv.TryAddResource(Mathf.RoundToInt(outputAmount));
        }

        // Восстанавливаем прогресс производства
        var producer = newIdentity.GetComponent<ResourceProducer>();
        if (producer != null)
        {
            // Восстанавливаем прогресс цикла
            var cycleTimerField = typeof(ResourceProducer).GetField("_cycleTimer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (cycleTimerField != null && producer.productionData != null)
            {
                float newCycleTime = producer.productionData.cycleTimeSeconds;
                float restoredTimer = productionProgress * newCycleTime;
                cycleTimerField.SetValue(producer, restoredTimer);
            }

            // Восстанавливаем эффективность разгона
            var rampUpField = typeof(ResourceProducer).GetField("_rampUpEfficiency",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (rampUpField != null)
            {
                rampUpField.SetValue(producer, rampUpEfficiency);
            }
        }

        // ПРИМЕЧАНИЕ: Модули НЕ переносятся автоматически.
        // Это решение для баланса игры - игрок должен заново построить модули
        // для здания нового уровня. Если нужно переносить модули - раскомментируйте код ниже:

        /*
        var modularBuilding = newIdentity.GetComponent<ModularBuilding>();
        if (modularBuilding != null && modulePositions.Count > 0)
        {
            // Здесь нужна логика переноса модулей
            // Это сложно, т.к. нужно взаимодействие с BuildingManager
        }
        */
    }
}