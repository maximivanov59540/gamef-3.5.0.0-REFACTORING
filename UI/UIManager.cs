using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
// --- НУЖНО ДОБАВИТЬ ЭТО ---
using UnityEngine.UI; 

public class UIManager : MonoBehaviour
{
    [SerializeField] private GameObject infoPanel;
    [SerializeField] private TextMeshProUGUI buildingNameText;
    [Header("Панель Подтверждения")]
    [SerializeField] private GameObject _confirmationPanel; 
    [SerializeField] private TextMeshProUGUI _confirmationText; 
    
    [Header("Контекстные Кнопки (Модули)")]
    [Tooltip("КОНТЕЙНЕР (пустой GameObject), куда будут 'спавниться' кнопки модулей")]
    [SerializeField] private GameObject moduleButtonContainer; // <-- ИЗМЕНЕНО (Был 'addModuleButton')

    [Tooltip("ПРЕФАБ кнопки 'UI_ModuleButton'")]
    [SerializeField] private GameObject moduleButtonPrefab; // <-- НОВАЯ СТРОКА
    [Header("Панель Производства")]
    [Tooltip("Весь 'контейнер' со слайдером (панель)")]
    [SerializeField] private GameObject productionPanel;
    [Tooltip("Сам Слайдер")]
    [SerializeField] private Slider productivitySlider;
    [Tooltip("Текст для отображения % (напр. '100%')")]
    [SerializeField] private TextMeshProUGUI productivityText;

    [Header("Панель Склада")]
    [SerializeField] private GameObject warehousePanel;
    [SerializeField] private TextMeshProUGUI warehouseQueueText;

    [Header("Панель Баланса")]
    [SerializeField] private GameObject balancePanel;

    // --- Приватные ссылки ---
    private ResourceProducer _selectedProducer;
    private ModularBuilding _selectedFarm;
    private System.Action _onConfirmAction;
    private System.Action _onCancelAction;
    private ZonedArea _selectedZone;
    private NotificationManager _notificationManager;
    private bool _sliderListenerActive = false; // FIX #6: Отслеживание подписки на слайдер
    


    void Start()
    {
        HideInfo();
        _confirmationPanel.SetActive(false);

        // --- ИЗМЕНЕНО ---
        if (moduleButtonContainer != null)
        {
            moduleButtonContainer.SetActive(false);
        }
        // --- КОНЕЦ ИЗМЕНЕНИЯ ---
        if (productionPanel)
            productionPanel.SetActive(false);

            if (warehousePanel)
            warehousePanel.SetActive(false);

        // Находим NotificationManager (для Шага 2.0, но он нужен и здесь)
        _notificationManager = FindFirstObjectByType<NotificationManager>();
        if (_notificationManager == null)
        {
            Debug.LogWarning("UIManager: NotificationManager не найден в сцене!");
        }
    }

