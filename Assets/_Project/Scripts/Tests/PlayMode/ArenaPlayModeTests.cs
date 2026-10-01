using System.Collections;
using System.Linq;
using Game.Characters;
using Game.Core;
using Game.Input;
using Game.Simulation;
using Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>
    /// Выдаёт команду в Update раньше всех игровых скриптов — так же, как Input System и распознаватель жестов
    /// (они работают до Update бойцов). Нужен, чтобы проверять отклик «в кадре касания».
    /// </summary>
    [DefaultExecutionOrder(-3000)]
    public class EarlyInputInjector : MonoBehaviour
    {
        private CommandType _type;
        private Vector2 _dir;
        private bool _pending;
        public int FiredFrame { get; private set; } = -1;

        public void Fire(CommandType type, Vector2 worldDir = default)
        {
            _type = type;
            _dir = worldDir;
            _pending = true;
        }

        private void Update()
        {
            if (!_pending) return;
            _pending = false;
            FiredFrame = Time.frameCount;
            double t = Time.realtimeSinceStartupAsDouble;
            EventBus.Raise(new CommandInputEvent(new InputCommand(_type, _dir, (float)t, t, t)));
        }
    }

    /// <summary> Сцена Arena: симуляция, виды, камера, HUD и отклик на ввод. </summary>
    public class ArenaSceneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/Arena.unity";

        private MatchRunner _runner;
        private EarlyInputInjector _input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Arena");
