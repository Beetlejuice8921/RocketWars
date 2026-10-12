using System;
using Unity.Netcode;

namespace RocketWars.Core
{
    /// <summary>
    /// Ключи и контрольные суммы для «спрятанных» значений. Ключ новый при каждой записи, поэтому в памяти нет
    /// ни самого числа (сканеры вроде ArtMoney не найдут «100» HP), ни постоянного зашифрованного образа.
    /// Контрольная сумма — общая формула для всех копий игры: значение шифрует сервер, проверяют клиенты.
    /// </summary>
    static class ObscuredKeys
    {
        static uint _state = (uint)Environment.TickCount ^ (uint)DateTime.UtcNow.Ticks ^ 0x9E3779B9u;

        /// <summary>Ненулевой псевдослучайный ключ (xorshift; вызывается только из основного потока).</summary>
        public static int Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x == 0 ? 0x6C8E9CF5u : x;
            return (int)(_state | 1u);
        }

        public static int Check(int raw, int key)
        {
            unchecked
            {
                uint h = (uint)raw * 0x2C1B3C6Du + 0x297A2D39u;
                h = (h << 13) | (h >> 19);
                return (int)(h ^ ((uint)key * 0x6F4F2A35u));
            }
        }
    }

    /// <summary>
    /// float, хранящийся в памяти в зашифрованном виде (XOR со случайным ключом + контрольная сумма).
    /// Подмена в памяти ломает контрольную сумму — AntiCheat.ReportTamper. Годится для NetworkVariable.
    /// </summary>
    public struct ObscuredFloat : INetworkSerializeByMemcpy, IEquatable<ObscuredFloat>
    {
        int _key, _enc, _chk;

        public ObscuredFloat(float value)
        {
            int raw = BitConverter.SingleToInt32Bits(value);
            _key = ObscuredKeys.Next();
            _enc = raw ^ _key;
            _chk = ObscuredKeys.Check(raw, _key);
        }

        public float Value
        {
            get
            {
                if (_key == 0 && _enc == 0 && _chk == 0)
                    return 0f; // default(ObscuredFloat)
                int raw = _enc ^ _key;
                if (_chk != ObscuredKeys.Check(raw, _key))
                    AntiCheat.ReportTamper("float");
                return BitConverter.Int32BitsToSingle(raw);
            }
        }

        public static implicit operator ObscuredFloat(float value) => new ObscuredFloat(value);
        public static implicit operator float(ObscuredFloat value) => value.Value;

        public bool Equals(ObscuredFloat other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is ObscuredFloat o && Equals(o);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
    }

    /// <summary>int, хранящийся в памяти в зашифрованном виде (см. ObscuredFloat).</summary>
    public struct ObscuredInt : INetworkSerializeByMemcpy, IEquatable<ObscuredInt>
    {
        int _key, _enc, _chk;

        public ObscuredInt(int value)
        {
            _key = ObscuredKeys.Next();
            _enc = value ^ _key;
            _chk = ObscuredKeys.Check(value, _key);
        }

        public int Value
        {
            get
            {
                if (_key == 0 && _enc == 0 && _chk == 0)
                    return 0;
                int raw = _enc ^ _key;
                if (_chk != ObscuredKeys.Check(raw, _key))
                    AntiCheat.ReportTamper("int");
                return raw;
            }
        }

        public static implicit operator ObscuredInt(int value) => new ObscuredInt(value);
        public static implicit operator int(ObscuredInt value) => value.Value;
        public static ObscuredInt operator ++(ObscuredInt value) => new ObscuredInt(value.Value + 1);
        public static ObscuredInt operator --(ObscuredInt value) => new ObscuredInt(value.Value - 1);

        public bool Equals(ObscuredInt other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ObscuredInt o && Equals(o);
        public override int GetHashCode() => Value;
        public override string ToString() => Value.ToString();
    }
}
