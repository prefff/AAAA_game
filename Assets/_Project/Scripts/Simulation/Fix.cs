using System;

namespace Game.Simulation
{
    /// <summary>
    /// Число с фиксированной точкой Q47.16 (long, 16 дробных бит, шаг ≈ 0,000015).
    /// Вся боевая симуляция считает только в нём: целочисленная арифметика даёт бит-в-бит одинаковый результат
    /// на любых процессорах и компиляторах, без чего rollback-неткод рассинхронизируется.
    /// Диапазон с запасом для арены: произведение двух чисел не переполняется, пока каждое по модулю меньше ~46 000.
    /// Тригонометрии нет намеренно: направления хранятся векторами.
    /// </summary>
    public readonly struct Fix : IEquatable<Fix>, IComparable<Fix>
    {
        public const int FractionalBits = 16;
        public const long OneRaw = 1L << FractionalBits;

        public readonly long Raw;

        private Fix(long raw) => Raw = raw;

        public static readonly Fix Zero = new(0);
        public static readonly Fix One = new(OneRaw);
        public static readonly Fix Half = new(OneRaw / 2);

        public static Fix FromRaw(long raw) => new(raw);
        public static Fix FromInt(int value) => new((long)value << FractionalBits);

        /// <summary> num / den без потери точности на промежуточном float. </summary>
        public static Fix Ratio(long num, long den) => new((num << FractionalBits) / den);

        /// <summary>
        /// Только для данных (ассеты, настройки) и тестов, не для вычислений внутри тика.
        /// Детерминировано: умножение double на степень двойки точное, Math.Round не зависит от платформы.
        /// </summary>
        public static Fix FromFloat(float value) => new((long)Math.Round((double)value * OneRaw));

        /// <summary> Только для отображения. </summary>
        public float ToFloat() => (float)((double)Raw / OneRaw);

        public static Fix operator +(Fix a, Fix b) => new(a.Raw + b.Raw);
        public static Fix operator -(Fix a, Fix b) => new(a.Raw - b.Raw);
        public static Fix operator -(Fix a) => new(-a.Raw);
        public static Fix operator *(Fix a, Fix b) => new((a.Raw * b.Raw) >> FractionalBits);
        public static Fix operator /(Fix a, Fix b) => new((a.Raw << FractionalBits) / b.Raw);
        public static Fix operator *(Fix a, int b) => new(a.Raw * b);
        public static Fix operator /(Fix a, int b) => new(a.Raw / b);

        public static bool operator ==(Fix a, Fix b) => a.Raw == b.Raw;
        public static bool operator !=(Fix a, Fix b) => a.Raw != b.Raw;
        public static bool operator <(Fix a, Fix b) => a.Raw < b.Raw;
        public static bool operator >(Fix a, Fix b) => a.Raw > b.Raw;
        public static bool operator <=(Fix a, Fix b) => a.Raw <= b.Raw;
        public static bool operator >=(Fix a, Fix b) => a.Raw >= b.Raw;

        public static Fix Abs(Fix v) => v.Raw < 0 ? new Fix(-v.Raw) : v;
        public static Fix Min(Fix a, Fix b) => a.Raw <= b.Raw ? a : b;
        public static Fix Max(Fix a, Fix b) => a.Raw >= b.Raw ? a : b;
        public static Fix Clamp(Fix v, Fix min, Fix max) => v.Raw < min.Raw ? min : (v.Raw > max.Raw ? max : v);

        /// <summary> Квадратный корень целочисленным методом (по битам); для отрицательных — 0. </summary>
        public static Fix Sqrt(Fix v)
        {
            if (v.Raw <= 0) return Zero;
            return new Fix((long)ISqrt((ulong)v.Raw << FractionalBits));
        }

        private static ulong ISqrt(ulong n)
        {
            ulong result = 0;
            ulong bit = 1UL << 62;
            while (bit > n) bit >>= 2;
            while (bit != 0)
            {
                if (n >= result + bit)
                {
                    n -= result + bit;
                    result = (result >> 1) + bit;
                }
                else result >>= 1;
                bit >>= 2;
            }
            return result;
        }

        public bool Equals(Fix other) => Raw == other.Raw;
        public override bool Equals(object obj) => obj is Fix f && f.Raw == Raw;
        public override int GetHashCode() => Raw.GetHashCode();
        public int CompareTo(Fix other) => Raw.CompareTo(other.Raw);
        public override string ToString() => ToFloat().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary> Вектор в плоскости арены: X — мировой X, Y — мировой Z. </summary>
    public readonly struct FixVec2 : IEquatable<FixVec2>
    {
        public readonly Fix X;
        public readonly Fix Y;

        public FixVec2(Fix x, Fix y)
        {
            X = x;
            Y = y;
        }

        public static readonly FixVec2 Zero = new(Fix.Zero, Fix.Zero);
        public static readonly FixVec2 Forward = new(Fix.Zero, Fix.One); // +Z мира
        public static readonly FixVec2 Right = new(Fix.One, Fix.Zero);   // +X мира

        public static FixVec2 FromFloat(float x, float y) => new(Fix.FromFloat(x), Fix.FromFloat(y));

        public bool IsZero => X.Raw == 0 && Y.Raw == 0;
        public Fix SqrMagnitude => X * X + Y * Y;
        public Fix Magnitude => Fix.Sqrt(SqrMagnitude);

        /// <summary> Единичный вектор того же направления; нулевой остаётся нулевым. </summary>
        public FixVec2 Normalized
        {
            get
            {
                var m = Magnitude;
                return m.Raw == 0 ? Zero : new FixVec2(X / m, Y / m);
            }
        }

        public FixVec2 ClampMagnitude(Fix max)
        {
            var sqr = SqrMagnitude;
            if (sqr <= max * max) return this;
            return Normalized * max;
        }

        public static Fix Dot(FixVec2 a, FixVec2 b) => a.X * b.X + a.Y * b.Y;

        public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new(a.X + b.X, a.Y + b.Y);
        public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new(a.X - b.X, a.Y - b.Y);
        public static FixVec2 operator -(FixVec2 a) => new(-a.X, -a.Y);
        public static FixVec2 operator *(FixVec2 a, Fix s) => new(a.X * s, a.Y * s);
        public static FixVec2 operator /(FixVec2 a, Fix s) => new(a.X / s, a.Y / s);
        public static bool operator ==(FixVec2 a, FixVec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(FixVec2 a, FixVec2 b) => !(a == b);

        public bool Equals(FixVec2 other) => this == other;
        public override bool Equals(object obj) => obj is FixVec2 v && v == this;
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => $"({X}, {Y})";
    }
}