    // --- FIX #5: Используем OnEnable/OnDisable вместо Start/OnDestroy для событий ---
    private void OnEnable()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SelectionChanged += OnSelectionChanged;
    }

    private void OnDisable()
    {
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SelectionChanged -= OnSelectionChanged;
    }

    private void OnDestroy()
    {
        // Дополнительная страховка при удалении объекта
        if (SelectionManager.Instance != null)
            SelectionManager.Instance.SelectionChanged -= OnSelectionChanged;
    }
    
    private void OnSelectionChanged(IReadOnlyCollection<BuildingIdentity> selection)
    {
        HideInfo(); // Сначала всегда "прячем"
        
        if (selection.Count == 1)
        {
            BuildingIdentity selectedBuilding = selection.First();
            if (selectedBuilding == null) return;
            
            ShowInfo(selectedBuilding.buildingData); // 1. Общая инфо

            // 2. "Ферма"?
            ModularBuilding farm = selectedBuilding.GetComponent<ModularBuilding>();
            if (farm != null)
            {
                _selectedFarm = farm;
                ShowModuleButtons(farm); 
            }
            // 3. "ИЛИ" "Монастырь"?
            else
            {
                ZonedArea zone = selectedBuilding.GetComponent<ZonedArea>();
                if (zone != null)
                {
                    _selectedZone = zone;
                    zone.ShowSlotHighlights();
                }
            }

            // 4. "ЭТО" "Склад"?
            Warehouse warehouse = selectedBuilding.GetComponent<Warehouse>();
            if (warehouse != null)
            {
                ShowWarehouseInfo(warehouse);
            }

            // 5. "ЭТО" "Производитель"?
            ResourceProducer producer = selectedBuilding.GetComponent<ResourceProducer>();
            if (producer != null)
            {
                _selectedProducer = producer;
                ShowProductionControls(producer); // "Включаем" слайдер
            }
        }
    }
    
    public void ShowInfo(BuildingData data)
    {
        if(data == null) return;
        infoPanel.SetActive(true);
        buildingNameText.text = data.buildingName;
    }

    public void HideInfo()
    {
        infoPanel.SetActive(false);
        buildingNameText.text = "";

        ClearModuleButtons();

        if (productionPanel)
            productionPanel.SetActive(false);

        // FIX #6: Безопасное удаление только своего слушателя
        if (productivitySlider && _sliderListenerActive)
        {
            productivitySlider.onValueChanged.RemoveListener(OnEfficiencySliderChanged);
            _sliderListenerActive = false;
        }

        if (warehousePanel)
            warehousePanel.SetActive(false);

        _selectedFarm = null;

        if (_selectedZone != null)
        {
            _selectedZone.HideSlotHighlights();
            _selectedZone = null;
        }
    }
    public void ToggleBalancePanel()
{
    if (balancePanel != null)
    {
        // Переключаем активность: если была вкл -> выкл, если выкл -> вкл
        balancePanel.SetActive(!balancePanel.activeSelf);
    }
}
    
    // --- "Остальной" "код" (Confirmation) "без" "изменений" ---
    public void ShowConfirmation(string message, System.Action onConfirm, System.Action onCancel)
    {
        _onConfirmAction = onConfirm;
        _onCancelAction = onCancel;
        _confirmationText.text = message;
        _confirmationPanel.SetActive(true);
    }
    public void OnConfirmButton()
    {
        _confirmationPanel.SetActive(false);
        _onConfirmAction?.Invoke();
        _onConfirmAction = null; _onCancelAction = null;
    }
    public void OnCancelButton()
    {
        _confirmationPanel.SetActive(false);
        _onCancelAction?.Invoke();
        _onConfirmAction = null; _onCancelAction = null;
    }
    
    // --- ⬇️ НОВЫЕ МЕТОДЫ ДЛЯ КНОПОК МОДУЛЕЙ (Шаг 2.0) ⬇️ ---

    /// <summary>
    /// "Чистит" "контейнер" кнопок (вызывается из HideInfo).
    /// </summary>
    private void ClearModuleButtons()
    {
        if (moduleButtonContainer == null) return;
        
        // --- РЕШЕНИЕ БАГА #17 ---
        // 1. "Собираем" "детей" "в" "безопасный" "список"
        var childrenToDestroy = new List<GameObject>();
        foreach (Transform child in moduleButtonContainer.transform)
        {
            childrenToDestroy.Add(child.gameObject);
        }

        // 2. "Теперь" "безопасно" "удаляем" "их"
        foreach (var child in childrenToDestroy)
        {
            Destroy(child);
        }
        // --- КОНЕЦ РЕШЕНИЯ ---

        moduleButtonContainer.SetActive(false);
    }
    
    /// <summary>
    /// "Создает" "и" "настраивает" "кнопки" "для" "выбранной" "Фермы".
    /// </summary>
    private void ShowModuleButtons(ModularBuilding farm)
    {
        if (moduleButtonContainer == null || moduleButtonPrefab == null) return;

        ClearModuleButtons(); // На всякий случай
        
        // "Показываем" "контейнер" (даже если кнопок не будет, 
        // он может быть "фоном" для текста "Нет модулей")
        moduleButtonContainer.SetActive(true); 

        foreach (BuildingData moduleBP in farm.allowedModules)
        {
            // 1. "Создаем" "кнопку" "из" "префаба"
            GameObject btnGO = Instantiate(moduleButtonPrefab, moduleButtonContainer.transform);

            // 2. "Настраиваем" "текст"
            TextMeshProUGUI txt = btnGO.GetComponentInChildren<TextMeshProUGUI>();
            if (txt)
            {
                txt.text = $"{moduleBP.buildingName} ({moduleBP.size.x}x{moduleBP.size.y})";
            }

            // 3. "Настраиваем" "клик" "и" "доступность"
            Button btn = btnGO.GetComponent<Button>();
            if (btn)
            {
                // Проверяем лимит
                bool canBuild = farm.CanAddModule();
                btn.interactable = canBuild;
                
                // "Вешаем" "событие" "на" "клик"
                btn.onClick.AddListener(() => OnClick_BuildModule(moduleBP));
            }
        }
    }
    private void ShowWarehouseInfo(Warehouse warehouse)
    {
        if (warehousePanel == null || warehouseQueueText == null) return;
        
        warehousePanel.SetActive(true);
        warehouseQueueText.text = $"Очередь: {warehouse.GetQueueCount()} / {warehouse.maxCartQueue}";
    }
    private void ShowProductionControls(ResourceProducer producer)
    {
        if (productionPanel == null || productivitySlider == null) return;

        productionPanel.SetActive(true);

        // 🛡️ SAFETY FIX: Проверяем наличие slider перед использованием
        if (productivitySlider != null)
        {
            // "Считываем" "текущий" "слайдер" "из" "мозга"
            float currentEfficiency = producer.GetEfficiency();
            productivitySlider.value = currentEfficiency;

            // FIX #6: Безопасное добавление слушателя
            if (!_sliderListenerActive)
            {
                productivitySlider.onValueChanged.AddListener(OnEfficiencySliderChanged);
                _sliderListenerActive = true;
            }
        }
        else
        {
            Debug.LogWarning("[UIManager] productivitySlider не назначен в Inspector!");
        }

        UpdateEfficiencyText(producer.GetFinalEfficiency()); // <-- ИСПОЛЬЗУЕМ ГЕТТЕР
    }
    private void OnEfficiencySliderChanged(float value)
    {
        if (_selectedProducer != null)
        {
            // 1. Отправляем "приказ" в "мозг"
            _selectedProducer.SetEfficiency(value);
            
            // --- ⬇️ ИЗМЕНЕНИЕ ⬇️ ---
            // 2. Обновляем текст (читаем ИТОГОВУЮ)
            UpdateEfficiencyText(_selectedProducer.GetFinalEfficiency());
            // --- ⬆️ КОНЕЦ ⬆️ ---
        }
    }
    private void UpdateEfficiencyText(float value)
    {
        if (productivityText)
        {
            productivityText.text = $"{value * 100:F0}%"; // "F0" = 0 знаков после запятой
        }
    }

    /// <summary>
    /// "Обработчик" "клика", "который" "вызывается" "динамической" "кнопкой".
    /// </summary>
    public void OnClick_BuildModule(BuildingData moduleToBuild)
    {
        if (_selectedFarm == null)
        {
            Debug.LogError("Кнопка 'Build Module' нажата, но _selectedFarm == null!");
            return;
        }

        if (!_selectedFarm.CanAddModule())
        {
            // FIX #1: Проверка на null перед использованием
            if (_notificationManager != null)
                _notificationManager.ShowNotification("Достигнут лимит модулей!");
            else
                Debug.LogWarning("UIManager: Не могу показать уведомление, NotificationManager == null");
            return;
        }

        // FIX #1: Проверка на null перед использованием
        if (PlayerInputController.Instance != null)
        {
            PlayerInputController.Instance.EnterPlacingModuleMode(_selectedFarm, moduleToBuild);
        }
        else
        {
            Debug.LogError("UIManager: PlayerInputController.Instance == null!");
        }
    }
    
}