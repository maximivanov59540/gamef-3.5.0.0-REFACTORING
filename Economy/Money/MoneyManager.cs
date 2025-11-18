using System.Collections;
using UnityEngine;

/// <summary>
/// Объединенный менеджер экономики (PHASE 3/4 - Singleton Reduction)
/// Объединяет функциональность:
/// - MoneyManager (казна/деньги)
/// - EconomyManager (долги/upkeep)
/// - TaxManager (налоги)
/// </summary>
public class MoneyManager : MonoBehaviour
{
    // === SINGLETON ===
    public static MoneyManager Instance { get; private set; }

    // === СОСТОЯНИЕ КАЗНЫ (ранее MoneyManager) ===

    [Header("Казна")]
    [Tooltip("Текущее количество денег. Можно смотреть в инспекторе для отладки.")]
    [SerializeField] private float _currentMoney = 100f; // Дадим немного стартовых денег

    // === СИСТЕМА ДОЛГОВ (ранее EconomyManager) ===

    [Header("Экономика и Долги")]
    [Tooltip("Мы в долгах? (Не можем строить)")]
    public bool IsInDebt { get; private set; } = false;

    /// <summary>
    /// Событие, которое срабатывает при изменении статуса долга.
    /// Использует event-driven подход вместо прямого polling IsInDebt.
    /// </summary>
    public event System.Action<bool> OnDebtStatusChanged;

    // === СИСТЕМА НАЛОГОВ (ранее TaxManager) ===

    [Header("Налоги")]
    [Tooltip("Доход в секунду (плавное начисление)")]
    [SerializeField] private float _incomePerSecond;

    // === ССЫЛКИ ===

    private NotificationManager _notificationManager;
    private Coroutine _minuteTickCoroutine;

    // === UNITY LIFECYCLE ===

