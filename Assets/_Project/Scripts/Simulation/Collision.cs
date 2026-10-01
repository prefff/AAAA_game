namespace Game.Simulation
{
    /// <summary>
    /// Коллизии в плоскости арены: бойцы — круги, препятствия — круги и прямоугольники по осям, края — стены.
    /// Выталкивание по кратчайшему пути, без скоростей и импульсов: детерминированно и предсказуемо для игрока.
    /// </summary>
    public static class Collision
    {
        /// <summary> Вытолкнуть круг из препятствий и стен арены. Два прохода — чтобы не застревать в углах. </summary>
        public static FixVec2 ResolveArena(FixVec2 pos, Fix radius, ArenaSpec arena)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var obstacles = arena.Obstacles;
                for (int i = 0; i < obstacles.Length; i++)
                {
                    var o = obstacles[i];
                    pos = o.Shape == ObstacleShape.Circle
                        ? PushOutOfCircle(pos, radius, o.Center, o.Radius)
                        : PushOutOfBox(pos, radius, o.Center, o.HalfExtents);
                }
                pos = new FixVec2(
                    Fix.Clamp(pos.X, arena.Min.X + radius, arena.Max.X - radius),
                    Fix.Clamp(pos.Y, arena.Min.Y + radius, arena.Max.Y - radius));
            }
            return pos;
        }

        public static FixVec2 PushOutOfCircle(FixVec2 pos, Fix radius, FixVec2 center, Fix obstacleRadius)
        {
            var d = pos - center;
            var minDist = radius + obstacleRadius;
            if (d.SqrMagnitude >= minDist * minDist) return pos;
            var dist = d.Magnitude;
            if (dist.Raw == 0) return center + FixVec2.Right * minDist;
            return center + d / dist * minDist;
        }

        public static FixVec2 PushOutOfBox(FixVec2 pos, Fix radius, FixVec2 center, FixVec2 half)
        {
            var closest = new FixVec2(
                Fix.Clamp(pos.X, center.X - half.X, center.X + half.X),
                Fix.Clamp(pos.Y, center.Y - half.Y, center.Y + half.Y));
            var d = pos - closest;

            if (d.IsZero)
            {
                // Центр внутри прямоугольника — выталкиваем по оси с меньшим проникновением.
                var local = pos - center;
                var penX = half.X + radius - Fix.Abs(local.X);
                var penY = half.Y + radius - Fix.Abs(local.Y);
                if (penX <= penY)
                    return new FixVec2(center.X + (local.X.Raw >= 0 ? half.X + radius : -(half.X + radius)), pos.Y);
                return new FixVec2(pos.X, center.Y + (local.Y.Raw >= 0 ? half.Y + radius : -(half.Y + radius)));
            }

            if (d.SqrMagnitude >= radius * radius) return pos;
            var dist = d.Magnitude;
            return closest + d / dist * radius;
        }

        /// <summary> Развести два круга поровну. Совпавшие центры расходятся по оси X (детерминированно). </summary>
        public static void SeparateCircles(ref FixVec2 a, Fix ra, ref FixVec2 b, Fix rb)
        {
            var d = b - a;
            var minDist = ra + rb;
            if (d.SqrMagnitude >= minDist * minDist) return;
            var dist = d.Magnitude;
            var normal = dist.Raw == 0 ? FixVec2.Right : d / dist;
            var overlap = minDist - dist;
            var halfOverlap = overlap / 2;
            a -= normal * halfOverlap;
            b += normal * (overlap - halfOverlap);
        }

        public static bool CirclesOverlap(FixVec2 a, Fix ra, FixVec2 b, Fix rb)
        {
            var r = ra + rb;
            return (b - a).SqrMagnitude < r * r;
        }

        /// <summary> Перекрыт ли отрезок a→b препятствием, блокирующим снаряды (для скиллов этапа 6). </summary>
        public static bool SegmentBlocked(FixVec2 a, FixVec2 b, ArenaSpec arena)
        {
            var obstacles = arena.Obstacles;
            for (int i = 0; i < obstacles.Length; i++)
            {
                var o = obstacles[i];
                if (!o.BlocksProjectiles) continue;
                bool hit = o.Shape == ObstacleShape.Circle
                    ? SegmentHitsCircle(a, b, o.Center, o.Radius)
                    : SegmentHitsBox(a, b, o.Center - o.HalfExtents, o.Center + o.HalfExtents);
                if (hit) return true;
            }
            return false;
        }

        private static bool SegmentHitsCircle(FixVec2 a, FixVec2 b, FixVec2 c, Fix r)
        {
            var ab = b - a;
            var lenSq = ab.SqrMagnitude;
            Fix t = lenSq.Raw == 0 ? Fix.Zero : Fix.Clamp(FixVec2.Dot(c - a, ab) / lenSq, Fix.Zero, Fix.One);
            var closest = a + ab * t;
            return (c - closest).SqrMagnitude <= r * r;
        }

        /// <summary> Метод плит (slab): пересечение отрезка с прямоугольником. </summary>
        private static bool SegmentHitsBox(FixVec2 a, FixVec2 b, FixVec2 min, FixVec2 max)
        {
            Fix tMin = Fix.Zero, tMax = Fix.One;
            if (!Slab(a.X, b.X - a.X, min.X, max.X, ref tMin, ref tMax)) return false;
            if (!Slab(a.Y, b.Y - a.Y, min.Y, max.Y, ref tMin, ref tMax)) return false;
            return true;
        }

        private static bool Slab(Fix origin, Fix delta, Fix min, Fix max, ref Fix tMin, ref Fix tMax)
        {
            if (delta.Raw == 0) return origin >= min && origin <= max;
            var t1 = (min - origin) / delta;
            var t2 = (max - origin) / delta;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Fix.Max(tMin, t1);
            tMax = Fix.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}
