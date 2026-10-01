using UnityEngine;

namespace Game.Input
{
    /// <summary>
    /// Перевод экранных направлений (свайп, джойстик) в мировые оси арены по текущей камере.
    /// Камера не вращается во время боя, поэтому это фиксированное преобразование; у второго игрока (камера
    /// развёрнута на 180°) «вверх по экрану» автоматически становится −Z. В симуляцию уходит уже мировое направление.
    /// </summary>
    public static class InputSpace
    {
        /// <summary> Экранное направление (x — вправо, y — вверх) → мировое в плоскости XZ (x = X, y = Z). </summary>
        public static Vector2 ScreenToWorld(Vector2 screenDir)
        {
            var cam = Camera.main;
            if (cam == null) return screenDir;
            var right = cam.transform.right; right.y = 0f; right.Normalize();
            var fwd = cam.transform.forward; fwd.y = 0f;
            // Камера смотрит строго вниз — «вперёд» берём из её «вверх».
            if (fwd.sqrMagnitude < 1e-6f) { fwd = cam.transform.up; fwd.y = 0f; }
            fwd.Normalize();
            var world = right * screenDir.x + fwd * screenDir.y;
            return new Vector2(world.x, world.z);
        }
    }
}
