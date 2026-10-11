using System;
using System.Reflection;
using GameFactory = ProcessorTycoon.Production.Factory;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The private counters of the game's <c>Factory</c> the reserve of lines writes (design.md, "Поля для рефлексии"; F28): <c>totalAllocatedLines</c> - the lines
    /// given out today, which the game caps at <c>ProductionCapacity</c> when it produces the CPUs and divides the operating cost by (<c>SpendOperationCost</c>) - and
    /// <c>totalDemandedLines</c> - the lines asked for, which <c>ResetProduction</c> adds to <c>DailyLinesDemand</c> of the monthly report. Read and written through
    /// <see cref="FieldInfo"/> once a day, without a patch. When a field is gone (a game update), <see cref="Verify"/> says so and the self-check switches off
    /// only <see cref="GpuCapability.Production"/>.
    /// </summary>
    internal static class FactoryAccess
    {
        public const string AllocatedField = "totalAllocatedLines";
        public const string DemandedField = "totalDemandedLines";

        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static bool _resolved;
        private static FieldInfo _allocated;
        private static FieldInfo _demanded;
        private static string _error;

        /// <summary>Both fields are there and are <c>int</c>; <paramref name="error"/> names what is missing.</summary>
        public static bool Verify(out string error)
        {
            Resolve();
            error = _error;
            return _error == null;
        }

        /// <summary>The field of <c>Factory</c> with this name and type <c>int</c> exists.</summary>
        public static bool HasIntField(string name)
        {
            return IntField(name) != null;
        }

        /// <summary><c>Factory.totalAllocatedLines</c>; 0 when the field is gone or the factory is null.</summary>
        public static int GetAllocated(GameFactory factory)
        {
            Resolve();
            if (factory == null || _allocated == null) return 0;
            return (int)_allocated.GetValue(factory);
        }

        /// <summary>Adds the reserve of the cards to both counters; false (nothing written) when a field is gone or the factory is null.</summary>
        public static bool AddReservation(GameFactory factory, int allocated, int demanded)
        {
            Resolve();
            if (factory == null || _allocated == null || _demanded == null) return false;
            int a = (int)_allocated.GetValue(factory);
            int d = (int)_demanded.GetValue(factory);
            _allocated.SetValue(factory, Add(a, allocated));
            _demanded.SetValue(factory, Add(d, demanded));
            return true;
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _allocated = IntField(AllocatedField);
            _demanded = IntField(DemandedField);
            if (_allocated == null && _demanded == null) _error = "Factory." + AllocatedField + " and Factory." + DemandedField + " are missing";
            else if (_allocated == null) _error = "Factory." + AllocatedField + " is missing";
            else if (_demanded == null) _error = "Factory." + DemandedField + " is missing";
        }

        private static FieldInfo IntField(string name)
        {
            FieldInfo field = typeof(GameFactory).GetField(name, InstanceFields);
            return field != null && field.FieldType == typeof(int) ? field : null;
        }

        private static int Add(int value, int more)
        {
            long sum = (long)value + Math.Max(0, more);
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }
    }
}