#endif
            yield return null;
            _runner = Object.FindAnyObjectByType<MatchRunner>();
            _input = new GameObject("TestInput").AddComponent<EarlyInputInjector>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                Object.Destroy(go);
            yield return null;
        }

        private IEnumerator WaitForFight()
        {
            float t = 0f;
            while (_runner.State.Phase != MatchPhase.Fight && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(MatchPhase.Fight, _runner.State.Phase, "Раунд не начался");
        }

        private FighterView LocalView => Object.FindObjectsByType<FighterView>(FindObjectsSortMode.None).First(v => v.Index == _runner.LocalPlayer);

        [UnityTest]
        public IEnumerator Arena_Builds_Simulation_Views_Camera_Hud_WithoutPhysicsBodies()
        {
            Assert.IsNotNull(_runner, "Нет MatchRunner");
            Assert.AreEqual(1, Object.FindObjectsByType<MatchRunner>(FindObjectsSortMode.None).Length);

            var views = Object.FindObjectsByType<FighterView>(FindObjectsSortMode.None);
            Assert.AreEqual(2, views.Length, "Должно быть два вида бойцов");
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, views.Select(v => v.Index).ToArray());

            Assert.AreEqual(0, Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length, "В геймплее не должно быть Rigidbody");
            foreach (var v in views)
                Assert.AreEqual(0, v.GetComponentsInChildren<Collider>().Length, "У вида бойца не должно быть коллайдеров");

            var cam = Camera.main.GetComponent<MobaCamera>();
            Assert.IsNotNull(cam);
            Assert.AreSame(LocalView.transform, cam.Target);

            Assert.AreEqual(2, Object.FindObjectsByType<FighterHUD>(FindObjectsSortMode.None).Length);
            Assert.IsNotNull(Object.FindAnyObjectByType<MatchHUD>());
            Assert.IsNotNull(Object.FindAnyObjectByType<OffscreenIndicator>());
            Assert.IsNotNull(Object.FindAnyObjectByType<LatencyOverlay>());
            Assert.AreEqual(1, Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name == "Floor"), "Пол продублирован");

            // Геометрия арены совпадает с коллизиями симуляции: 4 стены + препятствия.
            var arena = GameObject.Find("Arena");
            Assert.IsNotNull(arena);
            Assert.AreEqual(4 + _runner.Sim.Setup.Arena.Obstacles.Length, arena.transform.childCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Countdown_ShowsRoundBanner_ThenFight()
        {
            var hud = Object.FindAnyObjectByType<MatchHUD>();
            yield return null;
            Assert.AreEqual(MatchPhase.Countdown, _runner.State.Phase);
            StringAssert.Contains("Раунд", hud.BannerText);
            yield return WaitForFight();
        }

        [UnityTest]
        public IEnumerator Attack_IsVisible_InTheFrameOfTheCommand()
        {
            yield return WaitForFight();
            var rend = LocalView.Body;
            var block = new MaterialPropertyBlock();
            int id = rend.sharedMaterial.HasProperty("_BaseColor") ? Shader.PropertyToID("_BaseColor") : Shader.PropertyToID("_Color");
            // Пауза без команд, чтобы досрочный тик был разрешён.
            yield return new WaitForSeconds(0.1f);
            rend.GetPropertyBlock(block);
            var idleColor = block.GetColor(id);

            _input.Fire(CommandType.LightAttack);
            yield return null; // инжектор выдал команду в Update этого кадра
            yield return new WaitForEndOfFrame(); // конец кадра, в котором команда пришла — после LateUpdate вида

            Assert.AreEqual(Time.frameCount, _input.FiredFrame);
            Assert.AreEqual(ActionState.Attack, _runner.State.Fighters[_runner.LocalPlayer].State);
            rend.GetPropertyBlock(block);
            Assert.AreNotEqual(idleColor, block.GetColor(id), "Startup удара не отрисован в кадре команды");
        }

        [UnityTest]
        public IEnumerator Dodge_MovesTheView_InTheFrameOfTheCommand()
        {
            yield return WaitForFight();
            yield return new WaitForSeconds(0.1f);
            var view = LocalView.transform;
            var start = view.position;

            _input.Fire(CommandType.Dodge, Vector2.up); // мировое +Z
            yield return null; // инжектор выдал команду в Update этого кадра
            yield return new WaitForEndOfFrame();

            Assert.Greater(view.position.z, start.z + 0.05f, "Рывок не сдвинул бойца в кадре команды");
        }

        [UnityTest]
        public IEnumerator Joystick_MovesCameraRelative()
        {
            yield return WaitForFight();
            var view = LocalView.transform;
            var start = view.position;

            EventBus.Raise(new MoveInputEvent(Vector2.up)); // «вверх по экрану» = от камеры
            yield return new WaitForSeconds(0.2f);
            EventBus.Raise(new MoveInputEvent(Vector2.zero));

            var camForward = Camera.main.transform.forward;
            camForward.y = 0f;
            Assert.Greater(Vector3.Dot(view.position - start, camForward.normalized), 0.3f);
        }

        [UnityTest]
        public IEnumerator Command_IsRecordedInLatencyLog()
        {
            yield return WaitForFight();
            yield return new WaitForSeconds(0.1f);
            Latency.Log.Clear();
            _input.Fire(CommandType.LightAttack);
            yield return null; // инжектор выдал команду в Update этого кадра
            yield return new WaitForEndOfFrame();

            Assert.AreEqual(1, Latency.Log.Count);
            Assert.AreEqual(nameof(ActionState.Attack), Latency.Log.Last.ResultState);
        }

        [UnityTest]
        public IEnumerator DisabledRunner_UnsubscribesFromInput()
        {
            Assert.AreEqual(1, EventBus.GetSubscriberCount<CommandInputEvent>());
            _runner.enabled = false;
            Assert.AreEqual(0, EventBus.GetSubscriberCount<CommandInputEvent>());
            _runner.enabled = true;
            Assert.AreEqual(1, EventBus.GetSubscriberCount<CommandInputEvent>());
            yield return null;
        }

        [UnityTest]
        public IEnumerator SecondPlayerCamera_IsRotated_AndInputFollowsIt()
        {
            var cam = Camera.main.GetComponent<MobaCamera>();
            var up = InputSpace.ScreenToWorld(Vector2.up);
            Assert.AreEqual(0f, cam.Yaw);
            Assert.Greater(up.y, 0.99f, "Синяя сторона: вверх по экрану = +Z");

            cam.SetSide(1);
            yield return null;
            Assert.AreEqual(180f, cam.Yaw);
            up = InputSpace.ScreenToWorld(Vector2.up);
            Assert.Less(up.y, -0.99f, "Красная сторона: вверх по экрану = −Z");
            var right = InputSpace.ScreenToWorld(Vector2.right);
            Assert.Less(right.x, -0.99f);
        }

        [UnityTest]
        public IEnumerator Camera_FollowsWithoutLag()
        {
            yield return WaitForFight();
            var cam = Camera.main.transform;
            var view = LocalView.transform;
            var offset = cam.position - view.position;

            _input.Fire(CommandType.Dodge, Vector2.right);
            yield return null; // инжектор выдал команду в Update этого кадра
            yield return new WaitForEndOfFrame();
            Assert.Less((cam.position - view.position - offset).magnitude, 1e-3f, "Камера отстаёт от бойца");
        }

        [UnityTest]
        public IEnumerator OffscreenIndicator_ShowsOnlyWhenOpponentIsOutOfView()
        {
            yield return WaitForFight();
            var indicator = Object.FindAnyObjectByType<OffscreenIndicator>();
            yield return null;
            Assert.IsFalse(indicator.IsShown, "Противник рядом — стрелка не нужна");

            // Противника уводим в дальний угол арены прямо в симуляции.
            int opp = _runner.Opponent;
            var arena = _runner.Sim.Setup.Arena;
            _runner.State.Fighters[opp].Position = new FixVec2(arena.Max.X - Fix.One, arena.Max.Y - Fix.One);
            _runner.State.Fighters[_runner.LocalPlayer].Position = new FixVec2(arena.Min.X + Fix.One, arena.Min.Y + Fix.One);
            yield return null;
            yield return null;
            Assert.IsTrue(indicator.IsShown, "Противник за краем экрана — нужна стрелка");
            Assert.Greater(indicator.ViewportPosition.x, 0.5f, "Стрелка должна смотреть в сторону противника");
        }

        [UnityTest]
        public IEnumerator SkillButton_StartsCastInTheFrameOfTheCommand_ProjectileIsDrawn()
        {
            yield return WaitForFight();
            yield return new WaitForSeconds(0.1f);
            int me = _runner.LocalPlayer;
            Assert.IsNotNull(_runner.Sim.Setup.Fighters[me].Skill(0), "У тестового персонажа должен быть нюк");

            _input.Fire(CommandType.Ability1);
            yield return null; // инжектор выдал команду в Update этого кадра
            yield return new WaitForEndOfFrame();
            Assert.AreEqual(ActionState.Cast, _runner.State.Fighters[me].State, "Каст должен начаться в кадре нажатия");

            var view = Object.FindAnyObjectByType<SkillObjectsView>();
            Assert.IsNotNull(view);
            float t = 0f;
            while (view.VisibleProjectiles == 0 && t < 1f) { t += Time.deltaTime; yield return null; }
            Assert.AreEqual(1, view.VisibleProjectiles, "Снаряд должен быть виден");
        }

        [UnityTest]
        public IEnumerator SkillBar_And_AimIndicator_FollowTheSkillFinger()
        {
            yield return WaitForFight();
            var bar = Object.FindAnyObjectByType<SkillBar>();
            Assert.IsNotNull(bar);
            var indicator = Object.FindAnyObjectByType<SkillAimIndicator>();
            Assert.IsNotNull(indicator);
            Assert.IsFalse(indicator.IsShown);
            Assert.IsFalse(bar.CancelVisible);

            // Палец на кнопке телепорта, прицел — вправо на половину дальности.
            EventBus.Raise(new SkillAimInputEvent(1, SkillAimPhase.Held, new Vector2(0.5f, 0f), false));
            yield return null;
            Assert.IsTrue(indicator.IsShown, "Пока палец на кнопке, прицел виден");
            Assert.IsTrue(bar.CancelVisible, "Пока палец на кнопке, видна зона отмены");
            var me = _runner.State.Fighters[_runner.LocalPlayer].Position;
            float range = _runner.Sim.Setup.Fighters[_runner.LocalPlayer].Skill(1).Range.ToFloat();
            Assert.AreEqual(me.X.ToFloat() + range * 0.5f, indicator.TargetPoint.x, 0.1f);

            EventBus.Raise(new SkillAimInputEvent(1, SkillAimPhase.Released, new Vector2(0.5f, 0f), false));
            yield return null;
            Assert.IsFalse(indicator.IsShown);
            Assert.IsFalse(bar.CancelVisible);
        }

        [UnityTest]
        public IEnumerator UltimateButton_ShowsInitialCooldown()
        {
            yield return WaitForFight();
            var bar = Object.FindAnyObjectByType<SkillBar>();
            yield return null;
            Assert.Greater(bar.CooldownFill(2), 0.5f, "Ультимейт в начале раунда на перезарядке");
            Assert.AreEqual(0f, bar.CooldownFill(0), "Нюк готов сразу");
        }

        [UnityTest]
        public IEnumerator BotModeAttack_DamagesThePlayer()
        {
            yield return WaitForFight();
            _runner.BotMode = BotMode.Attack;
            int me = _runner.LocalPlayer;
            var max = _runner.Sim.Setup.Fighters[me].MaxHealth;
            float t = 0f;
            while (_runner.State.Fighters[me].Health == max && t < 5f) { t += Time.deltaTime; yield return null; }
            Assert.Less(_runner.State.Fighters[me].Health.Raw, max.Raw, "Бот в режиме «Атака» должен подойти и ударить");
        }
    }
}
