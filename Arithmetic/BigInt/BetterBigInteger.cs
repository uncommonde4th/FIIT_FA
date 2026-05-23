using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Arithmetic.BigInt.Interfaces;
using Arithmetic.BigInt.MultiplyStrategy;

namespace Arithmetic.BigInt;

public sealed class BetterBigInteger : IBigInteger
{
    private const int BitsPerWord = 32;

    // Пороги выбора алгоритма умножения
    private const int SimpleMultiplierZone = 32;
    private const int KaratsubaZone = 128;
    
    private static readonly IMultiplier SimpleMultiplierStrategy = new SimpleMultiplier();
    private static readonly IMultiplier KaratsubaMultiplierStrategy = new KaratsubaMultiplier();
    private static readonly IMultiplier FftMultiplierStrategy = new FftMultiplier();
    
    private int _signBit;
    private uint _smallValue;
    private uint[]? _data;
    
    private BetterBigInteger()
    {
        _signBit = 0;
        _smallValue = 0;
        _data = null;
    }
    
    public bool IsNegative => _signBit == 1 && !IsZero;
    internal bool IsZero => _data is null && _smallValue == 0u;
    private int WordCount => _data?.Length ?? 1;
    
    /// Конструктор от массива цифр (little endian)
    public BetterBigInteger(uint[] digits, bool isNegative = false)
    {
        if (digits is null)
            throw new ArgumentNullException(nameof(digits));
        
        StoreDigits(digits, isNegative);
    }
    
    /// Конструктор от перечисления цифр
    public BetterBigInteger(IEnumerable<uint> digits, bool isNegative = false)
    {
        if (digits is null)
            throw new ArgumentNullException(nameof(digits));
        
        StoreDigits(digits.ToArray(), isNegative);
    }
    
