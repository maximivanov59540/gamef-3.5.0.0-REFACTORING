using System.Collections;
using UnityEngine;

/// <summary>
/// "Мозг" экономики (Синглтон).
/// Управляет "Содержанием" (Upkeep) зданий и состоянием "Долга" (Debt).
/// </summary>
public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance { get; private set; }

    [Tooltip("Мы в долгах? (Не можем строить)")]
    public bool IsInDebt { get; private set; } = false;

    // === СОБЫТИЯ ДЛЯ РАЗРЫВА ЦИКЛИЧЕСКИХ ЗАВИСИМОСТЕЙ ===
    /// <summary>
    /// Событие, которое срабатывает при изменении статуса долга.
    /// Использует event-driven подход вместо прямого polling IsInDebt.
    /// </summary>
    public event System.Action<bool> OnDebtStatusChanged;

    private NotificationManager _notificationManager;
    private Coroutine _minuteTickCoroutine; // 🔥 FIX: Храним ссылку на корутину

    private void Awake()
    {
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
        _minuteTickCoroutine = StartCoroutine(MinuteTick());
    }

    // 🔥 FIX: Memory leak - останавливаем корутину при уничтожении
    private void OnDestroy()
    {
        if (_minuteTickCoroutine != null)
        {
            StopCoroutine(_minuteTickCoroutine);
            _minuteTickCoroutine = null;
        }
    }

    /// <summary>
    /// Корутина, которая срабатывает раз в 60 секунд.
    /// </summary>
    private IEnumerator MinuteTick()
    {
        while (true)
        {
            // Ждем 1 минуту
            yield return new WaitForSeconds(60f);

            float totalUpkeep = 0;

            // FIX #12: Используем BuildingRegistry вместо FindObjectsByType каждую минуту
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
                Debug.LogWarning("[EconomyManager] BuildingRegistry.Instance == null! Не могу получить список зданий.");
            }

            if (totalUpkeep > 0)
            {
                // 2. Пытаемся списать деньги из "Казны"
                bool success = MoneyManager.Instance.SpendMoney(totalUpkeep);

                // 3. Обновляем статус "в долгах" и отправляем событие
                bool newDebtStatus = !success;

                // FIX: Event-driven вместо polling - отправляем событие только при изменении статуса
                if (IsInDebt != newDebtStatus)
                {
                    IsInDebt = newDebtStatus;
                    OnDebtStatusChanged?.Invoke(IsInDebt);
                    Debug.Log($"[EconomyManager] Статус долга изменен: IsInDebt = {IsInDebt}");
                }

                if (!success)
                {
                    Debug.LogWarning($"[EconomyManager] Не удалось оплатить содержание! Upkeep: {totalUpkeep}. Мы в долгах!");
                    _notificationManager?.ShowNotification("Внимание: Казна пуста! Содержание не оплачено.");
                }
                else
                {
                    Debug.Log($"[EconomyManager] Содержание (Upkeep) оплачено: {totalUpkeep}");
                }
            }
            else
            {
                // Если платить не за что, мы не в долгах
                if (IsInDebt != false)
                {
                    IsInDebt = false;
                    OnDebtStatusChanged?.Invoke(IsInDebt);
                    Debug.Log($"[EconomyManager] Статус долга изменен: IsInDebt = false (нет расходов)");
                }
            }
        }
    }
}