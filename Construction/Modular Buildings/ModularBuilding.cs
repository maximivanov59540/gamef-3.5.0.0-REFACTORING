using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Компонент для "главного" здания (например, Фермы), которое
/// может "владеть" несколькими "модулями" (например, Полями).
/// </summary>
public class ModularBuilding : MonoBehaviour
{
    [Header("Настройки Модулей")]
    [Tooltip("Список 'Чертежей' (BuildingData) для модулей, которые строит это здание (напр., BD_Field, BD_Pasture)")]
    // БЫЛО: public BuildingData moduleData;
    public List<BuildingData> allowedModules = new List<BuildingData>(); // <-- ИЗМЕНЕНО

    [Tooltip("Максимальное кол-во модулей для этого здания")]
    public int maxModuleCount = 4; // <-- НОВАЯ СТРОКА
    
    [Header("Производство (Опционально)")]
    [Tooltip("Ссылка на Производителя на этом же здании (для бонусов)")]
    public ResourceProducer attachedProducer; // <-- НОВАЯ СТРОКА

    // ISSUE #12 FIX: Заменен List на HashSet для O(1) Contains/Add/Remove вместо O(n)
    private HashSet<BuildingModule> _modules = new HashSet<BuildingModule>();

    private void Awake()
    {
        // Попытка "схватить" продюсера, если не назначен в инспекторе
        if (attachedProducer == null)
            attachedProducer = GetComponent<ResourceProducer>();
    }

    /// <summary>
    /// Вызывается, когда мы "пристраиваем" новый модуль.
    /// </summary>
    public void RegisterModule(BuildingModule module)
    {
        // ISSUE #12 FIX: HashSet.Add возвращает false если элемент уже существует
        if (_modules.Add(module))
        {
            module.parentBuilding = this;

            // --- ОБНОВЛЕННЫЙ КОД ---
            // Сообщаем продюсеру, что кол-во модулей изменилось
            // Передаем и текущее количество, и максимальное для расчета процента
            attachedProducer?.UpdateProductionRate(GetModuleCount(), maxModuleCount);
            // --- КОНЕЦ ОБНОВЛЕННОГО КОДА ---

            Debug.Log($"Модуль добавлен к {gameObject.name}. Всего модулей: {GetModuleCount()} / {maxModuleCount}");
        }
    }

    /// <summary>
    /// Вызывается, когда модуль "уничтожается".
    /// </summary>
    public void UnregisterModule(BuildingModule module)
    {
        // ISSUE #12 FIX: HashSet.Remove возвращает true если элемент был удален
        if (_modules.Remove(module))
        {
            // --- ОБНОВЛЕННЫЙ КОД ---
            // Сообщаем продюсеру, что кол-во модулей изменилось
            // Передаем и текущее количество, и максимальное для расчета процента
            attachedProducer?.UpdateProductionRate(GetModuleCount(), maxModuleCount);
            // --- КОНЕЦ ОБНОВЛЕННОГО КОДА ---

            Debug.Log($"Модуль удален из {gameObject.name}. Всего модулей: {GetModuleCount()} / {maxModuleCount}");
        }
    }
    
    // --- НОВЫЕ ПУБЛИЧНЫЕ МЕТОДЫ ---

    /// <summary>
    /// Возвращает текущее кол-во модулей (для UI и Продюсера).
    /// </summary>
    public int GetModuleCount() => _modules.Count;

    /// <summary>
    /// Проверяет, не достигнут ли лимит (для UI).
    /// </summary>
    public bool CanAddModule() => GetModuleCount() < maxModuleCount;
    
    // --- КОНЕЦ НОВЫХ МЕТОДОВ ---


    /// <summary>
    /// Возвращает все модули (напр., для GridSystem при сносе).
    /// ISSUE #12 FIX: Создаем List из HashSet для обратной совместимости
    /// </summary>
    public List<BuildingModule> GetRegisteredModules()
    {
        return new List<BuildingModule>(_modules);
    }
    public void ClearAllModules()
    {
        // Рвем связь "в одну сторону"
        _modules.Clear();
        // Сбрасываем бонус продюсеру (0 модулей из maxModuleCount)
        attachedProducer?.UpdateProductionRate(0, maxModuleCount);
    }
}