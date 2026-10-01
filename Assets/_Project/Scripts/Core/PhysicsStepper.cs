using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Шагает физику вручную один раз за кадр — после ввода и Update бойцов, до LateUpdate камеры.
    ///
    /// Зачем: при стандартном режиме (FixedUpdate) физика считается ДО обработки ввода в кадре
    /// (порядок цикла Unity: FixedUpdate → PreUpdate/Input → Update), поэтому рывок/движение,
    /// запущенные касанием, становились видимы только в следующем кадре, а интерполяция Rigidbody
    /// добавляла ещё один шаг отставания. Здесь: ввод → состояние бойца → физика → рендер в том же кадре.
    ///
    /// УСТАРЕЛО: бой перешёл на детерминированную симуляцию (Game.Simulation), PhysX в геймплее не используется,
    /// поэтому стэппер больше не ставится автоматически. Остался только для старого прототипа (Characters/Fighter.cs),
    /// который ждёт удаления вместе с ним.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class PhysicsStepper : MonoBehaviour
    {
        /// <summary> Самый длинный шаг физики, сек. Длинный кадр (подгрузка, пауза) не должен «телепортировать» тела. </summary>
        public const float MaxStep = 1f / 20f;

        private static PhysicsStepper _instance;
        private SimulationMode _previousMode;

        public static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("_PhysicsStepper") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PhysicsStepper>();
        }

        private void Awake()
        {
            _previousMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
        }

        private void OnDestroy()
        {
            // В редакторе режим симуляции — настройка проекта; возвращаем как было при выходе из Play.
            Physics.simulationMode = _previousMode;
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, MaxStep);
            if (dt > 0f) Physics.Simulate(dt);
        }
    }
}