    private void Awake()
    {
        // Настройка синглтона
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    private void Start()
    {
        _notificationManager = FindFirstObjectByType<NotificationManager>();

        // Запускаем единую корутину для налогов и upkeep
        _minuteTickCoroutine = StartCoroutine(MinuteTick());
    }

    private void Update()
    {
        // Плавное начисление налогов каждый кадр (ранее TaxManager)
        if (_incomePerSecond > 0)
        {
            _currentMoney += _incomePerSecond * Time.deltaTime;
        }
    }

    private void OnDestroy()
    {
        // Останавливаем корутину при уничтожении (предотвращаем memory leak)
        if (_minuteTickCoroutine != null)
        {
            StopCoroutine(_minuteTickCoroutine);
            _minuteTickCoroutine = null;
        }
    }

    // === КОРУТИНА: НАЛОГИ + UPKEEP ===

    /// <summary>
    /// Объединенная корутина для обработки налогов и содержания зданий
    /// Срабатывает раз в 60 секунд
    /// </summary>
    private IEnumerator MinuteTick()
    {
        while (true)
        {
            // Ждем 1 минуту
            yield return new WaitForSeconds(60f);

            // 1. Пересчитываем налоги (ранее TaxManager)
            CalculateTaxIncome();

            // 2. Списываем upkeep (ранее EconomyManager)
            ProcessUpkeep();
        }
    }

    /// <summary>
    /// Пересчитывает общий доход от всех домов (ранее TaxManager.MinuteTick)
    /// </summary>
    private void CalculateTaxIncome()
    {
        float totalIncomePerMinute = 0;

        // Используем BuildingRegistry вместо FindObjectsByType каждую минуту
        if (BuildingRegistry.Instance != null)
        {
            var allResidences = BuildingRegistry.Instance.GetAllResidences();

            foreach (var residence in allResidences)
            {
                if (residence != null) // Проверяем на null (объект мог быть удален)
                {
                    // Собираем налог с каждого дома
                    totalIncomePerMinute += residence.GetCurrentTax();
                }
            }
        }
        else
        {
            Debug.LogWarning("[MoneyManager] BuildingRegistry.Instance == null! Не могу получить список резиденций.");
        }

        // Вычисляем доход в секунду
        _incomePerSecond = totalIncomePerMinute / 60f;

        Debug.Log($"[MoneyManager] Налоги: {totalIncomePerMinute}/мин, {_incomePerSecond}/сек");
    }

    /// <summary>
    /// Списывает содержание (upkeep) всех зданий (ранее EconomyManager.MinuteTick)
    /// </summary>
    private void ProcessUpkeep()
    {
        float totalUpkeep = 0;

        // Используем BuildingRegistry вместо FindObjectsByType каждую минуту
        if (BuildingRegistry.Instance != null)
        {
            var allBuildings = BuildingRegistry.Instance.GetAllBuildings();

            foreach (var building in allBuildings)
            {
                if (building == null) continue; // Проверяем на null (объект мог быть удален)

                // "Проекты" (чертежи) не тратят деньги на содержание
                if (!building.isBlueprint && building.buildingData != null)
                {
                    totalUpkeep += building.buildingData.upkeepCostPerMinute;
                }
            }
        }
        else
        {
            Debug.LogWarning("[MoneyManager] BuildingRegistry.Instance == null! Не могу получить список зданий.");
        }

        if (totalUpkeep > 0)
        {
            // Пытаемся списать деньги из казны
            bool success = SpendMoney(totalUpkeep);

            // Обновляем статус "в долгах" и отправляем событие
            bool newDebtStatus = !success;

            // Event-driven вместо polling - отправляем событие только при изменении статуса
            if (IsInDebt != newDebtStatus)
            {
                IsInDebt = newDebtStatus;
                OnDebtStatusChanged?.Invoke(IsInDebt);
                Debug.Log($"[MoneyManager] Статус долга изменен: IsInDebt = {IsInDebt}");
            }

            if (!success)
            {
                Debug.LogWarning($"[MoneyManager] Не удалось оплатить содержание! Upkeep: {totalUpkeep}. Мы в долгах!");
                _notificationManager?.ShowNotification("Внимание: Казна пуста! Содержание не оплачено.");
            }
            else
            {
                Debug.Log($"[MoneyManager] Содержание (Upkeep) оплачено: {totalUpkeep}");
            }
        }
        else
        {
            // Если платить не за что, мы не в долгах
            if (IsInDebt != false)
            {
                IsInDebt = false;
                OnDebtStatusChanged?.Invoke(IsInDebt);
                Debug.Log($"[MoneyManager] Статус долга изменен: IsInDebt = false (нет расходов)");
            }
        }
    }

    // === ПУБЛИЧНЫЕ МЕТОДЫ: КАЗНА (ранее MoneyManager) ===

    /// <summary>
    /// Возвращает текущий баланс. (Для UI)
    /// </summary>
    public float GetCurrentMoney()
    {
        return _currentMoney;
    }

    /// <summary>
    /// Добавляет деньги в казну (например, налоги).
    /// </summary>
    public void AddMoney(float amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning("Попытка добавить отрицательное кол-во денег. Используйте SpendMoney().");
            return;
        }
        _currentMoney += amount;
    }

    /// <summary>
    /// Проверяет, достаточно ли денег в казне.
    /// </summary>
    public bool CanAffordMoney(float amount)
    {
        return _currentMoney >= amount;
    }

    /// <summary>
    /// Пытается потратить деньги.
    /// Возвращает true, если оплата прошла успешно.
    /// Возвращает false, если денег не хватило.
    /// </summary>
    public bool SpendMoney(float amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning("Попытка потратить отрицательное кол-во денег.");
            return false;
        }

        if (CanAffordMoney(amount))
        {
            // Успех
            _currentMoney -= amount;
            return true;
        }
        else
        {
            // Провал, денег нет
            return false;
        }
    }

    // === УСТАРЕВШИЕ МЕТОДЫ (для обратной совместимости с TaxManager) ===

    /// <summary>
    /// DEPRECATED - Главный метод, который вызывают дома для уплаты налога (деньгами).
    /// Больше не используется, т.к. налоги собираются плавно через MinuteTick
    /// </summary>
    [System.Obsolete("Используйте автоматическую систему сбора налогов")]
    public void CollectTax(float amount)
    {
        if (amount <= 0) return;
        AddMoney(amount);
    }
}
