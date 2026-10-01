using Game.Characters;
using Game.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// HUD матча: таймер раунда и счёт сверху по центру, крупная надпись по центру экрана
    /// («Раунд 2» на отсчёте, «Бой!», «KO» / «Время!», итог матча) и кнопка «Ещё раз» после матча.
    /// Всё читается из состояния симуляции; рестарт идёт командой через симуляцию.
    /// </summary>
    public class MatchHUD : MonoBehaviour
    {
        private const float FightBannerSeconds = 0.7f;

        private MatchRunner _runner;
        private Text _timer;
        private Text _round;
        private Text _wins0;
        private Text _wins1;
        private Text _banner;
        private Text _subtitle;
        private Button _restart;
        private float _fightBannerUntil;

        public string BannerText => _banner != null && _banner.enabled ? _banner.text : string.Empty;
        public bool RestartVisible => _restart != null && _restart.gameObject.activeSelf;

        public void Bind(MatchRunner runner) => _runner = runner;

        private void Awake()
        {
            var top = new Vector2(0.5f, 1f);
            _timer = UiFactory.Label("Timer", transform, top, new Vector2(0f, -55f), new Vector2(200f, 70f), 54, TextAnchor.MiddleCenter);
            _round = UiFactory.Label("Round", transform, top, new Vector2(0f, -105f), new Vector2(300f, 30f), 24, TextAnchor.MiddleCenter);
            _wins0 = UiFactory.Label("Wins0", transform, top, new Vector2(-130f, -55f), new Vector2(120f, 50f), 40, TextAnchor.MiddleRight);
            _wins1 = UiFactory.Label("Wins1", transform, top, new Vector2(130f, -55f), new Vector2(120f, 50f), 40, TextAnchor.MiddleLeft);

            var center = new Vector2(0.5f, 0.5f);
            _banner = UiFactory.Label("Banner", transform, center, new Vector2(0f, 120f), new Vector2(900f, 140f), 110, TextAnchor.MiddleCenter);
            _subtitle = UiFactory.Label("Subtitle", transform, center, new Vector2(0f, 30f), new Vector2(900f, 60f), 40, TextAnchor.MiddleCenter);
            _restart = CreateButton("Restart", "Ещё раз", center, new Vector2(0f, -70f));
            _restart.onClick.AddListener(() => _runner?.RequestRestart());
        }

        private void OnEnable()
        {
            if (_runner != null) _runner.SimEventRaised += OnSimEvent;
        }

        private void OnDisable()
        {
            if (_runner != null) _runner.SimEventRaised -= OnSimEvent;
        }

        private void OnSimEvent(SimEvent e)
        {
            if (e.Type == SimEventType.RoundStarted) _fightBannerUntil = Time.unscaledTime + FightBannerSeconds;
        }

        private void LateUpdate()
        {
            if (_runner == null || _runner.State == null) return;
            var s = _runner.State;
            var rules = _runner.Sim.Setup.Rules;
            int me = _runner.LocalPlayer;

            _timer.text = rules.Training ? "∞" : Mathf.CeilToInt(s.RoundTicksLeft / (float)SimTime.TickRate).ToString();
            _round.text = rules.Training ? "Тренировка" : $"Раунд {s.Round}";
            // Свой счёт — слева (как свой HUD), счёт противника — справа.
            _wins0.text = rules.Training ? string.Empty : Pips(s.Wins(me), rules.RoundsToWin);
            _wins1.text = rules.Training ? string.Empty : Pips(s.Wins(1 - me), rules.RoundsToWin);

            string banner = null, subtitle = null;
            switch (s.Phase)
            {
                case MatchPhase.Countdown:
                    banner = rules.Training ? "Тренировка" : $"Раунд {s.Round}";
                    break;
                case MatchPhase.Fight:
                    if (Time.unscaledTime < _fightBannerUntil) banner = "Бой!";
                    break;
                case MatchPhase.RoundOver:
                    bool ko = !s.Fighters[0].IsAlive || !s.Fighters[1].IsAlive;
                    banner = ko ? "KO" : "Время!";
                    subtitle = s.LastRoundWinner < 0 ? "Ничья" : (s.LastRoundWinner == me ? "Раунд ваш" : "Раунд за соперником");
                    break;
                case MatchPhase.MatchOver:
                    banner = s.MatchWinner < 0 ? "Ничья" : (s.MatchWinner == me ? "Победа" : "Поражение");
                    subtitle = $"{s.Wins(me)} : {s.Wins(1 - me)}";
                    break;
            }
            SetText(_banner, banner);
            SetText(_subtitle, subtitle);

            bool showRestart = s.Phase == MatchPhase.MatchOver;
            if (_restart.gameObject.activeSelf != showRestart) _restart.gameObject.SetActive(showRestart);
        }

        private static string Pips(int wins, int needed)
        {
            var chars = new char[needed];
            for (int i = 0; i < needed; i++) chars[i] = i < wins ? '●' : '○';
            return new string(chars);
        }

        private static void SetText(Text label, string value)
        {
            bool show = !string.IsNullOrEmpty(value);
            if (label.enabled != show) label.enabled = show;
            if (show && label.text != value) label.text = value;
        }

        private Button CreateButton(string name, string caption, Vector2 anchor, Vector2 position)
        {
            var rt = UiFactory.Rect(name, transform, anchor, new Vector2(0.5f, 0.5f), position, new Vector2(320f, 90f));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiFactory.WhiteSprite();
            img.color = new Color(0.15f, 0.45f, 0.9f, 0.95f);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var label = UiFactory.Label("Label", rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 90f), 40, TextAnchor.MiddleCenter);
            label.text = caption;
            rt.gameObject.SetActive(false);
            return button;
        }
    }
}