    /// Конструктор от строки в произвольной системе счисления
    public BetterBigInteger(string value, int radix)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));
        
        if (radix < 2 || radix > 36)
            throw new ArgumentOutOfRangeException(nameof(radix), "Radix must be between 2 and 36");
        
        string text = value.Trim();
        if (text.Length == 0)
            throw new FormatException("Empty string is not a valid number");
        
        bool isNegative = false;
        int startIndex = 0;
        
        if (text[0] == '+')
            startIndex = 1;
        else if (text[0] == '-')
        {
            isNegative = true;
            startIndex = 1;
        }
        
        if (startIndex >= text.Length)
            throw new FormatException("No digits after sign");
        
        // Схема Горнера
        uint[] magnitude = [0u];
        
        for (int i = startIndex; i < text.Length; i++)
        {
            int digit = ParseDigit(text[i]);
            if (digit < 0 || digit >= radix)
                throw new FormatException($"Invalid digit '{text[i]}' for radix {radix}");
            
            magnitude = MultiplyByUInt(magnitude, (uint)radix);
            magnitude = AddUInt(magnitude, (uint)digit);
        }
        
        StoreDigits(magnitude, isNegative);
    }
    
    /// Возвращает цифры числа (модуль) в формате little-endian
    public ReadOnlySpan<uint> GetDigits()
    {
        return _data ?? [_smallValue];
    }
    
    /// Сравнение с другим большим целым
    public int CompareTo(IBigInteger? other)
    {
        if (other is null)
            return 1;
        
        if (IsNegative != other.IsNegative)
            return IsNegative ? -1 : 1;
        
        int magnitudeCompare = CompareMagnitudes(GetDigits(), other.GetDigits());
        return IsNegative ? -magnitudeCompare : magnitudeCompare;
    }
    
    /// Проверка на равенство
    public bool Equals(IBigInteger? other)
    {
        return CompareTo(other) == 0;
    }
    
    public override bool Equals(object? obj)
    {
        return obj is IBigInteger other && Equals(other);
    }
    
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(IsNegative);
        
        ReadOnlySpan<uint> digits = GetDigits();
        int length = GetRealLength(digits);
        
        if (length == 0)
        {
            hash.Add(0u);
            return hash.ToHashCode();
        }
        
        for (int i = 0; i < length; i++)
            hash.Add(digits[i]);
        
        return hash.ToHashCode();
    }
    
    /// Оператор сложения
    public static BetterBigInteger operator +(BetterBigInteger a, BetterBigInteger b)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        if (b is null) throw new ArgumentNullException(nameof(b));
        
        ReadOnlySpan<uint> left = a.GetDigits();
        ReadOnlySpan<uint> right = b.GetDigits();
        
        if (a.IsNegative == b.IsNegative)
            return FromMagnitude(AddMagnitudes(left, right), a.IsNegative);
        
        int comparison = CompareMagnitudes(left, right);
        
        if (comparison == 0)
            return Zero();
        
        if (comparison > 0)
            return FromMagnitude(SubtractMagnitudes(left, right), a.IsNegative);
        
        return FromMagnitude(SubtractMagnitudes(right, left), b.IsNegative);
    }
    
    /// Оператор вычитания
    public static BetterBigInteger operator -(BetterBigInteger a, BetterBigInteger b)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        if (b is null) throw new ArgumentNullException(nameof(b));
        
        return a + (-b);
    }
    
    /// Оператор унарного минуса
    public static BetterBigInteger operator -(BetterBigInteger a)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        
        if (a.IsZero)
            return Zero();
        
        return FromMagnitude(a.GetDigits(), !a.IsNegative);
    }
    
    /// Оператор деления
    public static BetterBigInteger operator /(BetterBigInteger a, BetterBigInteger b)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        if (b is null) throw new ArgumentNullException(nameof(b));
        
        if (b.IsZero)
            throw new DivideByZeroException("Division by zero");
        
        uint[] quotient = DivMagnitudes(a.GetDigits(), b.GetDigits(), out _);
        bool isNegative = (a.IsNegative ^ b.IsNegative) && !IsMagnitudeZero(quotient);
        return FromMagnitude(quotient, isNegative);
    }
    
    /// Оператор взятия остатка
    public static BetterBigInteger operator %(BetterBigInteger a, BetterBigInteger b)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        if (b is null) throw new ArgumentNullException(nameof(b));
        
        if (b.IsZero)
            throw new DivideByZeroException("Modulo by zero");
        
        DivMagnitudes(a.GetDigits(), b.GetDigits(), out uint[] remainder);
        bool isNegative = a.IsNegative && !IsMagnitudeZero(remainder);
        return FromMagnitude(remainder, isNegative);
    }
    
    /// Оператор умножения (делегируется стратегии)
    public static BetterBigInteger operator *(BetterBigInteger a, BetterBigInteger b)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        if (b is null) throw new ArgumentNullException(nameof(b));
        
        int size = Math.Max(a.WordCount, b.WordCount);
        
        IMultiplier strategy = size < SimpleMultiplierZone 
            ? SimpleMultiplierStrategy 
            : size < KaratsubaZone 
                ? KaratsubaMultiplierStrategy 
                : FftMultiplierStrategy;
        
        return strategy.Multiply(a, b);
    }
    
    /// Оператор побитовой инверсии
    public static BetterBigInteger operator ~(BetterBigInteger a)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        
        int targetWordCount = Math.Max(1, a.WordCount + 1);
        uint[] words = ToBinaryView(a, targetWordCount);
        
        for (int i = 0; i < words.Length; i++)
            words[i] = ~words[i];
        
        return FromBinaryView(words);
    }
    
    /// Оператор побитового И
    public static BetterBigInteger operator &(BetterBigInteger a, BetterBigInteger b)
    {
        return BinaryOperation(a, b, (x, y) => x & y);
    }
    
    /// Оператор побитового ИЛИ
    public static BetterBigInteger operator |(BetterBigInteger a, BetterBigInteger b)
    {
        return BinaryOperation(a, b, (x, y) => x | y);
    }
    
    /// Оператор побитового исключающего ИЛИ
    public static BetterBigInteger operator ^(BetterBigInteger a, BetterBigInteger b)
    {
        return BinaryOperation(a, b, (x, y) => x ^ y);
    }
    
    /// Оператор сдвига влево
    public static BetterBigInteger operator <<(BetterBigInteger a, int shift)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        
        if (shift == int.MinValue)
            throw new ArgumentOutOfRangeException(nameof(shift), "Cannot negate int.MinValue");
        
        if (shift < 0)
            return a >> -shift;
        
        if (shift == 0 || a.IsZero)
            return FromMagnitude(a.GetDigits(), a.IsNegative);
        
        return FromMagnitude(ShiftLeftMagnitude(a.GetDigits(), shift), a.IsNegative);
    }
    
    /// Оператор сдвига вправо
    public static BetterBigInteger operator >>(BetterBigInteger a, int shift)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        
        if (shift == int.MinValue)
            throw new ArgumentOutOfRangeException(nameof(shift), "Cannot negate int.MinValue");
        
        if (shift < 0)
            return a << -shift;
        
        if (shift == 0 || a.IsZero)
            return FromMagnitude(a.GetDigits(), a.IsNegative);
        
        if (!a.IsNegative)
            return FromMagnitude(ShiftRightMagnitude(a.GetDigits(), shift), false);
        
        // Для отрицательных чисел: округление вниз
        BetterBigInteger one = FromMagnitude([1u], false);
        BetterBigInteger temp = (one << shift) - one;
        BetterBigInteger adjusted = Abs(a) + temp;
        BetterBigInteger shifted = FromMagnitude(ShiftRightMagnitude(adjusted.GetDigits(), shift), false);
        return shifted.IsZero ? Zero() : -shifted;
    }
    
    public static bool operator ==(BetterBigInteger? a, BetterBigInteger? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;
        return a.Equals(b);
    }
    
    public static bool operator !=(BetterBigInteger? a, BetterBigInteger? b) => !(a == b);
    public static bool operator <(BetterBigInteger a, BetterBigInteger b) => a.CompareTo(b) < 0;
    public static bool operator >(BetterBigInteger a, BetterBigInteger b) => a.CompareTo(b) > 0;
    public static bool operator <=(BetterBigInteger a, BetterBigInteger b) => a.CompareTo(b) <= 0;
    public static bool operator >=(BetterBigInteger a, BetterBigInteger b) => a.CompareTo(b) >= 0;
    
    /// Преобразование в строку в десятичной системе
    public override string ToString() => ToString(10);
    
    /// Преобразование в строку в заданной системе счисления
    public string ToString(int radix)
    {
        if (radix < 2 || radix > 36)
            throw new ArgumentOutOfRangeException(nameof(radix), "Radix must be between 2 and 36");
        
        if (IsZero)
            return "0";
        
        uint[] work = NormalizeCopy(GetDigits());
        int length = work.Length;
        StringBuilder reversedDigits = new();
        
        while (length > 0)
        {
            uint remainder = DivSmall(work, ref length, (uint)radix);
            reversedDigits.Append(DigitToChar((int)remainder));
        }
        
        if (IsNegative)
            reversedDigits.Append('-');
        
        char[] chars = reversedDigits.ToString().ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }
    
    #region Helper Methods
    
    private static BetterBigInteger Zero() => new();
    private static BetterBigInteger Abs(BetterBigInteger value) => FromMagnitude(value.GetDigits(), false);
    internal static BetterBigInteger FromMagnitude(ReadOnlySpan<uint> digits, bool isNegative) 
        => new(digits.ToArray(), isNegative);
    
    private static bool IsMagnitudeZero(ReadOnlySpan<uint> digits) => GetRealLength(digits) == 0;
    
    /// Возвращает реальную длину массива без ведущих нулей
    internal static int GetRealLength(ReadOnlySpan<uint> digits)
    {
        int length = digits.Length;
        while (length > 0 && digits[length - 1] == 0u)
            length--;
        return length;
    }
    
    /// Создаёт копию массива без ведущих нулей
    internal static uint[] NormalizeCopy(ReadOnlySpan<uint> digits)
    {
        int length = GetRealLength(digits);
        if (length == 0)
            return [0u];
        
        uint[] result = new uint[length];
        for (int i = 0; i < length; i++)
            result[i] = digits[i];
        return result;
    }
    
    /// Сравнение двух модулей (абсолютных значений)
    private static int CompareMagnitudes(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        int leftLen = GetRealLength(left);
        int rightLen = GetRealLength(right);
        
        if (leftLen != rightLen)
            return leftLen < rightLen ? -1 : 1;
        
        for (int i = leftLen - 1; i >= 0; i--)
        {
            if (left[i] != right[i])
                return left[i] < right[i] ? -1 : 1;
        }
        return 0;
    }
    
    /// Сложение двух 32-битных слов с переносом
    private static uint Add32(uint left, uint right, uint carryIn, out uint carryOut)
    {
        uint sum = left + right;
        uint carry1 = sum < left ? 1u : 0u;
        
        uint result = sum + carryIn;
        uint carry2 = result < sum ? 1u : 0u;
        
        carryOut = carry1 + carry2;
        return result;
    }
    
    /// Умножение двух 32-битных слов, результат 64 бита (low + high)
    private static void Multiply32(uint left, uint right, out uint low, out uint high)
    {
        uint leftLow = left & 0xFFFFu;
        uint leftHigh = left >> 16;
        uint rightLow = right & 0xFFFFu;
        uint rightHigh = right >> 16;
        
        uint part00 = leftLow * rightLow;
        uint part01 = leftLow * rightHigh;
        uint part10 = leftHigh * rightLow;
        uint part11 = leftHigh * rightHigh;
        
        uint middle = part00 >> 16;
        uint carry = 0;
        
        uint sum = middle + (part01 & 0xFFFFu);
        if (sum < middle) carry++;
        middle = sum;
        
        sum = middle + (part10 & 0xFFFFu);
        if (sum < (middle & 0xFFFFu)) carry++;
        middle = sum;
        
        low = (part00 & 0xFFFFu) | ((middle & 0xFFFFu) << 16);
        high = part11 + (part01 >> 16) + (part10 >> 16) + (middle >> 16) + carry;
    }
    
    /// Добавление значения к массиву с распространением переноса
    private static void AddToWordArray(uint[] result, int index, uint value)
    {
        uint carry = value;
        int currentIndex = index;
        
        while (carry != 0)
        {
            uint sum = result[currentIndex] + carry;
            carry = sum < result[currentIndex] ? 1u : 0u;
            result[currentIndex] = sum;
            currentIndex++;
        }
    }
    
    /// Сложение двух модулей
    internal static uint[] AddMagnitudes(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        int maxLen = Math.Max(left.Length, right.Length);
        uint[] result = new uint[maxLen + 1];
        uint carry = 0;
        
        for (int i = 0; i < maxLen; i++)
        {
            uint leftVal = i < left.Length ? left[i] : 0u;
            uint rightVal = i < right.Length ? right[i] : 0u;
            result[i] = Add32(leftVal, rightVal, carry, out carry);
        }
        
        result[maxLen] = carry;
        return NormalizeCopy(result);
    }
    
    /// Вычитание модулей (left >= right)
    internal static uint[] SubtractMagnitudes(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        if (CompareMagnitudes(left, right) < 0)
            throw new ArgumentException("left must be >= right");
        
        uint[] result = new uint[left.Length];
        uint borrow = 0;
        
        for (int i = 0; i < left.Length; i++)
        {
            uint rightVal = i < right.Length ? right[i] : 0u;
            uint diff = left[i] - rightVal;
            uint newBorrow = diff > left[i] ? 1u : 0u;

            diff = diff - borrow;
            if (diff > (left[i] - rightVal)) { newBorrow++; }
            result[i] = diff;
            borrow = newBorrow;

        }
        
        return NormalizeCopy(result);
    }

    /// Классическое умножение O(n²)
    internal static uint[] MultiplyClassic(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right)
    {
        int leftLength = GetRealLength(left);
        int rightLength = GetRealLength(right);

        if (leftLength == 0 || rightLength == 0)
        {
            return [0u];
        }

        uint[] result = new uint[leftLength + rightLength];

        for (int i = 0; i < leftLength; i++)
        {
            uint acc = 0;

            for (int j = 0; j < rightLength; j++)
            {
                Multiply32(left[i], right[j], out uint productLow, out uint productHigh);

                uint sum = Add32(result[i + j], productLow, acc, out uint accFromLow);
                result[i + j] = sum;

                uint nextacc = productHigh + accFromLow;
                if (nextacc < productHigh)
                {
                    AddToWordArray(result, i + j + 2, 1u);
                }

                acc = nextacc;
            }

            AddToWordArray(result, i + rightLength, acc);
        }

        return NormalizeCopy(result);
    }
    
    /// Прибавление 32-битного числа к модулю
    private static uint[] AddUInt(ReadOnlySpan<uint> digits, uint value)
    {
        if (value == 0)
            return NormalizeCopy(digits);
        
        int length = GetRealLength(digits);
        if (length == 0)
            return [value];
        
        uint[] result = new uint[length + 1];
        uint carry = value;
        int i = 0;
        
        while (i < length && carry != 0)
        {
            uint sum = digits[i] + carry;
            result[i] = sum;
            carry = sum < digits[i] ? 1u : 0u;
            i++;
        }
        
        while (i < length)
        {
            result[i] = digits[i];
            i++;
        }
        
        if (carry > 0)
            result[length] = carry;
        
        return NormalizeCopy(result);
    }
    
    /// Умножение модуля на 32-битное число
    private static uint[] MultiplyByUInt(ReadOnlySpan<uint> digits, uint factor)
    {
        int length = GetRealLength(digits);
        if (length == 0 || factor == 0)
            return [0u];
        if (factor == 1)
            return NormalizeCopy(digits);
        
        uint[] result = new uint[length + 1];
        uint carry = 0;
        
        for (int i = 0; i < length; i++)
        {
            Multiply32(digits[i], factor, out uint low, out uint high);
            result[i] = Add32(low, carry, 0, out uint newCarry);
            carry = high + newCarry;
        }
        
        result[length] = carry;
        return NormalizeCopy(result);
    }
    
    /// Сдвиг модуля влево на заданное количество битов
    private static uint[] ShiftLeftMagnitude(ReadOnlySpan<uint> digits, int shift)
    {
        int length = GetRealLength(digits);
        if (length == 0 || shift == 0)
            return NormalizeCopy(digits);
        
        int wordShift = shift / BitsPerWord;
        int bitShift = shift % BitsPerWord;
        uint[] result = new uint[length + wordShift + 1];
        
        if (bitShift == 0)
        {
            for (int i = 0; i < length; i++)
                result[i + wordShift] = digits[i];
            return NormalizeCopy(result);
        }
        
        uint carry = 0;
        for (int i = 0; i < length; i++)
        {
            uint current = digits[i];
            result[i + wordShift] = (current << bitShift) | carry;
            carry = current >> (BitsPerWord - bitShift);
        }
        
        result[length + wordShift] = carry;
        return NormalizeCopy(result);
    }
    
    /// Сдвиг модуля вправо на заданное количество битов
    private static uint[] ShiftRightMagnitude(ReadOnlySpan<uint> digits, int shift)
    {
        int length = GetRealLength(digits);
        if (length == 0 || shift == 0)
            return NormalizeCopy(digits);
        
        int wordShift = shift / BitsPerWord;
        int bitShift = shift % BitsPerWord;
        
        if (wordShift >= length)
            return [0u];
        
        int resultLen = length - wordShift;
        uint[] result = new uint[resultLen];
        
        if (bitShift == 0)
        {
            for (int i = wordShift; i < length; i++)
                result[i - wordShift] = digits[i];
            return NormalizeCopy(result);
        }
        
        uint carry = 0;
        uint lowMask = (1u << bitShift) - 1u;
        
        for (int i = length - 1; i >= wordShift; i--)
        {
            uint current = digits[i];
            result[i - wordShift] = (current >> bitShift) | (carry << (BitsPerWord - bitShift));
            carry = current & lowMask;
        }
        
        return NormalizeCopy(result);
    }
    
    /// Вычисление битовой длины модуля
    private static int GetBitLength(ReadOnlySpan<uint> digits)
    {
        int length = GetRealLength(digits);
        if (length == 0)
            return 0;
        
        uint highest = digits[length - 1];
        return ((length - 1) * BitsPerWord) + (BitsPerWord - BitOperations.LeadingZeroCount(highest));
    }
    
    /// Получение значения отдельного бита
    private static bool GetBit(ReadOnlySpan<uint> digits, int bitIndex)
    {
        int wordIdx = bitIndex / BitsPerWord;
        if (wordIdx >= digits.Length)
            return false;
        
        int offset = bitIndex % BitsPerWord;
        return ((digits[wordIdx] >> offset) & 1u) != 0;
    }
    
    /// Деление модулей (бинарный алгоритм)
    private static uint[] DivMagnitudes(ReadOnlySpan<uint> dividend, ReadOnlySpan<uint> divisor, out uint[] remainder)
    {
        uint[] workDividend = NormalizeCopy(dividend);
        uint[] workDivisor = NormalizeCopy(divisor);
        
        if (IsMagnitudeZero(workDivisor))
            throw new DivideByZeroException();
        
        if (CompareMagnitudes(workDividend, workDivisor) < 0)
        {
            remainder = workDividend;
            return [0u];
        }
        
        int bitLength = GetBitLength(workDividend);
        uint[] quotient = new uint[(bitLength + BitsPerWord - 1) / BitsPerWord];
        uint[] acc = [0u];
        
        for (int bit = bitLength - 1; bit >= 0; bit--)
        {
            acc = ShiftLeftMagnitude(acc, 1);
            
            if (GetBit(workDividend, bit))
                acc = AddUInt(acc, 1u);
            
            if (CompareMagnitudes(acc, workDivisor) >= 0)
            {
                acc = SubtractMagnitudes(acc, workDivisor);
                quotient[bit / BitsPerWord] |= 1u << (bit % BitsPerWord);
            }
        }
        
        remainder = NormalizeCopy(acc);
        return NormalizeCopy(quotient);
    }
    
    /// Деление на однобайтовый делитель (для перевода в строку)
    private static uint DivSmall(uint[] digits, ref int length, uint divisor)
    {
        if (divisor == 0)
            throw new DivideByZeroException();
        
        uint remainder = 0;
        
        for (int i = length - 1; i >= 0; i--)
        {
            uint quotientWord = 0;
            uint currentWord = digits[i];
            
            for (int bit = BitsPerWord - 1; bit >= 0; bit--)
            {
                remainder <<= 1;
                if (((currentWord >> bit) & 1u) != 0)
                    remainder |= 1u;
                
                if (remainder >= divisor)
                {
                    remainder -= divisor;
                    quotientWord |= 1u << bit;
                }
            }
            
            digits[i] = quotientWord;
        }
        
        while (length > 0 && digits[length - 1] == 0)
            length--;
        
        return remainder;
    }
    
    /// Выполнение побитовой операции над двумя числами
    private static BetterBigInteger BinaryOperation(BetterBigInteger a, BetterBigInteger b, Func<uint, uint, uint> op)
    {
        if (a is null) throw new ArgumentNullException(nameof(a));
        if (b is null) throw new ArgumentNullException(nameof(b));
        
        int targetWords = Math.Max(a.WordCount, b.WordCount) + 1;
        uint[] leftWords = ToBinaryView(a, targetWords);
        uint[] rightWords = ToBinaryView(b, targetWords);
        uint[] resultWords = new uint[targetWords];
        
        for (int i = 0; i < targetWords; i++)
            resultWords[i] = op(leftWords[i], rightWords[i]);
        
        return FromBinaryView(resultWords);
    }
    
    /// Преобразование числа в дополнительный код (для побитовых операций)
    private static uint[] ToBinaryView(BetterBigInteger value, int wordCount)
    {
        uint[] words = new uint[wordCount];
        ReadOnlySpan<uint> digits = value.GetDigits();
        int copyLen = Math.Min(GetRealLength(digits), wordCount);
        
        for (int i = 0; i < copyLen; i++)
            words[i] = digits[i];
        
        if (!value.IsNegative)
            return words;
        
        for (int i = 0; i < words.Length; i++)
            words[i] = ~words[i];
        
        uint carry = 1;
        for (int i = 0; i < words.Length; i++)
        {
            uint sum = words[i] + carry;
            words[i] = sum;
            carry = sum == 0 ? 1u : 0u;
            if (carry == 0)
                break;
        }
        
        return words;
    }
    
    /// Преобразование из дополнительного кода обратно в число
    private static BetterBigInteger FromBinaryView(uint[] words)
    {
        bool isNegative = (words[^1] & 0x80000000u) != 0;
        
        if (!isNegative)
            return FromMagnitude(words, false);
        
        uint[] magnitude = new uint[words.Length];
        for (int i = 0; i < words.Length; i++)
            magnitude[i] = ~words[i];
        
        uint carry = 1;
        for (int i = 0; i < magnitude.Length; i++)
        {
            uint sum = magnitude[i] + carry;
            magnitude[i] = sum;
            carry = sum == 0 ? 1u : 0u;
            if (carry == 0)
                break;
        }
        
        magnitude = NormalizeCopy(magnitude);
        return IsMagnitudeZero(magnitude) ? Zero() : FromMagnitude(magnitude, true);
    }
    
    /// Сохранение цифр с оптимизацией для малых чисел
    private void StoreDigits(uint[] digits, bool isNegative)
    {
        int length = GetRealLength(digits);
        
        if (length == 0)
        {
            _signBit = 0;
            _smallValue = 0;
            _data = null;
            return;
        }
        
        if (length == 1)
        {
            _signBit = digits[0] == 0 ? 0 : (isNegative ? 1 : 0);
            _smallValue = digits[0];
            _data = null;
            return;
        }
        
        _signBit = isNegative ? 1 : 0;
        _smallValue = 0;
        _data = new uint[length];
        Array.Copy(digits, _data, length);
    }
    
    /// Преобразование символа в цифру (поддержка до 36-ричной системы)
    private static int ParseDigit(char c)
    {
        if (c >= '0' && c <= '9')
            return c - '0';
        if (c >= 'A' && c <= 'Z')
            return c - 'A' + 10;
        if (c >= 'a' && c <= 'z')
            return c - 'a' + 10;
        return -1;
    }
    
    /// Преобразование цифры в символ (поддержка до 36-ричной системы)
    private static char DigitToChar(int digit)
    {
        return digit < 10 ? (char)('0' + digit) : (char)('A' + digit - 10);
    }
    
    #endregion
}